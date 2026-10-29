using System.Text.Encodings.Web;
using System.Text.Unicode;
using Flaskeinnsamling.Api;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureTableServiceClient("tables");

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_000_000);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));
builder.Services.AddSingleton<SeasonStore>();
builder.Services.AddHostedService<TableInitializer>();

var allowedOrigins = ReadOrigins(builder.Configuration);
var allowLocalhost = builder.Environment.IsDevelopment();
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(origin => allowedOrigins.Contains(origin) || (allowLocalhost && IsLocalOrigin(origin)))
    .WithMethods(HttpMethods.Get, HttpMethods.Put)
    .WithHeaders(AdminKeyMiddleware.HeaderName, "Content-Type")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseMiddleware<AdminKeyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "Flaskeinnsamling API. Se /api/seasons.");

var seasons = app.MapGroup("/api/seasons");

seasons.MapGet("/", async (SeasonStore store, CancellationToken ct) =>
    Results.Ok(await store.ListSeasonsAsync(ct)));

// Housing (mailbox counts) is admin-only: a valid X-Admin-Key includes it, no key omits it, a wrong key is 401.
seasons.MapGet("/{season}", async (string season, HttpRequest request, IConfiguration configuration, SeasonStore store, CancellationToken ct) =>
{
    if (!Validation.IsSeason(season)) return Invalid("season", "Season must be yyyy-yy.");
    var isAdmin = AdminKeyMiddleware.HasKey(request);
    if (isAdmin && !AdminKeyMiddleware.IsAuthorized(request, configuration)) return Results.Unauthorized();
    var doc = await store.GetSeasonAsync(season, ct);
    if (doc is null) return Results.NotFound();
    return Results.Ok(isAdmin ? doc : doc with { Housing = null });
});

seasons.MapPut("/{season}", async (string season, SeasonDocument document, SeasonStore store, CancellationToken ct) =>
{
    if (!Validation.IsSeason(season)) return Invalid("season", "Season must be yyyy-yy.");
    var errors = new Dictionary<string, string[]>();
    var normalized = Validation.NormalizeSeason(season, document, errors);
    if (normalized is null) return Results.ValidationProblem(errors);
    await store.ReplaceSeasonAsync(normalized, ct);
    return Results.NoContent();
});

seasons.MapGet("/{season}/assignments", async (string season, SeasonStore store, CancellationToken ct) =>
{
    if (!Validation.IsSeason(season)) return Invalid("season", "Season must be yyyy-yy.");
    if (!await store.SeasonExistsAsync(season, ct)) return Results.NotFound();
    return Results.Ok(await store.GetAssignmentsAsync(season, ct));
});

seasons.MapPut("/{season}/assignments/{rode}", async (string season, string rode, List<string?> names, SeasonStore store, CancellationToken ct) =>
{
    if (!Validation.IsSeason(season)) return Invalid("season", "Season must be yyyy-yy.");
    if (!Validation.IsRode(rode)) return Invalid("rode", "Rode must be 4 digits.");
    var errors = new Dictionary<string, string[]>();
    var normalized = Validation.NormalizeNames(names, "names", errors);
    if (normalized is null) return Results.ValidationProblem(errors);
    if (!await store.SeasonExistsAsync(season, ct)) return Results.NotFound();
    await store.SetAssignmentAsync(season, rode, normalized, ct);
    return Results.NoContent();
});

seasons.MapGet("/{season}/reception", async (string season, SeasonStore store, CancellationToken ct) =>
{
    if (!Validation.IsSeason(season)) return Invalid("season", "Season must be yyyy-yy.");
    if (!await store.SeasonExistsAsync(season, ct)) return Results.NotFound();
    return Results.Ok(await store.GetReceptionAsync(season, ct));
});

seasons.MapPut("/{season}/reception/{date}/{slot}", async (string season, string date, string slot, List<string?> names, SeasonStore store, CancellationToken ct) =>
{
    if (!Validation.IsSeason(season)) return Invalid("season", "Season must be yyyy-yy.");
    if (!Validation.IsDate(date)) return Invalid("date", "Date must be yyyy-MM-dd.");
    if (!Validation.IsSlot(slot)) return Invalid("slot", "Slot must be HH:mm-HH:mm.");
    var errors = new Dictionary<string, string[]>();
    var normalized = Validation.NormalizeNames(names, "names", errors);
    if (normalized is null) return Results.ValidationProblem(errors);
    if (!await store.SeasonExistsAsync(season, ct)) return Results.NotFound();
    await store.SetReceptionAsync(season, date, slot, normalized, ct);
    return Results.NoContent();
});

app.MapDefaultEndpoints();

app.Run();

static IResult Invalid(string field, string message) =>
    Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

static HashSet<string> ReadOrigins(IConfiguration configuration)
{
    var section = configuration.GetSection("Cors:AllowedOrigins");
    var values = section.Value is { } single
        ? single.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : section.GetChildren().Select(c => c.Value).OfType<string>();
    return values.Select(v => v.TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

static bool IsLocalOrigin(string origin) =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
