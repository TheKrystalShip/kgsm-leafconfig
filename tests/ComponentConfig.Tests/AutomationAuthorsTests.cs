using Microsoft.Extensions.Logging.Abstractions;

using TheKrystalShip.KGSM.ComponentSurface;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>Who switched each automated behaviour on, kept beside the overrides.</summary>
public class AutomationAuthorsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "automation-authors-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ComponentAutomationAuthors Authors() =>
        new(new ComponentSurfaceOptions(Path.Combine(_dir, "desc.json"), Path.Combine(_dir, "overrides.env")),
            NullLogger<ComponentAutomationAuthors>.Instance);

    [Fact]
    public void The_config_view_names_who_switched_an_automation_on()
    {
        File.WriteAllText(Path.Combine(_dir, "desc.json"), """
            {
              "schemaVersion": 1, "id": "sample", "displayName": "Sample", "unit": "sample.service",
              "role": "A sample.", "onDemand": false, "applyMode": "restart", "floorSources": [],
              "fields": [
                { "key": "mode", "env": "Sample__Mode", "label": "Mode", "description": "Runs on its own.",
                  "type": "bool", "default": "false", "automates": true },
                { "key": "port", "env": "Sample__Port", "label": "Port", "description": "Where it listens.",
                  "type": "int", "default": "8080" }
              ]
            }
            """);
        var options = new ComponentSurfaceOptions(Path.Combine(_dir, "desc.json"), Path.Combine(_dir, "overrides.env"));
        var service = new ComponentConfigService(
            new ComponentDescriptorStore(options, NullLogger<ComponentDescriptorStore>.Instance),
            new ComponentOverrideStore(options, NullLogger<ComponentOverrideStore>.Instance),
            new ComponentFloorReader(options, NullLogger<ComponentFloorReader>.Instance),
            new ComponentUnitControl(options, NullLogger<ComponentUnitControl>.Instance),
            NullLogger<ComponentConfigService>.Instance,
            Authors());

        Authors().Record(["mode"], [], "usr_alice", Now);

        var fields = service.Read()!.Fields.ToDictionary(f => f.Key);
        Assert.True(fields["mode"].Automates);
        Assert.Equal("usr_alice", fields["mode"].AutomationAuthor);
        Assert.False(fields["port"].Automates);
        Assert.Null(fields["port"].AutomationAuthor);
    }

    [Fact]
    public void Setting_a_key_records_its_author_and_a_later_setter_replaces_them()
    {
        ComponentAutomationAuthors authors = Authors();
        authors.Record(["mode"], [], "usr_alice", Now);
        Assert.Equal("usr_alice", authors.AuthorOf("mode"));

        authors.Record(["mode"], [], "usr_bob", Now.AddMinutes(1));
        Assert.Equal("usr_bob", Authors().AuthorOf("mode"));
        Assert.Equal(Now.AddMinutes(1), Authors().Read()["mode"].Set);
    }

    [Fact]
    public void A_change_with_nobody_to_name_leaves_the_setting_with_no_author()
    {
        ComponentAutomationAuthors authors = Authors();
        authors.Record(["mode"], [], "usr_alice", Now);

        authors.Record(["mode"], [], author: null, Now);

        Assert.Null(authors.AuthorOf("mode"));
    }

    [Fact]
    public void Resetting_a_key_clears_its_author_and_the_last_one_removes_the_file()
    {
        ComponentAutomationAuthors authors = Authors();
        authors.Record(["mode", "cadence"], [], "usr_alice", Now);

        authors.Record([], ["mode"], "usr_bob", Now);
        Assert.Null(authors.AuthorOf("mode"));
        Assert.Equal("usr_alice", authors.AuthorOf("cadence"));

        authors.Record([], ["cadence"], "usr_bob", Now);
        Assert.False(File.Exists(authors.Path));
    }

    [Fact]
    public void The_file_is_owner_only()
    {
        ComponentAutomationAuthors authors = Authors();
        authors.Record(["mode"], [], "usr_alice", Now);

        if (OperatingSystem.IsLinux())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(authors.Path));
    }

    [Fact]
    public void An_unreadable_file_reads_as_nobody()
    {
        ComponentAutomationAuthors authors = Authors();
        File.WriteAllText(authors.Path, "{ not json");

        Assert.Null(authors.AuthorOf("mode"));
        Assert.Empty(authors.Read());
    }
}
