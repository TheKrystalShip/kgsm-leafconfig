using Microsoft.Extensions.Logging.Abstractions;

using TheKrystalShip.KGSM.ComponentSurface;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// A floor value is compared against a coded default, so how it is spelled is load-bearing.
/// </summary>
/// <remarks>
/// <para>
/// Every tier of a component's configuration is a string by the time a surface renders it: the coded
/// default the generator wrote into the descriptor, the settings file this reads, the unit's
/// <c>Environment=</c> lines, the override file. A surface tells them apart by comparing them, and
/// decides from that which tier a value came from and whether a switch is on.
/// </para>
/// <para>
/// So a JSON boolean has to arrive spelled the way the other tiers spell one. Left to
/// <c>JsonElement.ToString()</c> it arrives as <c>"True"</c>, and a surface comparing that against a
/// default of <c>"true"</c> finds two different values where the component has one — drawing a switch
/// that is ON as off, and naming the wrong tier as the source.
/// </para>
/// </remarks>
public class FloorSpellingTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "floor-spelling-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private IReadOnlyDictionary<string, string> Read(string json)
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, json);

        var options = new ComponentSurfaceOptions(
            Path.Combine(_dir, "descriptor.json"), Path.Combine(_dir, "overrides.env"));

        return new ComponentFloorReader(options, NullLogger<ComponentFloorReader>.Instance)
            .Read([new ComponentFloorSource("appsettings", path)]).Values;
    }

    [Fact]
    public void A_json_boolean_is_spelled_the_way_every_other_tier_spells_one()
    {
        IReadOnlyDictionary<string, string> floor = Read(
            """{"Sample":{"Enabled":true,"Disabled":false}}""");

        Assert.Equal("true", floor["Sample__Enabled"]);
        Assert.Equal("false", floor["Sample__Disabled"]);
    }

    /// <summary>
    /// The spelling a component's own parser writes and the descriptor's default carries, which is
    /// what makes the comparison behind the provenance tiers meaningful.
    /// </summary>
    [Fact]
    public void A_floor_boolean_matches_the_default_the_descriptor_carries_for_the_same_value()
    {
        const string CodedDefault = "true";

        Assert.Equal(CodedDefault, Read("""{"Sample":{"Enabled":true}}""")["Sample__Enabled"]);
    }

    /// <summary>Numbers and strings keep the spelling the file gave them.</summary>
    [Fact]
    public void Everything_else_arrives_as_it_was_written()
    {
        IReadOnlyDictionary<string, string> floor = Read(
            """{"Sample":{"Port":8080,"Path":"/run/x.sock","Ratio":1.5}}""");

        Assert.Equal("8080", floor["Sample__Port"]);
        Assert.Equal("/run/x.sock", floor["Sample__Path"]);
        Assert.Equal("1.5", floor["Sample__Ratio"]);
    }

    /// <summary>
    /// A nested section flattens to the env name it binds through, which is the whole reason one table
    /// can hold a settings file and an env file at once.
    /// </summary>
    [Fact]
    public void A_nested_boolean_flattens_to_its_env_name()
    {
        Assert.Equal("false",
            Read("""{"Sample":{"Voice":{"Enabled":false}}}""")["Sample__Voice__Enabled"]);
    }
}
