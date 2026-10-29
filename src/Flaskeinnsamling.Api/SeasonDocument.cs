using System.Text.Json.Serialization;

namespace Flaskeinnsamling.Api;

/// <summary>
/// Same shape as data/&lt;season&gt;.json. Unknown JSON properties (e.g. musicians, addresses,
/// birth dates) are ignored on deserialization and therefore never persisted.
/// </summary>
public sealed record SeasonDocument
{
    public string? Season { get; init; }
    public List<string>? Collections { get; init; }
    public Dictionary<string, string>? Handouts { get; init; }
    public Dictionary<string, string>? Notices { get; init; }
    public List<string>? Contacts { get; init; }
    public string? Vipps { get; init; }

    /// <summary>Rode → [addresses, dwellings]. Admin-only; omitted from public reads.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, int[]>? Housing { get; init; }

    /// <summary>Korps group (e.g. Mellomkorps) → musician names.</summary>
    public Dictionary<string, List<string>>? Korps { get; init; }

    /// <summary>Rode → names.</summary>
    public Dictionary<string, List<string>>? Assignments { get; init; }

    /// <summary>Date (yyyy-MM-dd) → slot (HH:mm-HH:mm) → names.</summary>
    public Dictionary<string, Dictionary<string, List<string>>>? Reception { get; init; }
}
