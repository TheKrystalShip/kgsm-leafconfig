using Microsoft.Extensions.Logging.Abstractions;

using TheKrystalShip.KGSM.ComponentSurface;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// A unit's own configuration, read the way systemd reads it.
/// </summary>
/// <remarks>
/// <para>
/// A component's floor is mostly its unit: <c>Environment=</c> lines and the files they pull in. Reading
/// it any other way than systemd does reports a value that is not in force — the worst thing a
/// provenance view can do, because it looks like an answer.
/// </para>
/// <para>
/// Two rules do the work. <b>One fragment</b>: systemd takes the unit file from the highest-precedence
/// root and shadows the rest, so merging them all would carry values out of a unit that is not running.
/// <b>Drop-ins merge across roots by filename</b>: every <c>&lt;unit&gt;.d/*.conf</c> applies, ordered by
/// name, with a higher root's file of the same name replacing a lower one's rather than being applied
/// beside it.
/// </para>
/// </remarks>
public class UnitFloorTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "unit-floor-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string Unit = "kgsm-sample.service";

    /// <summary>The floor as read from a unit directory this test owns, which is what UnitDirectory is for.</summary>
    private ComponentFloor Read()
    {
        var options = new ComponentSurfaceOptions(
            Path.Combine(_root, "descriptor.json"),
            Path.Combine(_root, "overrides.env"),
            CommandsPath: null,
            UnitDirectory: _root);

        return new ComponentFloorReader(options, NullLogger<ComponentFloorReader>.Instance)
            .Read([new ComponentFloorSource("systemd-unit", Unit)]);
    }

    private void WriteUnit(string body) => File.WriteAllText(Path.Combine(_root, Unit), body);

    private void WriteDropIn(string name, string body)
    {
        string dir = Directory.CreateDirectory(Path.Combine(_root, Unit + ".d")).FullName;
        File.WriteAllText(Path.Combine(dir, name), body);
    }

    // ── Environment= ────────────────────────────────────────────────────────

    [Fact]
    public void An_environment_line_sets_its_key()
    {
        WriteUnit("[Service]\nEnvironment=Sample__Port=8080\n");

        Assert.Equal("8080", Read().Values["Sample__Port"]);
    }

    /// <summary>
    /// systemd allows several assignments on one line. Reading only the first silently drops every key
    /// after it, and the panel reports those as unset.
    /// </summary>
    [Fact]
    public void Several_assignments_on_one_line_all_count()
    {
        WriteUnit("[Service]\nEnvironment=Sample__Port=8080 Sample__Host=localhost Sample__Debug=true\n");

        ComponentFloor floor = Read();
        Assert.Equal("8080", floor.Values["Sample__Port"]);
        Assert.Equal("localhost", floor.Values["Sample__Host"]);
        Assert.Equal("true", floor.Values["Sample__Debug"]);
    }

    [Fact]
    public void A_quoted_value_containing_a_space_stays_whole()
    {
        WriteUnit("""
            [Service]
            Environment="Sample__Role=the quick brown fox" Sample__Port=8080
            """);

        ComponentFloor floor = Read();
        Assert.Equal("the quick brown fox", floor.Values["Sample__Role"]);
        Assert.Equal("8080", floor.Values["Sample__Port"]);
    }

    [Fact]
    public void A_comment_sets_nothing()
    {
        WriteUnit("[Service]\n# Environment=Sample__Port=1\n; Environment=Sample__Port=2\n");

        Assert.DoesNotContain("Sample__Port", Read().Values.Keys);
    }

    // ── EnvironmentFile= ────────────────────────────────────────────────────

    [Fact]
    public void An_environment_file_the_unit_names_is_followed()
    {
        string env = Path.Combine(_root, "sample.env");
        File.WriteAllText(env, "Sample__Port=9001\nexport Sample__Host=elsewhere\n");
        WriteUnit($"[Service]\nEnvironmentFile=-{env}\n");

        ComponentFloor floor = Read();
        Assert.Equal("9001", floor.Values["Sample__Port"]);
        // Written to be sourceable by a shell as well as read by systemd.
        Assert.Equal("elsewhere", floor.Values["Sample__Host"]);
    }

    /// <summary>
    /// The override file is the tier ABOVE the floor. Counting it here would report every value the
    /// Control Panel set as something the host's own deploy had configured.
    /// </summary>
    [Fact]
    public void The_override_file_is_not_part_of_the_floor()
    {
        string overridePath = Path.Combine(_root, "overrides.env");
        File.WriteAllText(overridePath, "Sample__Port=7777\n");
        WriteUnit($"[Service]\nEnvironmentFile=-{overridePath}\n");

        Assert.DoesNotContain("Sample__Port", Read().Values.Keys);
    }

    // ── precedence ──────────────────────────────────────────────────────────

    [Fact]
    public void A_drop_in_wins_over_the_unit_it_applies_to()
    {
        WriteUnit("[Service]\nEnvironment=Sample__Port=8080\n");
        WriteDropIn("50-override.conf", "[Service]\nEnvironment=Sample__Port=9999\n");

        Assert.Equal("9999", Read().Values["Sample__Port"]);
    }

    [Fact]
    public void Drop_ins_apply_in_filename_order()
    {
        WriteUnit("[Service]\nEnvironment=Sample__Port=1\n");
        WriteDropIn("10-first.conf", "[Service]\nEnvironment=Sample__Port=2\n");
        WriteDropIn("90-last.conf", "[Service]\nEnvironment=Sample__Port=3\n");

        Assert.Equal("3", Read().Values["Sample__Port"]);
    }

    // ── what it will not claim ──────────────────────────────────────────────

    [Fact]
    public void A_unit_that_is_nowhere_is_an_incomplete_floor()
    {
        // Nothing written. Its Environment= lines are unknown rather than absent, and a surface must
        // not report a coded default as what the component is running with.
        ComponentFloor floor = Read();

        Assert.False(floor.Complete);
        Assert.Empty(floor.Values);
    }

    [Fact]
    public void A_unit_that_is_there_is_a_complete_floor()
    {
        WriteUnit("[Service]\nEnvironment=Sample__Port=8080\n");

        Assert.True(Read().Complete);
    }
}
