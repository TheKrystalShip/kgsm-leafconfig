using TheKrystalShip.KGSM.LeafConfig.Gen;
using Xunit;

namespace TheKrystalShip.KGSM.LeafConfig.Tests;

/// <summary>
/// The rules that fail a leaf's build. Each one stands for a way the Control Panel would otherwise
/// misreport a leaf, so each is asserted by the message it produces rather than only by failing.
/// </summary>
public class ValidatorTests
{
    private static string Faults(
        IReadOnlyList<FieldDef>? fields = null,
        IReadOnlyList<GroupDef>? groups = null,
        IReadOnlyList<FloorSource>? floorSources = null,
        IReadOnlyList<FrameworkNamespace>? exempt = null,
        IEnumerable<string>? settingsKeys = null,
        LeafIdentity? identity = null)
    {
        var descriptor = new Descriptor(
            identity ?? Identity(),
            floorSources ?? [new FloorSource("appsettings", "/opt/x/x.settings.json")],
            groups ?? [new GroupDef("general", "General", 1)],
            fields ?? [],
            exempt ?? []);

        GenException ex = Assert.Throws<GenException>(
            () => Validator.Check(descriptor, settingsKeys ?? descriptor.Fields.Select(f => f.Env)));

        return ex.Message;
    }

    private static LeafIdentity Identity(string applyMode = "restart", bool readOnly = false, string? reason = null) =>
        new("x", "X", "kgsm-x.service", "A leaf.", OnDemand: false, applyMode, readOnly, reason);

    private static FieldDef Field(
        string key = "k",
        string env = "X__K",
        string type = "string",
        string risk = "safe",
        string? group = "general",
        string description = "What this does.",
        IReadOnlyList<string>? values = null,
        string? @default = null,
        int? min = null,
        int? max = null,
        string? dependsOn = null) =>
        new()
        {
            Key = key,
            Env = env,
            Label = "K",
            Description = description,
            Group = group,
            Type = type,
            Values = values,
            Default = @default,
            Min = min,
            Max = max,
            Risk = risk,
            DependsOn = dependsOn,
            DescriptionFrom = DescriptionSource.Panel,
            Order = 0,
        };

    [Fact]
    public void A_settings_key_nothing_describes_fails()
    {
        // The drift that started all this: a knob the leaf reads and the panel cannot see.
        Assert.Contains(
            "'X__Orphan' is declared in the settings file but no [LeafField] describes it",
            Faults(fields: [Field()], settingsKeys: ["X__K", "X__Orphan"]));
    }

    [Fact]
    public void A_described_key_the_settings_file_lacks_fails()
    {
        // The mirror image, and the worse one: the panel reports the override applied while the leaf
        // carries on unchanged, because a variable only overrides a key the file declares.
        Assert.Contains(
            "describes 'X__Ghost', which the settings file does not declare",
            Faults(fields: [Field(env: "X__Ghost")], settingsKeys: []));
    }

    [Fact]
    public void An_exempt_namespace_satisfies_coverage_in_both_directions()
    {
        var descriptor = new Descriptor(
            Identity(),
            [new FloorSource("appsettings", "/opt/x/x.settings.json")],
            [new GroupDef("general", "General", 1)],
            [Field(env: "Logging__LogLevel__Default")],
            [new FrameworkNamespace("Logging__", "open-ended")]);

        Validator.Check(descriptor, ["Logging__LogLevel__Microsoft.AspNetCore"]);
    }

    [Fact]
    public void A_duplicate_key_fails()
    {
        // Overrides are stored by key, so two fields sharing one would collide in the store.
        Assert.Contains(
            "key 'k' is declared more than once",
            Faults(fields: [Field(env: "X__A"), Field(env: "X__B")]));
    }

    [Fact]
    public void A_field_with_no_description_fails()
    {
        Assert.Contains("no description", Faults(fields: [Field(description: "")]));
    }

    [Fact]
    public void An_unknown_group_fails()
    {
        Assert.Contains("group 'nowhere' is not declared", Faults(fields: [Field(group: "nowhere")]));
    }

    [Fact]
    public void A_dependsOn_naming_no_field_fails()
    {
        Assert.Contains("dependsOn 'missing' is not a field", Faults(fields: [Field(dependsOn: "missing")]));
    }

    [Fact]
    public void An_enum_with_no_values_fails()
    {
        Assert.Contains("no values", Faults(fields: [Field(type: "enum")]));
    }

    [Fact]
    public void An_enum_default_outside_its_values_fails()
    {
        Assert.Contains(
            "default 'Purple' is not one of its values",
            Faults(fields: [Field(type: "enum", values: ["Red", "Blue"], @default: "Purple")]));
    }

    [Fact]
    public void Bounds_on_a_non_numeric_field_fail()
    {
        // A bound is what the API rejects against before restarting anything, so it has to bound
        // something numeric or it silently never applies.
        Assert.Contains("has no numeric range", Faults(fields: [Field(type: "path", min: 1)]));
    }

    [Fact]
    public void An_unknown_type_or_risk_fails()
    {
        Assert.Contains("type 'colour' is not one of", Faults(fields: [Field(type: "colour")]));
        Assert.Contains("risk 'mild' is not one of", Faults(fields: [Field(risk: "mild")]));
    }

    [Fact]
    public void A_floor_source_order_that_does_not_start_with_the_settings_file_fails()
    {
        // floorSources is read lowest-precedence-first. Listed anywhere else, the panel resolves a knob
        // to the settings file's value and reports it as the deployed one — a blank where the unit sets
        // a real path, on the one screen whose job is saying where a value came from.
        Assert.Contains(
            "the settings file is the lowest-precedence source and must be declared first",
            Faults(floorSources: [new FloorSource("env-file", "/etc/x/x.env"), new FloorSource("appsettings", "/opt/x/x.settings.json")]));
    }

    [Fact]
    public void ReadOnly_with_no_reason_fails()
    {
        Assert.Contains(
            "readOnly is set with no readOnlyReason",
            Faults(identity: Identity(readOnly: true)));
    }

    [Fact]
    public void An_unknown_applyMode_fails()
    {
        Assert.Contains("applyMode 'reboot' is not one of", Faults(identity: Identity(applyMode: "reboot")));
    }
}
