using System.Text.Json;

using TheKrystalShip.KGSM.ComponentConfig.Gen;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// The generator against a real compiled leaf — the fixture in tests/Fixtures/SampleLeaf, read the
/// same way a live leaf is read: from its assembly's metadata, with nothing loaded for execution.
/// </summary>
public class GeneratorTests
{
    private static BuildResult Build() => ComponentDescriptorFactory.Build(Fixture.Assembly, Fixture.Settings);

    private static FieldDef Field(string key) =>
        Build().Descriptor.Fields.Single(f => f.Key == key);

    /// <summary>
    /// The whole emitted file, pinned. Every other test here says why one value is what it is; this
    /// one catches a change nobody meant to make — a reordered key, a dropped optional, a shifted
    /// group — which is the failure a descriptor's consumers would feel and no targeted test names.
    /// </summary>
    [Fact]
    public void The_emitted_descriptor_matches_the_golden_file()
    {
        string expected = File.ReadAllText(Fixture.Golden("sample.leaf.json"));
        string actual = Emitter.Render(Build().Descriptor);

        Assert.Equal(expected, actual);
    }

    // ── Derivation: the things a leaf no longer has to write down ────────────

    [Fact]
    public void The_env_name_comes_from_the_property_path()
    {
        // This is the honesty property the format cares about most: the descriptor cannot name a
        // variable the leaf does not read, because the name is the binder's own address for it.
        Assert.Equal("Sample__Port", Field("port").Env);
        Assert.Equal("Sample__Status__Online", Field("statusOnline").Env);
    }

    [Fact]
    public void The_default_comes_from_the_settings_file()
    {
        Assert.Equal("8080", Field("port").Default);
        Assert.Equal("online", Field("statusOnline").Default);
    }

    [Fact]
    public void A_suppressed_default_is_absent_rather_than_blank()
    {
        // The settings file says "", but the leaf resolves a blank host id to the machine name. An
        // empty-string default would be a value the leaf never actually starts with.
        Assert.Null(Field("hostId").Default);
    }

    [Fact]
    public void The_panel_type_is_read_off_the_property()
    {
        Assert.Equal("int", Field("port").Type);
        Assert.Equal("string", Field("statusOnline").Type);
    }

    [Fact]
    public void An_enum_property_supplies_its_own_values()
    {
        FieldDef backend = Field("backend");

        Assert.Equal("enum", backend.Type);
        Assert.Equal(["Ufw", "NfTables"], backend.Values);
    }

    [Fact]
    public void A_declared_type_wins_over_the_derived_one()
    {
        // Token is a string; only the leaf knows it is a secret.
        Assert.Equal("secret", Field("token").Type);
    }

    // ── Prose ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_panel_tag_is_the_description()
    {
        FieldDef port = Field("port");

        Assert.Equal("TCP port the sample leaf listens on.", port.Description);
        Assert.Equal(DescriptionSource.Panel, port.DescriptionFrom);
    }

    [Fact]
    public void A_field_with_no_panel_tag_falls_back_to_its_summary()
    {
        FieldDef token = Field("token");

        Assert.Equal("Shared secret for the fixture's imaginary API. Written, never read back.", token.Description);
        Assert.Equal(DescriptionSource.Summary, token.DescriptionFrom);
    }

    [Fact]
    public void A_wrapped_doc_comment_becomes_one_paragraph()
    {
        // Doc comments wrap across source lines; the panel renders a sentence.
        Assert.DoesNotContain('\n', Field("backend").Description);
        Assert.Contains("Changing it rewrites nothing that is already applied.", Field("backend").Description);
    }

    // ── What is deliberately left out ────────────────────────────────────────

    [Fact]
    public void A_name_keyed_map_is_not_a_field()
    {
        // One variable cannot express a collection, and systemd refuses a variable name containing a
        // hyphen — so a map keyed by a name the operator chose could never be delivered through the
        // env file. Skipping it here is what keeps that rule from depending on anyone remembering it.
        Assert.DoesNotContain(Build().Descriptor.Fields, f => f.Env.Contains("Channels"));
    }

    [Fact]
    public void An_ignored_property_is_not_a_field()
    {
        Assert.DoesNotContain(Build().Descriptor.Fields, f => f.Env.Contains("CachePath"));
    }

