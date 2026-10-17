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

    public JsonObject SeasonFile { get; } = (JsonObject)JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", $"{SeasonId}.json")))!;

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Flaskeinnsamling_AppHost>(cts.Token);
        builder.Configuration["Parameters:admin-api-key"] = AdminKey;

        // The AppHost gives Azurite a named data volume shared with local dev runs. Drop it so every
        // test run starts from an empty emulator, seeds from data/*.json and never touches dev data.
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

        // Seeding runs in the background after startup; wait until the season is readable.
        while (true)
        {
            using var response = await Client.GetAsync($"/api/seasons/{SeasonId}", cts.Token);
            if (response.StatusCode == HttpStatusCode.OK) break;
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

    public async Task<JsonNode> GetJsonAsync(string url)
    {
        using var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
