using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace Flaskeinnsamling.Api;

// Table schema — PartitionKey is always the season id (e.g. "2026-27").
//   Seasons     RowKey = season id; Collections, Handouts, Notices, Contacts, Korps (JSON strings), Vipps,
//               SeedHash (SHA-256 of the data/<season>.json last imported; null if never seeded)
//   Housing     RowKey = rode;      Addresses (int), Dwellings (int)
//   Assignments RowKey = rode;      Names (JSON array string)
//   Reception   RowKey = "{date}_{slot}"; Date, Slot, Names (JSON array string)

public sealed class SeasonEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Collections { get; set; } = "[]";
    public string Handouts { get; set; } = "{}";
    public string Notices { get; set; } = "{}";
    public string Contacts { get; set; } = "[]";
    public string Korps { get; set; } = "{}";
    public string Vipps { get; set; } = "";
    public string? SeedHash { get; set; }
}

public sealed class HousingEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int Addresses { get; set; }
    public int Dwellings { get; set; }
}

public sealed class AssignmentEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Names { get; set; } = "[]";
}

public sealed class ReceptionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Date { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Names { get; set; } = "[]";
}

public sealed class SeasonStore(TableServiceClient service)
{
    public const string SeasonsTable = "Seasons";
    public const string HousingTable = "Housing";
    public const string AssignmentsTable = "Assignments";
    public const string ReceptionTable = "Reception";

    private static readonly string[] AllTables = [SeasonsTable, HousingTable, AssignmentsTable, ReceptionTable];
    /// <summary>Serializer for the JSON string columns; also used by <see cref="Validation"/> to enforce column size limits.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TableClient Seasons => service.GetTableClient(SeasonsTable);
    private TableClient Housing => service.GetTableClient(HousingTable);
    private TableClient Assignments => service.GetTableClient(AssignmentsTable);
    private TableClient Reception => service.GetTableClient(ReceptionTable);

    public async Task EnsureTablesAsync(CancellationToken ct = default)
    {
        foreach (var name in AllTables)
            await service.GetTableClient(name).CreateIfNotExistsAsync(ct);
    }

    public async Task<List<string>> ListSeasonsAsync(CancellationToken ct = default)
    {
        var seasons = new List<string>();
        await foreach (var e in Seasons.QueryAsync<TableEntity>(select: ["RowKey"], cancellationToken: ct))
            seasons.Add(e.RowKey);
        seasons.Sort(StringComparer.Ordinal);
        return seasons;
    }

    public async Task<bool> SeasonExistsAsync(string season, CancellationToken ct = default)
    {
        var e = await Seasons.GetEntityIfExistsAsync<SeasonEntity>(season, season, cancellationToken: ct);
        return e.HasValue;
    }

    /// <summary>Returns whether the season exists and the hash of the seed file it was last imported from.</summary>
    public async Task<(bool Exists, string? SeedHash)> GetSeedStateAsync(string season, CancellationToken ct = default)
    {
        var e = await Seasons.GetEntityIfExistsAsync<SeasonEntity>(season, season, select: ["RowKey", "SeedHash"], cancellationToken: ct);
        return e.HasValue && e.Value is { } s ? (true, s.SeedHash) : (false, null);
    }

    public async Task<SeasonDocument?> GetSeasonAsync(string season, CancellationToken ct = default)
    {
        var response = await Seasons.GetEntityIfExistsAsync<SeasonEntity>(season, season, cancellationToken: ct);
        if (!response.HasValue || response.Value is not { } s) return null;

        var housing = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        await foreach (var h in Housing.QueryAsync<HousingEntity>(x => x.PartitionKey == season, cancellationToken: ct))
            housing[h.RowKey] = [h.Addresses, h.Dwellings];

        return new SeasonDocument
        {
            Season = season,
            Collections = Deserialize<List<string>>(s.Collections) ?? [],
            Handouts = Deserialize<Dictionary<string, string>>(s.Handouts) ?? [],
            Notices = Deserialize<Dictionary<string, string>>(s.Notices) ?? [],
            Contacts = Deserialize<List<string>>(s.Contacts) ?? [],
            Vipps = s.Vipps,
            Housing = new Dictionary<string, int[]>(housing),
            Korps = Deserialize<Dictionary<string, List<string>>>(s.Korps) ?? [],
            Assignments = await GetAssignmentsAsync(season, ct),
            Reception = await GetReceptionAsync(season, ct),
        };
    }

    public async Task<Dictionary<string, List<string>>> GetAssignmentsAsync(string season, CancellationToken ct = default)
    {
        var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        await foreach (var a in Assignments.QueryAsync<AssignmentEntity>(x => x.PartitionKey == season, cancellationToken: ct))
            result[a.RowKey] = Deserialize<List<string>>(a.Names) ?? [];
        return new Dictionary<string, List<string>>(result);
    }

    public async Task<Dictionary<string, Dictionary<string, List<string>>>> GetReceptionAsync(string season, CancellationToken ct = default)
    {
        var rows = new List<ReceptionEntity>();
        await foreach (var r in Reception.QueryAsync<ReceptionEntity>(x => x.PartitionKey == season, cancellationToken: ct))
            rows.Add(r);

        var result = new Dictionary<string, Dictionary<string, List<string>>>();
        foreach (var r in rows.OrderBy(r => r.Date, StringComparer.Ordinal).ThenBy(r => r.Slot, StringComparer.Ordinal))
        {
            if (!result.TryGetValue(r.Date, out var slots))
                result[r.Date] = slots = [];
            slots[r.Slot] = Deserialize<List<string>>(r.Names) ?? [];
        }
        return result;
    }

