using System.Security.Cryptography;
using System.Text.Json;

namespace Flaskeinnsamling.Api;

/// <summary>
/// Creates the tables and imports each data/&lt;season&gt;.json whose SHA-256 differs from the SeedHash
/// stored on the season (or whose season is missing). The repo file wins when it changes; an
/// unchanged file never overwrites admin edits, because admin imports keep the stored SeedHash.
/// </summary>
public sealed class SeedService(SeasonStore store, IConfiguration configuration, ILogger<SeedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await SeedAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested && attempt < 10)
            {
                logger.LogWarning(ex, "Seeding failed (attempt {Attempt}); retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 2 * attempt)), stoppingToken);
            }
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        await store.EnsureTablesAsync(ct);

        var dir = configuration["Seed:Path"] ?? Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(dir))
        {
            logger.LogInformation("No seed directory at {Dir}", dir);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            var season = Path.GetFileNameWithoutExtension(file);
            if (!Validation.IsSeason(season)) continue;

            var bytes = await File.ReadAllBytesAsync(file, ct);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            var (exists, storedHash) = await store.GetSeedStateAsync(season, ct);
            if (exists && storedHash == hash)
            {
                logger.LogInformation("Season {Season} is up to date with its seed file; not seeding", season);
                continue;
            }

            var doc = JsonSerializer.Deserialize<SeasonDocument>(bytes, Json);
            var errors = new Dictionary<string, string[]>();
            var normalized = Validation.NormalizeSeason(season, doc, errors);
            if (normalized is null)
            {
                logger.LogError("Seed file for {Season} is invalid: {Fields}", season, string.Join(", ", errors.Keys));
                continue;
            }

            await store.ImportSeedAsync(normalized, hash, ct);
            if (exists) logger.LogInformation("Seed file for {Season} changed; re-imported season", season);
            else logger.LogInformation("Seeded season {Season}", season);
        }
    }
}
