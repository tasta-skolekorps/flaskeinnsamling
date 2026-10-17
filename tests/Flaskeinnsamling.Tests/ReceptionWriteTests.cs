using System.Text.Json.Nodes;

namespace Flaskeinnsamling.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReceptionWriteTests(ApiFixture fx)
{
    private const string Season = ApiFixture.SeasonId;
    private const string Date = "2026-10-21";

    /// <summary>A slot that is not in the season file, so writes never clobber real reception data.</summary>
    private const string FreeSlot = "21:15-22:00";

    private async Task<JsonNode?> GetSlotAsync(string date, string slot) =>
        (await fx.GetJsonAsync($"/api/seasons/{Season}/reception"))[date]?[slot];

    [Fact]
    public void FreeSlot_IsNotInSeasonFile() =>
        Assert.Null(fx.SeasonFile["reception"]?[Date]?[FreeSlot]);

    [Fact]
    public async Task Put_WithoutKey_Returns401AndDoesNotWrite()
    {
        using var response = await fx.PutAsync($"/api/seasons/{Season}/reception/{Date}/{FreeSlot}", new JsonArray("Uautorisert Navn"), key: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await GetSlotAsync(Date, FreeSlot));
    }

    [Fact]
    public async Task Put_WithWrongKey_Returns401AndDoesNotWrite()
    {
        using var response = await fx.PutAsync($"/api/seasons/{Season}/reception/{Date}/{FreeSlot}", new JsonArray("Uautorisert Navn"), "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await GetSlotAsync(Date, FreeSlot));
    }

    [Theory]
    [InlineData("21:15-22:00")]
    [InlineData("21%3A15-22%3A00")]
    public async Task Put_WithKey_RoundTripsAndEmptyArrayDeletes(string slotInUrl)
    {
        var url = $"/api/seasons/{Season}/reception/{Date}/{slotInUrl}";
        var original = (await GetSlotAsync(Date, FreeSlot))?.DeepClone();
        try
        {
            using (var put = await fx.PutAsync(url, new JsonArray("Vakt Testesen"), fx.AdminKey))
                Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.True(JsonNode.DeepEquals(new JsonArray("Vakt Testesen"), await GetSlotAsync(Date, FreeSlot)));

            var season = await fx.GetJsonAsync($"/api/seasons/{Season}");
            Assert.True(JsonNode.DeepEquals(new JsonArray("Vakt Testesen"), season["reception"]![Date]![FreeSlot]));

            // Existing slots on the same date are untouched.
            foreach (var (slot, names) in fx.SeasonFile["reception"]![Date]!.AsObject())
                Assert.True(JsonNode.DeepEquals(names, season["reception"]![Date]![slot]), $"slot {slot} changed");

            using (var clear = await fx.PutAsync(url, new JsonArray(), fx.AdminKey))
                Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
            Assert.Null(await GetSlotAsync(Date, FreeSlot));
        }
        finally
        {
            using var restore = await fx.PutAsync($"/api/seasons/{Season}/reception/{Date}/{FreeSlot}", original ?? new JsonArray(), fx.AdminKey);
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }
    }

    [Fact]
    public async Task Put_NewDate_RoundTripsAndEmptyArrayRemovesDate()
    {
        const string date = "2027-01-06";
        Assert.Null(fx.SeasonFile["reception"]?[date]?[FreeSlot]);
        var hadDate = fx.SeasonFile["reception"]?[date] is not null;
        var url = $"/api/seasons/{Season}/reception/{date}/{FreeSlot}";
        try
        {
            using (var put = await fx.PutAsync(url, new JsonArray("Vakt Testesen"), fx.AdminKey))
                Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.True(JsonNode.DeepEquals(new JsonArray("Vakt Testesen"), await GetSlotAsync(date, FreeSlot)));
        }
        finally
        {
            using var restore = await fx.PutAsync(url, new JsonArray(), fx.AdminKey);
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }
        var reception = await fx.GetJsonAsync($"/api/seasons/{Season}/reception");
        Assert.Equal(hadDate, reception[date] is not null);
    }

    [Theory]
    [InlineData("2026", Date, FreeSlot)]
    [InlineData(Season, "2026-02-30", FreeSlot)]
    [InlineData(Season, "2026-13-01", FreeSlot)]
    [InlineData(Season, "21-10-2026", FreeSlot)]
    [InlineData(Season, "20261021", FreeSlot)]
    [InlineData(Season, Date, "20:00-17:00")]
    [InlineData(Season, Date, "17:00-17:00")]
    [InlineData(Season, Date, "25:00-26:00")]
    [InlineData(Season, Date, "17:00")]
    [InlineData(Season, Date, "1700-1830")]
    [InlineData(Season, Date, "7:00-8:30")]
    public async Task Put_InvalidSeasonDateOrSlot_Returns400(string season, string date, string slot)
    {
        using var response = await fx.PutAsync($"/api/seasons/{season}/reception/{date}/{slot}", new JsonArray("Navn"), fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_TooManyNames_Returns400()
    {
        var names = new JsonArray(Enumerable.Range(1, 21).Select(i => (JsonNode?)JsonValue.Create($"Navn {i}")).ToArray());
        using var response = await fx.PutAsync($"/api/seasons/{Season}/reception/{Date}/{FreeSlot}", names, fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await GetSlotAsync(Date, FreeSlot));
    }

    [Fact]
    public async Task Put_UnknownSeason_Returns404()
    {
        using var response = await fx.PutAsync($"/api/seasons/2098-99/reception/{Date}/{FreeSlot}", new JsonArray("Navn"), fx.AdminKey);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
