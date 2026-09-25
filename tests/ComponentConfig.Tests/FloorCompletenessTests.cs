using Microsoft.Extensions.Logging.Abstractions;

using TheKrystalShip.Api.Contracts;
using TheKrystalShip.KGSM.ComponentConfig.Gen;
using TheKrystalShip.KGSM.ComponentSurface;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// "Nothing sets this" and "I could not read the thing that sets it" are different facts.
/// </summary>
/// <remarks>
/// <para>
/// A key absent from the floor means the coded default is what the component is running with — but only
/// when every declared source was actually read. A source that is there and unreadable makes that
/// inference a guess, and the value a surface would print is one nobody measured.
/// </para>
/// <para>
/// So the reader carries whether it got everything, and the projection spends it: with an incomplete
/// floor a field falls to <c>unknown</c> rather than to its default, and its effective value is withheld
/// rather than invented. This is the ecosystem's first invariant applied to configuration — measured, or
/// explicitly unknown, never plausible.
/// </para>
/// </remarks>
public class FloorCompletenessTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "floor-complete-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ComponentFloorReader Reader() =>
        new(new ComponentSurfaceOptions(
                Path.Combine(_dir, "descriptor.json"), Path.Combine(_dir, "overrides.env")),
            NullLogger<ComponentFloorReader>.Instance);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ── what counts as complete ─────────────────────────────────────────────

    [Fact]
    public void Every_source_read_is_a_complete_floor()
    {
        ComponentFloor floor = Reader().Read([
            new ComponentFloorSource("appsettings", Write("s.json", """{"Sample":{"Port":8080}}""")),
            new ComponentFloorSource("env-file", Write("e.env", "Sample__Path=/run/x.sock\n")),
        ]);

        Assert.True(floor.Complete);
        Assert.Equal("8080", floor.Values["Sample__Port"]);
        Assert.Equal("/run/x.sock", floor.Values["Sample__Path"]);
    }

    /// <summary>
    /// An absent file says nothing sets those keys, which is a measurement rather than a failure —
    /// systemd tolerates the same absence, and a component may genuinely ship without a settings file.
    /// </summary>
    [Theory]
    [InlineData("appsettings")]
    [InlineData("env-file")]
    public void A_source_that_is_simply_not_there_is_still_complete(string kind)
    {
        ComponentFloor floor = Reader().Read([
            new ComponentFloorSource(kind, Path.Combine(_dir, "nothing-here")),
        ]);

        Assert.True(floor.Complete);
        Assert.Empty(floor.Values);
    }

    // ── what makes it incomplete ────────────────────────────────────────────

    [Fact]
    public void A_settings_file_that_is_there_and_unparseable_is_incomplete()
    {
        ComponentFloor floor = Reader().Read([
            new ComponentFloorSource("appsettings", Write("broken.json", "{ this is not json")),
        ]);

        Assert.False(floor.Complete);
    }

    [Fact]
    public void A_file_that_is_there_and_unreadable_is_incomplete()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
            return;   // root reads it regardless, so there is nothing to observe

        string path = Write("locked.env", "Sample__Port=9001\n");
        File.SetUnixFileMode(path, UnixFileMode.None);

        ComponentFloor floor = Reader().Read([new ComponentFloorSource("env-file", path)]);

        Assert.False(floor.Complete);
        Assert.Empty(floor.Values);
    }

    [Fact]
    public void A_unit_found_nowhere_is_incomplete()
    {
        // Its Environment= lines are unknown rather than empty: the fact this reader wanted is in a
        // file it never saw.
        Assert.False(Reader()
            .Read([new ComponentFloorSource("systemd-unit", "kgsm-nothing-like-this.service")]).Complete);
    }

    [Fact]
    public void A_source_kind_this_build_does_not_know_is_incomplete()
    {
        // It contributes no values and cannot say they are absent — a newer descriptor naming a tier
        // this build cannot read is exactly the case where a default must not be reported as in force.
        Assert.False(Reader().Read([new ComponentFloorSource("something-new", "/etc/whatever")]).Complete);
    }

    [Fact]
    public void One_unreadable_source_does_not_discard_what_the_others_said()
    {
        ComponentFloor floor = Reader().Read([
            new ComponentFloorSource("appsettings", Write("good.json", """{"Sample":{"Port":8080}}""")),
            new ComponentFloorSource("appsettings", Write("bad.json", "{ nope")),
        ]);

        Assert.False(floor.Complete);
        Assert.Equal("8080", floor.Values["Sample__Port"]);
    }

    // ── what the projection does with it ────────────────────────────────────

    /// <summary>
    /// The whole point of carrying the fact: a field nothing sets reports its coded default, and the
    /// same field reports <c>unknown</c> with no effective value once a source could not be read.
    /// </summary>
    [Fact]
    public void An_incomplete_floor_reports_unknown_rather_than_the_coded_default()
    {
        ComponentConfigField nothingSetsIt = Served(Path.Combine(_dir, "absent.json"));
        Assert.Equal(ComponentConfigSource.Default, nothingSetsIt.Source);
        Assert.Equal("8080", nothingSetsIt.Effective);
        Assert.Null(nothingSetsIt.Floor);

        ComponentConfigField couldNotTell = Served(Write("unreadable.json", "{ nope"));
        Assert.Equal(ComponentConfigSource.Unknown, couldNotTell.Source);
        Assert.Null(couldNotTell.Effective);
        // The default is still reported beside it — resetting has to be able to say what it restores.
        Assert.Equal("8080", couldNotTell.Default);
    }

    /// <summary>
    /// A value a source DID supply stays measured, however the rest of the floor went. Only what was
    /// inferred from absence is withdrawn.
    /// </summary>
    [Fact]
    public void An_incomplete_floor_does_not_withdraw_a_value_it_actually_read()
    {
        string good = Write("good.json", """{"Sample":{"Port":9090}}""");
        string bad = Write("bad.json", "{ nope");

        ComponentConfigField port = Served(good, bad);

        Assert.Equal(ComponentConfigSource.Floor, port.Source);
        Assert.Equal("9090", port.Floor);
        Assert.Equal("9090", port.Effective);
    }

    /// <summary>
    /// The <c>port</c> field as the shipped projection serves it, with the fixture's declared floor
    /// sources replaced by the ones named here — so what is under test is
    /// <see cref="ComponentConfigService"/>'s own rule rather than a copy of it written for the test.
    /// </summary>
    private ComponentConfigField Served(params string[] settingsPaths)
    {
        Descriptor built = ComponentDescriptorFactory.Build(Fixture.Assembly, Fixture.Settings).Descriptor;
        string rendered = Emitter.Render(built);

        // The emitted document with its floorSources swapped for these, which is the one thing a test
        // cannot get from the fixture: where a component's deploy files sit is a property of the host.
        var node = System.Text.Json.Nodes.JsonNode.Parse(rendered)!.AsObject();
        var sources = new System.Text.Json.Nodes.JsonArray();
        foreach (string path in settingsPaths)
        {
            sources.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["kind"] = "appsettings",
                ["path"] = path,
            });
        }
        node["floorSources"] = sources;

        string descriptorPath = Path.Combine(_dir, "descriptor.json");
        File.WriteAllText(descriptorPath, node.ToJsonString());

        var options = new ComponentSurfaceOptions(descriptorPath, Path.Combine(_dir, "overrides.env"));
        var service = new ComponentConfigService(
            new ComponentDescriptorStore(options, NullLogger<ComponentDescriptorStore>.Instance),
            new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance),
            new ComponentFloorReader(options, NullLogger<ComponentFloorReader>.Instance),
            new ComponentUnitControl(NullLogger<ComponentUnitControl>.Instance),
            NullLogger<ComponentConfigService>.Instance);

        return service.Read()!.Fields.Single(f => f.Key == "port");
    }
}
