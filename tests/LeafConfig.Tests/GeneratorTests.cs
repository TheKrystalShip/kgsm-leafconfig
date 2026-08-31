using TheKrystalShip.KGSM.LeafConfig.Gen;
using Xunit;

namespace TheKrystalShip.KGSM.LeafConfig.Tests;

/// <summary>
/// The generator against a real compiled leaf — the fixture in tests/Fixtures/SampleLeaf, read the
/// same way a live leaf is read: from its assembly's metadata, with nothing loaded for execution.
/// </summary>
public class GeneratorTests
{
    private static BuildResult Build() => LeafDescriptorFactory.Build(Fixture.Assembly, Fixture.Settings);

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
            () => LeafDescriptorFactory.Build(Fixture.MissingSectionAssembly, Fixture.Settings));

        Assert.Contains("names an assembly that is not beside the leaf", ex.Message);
    }

    // ── Leaf-level ───────────────────────────────────────────────────────────

    [Fact]
    public void The_leaf_identity_is_read_from_the_assembly()
    {
        LeafIdentity identity = Build().Descriptor.Identity;

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

    [Fact]
    public void An_anchor_says_so_and_a_leaf_says_nothing()
    {
        // A cluster anchor serves one capability to the whole cluster and is a peer of the node it
        // sits beside, so it belongs on no node's service board. The board reads this key to leave it
        // off; absence is what a node's leaf looks like, and the sample fixture is one.
        Descriptor leaf = Build().Descriptor;
        Assert.False(leaf.Identity.Anchor);
        Assert.DoesNotContain("\"anchor\"", Emitter.Render(leaf));

        Descriptor anchor = leaf with { Identity = leaf.Identity with { Anchor = true } };
        Assert.Contains("\"anchor\": true", Emitter.Render(anchor));
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
