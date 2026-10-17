using System.Text.Json.Nodes;

namespace Flaskeinnsamling.Tests;

[Collection(ApiCollection.Name)]
public sealed class SeasonReadTests(ApiFixture fx)
{
    [Fact]
    public async Task ListSeasons_ContainsSeededSeason()
    {
        var seasons = (await fx.GetJsonAsync("/api/seasons")).AsArray().Select(n => n!.GetValue<string>());
        Assert.Contains(ApiFixture.SeasonId, seasons);
    }

    [Theory]
    [InlineData("collections")]
    [InlineData("contacts")]
    [InlineData("vipps")]
    [InlineData("housing")]
    [InlineData("korps")]
    [InlineData("assignments")]
    [InlineData("reception")]
    public async Task GetSeason_FieldEqualsSeasonFile(string field)
    {
        var season = await fx.GetJsonAsync($"/api/seasons/{ApiFixture.SeasonId}");

        var expected = fx.SeasonFile[field];
        var actual = season[field];
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"'{field}' differs.\nExpected: {expected?.ToJsonString()}\nActual:   {actual?.ToJsonString()}");
    }

    [Fact]
    public async Task GetSeason_HasSeasonIdAndNoMusicians()
    {
        var season = (await fx.GetJsonAsync($"/api/seasons/{ApiFixture.SeasonId}")).AsObject();
        Assert.Equal(ApiFixture.SeasonId, season["season"]!.GetValue<string>());
        Assert.False(season.ContainsKey("musicians"));
    }

    [Fact]
    public async Task GetAssignmentsAndReception_EqualSeasonFile()
    {
        var assignments = await fx.GetJsonAsync($"/api/seasons/{ApiFixture.SeasonId}/assignments");
        var reception = await fx.GetJsonAsync($"/api/seasons/{ApiFixture.SeasonId}/reception");
        Assert.True(JsonNode.DeepEquals(fx.SeasonFile["assignments"], assignments), "assignments differ");
        Assert.True(JsonNode.DeepEquals(fx.SeasonFile["reception"], reception), "reception differs");
    }

    [Theory]
    [InlineData("/api/seasons/2098-99")]
    [InlineData("/api/seasons/2098-99/assignments")]
    [InlineData("/api/seasons/2098-99/reception")]
    public async Task UnknownSeason_Returns404(string url)
    {
        using var response = await fx.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/seasons/2026")]
    [InlineData("/api/seasons/abcd-ef")]
    [InlineData("/api/seasons/2026-2027")]
    [InlineData("/api/seasons/2026-2/assignments")]
    [InlineData("/api/seasons/x/reception")]
    public async Task InvalidSeason_Returns400(string url)
    {
        using var response = await fx.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
