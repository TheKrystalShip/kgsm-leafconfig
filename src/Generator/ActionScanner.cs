using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace TheKrystalShip.KGSM.ComponentConfig.Gen;

/// <summary>
/// Reads a component's actions and requirements out of its compiled assemblies, and checks them
/// against what its code does.
/// </summary>
/// <remarks>
/// <para>
/// Two things are checked, and each fails the build:
/// </para>
/// <list type="bullet">
/// <item>
///   <b>Nothing is called without being required.</b> Every call to a client-package method marked
///   <c>[Performs(action)]</c> must have a <c>[Requires(action)]</c> beside it — on the calling method,
///   the method a lambda or state machine was compiled out of, an enclosing type, or the assembly.
/// </item>
/// <item>
///   <b>Nothing is enforced without being declared.</b> A string in this component's own action
///   namespace — a literal the code checks, or a constant naming one — must be declared by an
///   <c>[Action]</c> or be one of the standard surface actions.
/// </item>
/// </list>
/// <para>
/// Method bodies are read as IL through <see cref="System.Reflection.Metadata"/>, and the methods they
/// call are resolved through the same <see cref="MetadataLoadContext"/> the descriptor is read with.
/// Nothing is loaded for execution.
/// </para>
/// </remarks>
internal sealed class ActionScanner(MetadataLoadContext context, IReadOnlyList<Assembly> assemblies, ComponentIdentity identity)
{
    private static readonly Regex LocalId = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex ComponentPart = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<short, OperandType> Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value, o => o.OperandType);

    private readonly List<string> _faults = [];
    private readonly Dictionary<string, List<ActionDef>> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(RequirementDef Requirement, string Where)>> _requires = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> _referenced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _performsCache = new(StringComparer.Ordinal);

    /// <summary>Read and check everything; throws naming every fault at once.</summary>
    public ActionSurface Scan()
    {
        if (!ComponentPart.IsMatch(identity.Namespace))
            _faults.Add($"the action namespace '{identity.Namespace}' is not lowercase kebab ([a-z0-9-])");

        foreach (Assembly assembly in assemblies)
        {
            ReadDeclarations(assembly);
            ReadCode(assembly);
        }

        ActionSurface surface = new(
            identity.Namespace,
            VersionOf(assemblies[0]),
            [.. MergeActions().OrderBy(a => a.Id, StringComparer.Ordinal)],
            [.. MergeRequirements().OrderBy(r => r.Action, StringComparer.Ordinal)]);

        CheckReferenced(surface);

        if (_faults.Count > 0)
            throw new GenException(
                $"the actions this component declares do not agree with its code:\n  - {string.Join("\n  - ", _faults)}");

        return surface;
    }

    // ── Declarations ──────────────────────────────────────────────────────────

    private void ReadDeclarations(Assembly assembly)
    {
        Declarations(assembly.GetCustomAttributesData(), $"assembly {assembly.GetName().Name}");

        foreach (Type type in assembly.GetTypes())
        {
            Declarations(type.GetCustomAttributesData(), type.FullName!);

            foreach (MethodBase method in Methods(type))
                Declarations(method.GetCustomAttributesData(), $"{Readable(type)}.{method.Name}");
        }
    }

    private void Declarations(IEnumerable<CustomAttributeData> attributes, string where)
    {
        foreach (CustomAttributeData a in attributes)
        {
            if (Named(a, Names.Attributes.Action))
                AddAction(a, where);
            else if (Named(a, Names.Attributes.Requires))
                AddRequirement(a, where);
        }
    }

    private void AddAction(CustomAttributeData a, string where)
    {
        string declared = (string)a.ConstructorArguments[0].Value!;
        string title = (string)a.ConstructorArguments[1].Value!;
        string? local = LocalOf(declared, where);
        if (local is null)
            return;

        if (Standard.Ids.Contains(local))
        {
            _faults.Add(
                $"{where} declares '{local}', which every component's standard surface declares already — " +
                "the package writes it, so the component does not");
            return;
        }

        if (string.IsNullOrWhiteSpace(title))
            _faults.Add($"{where} declares '{local}' with no title, so the role editor has nothing to show");

        bool self = a.NamedArguments.Any(n => n.MemberName == Names.Args.Self && (bool)n.TypedValue.Value!);
        ActionDef action = new(local, title.Trim(), EnumName(a.ConstructorArguments[2]), EnumName(a.ConstructorArguments[3]), self);

        if (!_actions.TryGetValue(local, out List<ActionDef>? seen))
            _actions[local] = seen = [];

        seen.Add(action with { DeclaredAt = where });
    }

    /// <summary>
    /// The local half of a declared id. A full id is accepted only in this component's own namespace:
    /// a component declares its own actions and nobody else's.
    /// </summary>
    private string? LocalOf(string declared, string where)
    {
        string local = declared;
        int colon = declared.IndexOf(':');
        if (colon >= 0)
        {
            string component = declared[..colon];
            if (component != identity.Namespace)
            {
                _faults.Add(
                    $"{where} declares '{declared}', in '{component}'s namespace. A component declares only its " +
                    $"own actions, as '{identity.Namespace}:…'; one it performs on another component is a [Requires].");
                return null;
            }

            local = declared[(colon + 1)..];
        }

        if (!LocalId.IsMatch(local))
        {
            _faults.Add($"{where} declares '{declared}', which is not an action id ([a-z0-9][a-z0-9._-]*)");
            return null;
        }

        return local;
    }

    private void AddRequirement(CustomAttributeData a, string where)
    {
        string action = (string)a.ConstructorArguments[0].Value!;
        string why = (string)a.ConstructorArguments[2].Value!;

        int colon = action.IndexOf(':');
        if (colon <= 0 || !ComponentPart.IsMatch(action[..colon]) || !LocalId.IsMatch(action[(colon + 1)..]))
        {
            _faults.Add($"{where} requires '{action}', which is not a full action id (<component>:<id>)");
            return;
        }

        if (action[..colon] == identity.Namespace)
        {
            _faults.Add(
                $"{where} requires '{action}', one of this component's own actions. A component performs its " +
                "own actions; it requires only another component's.");
            return;
        }

        if (string.IsNullOrWhiteSpace(why))
            _faults.Add($"{where} requires '{action}' without saying why, which is what the person reviewing it reads");

        if (!_requires.TryGetValue(action, out var seen))
            _requires[action] = seen = [];

        seen.Add((new RequirementDef(action, EnumName(a.ConstructorArguments[1]), why.Trim()), where));
    }

    private IEnumerable<ActionDef> MergeActions()
    {
        foreach ((string id, List<ActionDef> declared) in _actions)
        {
            ActionDef first = declared[0];
            foreach (ActionDef other in declared.Skip(1))
            {
                if (other with { DeclaredAt = null } != first with { DeclaredAt = null })
                    _faults.Add(
                        $"'{id}' is declared differently by {first.DeclaredAt} and {other.DeclaredAt}; one action " +
                        "has one title, effect, scope and self");
            }

            yield return first with { DeclaredAt = null };
        }
    }

    /// <summary>
    /// One entry per action. Several call sites may need the same action and give their own reasons,
    /// which are all kept; they must agree on the scope, because one requirement is approved at one.
    /// </summary>
    private IEnumerable<RequirementDef> MergeRequirements()
    {
        foreach ((string action, var declared) in _requires)
        {
            string[] scopes = [.. declared.Select(d => d.Requirement.Scope).Distinct()];
            if (scopes.Length > 1)
                _faults.Add(
                    $"'{action}' is required at {string.Join(" and ", scopes)} by " +
                    $"{string.Join(", ", declared.Select(d => d.Where))}; one requirement needs one scope");

            string why = string.Join("; ", declared.Select(d => d.Requirement.Why).Distinct().Order(StringComparer.Ordinal));
            yield return new RequirementDef(action, scopes[0], why);
        }
    }

    // ── Code ──────────────────────────────────────────────────────────────────

    private void ReadCode(Assembly assembly)
    {
        Dictionary<int, MethodBase> byToken = [];
        Dictionary<string, MethodBase> stateMachines = new(StringComparer.Ordinal);

        foreach (Type type in assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral && field.FieldType.FullName == Names.Clr.String && field.GetRawConstantValue() is string value)
                    Reference(value, $"{Readable(type)}.{field.Name}");
            }

            foreach (MethodBase method in Methods(type))
            {
                byToken[method.MetadataToken] = method;

                foreach (CustomAttributeData a in method.GetCustomAttributesData())
                {
                    if ((Named(a, Names.Attributes.AsyncStateMachine) || Named(a, Names.Attributes.IteratorStateMachine)
                         || Named(a, Names.Attributes.AsyncIteratorStateMachine))
                        && a.ConstructorArguments[0].Value is Type machine)
                    {
                        stateMachines[machine.FullName!] = method;
                    }
                }
            }
        }

        using FileStream file = File.OpenRead(assembly.Location);
        using PEReader pe = new(file);
        MetadataReader reader = pe.GetMetadataReader();

        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition definition = reader.GetMethodDefinition(handle);
            if (definition.RelativeVirtualAddress == 0)
                continue;

            if (!byToken.TryGetValue(MetadataTokens.GetToken(handle), out MethodBase? caller))
                continue;

            MethodBodyBlock body = pe.GetMethodBody(definition.RelativeVirtualAddress);
            Walk(body.GetILReader(), reader, assembly, caller, stateMachines);
        }
    }

    private void Walk(BlobReader il, MetadataReader reader, Assembly assembly, MethodBase caller, Dictionary<string, MethodBase> stateMachines)
    {
        while (il.RemainingBytes > 0)
        {
            int first = il.ReadByte();
            short code = first == 0xFE ? unchecked((short)(0xFE00 | il.ReadByte())) : (short)first;

            if (!Operands.TryGetValue(code, out OperandType operand))
                return;

            switch (operand)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    il.ReadByte();
                    break;
                case OperandType.InlineVar:
                    il.ReadInt16();
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    il.ReadInt64();
                    break;
                case OperandType.InlineSwitch:
                    int count = il.ReadInt32();
                    for (int i = 0; i < count; i++)
                        il.ReadInt32();
                    break;
                case OperandType.InlineString:
                    string literal = reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32()));
                    Reference(literal, Where(caller, stateMachines));
                    break;
                case OperandType.InlineMethod:
                    int token = il.ReadInt32();
                    foreach (string action in PerformedBy(reader, assembly, MetadataTokens.EntityHandle(token)))
                    {
                        if (!Covered(caller, action, stateMachines, depth: 0))
                            _faults.Add(
                                $"{Where(caller, stateMachines)} calls a method that performs '{action}' with no " +
                                $"[Requires(\"{action}\", …)] beside it — on the method, its type or the assembly");
                    }

                    break;
                default:
                    il.ReadInt32();
                    break;
            }
        }
    }

    // ── What a call performs ──────────────────────────────────────────────────

    /// <summary>The actions the method a call names performs, from its <c>[Performs]</c> marks.</summary>
    /// <remarks>
    /// Resolved by declaring type, name and parameter count. Overloads that share a count are read
    /// together; a client package marks overloads of one operation with one action, so the union is the
    /// answer either way.
    /// </remarks>
    private IReadOnlyList<string> PerformedBy(MetadataReader reader, Assembly assembly, EntityHandle handle)
    {
        if (Target(reader, handle) is not { } target)
            return [];

        if (target.Assembly is { } name && IsFramework(name))
            return [];

        string key = $"{target.Assembly ?? assembly.GetName().Name}|{target.Type}|{target.Method}|{target.Parameters}";
        if (_performsCache.TryGetValue(key, out IReadOnlyList<string>? cached))
            return cached;

        IReadOnlyList<string> actions;
        try
        {
            Assembly declaring = target.Assembly is null ? assembly : context.LoadFromAssemblyName(target.Assembly);
            Type? type = declaring.GetType(target.Type, throwOnError: false);
            actions = type is null ? [] : Performs(type, target.Method, target.Parameters);
        }
        catch (Exception e) when (e is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
        {
            // A type this tool cannot resolve is not a client package's: those are compiled against and
            // sit beside the component.
            actions = [];
        }

        _performsCache[key] = actions;
        return actions;
    }

    private static IReadOnlyList<string> Performs(Type type, string method, int parameters)
    {
        static IEnumerable<string> On(Type t, string name, int count) =>
            t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == name && m.GetParameters().Length == count)
                .SelectMany(m => m.GetCustomAttributesData())
                .Where(a => Named(a, Names.Attributes.Performs))
                .Select(a => (string)a.ConstructorArguments[0].Value!);

        List<string> found = [.. On(type, method, parameters)];
        if (found.Count == 0 && !type.IsInterface)
        {
            foreach (Type contract in type.GetInterfaces())
                found.AddRange(On(contract, method, parameters));
        }

        return [.. found.Distinct(StringComparer.Ordinal)];
    }

    private sealed record CallTarget(string? Assembly, string Type, string Method, int Parameters);

    private static CallTarget? Target(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodSpecification:
                return Target(reader, reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);

            case HandleKind.MethodDefinition:
            {
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return new CallTarget(null, TypeName(reader, method.GetDeclaringType()),
                    reader.GetString(method.Name), ParameterCount(reader, method.Signature));
            }

            case HandleKind.MemberReference:
            {
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)handle);
                if (member.GetKind() != MemberReferenceKind.Method)
                    return null;

                (string? assembly, string? type) = Owner(reader, member.Parent);
                return type is null
                    ? null
                    : new CallTarget(assembly, type, reader.GetString(member.Name), ParameterCount(reader, member.Signature));
            }

            default:
                return null;
        }
    }

    private static (string? Assembly, string? Type) Owner(MetadataReader reader, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeDefinition:
                return (null, TypeName(reader, (TypeDefinitionHandle)parent));

            case HandleKind.TypeReference:
                return Reference(reader, (TypeReferenceHandle)parent);

            case HandleKind.TypeSpecification:
            {
                // A generic instantiation: GENERICINST (CLASS|VALUETYPE) <type> <args…>. The method lives
                // on the generic definition.
                BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadByte() != 0x15)
                    return (null, null);

                blob.ReadByte();
                return Owner(reader, blob.ReadTypeHandle());
            }

            default:
                return (null, null);
        }
    }

    private static (string? Assembly, string? Type) Reference(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);

        switch (type.ResolutionScope.Kind)
        {
            case HandleKind.TypeReference:
                (string? assembly, string? outer) = Reference(reader, (TypeReferenceHandle)type.ResolutionScope);
                return (assembly, outer is null ? null : $"{outer}+{name}");

            case HandleKind.AssemblyReference:
                string ns = reader.GetString(type.Namespace);
                string assemblyName = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name);
                return (assemblyName, ns.Length == 0 ? name : $"{ns}.{name}");

            default:
                string local = reader.GetString(type.Namespace);
                return (null, local.Length == 0 ? name : $"{local}.{name}");
        }
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);

        TypeDefinitionHandle outer = type.GetDeclaringType();
        if (!outer.IsNil)
            return $"{TypeName(reader, outer)}+{name}";

        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static int ParameterCount(MetadataReader reader, BlobHandle signature)
    {
        BlobReader blob = reader.GetBlobReader(signature);
        SignatureHeader header = blob.ReadSignatureHeader();
        if (header.IsGeneric)
            blob.ReadCompressedInteger();

        return blob.ReadCompressedInteger();
    }

    private static bool IsFramework(string assembly) =>
        assembly.StartsWith("System", StringComparison.Ordinal)
        || assembly.StartsWith("Microsoft.", StringComparison.Ordinal)
        || assembly is "mscorlib" or "netstandard";

    // ── Where a call sits ─────────────────────────────────────────────────────

    /// <summary>
    /// Whether a <c>[Requires(action)]</c> covers a call made in <paramref name="caller"/>.
    /// </summary>
    /// <remarks>
    /// The compiler moves code out of the method it was written in: an async or iterator body into a
    /// state machine type, a lambda or local function into a method named after its parent. The
    /// declaration is written on the method a person wrote, so each of those is traced back to it — a
    /// state machine through the attribute the compiler leaves on its origin, a lambda through the name
    /// it is given — and every enclosing type and the assembly count as well.
    /// </remarks>
    private bool Covered(MethodBase caller, string action, Dictionary<string, MethodBase> stateMachines, int depth)
    {
        if (depth > 8)
            return false;

        if (RequiresOn(caller.GetCustomAttributesData(), action))
            return true;

        if (caller.DeclaringType is not { } type)
            return false;

        if (stateMachines.TryGetValue(type.FullName!, out MethodBase? origin) && Covered(origin, action, stateMachines, depth + 1))
            return true;

        HashSet<string> written = new(StringComparer.Ordinal);
        if (SourceName(caller.Name) is { } fromMethod)
            written.Add(fromMethod);

        Type? owner = type;
        for (; owner is not null && Generated(owner.Name); owner = owner.DeclaringType)
        {
            if (SourceName(owner.Name) is { } fromType)
                written.Add(fromType);
        }

        if (owner is not null)
        {
            // The method a person wrote, by name — or the one top-level statements compile into, which
            // is <Main>$.
            foreach (MethodBase method in Methods(owner))
            {
                bool wrote = written.Contains(method.Name)
                             || (method.Name.EndsWith('$') && SourceName(method.Name) is { } top && written.Contains(top));

                if (wrote && method != caller && Covered(method, action, stateMachines, depth + 1))
                    return true;
            }
        }

        for (Type? t = type; t is not null; t = t.DeclaringType)
        {
            if (RequiresOn(t.GetCustomAttributesData(), action))
                return true;
        }

        return RequiresOn(type.Assembly.GetCustomAttributesData(), action);
    }

    private static bool RequiresOn(IEnumerable<CustomAttributeData> attributes, string action) =>
        attributes.Any(a => Named(a, Names.Attributes.Requires) && (string?)a.ConstructorArguments[0].Value == action);

    /// <summary>Whether a type or method name is one the compiler made up: it starts with <c>&lt;</c>.</summary>
    private static bool Generated(string name) => name.StartsWith('<');

    /// <summary>The method a compiler-made name was made from: <c>&lt;&lt;Handle&gt;b__0&gt;d</c> → <c>Handle</c>.</summary>
    private static string? SourceName(string name)
    {
        if (!Generated(name))
            return null;

        string inner = name.TrimStart('<');
        int end = inner.IndexOf('>');
        return end > 0 ? inner[..end] : null;
    }

    /// <summary>A readable place for a message: the type and method a person wrote.</summary>
    private static string Where(MethodBase caller, Dictionary<string, MethodBase> stateMachines)
    {
        if (caller.DeclaringType is { } type && stateMachines.TryGetValue(type.FullName!, out MethodBase? origin))
            return Where(origin, stateMachines);

        Type? owner = caller.DeclaringType;
        while (owner is not null && Generated(owner.Name))
            owner = owner.DeclaringType;

        string method = SourceName(caller.Name) ?? caller.Name;
        return owner is null ? method : $"{Readable(owner)}.{method}";
    }

    private static string Readable(Type type) => type.FullName!.Replace('+', '.');

    // ── Own ids the code names ────────────────────────────────────────────────

    private void Reference(string value, string where)
    {
        int colon = value.IndexOf(':');
        if (colon <= 0 || value[..colon] != identity.Namespace || !LocalId.IsMatch(value[(colon + 1)..]))
            return;

        if (!_referenced.TryGetValue(value[(colon + 1)..], out SortedSet<string>? places))
            _referenced[value[(colon + 1)..]] = places = new SortedSet<string>(StringComparer.Ordinal);

        places.Add(where);
    }

    private void CheckReferenced(ActionSurface surface)
    {
        HashSet<string> declared = [.. surface.Actions.Select(a => a.Id), .. Standard.Ids];

        foreach ((string local, SortedSet<string> places) in _referenced.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (!declared.Contains(local))
                _faults.Add(
                    $"'{identity.Namespace}:{local}' is named by {string.Join(", ", places)} but no [Action] declares it; " +
                    "an action checked without being declared cannot be granted to anyone but an Owner");
        }
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    private static IEnumerable<MethodBase> Methods(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
    }

    /// <summary>
    /// The version the manifest carries: the assembly's informational version without its source
    /// revision, or its assembly version when there is none.
    /// </summary>
    private static string VersionOf(Assembly assembly)
    {
        string? informational = assembly.GetCustomAttributesData()
            .FirstOrDefault(a => Named(a, Names.Attributes.InformationalVersion))?
            .ConstructorArguments[0].Value as string;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static string EnumName(CustomAttributeTypedArgument argument)
    {
        foreach (FieldInfo member in argument.ArgumentType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (Equals(member.GetRawConstantValue(), argument.Value))
                return member.Name.ToLowerInvariant();
        }

        throw new GenException($"{argument.ArgumentType.Name} has no member with value {argument.Value}.");
    }

    private static bool Named(CustomAttributeData attribute, string name)
    {
        try
        {
            return attribute.AttributeType.Name == name;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