    public async Task SetAssignmentAsync(string season, string rode, List<string> names, CancellationToken ct = default)
    {
        if (names.Count == 0)
        {
            await Assignments.DeleteEntityAsync(season, rode, ETag.All, ct);
            return;
        }
        await Assignments.UpsertEntityAsync(NewAssignment(season, rode, names), TableUpdateMode.Replace, ct);
    }

    public async Task SetReceptionAsync(string season, string date, string slot, List<string> names, CancellationToken ct = default)
    {
        var rowKey = ReceptionRowKey(date, slot);
        if (names.Count == 0)
        {
            await Reception.DeleteEntityAsync(season, rowKey, ETag.All, ct);
            return;
        }
        await Reception.UpsertEntityAsync(NewReception(season, date, slot, names), TableUpdateMode.Replace, ct);
    }

    /// <summary>
    /// Admin import: replaces every row of a season but keeps the stored SeedHash, so an unchanged
    /// seed file does not overwrite the admin's data on the next startup while a changed one does.
    /// </summary>
    public async Task ReplaceSeasonAsync(SeasonDocument doc, CancellationToken ct = default)
    {
        var (_, seedHash) = await GetSeedStateAsync(doc.Season!, ct);
        await WriteSeasonAsync(doc, seedHash, ct);
    }

    /// <summary>Seed import from data/&lt;season&gt;.json; records the file hash.</summary>
    public Task ImportSeedAsync(SeasonDocument doc, string seedHash, CancellationToken ct = default) =>
        WriteSeasonAsync(doc, seedHash, ct);

    /// <summary>
    /// Upserts the new rows first and only then deletes rows that are no longer present, so readers
    /// never see an empty season and a failure midway leaves the old data mostly intact. The Seasons
    /// row (with SeedHash) is written last so a half-finished seed is retried on the next startup.
    /// </summary>
    private async Task WriteSeasonAsync(SeasonDocument doc, string? seedHash, CancellationToken ct)
    {
        var season = doc.Season!;

        var housing = (doc.Housing ?? []).Select(h => new HousingEntity
        {
            PartitionKey = season,
            RowKey = h.Key,
            Addresses = h.Value[0],
            Dwellings = h.Value[1],
        }).ToList();
        var assignments = (doc.Assignments ?? []).Select(a => NewAssignment(season, a.Key, a.Value)).ToList();
        var reception = (doc.Reception ?? [])
            .SelectMany(d => d.Value.Select(s => NewReception(season, d.Key, s.Key, s.Value))).ToList();

        await UpsertAsync(Housing, housing, ct);
        await UpsertAsync(Assignments, assignments, ct);
        await UpsertAsync(Reception, reception, ct);

        await DeleteStaleAsync(Housing, season, housing.Select(e => e.RowKey), ct);
        await DeleteStaleAsync(Assignments, season, assignments.Select(e => e.RowKey), ct);
        await DeleteStaleAsync(Reception, season, reception.Select(e => e.RowKey), ct);

        await Seasons.UpsertEntityAsync(new SeasonEntity
        {
            PartitionKey = season,
            RowKey = season,
            Collections = JsonSerializer.Serialize(doc.Collections ?? [], Json),
            Handouts = JsonSerializer.Serialize(doc.Handouts ?? [], Json),
            Notices = JsonSerializer.Serialize(doc.Notices ?? [], Json),
            Contacts = JsonSerializer.Serialize(doc.Contacts ?? [], Json),
            Korps = JsonSerializer.Serialize(doc.Korps ?? [], Json),
            Vipps = doc.Vipps ?? "",
            SeedHash = seedHash,
        }, TableUpdateMode.Replace, ct);
    }

    public static string ReceptionRowKey(string date, string slot) => $"{date}_{slot}";

    private static AssignmentEntity NewAssignment(string season, string rode, List<string> names) => new()
    {
        PartitionKey = season,
        RowKey = rode,
        Names = JsonSerializer.Serialize(names, Json),
    };

    private static ReceptionEntity NewReception(string season, string date, string slot, List<string> names) => new()
    {
        PartitionKey = season,
        RowKey = ReceptionRowKey(date, slot),
        Date = date,
        Slot = slot,
        Names = JsonSerializer.Serialize(names, Json),
    };

    private static async Task DeleteStaleAsync(TableClient table, string partitionKey, IEnumerable<string> keep, CancellationToken ct)
    {
        var keepSet = keep.ToHashSet(StringComparer.Ordinal);
        var actions = new List<TableTransactionAction>();
        await foreach (var e in table.QueryAsync<TableEntity>(x => x.PartitionKey == partitionKey, select: ["PartitionKey", "RowKey"], cancellationToken: ct))
        {
            if (!keepSet.Contains(e.RowKey))
                actions.Add(new TableTransactionAction(TableTransactionActionType.Delete, e, ETag.All));
        }
        await SubmitAsync(table, actions, ct);
    }

    private static Task UpsertAsync<T>(TableClient table, IEnumerable<T> entities, CancellationToken ct) where T : ITableEntity =>
        SubmitAsync(table, entities.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e)).ToList(), ct);

    // Entity group transactions are limited to 100 operations within a single partition.
    private static async Task SubmitAsync(TableClient table, List<TableTransactionAction> actions, CancellationToken ct)
    {
        foreach (var chunk in actions.Chunk(100))
            await table.SubmitTransactionAsync(chunk, ct);
    }

    private static T? Deserialize<T>(string? json) where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json, Json);
}
