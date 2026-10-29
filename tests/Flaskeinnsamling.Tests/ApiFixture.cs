using System.Text.Json.Nodes;
using Aspire.Hosting;

namespace Flaskeinnsamling.Tests;

/// <summary>
/// Starts the AppHost once (Azurite + API) and shares it across all tests in <see cref="ApiCollection"/>.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminHeader = "X-Admin-Key";
    public const string SeasonId = "2026-27";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>Throwaway key generated per run; never a real key.</summary>
    public string AdminKey { get; } = "test-key-" + Guid.NewGuid().ToString("N");

    public HttpClient Client { get; private set; } = default!;

    /// <summary>Synthetic season imported through the admin endpoint at startup; the tests compare against it.</summary>
    public JsonObject Season { get; } = new()
    {
        ["season"] = SeasonId,
        ["collections"] = new JsonArray("2026-10-21", "2027-01-06", "2027-05-19"),
        ["handouts"] = new JsonObject { ["2026-10-21"] = "2026-10-05" },
        ["notices"] = new JsonObject { ["2027-01-06"] = "2026-12-27" },
        ["contacts"] = new JsonArray("Komité Testesen", "Komité Prøvesen"),
        ["vipps"] = "#00000",
        ["housing"] = new JsonObject
        {
            ["2001"] = new JsonArray(40, 60),
            ["2002"] = new JsonArray(50, 81),
            ["2003"] = new JsonArray(30, 59),
        },
        ["korps"] = new JsonObject
        {
            ["Mellomkorps"] = new JsonArray("Musikant Én", "Musikant To"),
            ["Seniorkorps"] = new JsonArray("Senior Én", "Senior To"),
        },
        ["assignments"] = new JsonObject
        {
            ["2001"] = new JsonArray("Musikant Én"),
            ["2002"] = new JsonArray("Musikant To", "Musikant Én"),
        },
        ["reception"] = new JsonObject
        {
            ["2026-10-21"] = new JsonObject
            {
                ["17:00-18:30"] = new JsonArray("Senior Én"),
                ["18:30-20:00"] = new JsonArray("Senior To"),
            },
        },
    };

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Flaskeinnsamling_AppHost>(cts.Token);
        builder.Configuration["Parameters:admin-api-key"] = AdminKey;

        // The AppHost gives Azurite a named data volume shared with local dev runs. Drop it so every
        // test run starts from an empty emulator and never touches dev data.
        foreach (var resource in builder.Resources)
        {
            foreach (var mount in resource.Annotations.OfType<ContainerMountAnnotation>()
                         .Where(m => m.Type == ContainerMountType.Volume).ToList())
            {
                resource.Annotations.Remove(mount);
            }
        }

        _app = await builder.BuildAsync(cts.Token);
        await _app.StartAsync(cts.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);

        Client = _app.CreateHttpClient("api", "http");

        // Tables are created in the background after startup; retry the import until it succeeds.
        while (true)
        {
            using var response = await Client.SendAsync(Request(HttpMethod.Put, $"/api/seasons/{SeasonId}", Season, AdminKey), cts.Token);
            if (response.StatusCode == HttpStatusCode.NoContent) break;
            await Task.Delay(500, cts.Token);
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    public HttpRequestMessage Request(HttpMethod method, string url, JsonNode? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        if (key is not null) request.Headers.Add(AdminHeader, key);
        return request;
    }

    public async Task<HttpResponseMessage> PutAsync(string url, JsonNode body, string? key) =>
        await Client.SendAsync(Request(HttpMethod.Put, url, body, key));

    public async Task<JsonNode> GetJsonAsync(string url, string? key = null)
    {
        using var response = await Client.SendAsync(Request(HttpMethod.Get, url, key: key));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