    [Fact]
    public void A_bound_property_nobody_described_is_reported()
    {
        Assert.Contains(Build().Warnings, w => w.Contains("Undescribed") && w.Contains("Sample__Undescribed"));
    }

    // ── Ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fields_follow_their_group_order_then_their_declaration_order()
    {
        // The panel's layout is readable straight off the settings type, and a framework field sorts
        // ahead of the bound properties in its group.
        Assert.Equal(
            ["logLevel", "token", "hostId", "statusOnline", "port", "backend", "retryAttempts"],
            Build().Descriptor.Fields.Select(f => f.Key));
    }

    [Fact]
    public void A_section_in_a_declared_assembly_is_described()
    {
        // A leaf's configuration types do not have to live in its entry assembly. kgsm-bot and kgsm-api
        // both bind sections from a layer below, and a descriptor that stopped at the entry assembly
        // would silently omit them — every one a knob the Control Panel could not show. The leaf names
        // that assembly rather than the generator guessing, because leaves share libraries.
        FieldDef retry = Field("retryAttempts");

        Assert.Equal("Retry__Attempts", retry.Env);
        Assert.Equal("3", retry.Default);
        Assert.Equal("How many times a failed call is retried before the leaf gives up on it.", retry.Description);
    }

    [Fact]
    public void A_declared_section_assembly_that_is_missing_is_an_error()
    {
        // Silently skipping it would drop every knob it declares — the failure this whole mechanism
        // exists to make impossible, reintroduced by a typo.
        GenException ex = Assert.Throws<GenException>(
            () => ComponentDescriptorFactory.Build(Fixture.MissingSectionAssembly, Fixture.Settings));

        Assert.Contains("names an assembly that is not beside the leaf", ex.Message);
    }

    // ── Leaf-level ───────────────────────────────────────────────────────────

    [Fact]
    public void The_leaf_identity_is_read_from_the_assembly()
    {
        ComponentIdentity identity = Build().Descriptor.Identity;

        Assert.Equal("sample", identity.Id);
        Assert.Equal("kgsm-sample.service", identity.Unit);
        Assert.Equal("restart", identity.ApplyMode);
        Assert.False(identity.OnDemand);
    }

    [Fact]
    public void A_declared_gpu_backend_unit_is_read_from_the_assembly()
    {
        // The leaf spends the card through a process it does not own, so nothing in its own cgroup
        // could reveal this. Declaring it is what lets the monitor attribute the figure and still say
        // which unit it came from.
        Assert.Equal(["kgsm-sample-backend.service"], Build().Descriptor.GpuBackendUnits);
    }

    // ── Which kind of component this is ───────────────────────────────────────

    [Fact]
    public void An_anchor_is_described_by_the_same_body_a_leaf_is()
    {
        // The two identity attributes differ in nothing but which they are. An anchor's groups, floor
        // sources and fields go through exactly the code a leaf's do, which is what makes one shared
        // body honest rather than a coincidence that will drift.
        Descriptor anchor = ComponentDescriptorFactory
            .Build(Fixture.AnchorAssembly, Fixture.AnchorSettings).Descriptor;

        Assert.Equal(ComponentKind.Anchor, anchor.Identity.Kind);
        Assert.Equal("sample-anchor", anchor.Identity.Id);
        Assert.Equal("kgsm-sample-anchor.service", anchor.Identity.Unit);
        Assert.Contains(anchor.Fields, f => f.Key == "logLevel");
        Assert.Contains(anchor.Groups, g => g.Id == "general");
    }

    [Fact]
    public void The_file_records_no_kind_because_where_it_is_installed_says_which()
    {
        // A leaf is installed where the node that runs it is scanned; an anchor is not. The location
        // is the fact, and a key repeating it is a second record of one thing that can disagree with
        // the first. So the two files are the same shape, and the leaf's is byte-comparable to the
        // anchor's wherever their content matches.
        string leaf = Emitter.Render(Build().Descriptor);
        string anchor = Emitter.Render(ComponentDescriptorFactory
            .Build(Fixture.AnchorAssembly, Fixture.AnchorSettings).Descriptor);

        // Read as JSON rather than searched as text: a floor source carries its own `kind`, and a
        // substring check would find that one and call the file guilty of something it does not do.
        foreach (string rendered in new[] { leaf, anchor })
        {
            JsonElement root = JsonDocument.Parse(rendered).RootElement;
            foreach (string spelling in new[] { "kind", "anchor", "leaf", "component" })
                Assert.False(root.TryGetProperty(spelling, out _), $"the descriptor states '{spelling}'");
        }
    }

