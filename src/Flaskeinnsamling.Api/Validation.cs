using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flaskeinnsamling.Api;

public static partial class Validation
{
    public const int MaxNameLength = 100;
    public const int MaxNamesPerEntry = 20;
    public const int MaxNamesPerKorps = 500;
    public const int MaxKorpsGroups = 10;

    /// <summary>Table Storage string properties hold at most 32K UTF-16 chars; keep a margin.</summary>
    public const int MaxJsonColumnLength = 30_000;

    [GeneratedRegex(@"^\d{4}-\d{2}$")]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex RodeRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(?<from>\d{2}:\d{2})-(?<to>\d{2}:\d{2})$")]
    private static partial Regex SlotRegex();

    public static bool IsSeason(string? value) => value is not null && SeasonRegex().IsMatch(value);

    public static bool IsRode(string? value) => value is not null && RodeRegex().IsMatch(value);

    public static bool IsDate(string? value) =>
        value is not null && DateRegex().IsMatch(value) &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool IsSlot(string? value)
    {
        if (value is null) return false;
        var match = SlotRegex().Match(value);
        return match.Success &&
            TimeOnly.TryParseExact(match.Groups["from"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) &&
            TimeOnly.TryParseExact(match.Groups["to"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) &&
            from < to;
    }

    /// <summary>Trims names, drops blanks and duplicates; returns null and an error if invalid.</summary>
    public static List<string>? NormalizeNames(IEnumerable<string?>? names, string field, IDictionary<string, string[]> errors, int maxCount = MaxNamesPerEntry)
    {
        var result = new List<string>();
        foreach (var raw in names ?? [])
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Length > MaxNameLength || name.Any(char.IsControl))
            {
                errors[field] = [$"Names must be at most {MaxNameLength} characters without control characters."];
                return null;
            }
            if (!result.Contains(name, StringComparer.Ordinal)) result.Add(name);
        }
        if (result.Count > maxCount)
        {
            errors[field] = [$"At most {maxCount} names are allowed."];
            return null;
        }
        return result;
    }

    /// <summary>Validates and normalizes a full season document for import.</summary>
    public static SeasonDocument? NormalizeSeason(string season, SeasonDocument? doc, IDictionary<string, string[]> errors)
    {
        if (doc is null)
        {
            errors["body"] = ["A season document is required."];
            return null;
        }
        if (doc.Season is not null && doc.Season != season)
            errors["season"] = ["Season in body must match the route."];

        var collections = new List<string>();
        foreach (var date in doc.Collections ?? [])
        {
            if (!IsDate(date)) { errors["collections"] = ["Dates must be yyyy-MM-dd."]; break; }
            if (!collections.Contains(date)) collections.Add(date);
        }
        collections.Sort(StringComparer.Ordinal);

        var handouts = NormalizeDateMap(doc.Handouts, "handouts", errors);
        var notices = NormalizeDateMap(doc.Notices, "notices", errors);
        var contacts = NormalizeNames(doc.Contacts, "contacts", errors) ?? [];

        var vipps = doc.Vipps?.Trim() ?? string.Empty;
        if (vipps.Length > 50 || vipps.Any(char.IsControl)) errors["vipps"] = ["Vipps must be at most 50 characters."];

        var housing = new Dictionary<string, int[]>();
        foreach (var (rode, value) in doc.Housing ?? [])
        {
            if (!IsRode(rode) || value is not { Length: 2 } || value[0] < 0 || value[1] < 0)
            {
                errors["housing"] = ["Housing must map a 4-digit rode to [addresses, dwellings] (non-negative)."];
                break;
            }
            housing[rode] = [value[0], value[1]];
        }

        var korps = new Dictionary<string, List<string>>();
        foreach (var (group, names) in doc.Korps ?? [])
        {
            var key = group?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 50 || key.Any(char.IsControl))
            {
                errors["korps"] = ["Korps group names must be 1-50 characters."];
                break;
            }
            // Korps lists hold every musician in a group, so they get a larger limit than a rode or slot.
            var list = NormalizeNames(names, "korps", errors, MaxNamesPerKorps);
            if (list is null) break;
            korps[key] = list;
        }
        if (korps.Count > MaxKorpsGroups) errors["korps"] = [$"At most {MaxKorpsGroups} korps groups are allowed."];

        var assignments = new Dictionary<string, List<string>>();
        foreach (var (rode, names) in doc.Assignments ?? [])
        {
            if (!IsRode(rode)) { errors["assignments"] = ["Rode must be 4 digits."]; break; }
            var list = NormalizeNames(names, "assignments", errors);
            if (list is null) break;
            if (list.Count > 0) assignments[rode] = list;
        }

        var reception = new Dictionary<string, Dictionary<string, List<string>>>();
        foreach (var (date, slots) in doc.Reception ?? [])
        {
            if (!IsDate(date)) { errors["reception"] = ["Reception dates must be yyyy-MM-dd."]; break; }
            var bySlot = new Dictionary<string, List<string>>();
            foreach (var (slot, names) in slots ?? [])
            {
                if (!IsSlot(slot)) { errors["reception"] = ["Reception slots must be HH:mm-HH:mm."]; break; }
                var list = NormalizeNames(names, "reception", errors);
                if (list is null) break;
                if (list.Count > 0) bySlot[slot] = list;
            }
            if (bySlot.Count > 0) reception[date] = bySlot;
        }

        CheckJsonColumn(collections, "collections", errors);
        CheckJsonColumn(handouts, "handouts", errors);
        CheckJsonColumn(notices, "notices", errors);
        CheckJsonColumn(contacts, "contacts", errors);
        CheckJsonColumn(korps, "korps", errors);

        if (errors.Count > 0) return null;

        return new SeasonDocument
        {
            Season = season,
            Collections = collections,
            Handouts = handouts,
            Notices = notices,
            Contacts = contacts,
            Vipps = vipps,
            Housing = housing,
            Korps = korps,
            Assignments = assignments,
            Reception = reception,
        };
    }

    private static void CheckJsonColumn<T>(T value, string field, IDictionary<string, string[]> errors)
    {
        if (errors.ContainsKey(field)) return;
        if (JsonSerializer.Serialize(value, SeasonStore.Json).Length > MaxJsonColumnLength)
            errors[field] = [$"Field is too large (max {MaxJsonColumnLength} characters as JSON)."];
    }

    private static Dictionary<string, string> NormalizeDateMap(Dictionary<string, string>? map, string field, IDictionary<string, string[]> errors)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in map ?? [])
        {
            if (!IsDate(key) || !IsDate(value))
            {
                errors[field] = ["Keys and values must be dates (yyyy-MM-dd)."];
                break;
            }
            result[key] = value;
        }
        return result;
    }
}
