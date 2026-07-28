using System.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record ProjectionDefinition(string Id, string Name, string? WellKnownText = null);

public static class ProjectionService {
    public const string Wgs84Id = "epsg:4326";
    public const string WebMercatorId = "epsg:3857";
    public const string Cgcs2000Id = "epsg:4490";
    public const string Cgcs2000MercatorId = "cgcs2000:mercator";
    public const string Jgd2011Id = "epsg:6668";
    public const string Jgd2000Id = "epsg:4612";
    public const string Etrs89Id = "epsg:4258";
    public const string Etrs89Utm32NId = "epsg:25832";
    public const string Etrs89Utm33NId = "epsg:25833";
    public const string CustomWktId = "custom:wkt";

    private const string Cgcs2000GeographicWkt =
        "GEOGCS[\"China Geodetic Coordinate System 2000\",DATUM[\"China_2000\",SPHEROID[\"CGCS2000\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4490\"]]";

    private const string Cgcs2000MercatorWkt =
        "PROJCS[\"CGCS2000 / Mercator\",GEOGCS[\"China Geodetic Coordinate System 2000\",DATUM[\"China_2000\",SPHEROID[\"CGCS2000\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Mercator_1SP\"],PARAMETER[\"central_meridian\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]";

    private const string Jgd2011GeographicWkt =
        "GEOGCS[\"JGD2011\",DATUM[\"Japanese_Geodetic_Datum_2011\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"6668\"]]";

    private const string Jgd2000GeographicWkt =
        "GEOGCS[\"JGD2000\",DATUM[\"Japanese_Geodetic_Datum_2000\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4612\"]]";

    private const string Etrs89GeographicWkt =
        "GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]]";