    [Fact]
    public void A_component_that_claims_two_identities_is_refused()
    {
        // A component states one thing about what it is. Picking one by a precedence rule would put a
        // component in the wrong directory on a deploy nobody re-read.
        GenException ex = Assert.Throws<GenException>(
            () => ComponentDescriptorFactory.Build(Fixture.ConfusedAssembly, Fixture.Settings));

        // Both are named, because the fix is deleting one and the message has to say which two are
        // there to choose between.
        Assert.Contains("[assembly: Leaf(...)]", ex.Message);
        Assert.Contains("[assembly: Anchor(...)]", ex.Message);
    }

    [Fact]
    public void A_component_whose_deployment_decides_its_kind_is_described_both_ways()
    {
        // The same body again, and the one field that cannot be shared: a leaf's role sentence names
        // the host it serves, and an anchor has no host. Everything else is identical, which is what
        // makes describing it twice safe rather than a second declaration to keep in step.
        Descriptor either = ComponentDescriptorFactory
            .Build(Fixture.EitherAssembly, Fixture.EitherSettings).Descriptor;

        Assert.Equal(ComponentKind.Either, either.Identity.Kind);
        Assert.Equal("A fixture serving the host it runs on.", either.Identity.RoleFor(ComponentKind.Leaf));
        Assert.Equal("A fixture serving the whole cluster.", either.Identity.RoleFor(ComponentKind.Anchor));

        string leaf = Emitter.Render(either, ComponentKind.Leaf);
        string anchor = Emitter.Render(either, ComponentKind.Anchor);

        Assert.NotEqual(leaf, anchor);
        foreach (string rendered in new[] { leaf, anchor })
        {
            JsonElement root = JsonDocument.Parse(rendered).RootElement;
            Assert.Equal("sample-either", root.GetProperty("id").GetString());
            Assert.Equal("kgsm-sample-either.service", root.GetProperty("unit").GetString());

            // Still no kind in either file. Being describable both ways is exactly the case where a
            // key repeating what the directory already says would be believed over the directory.
            Assert.False(root.TryGetProperty("kind", out _));
        }
    }

    [Fact]
    public void An_unset_anchor_role_repeats_the_one_that_is_set()
    {
        // The sentence is shared when nobody says otherwise. Emitting an empty role for the standing
        // that did not declare one would put a blank line where the panel describes the component.
        var identity = new ComponentIdentity(
            ComponentKind.Either, "x", "X", "kgsm-x.service", "One sentence.",
            OnDemand: false, ApplyMode: "restart", ReadOnly: false, ReadOnlyReason: null);

        Assert.Equal("One sentence.", identity.RoleFor(ComponentKind.Leaf));
        Assert.Equal("One sentence.", identity.RoleFor(ComponentKind.Anchor));
    }

    [Fact]
    public void A_component_that_claims_neither_is_refused()
    {
        // The generator is pointed at its own assembly, which carries no identity attribute at all.
        GenException ex = Assert.Throws<GenException>(
            () => ComponentDescriptorFactory.Build(typeof(Emitter).Assembly.Location, Fixture.Settings));

        Assert.Contains("none of", ex.Message);
    }

    [Fact]
    public void A_leaf_that_drives_no_backend_emits_no_gpu_key()
    {
        // Absent, not an empty array: an empty one reads as a leaf that reaches a card and spends
        // nothing on it, which is a measurement this file never makes.
        Descriptor descriptor = Build().Descriptor with { GpuBackendUnits = [] };

        Assert.DoesNotContain("gpuBackendUnits", Emitter.Render(descriptor));
    }

    [Fact]
    public void An_open_ended_key_namespace_is_exempt_from_coverage()
    {
        // Logging__LogLevel__Microsoft.AspNetCore is in the settings file and described by nothing.
        // Without the declared exemption the build would fail on a filter the leaf legitimately honours.
        Assert.Contains(Build().Descriptor.FrameworkNamespaces, n => n.Prefix == "Logging__");
    }
}
