using System.Text.Json.Nodes;

namespace Flaskeinnsamling.Tests;

[Collection(ApiCollection.Name)]
public sealed class AssignmentWriteTests(ApiFixture fx)
{
    private const string Season = ApiFixture.SeasonId;

    /// <summary>A rode with housing data but no collector, so writes never clobber a real assignment.</summary>
    private string FreeRode
    {
        get
        {
            var assigned = fx.SeasonFile["assignments"]!.AsObject().Select(p => p.Key).ToHashSet();
            return fx.SeasonFile["housing"]!.AsObject().Select(p => p.Key).FirstOrDefault(r => !assigned.Contains(r)) ?? "9999";
        }
    }

    private async Task<JsonObject> GetAssignmentsAsync() =>
        (await fx.GetJsonAsync($"/api/seasons/{Season}/assignments")).AsObject();

    [Fact]
    public async Task Put_WithoutKey_Returns401AndDoesNotWrite()
    {
        using var response = await fx.PutAsync($"/api/seasons/{Season}/assignments/{FreeRode}", new JsonArray("Uautorisert Navn"), key: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await GetAssignmentsAsync()).ContainsKey(FreeRode));
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData(" ")]
    public async Task Put_WithWrongKey_Returns401AndDoesNotWrite(string key)
    {
        using var response = await fx.PutAsync($"/api/seasons/{Season}/assignments/{FreeRode}", new JsonArray("Uautorisert Navn"), key);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await GetAssignmentsAsync()).ContainsKey(FreeRode));
    }

    [Fact]
    public async Task Put_WithKey_RoundTripsAndEmptyArrayDeletes()
    {
        var rode = FreeRode;
        var url = $"/api/seasons/{Season}/assignments/{rode}";
        var original = (await GetAssignmentsAsync())[rode]?.DeepClone();
        try
        {
            using (var put = await fx.PutAsync(url, new JsonArray("Test Testesen", "Test Testesen 2"), fx.AdminKey))
                Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

            var afterPut = await GetAssignmentsAsync();
            Assert.True(JsonNode.DeepEquals(new JsonArray("Test Testesen", "Test Testesen 2"), afterPut[rode]));

            var season = await fx.GetJsonAsync($"/api/seasons/{Season}");
            Assert.True(JsonNode.DeepEquals(afterPut[rode], season["assignments"]![rode]));

            using (var clear = await fx.PutAsync(url, new JsonArray(), fx.AdminKey))
                Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
            Assert.False((await GetAssignmentsAsync()).ContainsKey(rode));
        }
        finally
        {
            using var restore = await fx.PutAsync(url, original ?? new JsonArray(), fx.AdminKey);
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }
    }

    [Fact]
    public async Task Put_NormalizesNames()
    {
        var rode = FreeRode;
        var url = $"/api/seasons/{Season}/assignments/{rode}";
        try
        {
            using (var put = await fx.PutAsync(url, new JsonArray("  Ola Nordmann ", "Ola Nordmann", "", "   ", null), fx.AdminKey))
                Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.True(JsonNode.DeepEquals(new JsonArray("Ola Nordmann"), (await GetAssignmentsAsync())[rode]));
        }
        finally
        {
            using var restore = await fx.PutAsync(url, new JsonArray(), fx.AdminKey);
        }
        Assert.False((await GetAssignmentsAsync()).ContainsKey(rode));
    }

    [Theory]
    [InlineData("2026", "2001")]
    [InlineData("26-27", "2001")]
    [InlineData(Season, "201")]
    [InlineData(Season, "20011")]
    [InlineData(Season, "abcd")]
    public async Task Put_InvalidSeasonOrRode_Returns400(string season, string rode)
    {
        using var response = await fx.PutAsync($"/api/seasons/{season}/assignments/{rode}", new JsonArray("Navn"), fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_TooLongName_Returns400()
    {
        using var response = await fx.PutAsync($"/api/seasons/{Season}/assignments/{FreeRode}", new JsonArray(new string('x', 101)), fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await GetAssignmentsAsync()).ContainsKey(FreeRode));
    }

    [Fact]
    public async Task Put_TooManyNames_Returns400()
    {
        var names = new JsonArray(Enumerable.Range(1, 21).Select(i => (JsonNode?)JsonValue.Create($"Navn {i}")).ToArray());
        using var response = await fx.PutAsync($"/api/seasons/{Season}/assignments/{FreeRode}", names, fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await GetAssignmentsAsync()).ContainsKey(FreeRode));
    }

    [Fact]
    public async Task Put_UnknownSeason_Returns404()
    {
        using var response = await fx.PutAsync("/api/seasons/2098-99/assignments/2001", new JsonArray("Navn"), fx.AdminKey);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
