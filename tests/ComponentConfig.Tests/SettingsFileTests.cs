using TheKrystalShip.KGSM.ComponentConfig.Gen;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// The settings file is where every field's default comes from, flattened the way
/// <c>IConfiguration</c> maps environment variables. It is the same artifact the leaf loads at
/// runtime, which is what stops a descriptor claiming a default the leaf does not start with.
/// </summary>
public class SettingsFileTests
{
    private static IReadOnlyDictionary<string, string?> Flatten(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"leafcfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            return SettingsFile.Flatten(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Nesting_flattens_to_the_env_separator()
    {
        var flat = Flatten("""{ "A": { "B": { "C": 1 } } }""");

        Assert.Equal("1", flat["A__B__C"]);
    }

    [Fact]
    public void Scalars_render_the_way_a_variable_would_carry_them()
    {
        var flat = Flatten("""{ "S": { "N": 15000, "T": true, "F": false, "Str": "x", "Blank": "" } }""");

        Assert.Equal("15000", flat["S__N"]);
        Assert.Equal("true", flat["S__T"]);
        Assert.Equal("false", flat["S__F"]);
        Assert.Equal("x", flat["S__Str"]);
        Assert.Equal("", flat["S__Blank"]);
    }

    [Fact]
    public void A_json_null_is_a_declared_key_with_no_default()
    {
        // Distinct from blank: the key exists, so an override binds to it, but the leaf has nothing to
        // fall back to. Rendering it as "" would publish a default it never had.
        var flat = Flatten("""{ "S": { "P": null } }""");

        Assert.True(flat.ContainsKey("S__P"));
        Assert.Null(flat["S__P"]);
    }

    [Fact]
    public void An_array_is_addressed_by_index()
    {
        // IConfiguration addresses an element by index, so that is what a variable would have to spell.
        var flat = Flatten("""{ "S": { "Hosts": ["a", "b"] } }""");

        Assert.Equal("a", flat["S__Hosts__0"]);
        Assert.Equal("b", flat["S__Hosts__1"]);
        Assert.False(flat.ContainsKey("S__Hosts"));
    }

    [Fact]
    public void An_empty_array_declares_no_key()
    {
        // Right, and load-bearing: an empty allow-list is a declared property with nothing in it, so
        // there is no key to override, and none to demand a description for.
        var flat = Flatten("""{ "S": { "Hosts": [] } }""");

        Assert.Empty(flat);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_accepted()
    {
        // Microsoft.Extensions.Configuration's own JSON provider accepts both, and these files carry
        // explanatory headers. Rejecting them here would report a leaf's whole floor as unknown over
        // punctuation.
        var flat = Flatten("""
            {
              // why this leaf is configured the way it is
              "S": { "N": 1, }
            }
            """);

        Assert.Equal("1", flat["S__N"]);
    }

    [Fact]
    public void A_missing_file_is_named()
    {
        GenException ex = Assert.Throws<GenException>(() => SettingsFile.Flatten("/nonexistent/x.json"));

        Assert.Contains("the settings file is missing", ex.Message);
    }
}
