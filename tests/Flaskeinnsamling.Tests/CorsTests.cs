namespace Flaskeinnsamling.Tests;

[Collection(ApiCollection.Name)]
public sealed class CorsTests(ApiFixture fx)
{
    private const string PagesOrigin = "https://tasta-skolekorps.github.io";

    private async Task<HttpResponseMessage> PreflightAsync(string origin, string method = "PUT")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, $"/api/seasons/{ApiFixture.SeasonId}/assignments/2001");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "x-admin-key,content-type");
        return await fx.Client.SendAsync(request);
    }

    private static string? AllowOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    [Fact]
    public async Task Preflight_FromGitHubPages_IsAllowed()
    {
        using var response = await PreflightAsync(PagesOrigin);
        Assert.True(response.IsSuccessStatusCode, $"status {response.StatusCode}");
        Assert.Equal(PagesOrigin, AllowOrigin(response));

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Methods", out var methods));
        Assert.Contains("PUT", string.Join(",", methods), StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out var headers));
        var allowedHeaders = string.Join(",", headers);
        Assert.Contains("x-admin-key", allowedHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("content-type", allowedHeaders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preflight_FromOtherOrigin_IsNotAllowed()
    {
        using var response = await PreflightAsync("https://evil.example");
        Assert.Null(AllowOrigin(response));
    }

    [Fact]
    public async Task Preflight_ForDelete_IsNotAllowed()
    {
        using var response = await PreflightAsync(PagesOrigin, "DELETE");
        var methods = response.Headers.TryGetValues("Access-Control-Allow-Methods", out var values) ? string.Join(",", values) : "";
        Assert.DoesNotContain("DELETE", methods, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PagesOrigin, true)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://tasta-skolekorps.github.io.evil.example", false)]
    [InlineData("http://tasta-skolekorps.github.io", false)]
    public async Task SimpleGet_AllowOriginOnlyForPages(string origin, bool allowed)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/seasons");
        request.Headers.Add("Origin", origin);
        using var response = await fx.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(allowed ? origin : null, AllowOrigin(response));
    }
}
