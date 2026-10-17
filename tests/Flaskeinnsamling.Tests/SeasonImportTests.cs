using System.Text.Json.Nodes;

namespace Flaskeinnsamling.Tests;

/// <summary>
/// Full-season imports use a throwaway season id so real data is never clobbered. There is no
/// DELETE endpoint; the test season is left in the ephemeral (volume-less) Azurite instance.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SeasonImportTests(ApiFixture fx)
{
    private const string TestSeason = "2099-00";

    private static readonly string[] ForbiddenValues =
    [
        "Hemmeligveien 42",
        "4021 Stavanger",
        "2014-03-15",
        "15.03.2014",
        "+47 98765432",
        "forelder@example.com",
        "Forelder Hemmelig",
    ];

    private static JsonObject ForbiddenFields() => new()
    {
        ["address"] = ForbiddenValues[0],
        ["postnummer"] = ForbiddenValues[1],
        ["birthDate"] = ForbiddenValues[2],
        ["fodselsdato"] = ForbiddenValues[3],
        ["parentPhone"] = ForbiddenValues[4],
        ["parentEmail"] = ForbiddenValues[5],
        ["parentName"] = ForbiddenValues[6],
    };

    private static JsonObject Document()
    {
        var doc = new JsonObject
        {
            ["season"] = TestSeason,
            ["collections"] = new JsonArray("2099-10-21", "2100-01-06"),
            ["handouts"] = new JsonObject { ["2099-10-21"] = "2099-10-05" },
            ["notices"] = new JsonObject { ["2100-01-06"] = "2099-12-27" },
            ["contacts"] = new JsonArray("Komité Testesen"),
            ["vipps"] = "#00000",
            ["housing"] = new JsonObject { ["2001"] = new JsonArray(10, 12), ["2002"] = new JsonArray(5, 5) },
            ["korps"] = new JsonObject { ["Mellomkorps"] = new JsonArray("Musikant Testesen") },
            ["assignments"] = new JsonObject { ["2001"] = new JsonArray("Musikant Testesen") },
            ["reception"] = new JsonObject
            {
                ["2099-10-21"] = new JsonObject { ["17:00-18:30"] = new JsonArray("Vakt Testesen") },
            },
            ["musicians"] = new JsonArray(new JsonObject
            {
                ["name"] = "Musikant Testesen",
                ["address"] = ForbiddenValues[0],
                ["birthDate"] = ForbiddenValues[2],
                ["parentPhone"] = ForbiddenValues[4],
                ["parentEmail"] = ForbiddenValues[5],
            }),
        };
        foreach (var (key, value) in ForbiddenFields()) doc[key] = value?.DeepClone();
        return doc;
    }

    private static void AssertNoForbidden(string body)
    {
        foreach (var value in ForbiddenValues)
            Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
        foreach (var key in ForbiddenFields().Select(p => p.Key).Append("musicians"))
            Assert.DoesNotContain($"\"{key}\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PutSeason_WithoutOrWrongKey_Returns401()
    {
        using (var none = await fx.PutAsync($"/api/seasons/{TestSeason}", Document(), key: null))
            Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        using (var wrong = await fx.PutAsync($"/api/seasons/{TestSeason}", Document(), "wrong-key"))
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task PutSeason_DropsForbiddenFields_AndRoundTrips()
    {
        var sent = Document();
        using (var put = await fx.PutAsync($"/api/seasons/{TestSeason}", sent, fx.AdminKey))
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        foreach (var url in new[]
                 {
                     $"/api/seasons/{TestSeason}",
                     $"/api/seasons/{TestSeason}/assignments",
                     $"/api/seasons/{TestSeason}/reception",
                 })
        {
            using var response = await fx.Client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNoForbidden(await response.Content.ReadAsStringAsync());
        }

        var season = await fx.GetJsonAsync($"/api/seasons/{TestSeason}");
        foreach (var field in new[] { "season", "collections", "handouts", "notices", "contacts", "vipps", "housing", "korps", "assignments", "reception" })
        {
            Assert.True(JsonNode.DeepEquals(sent[field], season[field]),
                $"'{field}' differs.\nSent: {sent[field]?.ToJsonString()}\nGot:  {season[field]?.ToJsonString()}");
        }

        var seasons = (await fx.GetJsonAsync("/api/seasons")).AsArray().Select(n => n!.GetValue<string>());
        Assert.Contains(TestSeason, seasons);
    }

    [Fact]
    public async Task PutSeason_ReplacesAllRows()
    {
        using (var first = await fx.PutAsync($"/api/seasons/{TestSeason}", Document(), fx.AdminKey))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var smaller = Document();
        smaller["housing"] = new JsonObject { ["2002"] = new JsonArray(5, 5) };
        smaller["assignments"] = new JsonObject { ["2002"] = new JsonArray("Annen Testesen") };
        smaller["reception"] = new JsonObject();
        using (var second = await fx.PutAsync($"/api/seasons/{TestSeason}", smaller, fx.AdminKey))
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var season = await fx.GetJsonAsync($"/api/seasons/{TestSeason}");
        Assert.True(JsonNode.DeepEquals(smaller["housing"], season["housing"]), season["housing"]?.ToJsonString());
        Assert.True(JsonNode.DeepEquals(smaller["assignments"], season["assignments"]), season["assignments"]?.ToJsonString());
        Assert.True(JsonNode.DeepEquals(new JsonObject(), season["reception"]), season["reception"]?.ToJsonString());
    }

    [Fact]
    public async Task PutSeason_BodySeasonMismatch_Returns400()
    {
        var doc = Document();
        doc["season"] = "2098-99";
        using var response = await fx.PutAsync($"/api/seasons/{TestSeason}", doc, fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("collections", "[\"2099-02-30\"]")]
    [InlineData("housing", "{\"abc\":[1,2]}")]
    [InlineData("housing", "{\"2001\":[1]}")]
    [InlineData("housing", "{\"2001\":[-1,2]}")]
    [InlineData("assignments", "{\"20011\":[\"Navn\"]}")]
    [InlineData("reception", "{\"2099-10-21\":{\"18:30-17:00\":[\"Navn\"]}}")]
    [InlineData("reception", "{\"21.10.2099\":{\"17:00-18:30\":[\"Navn\"]}}")]
    [InlineData("handouts", "{\"2099-10-21\":\"neste mandag\"}")]
    public async Task PutSeason_InvalidField_Returns400(string field, string json)
    {
        var doc = Document();
        doc["season"] = "2099-01";
        doc[field] = JsonNode.Parse(json);
        using var response = await fx.PutAsync("/api/seasons/2099-01", doc, fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var get = await fx.Client.GetAsync("/api/seasons/2099-01");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task PutSeason_InvalidRouteSeason_Returns400()
    {
        using var response = await fx.PutAsync("/api/seasons/2099", Document(), fx.AdminKey);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportedSeason_ContainsNoForbiddenFields()
    {
        using var response = await fx.Client.GetAsync($"/api/seasons/{ApiFixture.SeasonId}");
        var body = await response.Content.ReadAsStringAsync();
        foreach (var key in new[] { "\"musicians\"", "\"address\"", "\"birthDate\"", "\"parentPhone\"", "\"phone\"", "\"email\"" })
            Assert.DoesNotContain(key, body, StringComparison.OrdinalIgnoreCase);
    }
}
