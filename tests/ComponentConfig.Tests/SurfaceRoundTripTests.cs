using Microsoft.Extensions.Logging.Abstractions;

using TheKrystalShip.Api.Contracts;
using TheKrystalShip.KGSM.ComponentConfig.Gen;
using TheKrystalShip.KGSM.ComponentSurface;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// The descriptor's round trip: what the generator writes is what the serving library reads back.
/// </summary>
/// <remarks>
/// <para>
/// This is the reason the reader lives in this repo. The format has one writer and the readers are
/// what every component's configuration page is rendered from — kept apart, the two drift on the first
/// key either of them learns about, and nothing fails when they do. Here the generator emits from a
/// real compiled fixture and the library parses the bytes it produced, so a key added to one and not
/// the other fails a test instead of reaching a panel as a control that does nothing.
/// </para>
/// <para>
/// The fixture is the same <c>SampleLeaf</c> the generator's own tests use, which is what makes this a
/// round trip rather than two readings of a file written for it.
/// </para>
/// </remarks>
public class SurfaceRoundTripTests : IDisposable
{
    // Under the run's own temp root (TestTempRoot redirects TMPDIR), so an abandoned directory is
    // swept with the rest rather than left on the machine.
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "surface-roundtrip-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string WriteDescriptor()
    {
        string path = Path.Combine(_dir, "sample.json");
        File.WriteAllText(path, Emitter.Render(ComponentDescriptorFactory.Build(Fixture.Assembly, Fixture.Settings).Descriptor));
        return path;
    }

    private ComponentSurfaceOptions Options() =>
        new(WriteDescriptor(), Path.Combine(_dir, "overrides.env"));

    private ComponentDescriptorStore Store(ComponentSurfaceOptions options) =>
        new(options, NullLogger<ComponentDescriptorStore>.Instance);

