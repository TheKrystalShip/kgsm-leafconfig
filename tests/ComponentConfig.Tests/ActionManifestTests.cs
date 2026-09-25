using TheKrystalShip.KGSM.ComponentConfig.Gen;
using TheKrystalShip.KGSM.ComponentSurface;
using Xunit;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// The action manifest, generated from a real compiled component — and the build that fails when the
/// component's declarations and its code disagree.
/// </summary>
public class ActionManifestTests
{
    private static BuildResult Reactor() =>
        ComponentDescriptorFactory.Build(Fixture.ReactorAssembly, Fixture.ReactorSettings);

    private static GenException Careless() =>
        Assert.Throws<GenException>(() => ComponentDescriptorFactory.Build(Fixture.CarelessAssembly, Fixture.CarelessSettings));

    [Fact]
    public void The_reactor_is_described_by_the_reference_manifest()
    {
        BuildResult result = Reactor();

        string expected = File.ReadAllText(Fixture.Golden("reactor.leaf.actions.json"));
        string actual = ActionManifestEmitter.Render(result.Actions, result.Descriptor.Identity, ComponentKind.Leaf);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void The_manifest_sits_beside_the_descriptor_with_its_suffix()
    {
        Assert.Equal("deploy/kgsm-reactor.leaf.actions.json", ActionManifestEmitter.PathFor("deploy/kgsm-reactor.leaf.json"));
        Assert.Equal("deploy/kgsm-auth.anchor.actions.json", ActionManifestEmitter.PathFor("deploy/kgsm-auth.anchor.json"));
    }

    [Fact]
    public void A_call_through_a_lambda_and_a_concrete_class_is_found_and_covered_by_its_type()
    {
        // Sweeper calls Engine.RestartAsync, not IEngine's, from inside a lambda. Were either hop missed
        // the requirement would still be declared — by the type — but the careless build below would
        // pass for Sweeper, which the next test rules out.
        Assert.Single(Reactor().Actions.Requires);
    }

    // ── Taking the declarations away ─────────────────────────────────────────

    [Fact]
    public void Removing_the_action_from_its_handler_fails_the_build()
    {
        GenException e = Careless();

        Assert.Contains("'reactor:rules.write' is named by", e.Message);
        Assert.Contains("SampleReactor.ReactorActions.RulesWrite", e.Message);
        Assert.Contains("SampleReactor.RulesEndpoint.Write", e.Message);
    }

    [Fact]
    public void Removing_a_requirement_fails_the_build_at_every_call_it_covered()
    {
        GenException e = Careless();

        Assert.Contains("SampleReactor.Restarter.OnCrashAsync calls a method that performs 'kgsm:server.restart'", e.Message);
        Assert.Contains("SampleReactor.Sweeper.Run calls a method that performs 'kgsm:server.restart'", e.Message);
    }

    [Fact]
    public void A_call_that_performs_nothing_needs_nothing()
    {
        Assert.DoesNotContain("Restarter.Look", Careless().Message);
    }

    [Fact]
    public void A_call_made_for_a_person_is_covered_by_the_check_that_names_its_action()
    {
        // StartEndpoint checks the person for kgsm:server.start — in its body, and in a policy
        // attribute — and requires nothing. It stays covered with every declaration removed, and adds
        // no requirement to the service account's manifest.
        Assert.DoesNotContain("StartEndpoint", Careless().Message);
        Assert.DoesNotContain(Reactor().Actions.Requires, r => r.Action == "kgsm:server.start");
    }

    [Fact]
    public void An_automating_setting_that_defaults_on_fails_the_build()
    {
        GenException e = Assert.Throws<GenException>(
            () => ComponentDescriptorFactory.Build(Fixture.ReactorAssembly, Fixture.EnforcingSettings));

        Assert.Contains("enforce is [Automates] and defaults to 'true'", e.Message);
    }

    [Fact]
    public void An_automating_setting_is_marked_in_the_descriptor()
    {
        FieldDef enforce = Reactor().Descriptor.Fields.Single(f => f.Key == "enforce");

        Assert.True(enforce.Automates);
        Assert.Contains("\"automates\": true", Emitter.Render(Reactor().Descriptor));
    }

    // ── Standing, namespace and assembly-level declarations ──────────────────

    [Fact]
    public void The_standard_surface_is_administered_where_the_component_is()
    {
        BuildResult either = ComponentDescriptorFactory.Build(Fixture.EitherAssembly, Fixture.EitherSettings);

        string leaf = ActionManifestEmitter.Render(either.Actions, either.Descriptor.Identity, ComponentKind.Leaf);
        string anchor = ActionManifestEmitter.Render(either.Actions, either.Descriptor.Identity, ComponentKind.Anchor);

        Assert.Contains("\"id\": \"config.write\",\n      \"title\": \"Change Sample either settings\",\n      \"effect\": \"write\",\n      \"scope\": \"node\"", leaf);
        Assert.Contains("\"id\": \"config.write\",\n      \"title\": \"Change Sample either settings\",\n      \"effect\": \"write\",\n      \"scope\": \"cluster\"", anchor);
    }

    [Fact]
    public void A_declared_namespace_names_the_component_and_a_full_id_is_read_as_its_local_half()
    {
        ActionSurface actions = ComponentDescriptorFactory.Build(Fixture.EitherAssembly, Fixture.EitherSettings).Actions;

        Assert.Equal("either", actions.Component);
        Assert.Contains(actions.Actions, a => a.Id == "history.personal-fields" && a.Effect == "read");
    }

    [Fact]
    public void A_self_action_says_so_and_nothing_else_does()
    {
        BuildResult either = ComponentDescriptorFactory.Build(Fixture.EitherAssembly, Fixture.EitherSettings);
        string rendered = ActionManifestEmitter.Render(either.Actions, either.Descriptor.Identity, ComponentKind.Leaf);

        Assert.Contains(either.Actions.Actions, a => a.Id == "preferences.write" && a.Self);
        Assert.Single(rendered.Split('\n'), l => l.Contains("\"self\""));
    }

    [Fact]
    public void The_generator_and_the_surface_agree_on_the_standard_actions() =>
        Assert.Equal(ComponentSurfaceActions.All, Standard.Ids);
}
