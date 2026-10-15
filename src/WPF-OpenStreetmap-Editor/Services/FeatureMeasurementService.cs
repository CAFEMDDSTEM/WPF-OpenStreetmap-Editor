using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record FeatureMeasurement(int FeatureCount, int VertexCount, double LengthMeters, double AreaSquareMeters);

public static class FeatureMeasurementService {
    /// <summary>
    /// Lines contribute their length; polygons contribute every ring's perimeter to the length and their area
    /// (part 0 is the outer ring, further parts are holes, except for relation features whose parts are all
    /// outer rings because inner members are not materialized); points contribute only to the counts.
    /// The closing duplicate vertex of a ring is not counted.
    /// </summary>
    public static FeatureMeasurement Measure(IEnumerable<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(features);

        var featureCount = 0;
        var vertexCount = 0;
        var lengthMeters = 0.0;
        var areaSquareMeters = 0.0;
        foreach (var feature in features) {
            featureCount++;
            vertexCount += feature.Parts.Sum(CountVertices);
            switch (feature.GeometryType) {
                case MapGeometryType.LineString:
                    lengthMeters += feature.Parts.Sum(GeoMeasurement.LengthMeters);
                    break;
                case MapGeometryType.Polygon:
                    lengthMeters += feature.Parts.Sum(PerimeterMeters);
                    var holesStartAt = feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation ? int.MaxValue : 1;
                    areaSquareMeters += Math.Max(0.0, feature.Parts
                        .Select((ring, index) => (index < holesStartAt ? 1 : -1) * GeoMeasurement.AreaSquareMeters(ring))
                        .Sum());
                    break;
            }
        }

        return new FeatureMeasurement(featureCount, vertexCount, lengthMeters, areaSquareMeters);
    }

    private static int CountVertices(List<GeoPoint> part) {
        return part.Count > 2 && part[0] == part[^1] ? part.Count - 1 : part.Count;
    }

    private static double PerimeterMeters(List<GeoPoint> ring) {
        var closingSegment = ring.Count > 1 && ring[0] != ring[^1]
            ? GeoMeasurement.DistanceMeters(ring[^1], ring[0])
            : 0.0;
        return GeoMeasurement.LengthMeters(ring) + closingSegment;
    }
}