    private const string Etrs89Utm32NWkt =
        "PROJCS[\"ETRS89 / UTM zone 32N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],TOWGS84[0,0,0,0,0,0,0],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",9],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"25832\"]]";

    private const string Etrs89Utm33NWkt =
        "PROJCS[\"ETRS89 / UTM zone 33N\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],TOWGS84[0,0,0,0,0,0,0],AUTHORITY[\"EPSG\",\"6258\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",15],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"25833\"]]";

    private static readonly IReadOnlyList<ProjectionDefinition> BuiltInDefinitions = [
        new(Wgs84Id, "WGS 84 longitude/latitude (EPSG:4326)"),
        new(WebMercatorId, "Web Mercator meters (EPSG:3857)"),
        new(Cgcs2000Id, "CGCS2000 longitude/latitude (EPSG:4490)", Cgcs2000GeographicWkt),
        new(Cgcs2000MercatorId, "CGCS2000 Mercator meters", Cgcs2000MercatorWkt),
        new(Jgd2011Id, "JGD2011 longitude/latitude (EPSG:6668)", Jgd2011GeographicWkt),
        new(Jgd2000Id, "JGD2000 longitude/latitude (EPSG:4612)", Jgd2000GeographicWkt),
        new(Etrs89Id, "ETRS89 longitude/latitude (EPSG:4258)", Etrs89GeographicWkt),
        new(Etrs89Utm32NId, "ETRS89 / UTM zone 32N (EPSG:25832)", Etrs89Utm32NWkt),
        new(Etrs89Utm33NId, "ETRS89 / UTM zone 33N (EPSG:25833)", Etrs89Utm33NWkt),
        new(CustomWktId, "Custom WKT")
    ];

    private static readonly IReadOnlyDictionary<string, string> ProjectionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["wgs84"] = Wgs84Id,
            ["wgs 84"] = Wgs84Id,
            ["epsg4326"] = Wgs84Id,
            ["epsg:4326"] = Wgs84Id,
            ["webmercator"] = WebMercatorId,
            ["web mercator"] = WebMercatorId,
            ["epsg3857"] = WebMercatorId,
            ["epsg:3857"] = WebMercatorId,
            ["epsg900913"] = WebMercatorId,
            ["epsg:900913"] = WebMercatorId,
            ["epsg102100"] = WebMercatorId,
            ["epsg:102100"] = WebMercatorId,
            ["cgcs2000"] = Cgcs2000Id,
            ["epsg4490"] = Cgcs2000Id,
            ["epsg:4490"] = Cgcs2000Id,
            ["cgcs2000:mercator"] = Cgcs2000MercatorId,
            ["jgd2011"] = Jgd2011Id,
            ["epsg6668"] = Jgd2011Id,
            ["epsg:6668"] = Jgd2011Id,
            ["jgd2000"] = Jgd2000Id,
            ["epsg4612"] = Jgd2000Id,
            ["epsg:4612"] = Jgd2000Id,
            ["etrs89"] = Etrs89Id,
            ["epsg4258"] = Etrs89Id,
            ["epsg:4258"] = Etrs89Id,
            ["etrs89utm32n"] = Etrs89Utm32NId,
            ["etrs89 / utm zone 32n"] = Etrs89Utm32NId,
            ["utm32n"] = Etrs89Utm32NId,
            ["epsg25832"] = Etrs89Utm32NId,
            ["epsg:25832"] = Etrs89Utm32NId,
            ["etrs89utm33n"] = Etrs89Utm33NId,
            ["etrs89 / utm zone 33n"] = Etrs89Utm33NId,
            ["utm33n"] = Etrs89Utm33NId,
            ["epsg25833"] = Etrs89Utm33NId,
            ["epsg:25833"] = Etrs89Utm33NId,
            ["custom"] = CustomWktId,
            ["custom:wkt"] = CustomWktId
        };

    public static IReadOnlyList<ProjectionDefinition> GetDefinitions() => BuiltInDefinitions;

    public static string NormalizeProjectionId(string? projectionId) {
        if (string.IsNullOrWhiteSpace(projectionId)) return Wgs84Id;

        var trimmed = projectionId.Trim();
        return ProjectionAliases.TryGetValue(trimmed, out var projection)
            ? projection
            : Wgs84Id;
    }

    public static Func<double, double, GeoPoint> CreateImportTransform(SpatialImportOptions options) {
        return CreateImportTransform(options.SourceProjectionId, options.CustomProjectionWkt);
    }

    public static Func<double, double, GeoPoint> CreateImportTransform(string? projectionId, string? customWkt = null) {
        var normalizedId = NormalizeProjectionId(projectionId);
        if (normalizedId == Wgs84Id) {
            return static (x, y) => ValidatePoint(x, y);
        }

        var transform = CreateCoordinateTransform(normalizedId, Wgs84Id, customWkt);

        return (x, y) => {
            var result = transform(x, y);
            return ValidatePoint(result.X, result.Y);
        };
    }

    public static Func<double, double, (double X, double Y)> CreateCoordinateTransform(
        string? sourceProjectionId,
        string? targetProjectionId,
        string? sourceCustomWkt = null,
        string? targetCustomWkt = null) {
        var normalizedSourceId = NormalizeProjectionId(sourceProjectionId);
        var normalizedTargetId = NormalizeProjectionId(targetProjectionId);
        if (normalizedSourceId == normalizedTargetId) {
            return static (x, y) => (x, y);
        }

        var source = CreateCoordinateSystem(normalizedSourceId, sourceCustomWkt);
        var target = CreateCoordinateSystem(normalizedTargetId, targetCustomWkt);
        var transform = new CoordinateTransformationFactory()
            .CreateFromCoordinateSystems(source, target)
            .MathTransform;

        return (x, y) => {
            var result = transform.Transform([x, y]);
            return (result[0], result[1]);
        };
    }

    public static GeoPoint ValidatePoint(double longitude, double latitude) {
        var point = new GeoPoint(longitude, latitude);
        if (!point.IsValid) {
            throw new InvalidDataException("Spatial data contains coordinates outside the valid longitude/latitude range. Select the correct import projection and try again.");
        }

        return point;
    }

    private static CoordinateSystem CreateCoordinateSystem(string projectionId, string? customWkt) {
        if (projectionId == Wgs84Id) {
            return GeographicCoordinateSystem.WGS84;
        }

        if (projectionId == WebMercatorId) {
            return ProjectedCoordinateSystem.WebMercator;
        }

        var wkt = projectionId == CustomWktId
            ? customWkt?.Trim()
            : BuiltInDefinitions.FirstOrDefault(definition => definition.Id == projectionId)?.WellKnownText;
        if (string.IsNullOrWhiteSpace(wkt)) {
            throw new InvalidDataException("Enter a coordinate system WKT before using the custom projection.");
        }

        try {
            return new CoordinateSystemFactory().CreateFromWkt(wkt);
        } catch (Exception ex) when (ex is not InvalidDataException) {
            throw new InvalidDataException("The configured projection WKT could not be parsed.", ex);
        }
    }
}
