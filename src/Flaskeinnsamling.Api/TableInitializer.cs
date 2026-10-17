namespace Flaskeinnsamling.Api;

/// <summary>
/// Creates the tables on startup if they are missing. Existing rows are never touched: the API
/// (Table Storage) is the master for season data, and seasons are created and edited only through
/// the admin endpoints.
/// </summary>
public sealed class TableInitializer(SeasonStore store, ILogger<TableInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await store.EnsureTablesAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested && attempt < 10)
            {
                logger.LogWarning(ex, "Creating tables failed (attempt {Attempt}); retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 2 * attempt)), stoppingToken);
            }
        }
    }
}
