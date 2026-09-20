using System.Text.Json.Serialization;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// The descriptor as it sits on disk, all-nullable so a missing key is a named refusal rather than a
/// deserialization exception. Unknown properties are ignored — the format is additive, and a reader
/// that failed on a key a later generator added would break every component that upgraded first.
/// </summary>
internal sealed record RawComponentDescriptor(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("onDemand")] bool OnDemand,
    [property: JsonPropertyName("applyMode")] string? ApplyMode,
    [property: JsonPropertyName("readOnly")] bool ReadOnly,
    [property: JsonPropertyName("readOnlyReason")] string? ReadOnlyReason,
    [property: JsonPropertyName("floorSources")] IReadOnlyList<RawFloorSource>? FloorSources,
    [property: JsonPropertyName("groups")] IReadOnlyList<RawComponentGroup>? Groups,
    [property: JsonPropertyName("fields")] IReadOnlyList<RawComponentField>? Fields);

internal sealed record RawFloorSource(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("path")] string? Path);

internal sealed record RawComponentGroup(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("order")] int Order);

internal sealed record RawComponentField(
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("env")] string? Env,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("default")] string? Default,
    [property: JsonPropertyName("values")] IReadOnlyList<string>? Values,
    [property: JsonPropertyName("min")] double? Min,
    [property: JsonPropertyName("max")] double? Max,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("risk")] string? Risk,
    [property: JsonPropertyName("pairedApiKey")] string? PairedApiKey,
    [property: JsonPropertyName("dependsOn")] string? DependsOn);

/// <summary>
/// Source-generated reading for the descriptor, so a Native-AOT component gains no reflection from
/// serving its own surface — which is the whole reason the attributes are compiled in as source and
/// read back out of process in the first place.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(RawComponentDescriptor))]
internal sealed partial class ComponentDescriptorJson : JsonSerializerContext;