    private ComponentConfigService Service(ComponentSurfaceOptions options)
    {
        ComponentDescriptorStore store = Store(options);
        return new ComponentConfigService(
            store,
            new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance),
            new ComponentFloorReader(options, NullLogger<ComponentFloorReader>.Instance),
            new ComponentUnitControl(NullLogger<ComponentUnitControl>.Instance),
            NullLogger<ComponentConfigService>.Instance);
    }

    // ── the round trip itself ───────────────────────────────────────────────

    [Fact]
    public void Every_emitted_field_is_read_back()
    {
        Descriptor emitted = ComponentDescriptorFactory.Build(Fixture.Assembly, Fixture.Settings).Descriptor;
        ComponentDescriptor? read = Store(Options()).Current();

        Assert.NotNull(read);
        Assert.Equal(emitted.Identity.Id, read.Id);
        Assert.Equal(emitted.Identity.Unit, read.Unit);
        Assert.Equal(emitted.Identity.DisplayName, read.DisplayName);
        Assert.Equal(emitted.Identity.ApplyMode, read.ApplyMode);
        Assert.Equal(emitted.Identity.OnDemand, read.OnDemand);

        // Field for field, by key — a descriptor whose reader silently drops one is a panel missing a
        // control nobody knows is missing.
        Assert.Equal(
            emitted.Fields.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal),
            read.Fields.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal(
            emitted.Groups.Select(g => g.Id).OrderBy(k => k, StringComparer.Ordinal),
            read.Groups.Select(g => g.Id).OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal(
            emitted.FloorSources.Select(f => f.Kind + ":" + f.Path).OrderBy(k => k, StringComparer.Ordinal),
            read.FloorSources.Select(f => f.Kind + ":" + f.Path).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Each_fields_declarations_survive_the_trip()
    {
        Descriptor emitted = ComponentDescriptorFactory.Build(Fixture.Assembly, Fixture.Settings).Descriptor;
        ComponentDescriptor read = Store(Options()).Current()!;

        foreach (FieldDef wrote in emitted.Fields)
        {
            ComponentFieldDef? back = read.Field(wrote.Key);
            Assert.NotNull(back);
            Assert.Equal(wrote.Env, back.Env);
            Assert.Equal(wrote.Label, back.Label);
            Assert.Equal(wrote.Type, back.Type);
            Assert.Equal(wrote.Default, back.Default);
            Assert.Equal(wrote.Group, back.Group);
            Assert.Equal(wrote.Risk, back.Risk);
            Assert.Equal(wrote.Unit, back.Unit);
            Assert.Equal(wrote.Min, back.Min);
            Assert.Equal(wrote.Max, back.Max);
            Assert.Equal(wrote.Values, back.Values);
            Assert.Equal(wrote.PairedApiKey, back.PairedApiKey);
            Assert.Equal(wrote.DependsOn, back.DependsOn);
        }
    }

    [Fact]
    public void An_unknown_schema_version_is_refused_rather_than_guessed_at()
    {
        var options = new ComponentSurfaceOptions(
            Path.Combine(_dir, "future.json"), Path.Combine(_dir, "overrides.env"));
        File.WriteAllText(options.DescriptorPath,
            """{"schemaVersion":99,"id":"sample","unit":"x.service","fields":[]}""");

        Assert.Null(Store(options).Current());
    }

    [Fact]
    public void A_missing_descriptor_is_no_surface_rather_than_an_empty_one()
    {
        var options = new ComponentSurfaceOptions(
            Path.Combine(_dir, "nothing-here.json"), Path.Combine(_dir, "overrides.env"));

        Assert.Null(Store(options).Current());
        Assert.Null(Service(options).Read());
    }

    // ── the projection ──────────────────────────────────────────────────────

    [Fact]
    public void A_field_with_no_override_reports_its_coded_default_as_the_source()
    {
        ComponentConfigView view = Service(Options()).Read()!;
        ComponentConfigField port = view.Fields.Single(f => f.Key == "port");

        Assert.False(port.Overridden);
        Assert.Null(port.Value);
        Assert.Equal("8080", port.Default);
        Assert.Equal(ComponentConfigSource.Default, port.Source);
        Assert.Equal("8080", port.Effective);
    }

    [Fact]
    public void An_override_is_written_read_back_and_named_as_the_source()
    {
        ComponentSurfaceOptions options = Options();
        var store = new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance);

        store.Write(new Dictionary<string, string>(StringComparer.Ordinal) { ["Sample__Port"] = "9001" });

        ComponentConfigField port = Service(options).Read()!.Fields.Single(f => f.Key == "port");
        Assert.True(port.Overridden);
        Assert.Equal("9001", port.Value);
        Assert.Equal(ComponentConfigSource.Override, port.Source);
        Assert.Equal("9001", port.Effective);
        // The coded default is still reported beside it — resetting has to be able to say what it
        // would restore.
        Assert.Equal("8080", port.Default);
    }

    [Fact]
    public void Resetting_every_key_removes_the_file_rather_than_emptying_it()
    {
        ComponentSurfaceOptions options = Options();
        var store = new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance);

        store.Write(new Dictionary<string, string>(StringComparer.Ordinal) { ["Sample__Port"] = "9001" });
        Assert.True(File.Exists(options.OverridePath));

        store.Write(new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.False(File.Exists(options.OverridePath));
    }

    [Fact]
    public void A_value_cannot_smuggle_a_second_variable_in_on_a_newline()
    {
        ComponentSurfaceOptions options = Options();
        var store = new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance);

        store.Write(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sample__Port"] = "9001\nSample__Secret=stolen",
        });

        IReadOnlyDictionary<string, string> back = store.Read();
        Assert.Equal("9001Sample__Secret=stolen", back["Sample__Port"]);
        Assert.Single(back);
    }

    [Fact]
    public void The_override_file_is_not_readable_by_anybody_else()
    {
        if (!OperatingSystem.IsLinux())
            return;

        ComponentSurfaceOptions options = Options();
        new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance)
            .Write(new Dictionary<string, string>(StringComparer.Ordinal) { ["Sample__Port"] = "9001" });

        UnixFileMode mode = File.GetUnixFileMode(options.OverridePath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    // ── validation, which happens before anything is written ────────────────

    [Fact]
    public void A_key_the_component_does_not_declare_is_refused()
    {
        ComponentSurfaceOptions options = Options();
        (ComponentApplyOutcome? outcome, string? error) = Service(options).Apply(
            new ComponentConfigUpdate(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["nothingLikeThis"] = "1" }, null));

        Assert.Null(outcome);
        Assert.Contains("nothingLikeThis", error);
        Assert.False(File.Exists(options.OverridePath));
    }

    [Fact]
    public void A_value_outside_the_declared_bounds_is_refused_before_it_is_written()
    {
        ComponentSurfaceOptions options = Options();
        ComponentDescriptor descriptor = Store(options).Current()!;
        ComponentFieldDef bounded = descriptor.Fields.First(f => f.Type == "int" && f.Min is not null);

        (ComponentApplyOutcome? outcome, string? error) = Service(options).Apply(
            new ComponentConfigUpdate(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [bounded.Key] = ((long)bounded.Min! - 1).ToString(),
                },
                null));

        Assert.Null(outcome);
        Assert.Contains(bounded.Key, error);
        // Nothing reached the disk: a half-applied change would leave the component running on a set
        // nobody asked for.
        Assert.False(File.Exists(options.OverridePath));
    }

    [Fact]
    public void A_key_in_both_values_and_reset_is_refused_rather_than_resolved()
    {
        (ComponentApplyOutcome? outcome, string? error) = Service(Options()).Apply(
            new ComponentConfigUpdate(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["port"] = "9001" },
                ["port"]));

        Assert.Null(outcome);
        Assert.Contains("opposite things", error);
    }

    [Fact]
    public void A_non_numeric_value_for_an_int_is_refused()
    {
        (ComponentApplyOutcome? outcome, string? error) = Service(Options()).Apply(
            new ComponentConfigUpdate(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["port"] = "eighty-eighty" }, null));

        Assert.Null(outcome);
        Assert.Contains("whole number", error);
    }

    [Fact]
    public void A_value_outside_a_declared_enum_is_refused()
    {
        ComponentDescriptor descriptor = Store(Options()).Current()!;
        ComponentFieldDef choice = descriptor.Fields.First(f => f.Type == "enum" && f.Values is { Count: > 0 });

        (ComponentApplyOutcome? outcome, string? error) = Service(Options()).Apply(
            new ComponentConfigUpdate(
                new Dictionary<string, string>(StringComparer.Ordinal) { [choice.Key] = "not-one-of-them" }, null));

        Assert.Null(outcome);
        Assert.Contains(choice.Key, error);
    }

    // ── secrets ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_secret_is_reported_as_set_and_never_echoed()
    {
        ComponentSurfaceOptions options = Options();
        ComponentDescriptor descriptor = Store(options).Current()!;
        // Asserted rather than skipped past: a fixture that quietly lost its secret would turn this
        // into a test that passes without checking anything.
        ComponentFieldDef secret = Assert.Single(descriptor.Fields, f => f.IsSecret);

        new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance)
            .Write(new Dictionary<string, string>(StringComparer.Ordinal) { [secret.Env] = "hunter2andmore" });

        ComponentConfigField back = Service(options).Read()!.Fields.Single(f => f.Key == secret.Key);
        Assert.True(back.Overridden);
        Assert.Null(back.Value);
        Assert.Null(back.Effective);
        Assert.True(back.Set);
        Assert.Equal("more", back.Fingerprint);
    }
}
