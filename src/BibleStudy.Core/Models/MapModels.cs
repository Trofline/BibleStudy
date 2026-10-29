using System.Text.Json;
using BibleStudy.Core.Storage;

namespace BibleStudy.Core.Models;

/// <summary>A place mentioned in a chapter (Theographic Bible Metadata, CC BY-SA 4.0).</summary>
public sealed record MapPlace(string Id, string Name, string FeatureType, double Latitude, double Longitude, IReadOnlyList<int> Verses);

/// <summary>Base map from Natural Earth (public domain): land and lake outlines and river lines as lon/lat sequences.</summary>
public sealed class WorldMap
{
    public static string Attribution => L("Kartendaten: Natural Earth (gemeinfrei)");
    const string Base = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/";

    public IReadOnlyList<double[]> Land { get; init; } = [];
    public IReadOnlyList<double[]> Lakes { get; init; } = [];
    public IReadOnlyList<double[]> Rivers { get; init; } = [];

    /// <summary>Loads (and caches on disk) the three map layers. Each shape is a flat array lon0, lat0, lon1, lat1, …</summary>
    public static async Task<WorldMap> LoadAsync(HttpClient http, string cacheDir, CancellationToken ct = default)
    {
        async Task<List<double[]>> Layer(string name)
        {
            var file = Path.Combine(cacheDir, "map", name + ".geojson");
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await SafeFile.WriteAllBytesAsync(file, await http.GetByteArrayAsync($"{Base}{name}.geojson", ct));
            }
            return await Task.Run(() => ParseShapes(File.ReadAllBytes(file)), ct);
        }
        var land = Layer("ne_50m_land"); var lakes = Layer("ne_50m_lakes"); var rivers = Layer("ne_50m_rivers_lake_centerlines");
        return new WorldMap { Land = await land, Lakes = await lakes, Rivers = await rivers };
    }

    /// <summary>All polygon rings and lines of a GeoJSON file (holes are kept as rings of their own).</summary>
    public static List<double[]> ParseShapes(byte[] geojson)
    {
        var shapes = new List<double[]>();
        using var doc = JsonDocument.Parse(geojson);
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object) continue;
            var coords = g.GetProperty("coordinates");
            switch (g.GetProperty("type").GetString())
            {
                case "Polygon": foreach (var ring in coords.EnumerateArray()) shapes.Add(Flatten(ring)); break;
                case "MultiPolygon": foreach (var polygon in coords.EnumerateArray()) foreach (var ring in polygon.EnumerateArray()) shapes.Add(Flatten(ring)); break;
                case "LineString": shapes.Add(Flatten(coords)); break;
                case "MultiLineString": foreach (var line in coords.EnumerateArray()) shapes.Add(Flatten(line)); break;
            }
        }
        return shapes;
    }

    static double[] Flatten(JsonElement points)
    {
        var result = new double[points.GetArrayLength() * 2];
        var i = 0;
        foreach (var p in points.EnumerateArray()) { result[i++] = p[0].GetDouble(); result[i++] = p[1].GetDouble(); }
        return result;
    }
}

/// <summary>An event of the Bible's timeline (year: negative = BC).</summary>
public sealed record TimelineEvent(string Id, string Name, int Year, int References);

public sealed record EventDetail(string Id, string Name, int Year, string Duration, IReadOnlyList<(string Id, string Name)> Participants,
    IReadOnlyList<(string Id, string Name)> Locations, IReadOnlyList<(VerseRef Ref, int EndVerse)> References);

/// <summary>A person of the family tree (Theographic): parents, children and years (negative = BC).</summary>
public sealed record PersonNode(string Id, string Name, string Gender, int? BirthYear, int? DeathYear,
    IReadOnlyList<(string Id, string Name)> Parents, IReadOnlyList<(string Id, string Name)> Children, IReadOnlyList<(string Id, string Name)> Partners);
