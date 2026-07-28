namespace WPF_OpenStreetmap_Editor.Services;

public sealed class ThemeMapStyle {
    public ThemeAreaStyle? GenericArea { get; init; }
    public ThemeAreaStyle? Water { get; init; }
    public ThemeAreaStyle? Farmland { get; init; }
    public ThemeAreaStyle? Forest { get; init; }
    public ThemeAreaStyle? Park { get; init; }
    public ThemeAreaStyle? BuiltArea { get; init; }
    public ThemeAreaStyle? Building { get; init; }
    public ThemeLineStyle? GenericLine { get; init; }
    public ThemeLineStyle? Boundary { get; init; }
    public ThemeLineStyle? Waterway { get; init; }
    public ThemeLineStyle? Rail { get; init; }
    public ThemeLineStyle? Path { get; init; }
    public ThemeLineStyle? TrackRoad { get; init; }
    public ThemeLineStyle? ServiceRoad { get; init; }
    public ThemeLineStyle? ResidentialRoad { get; init; }
    public ThemeLineStyle? LivingStreetRoad { get; init; }
    public ThemeLineStyle? UnclassifiedRoad { get; init; }
    public ThemeLineStyle? LocalRoad { get; init; }
    public ThemeLineStyle? TertiaryRoad { get; init; }
    public ThemeLineStyle? SecondaryRoad { get; init; }
    public ThemeLineStyle? PrimaryRoad { get; init; }
    public ThemeLineStyle? TrunkRoad { get; init; }
    public ThemeLineStyle? Motorway { get; init; }
    public ThemePointStyle? GenericPoint { get; init; }
    public ThemePointStyle? Poi { get; init; }
    public ThemePointStyle? FoodPoint { get; init; }
    public ThemePointStyle? ParkingPoint { get; init; }
    public ThemePointStyle? MedicalPoint { get; init; }
    public ThemePointStyle? EducationPoint { get; init; }
    public ThemePointStyle? TransitPoint { get; init; }
    public ThemePointStyle? FuelPoint { get; init; }
    public ThemePointStyle? BankPoint { get; init; }
    public ThemePointStyle? ToiletPoint { get; init; }
    public ThemePointStyle? SafetyPoint { get; init; }
    public ThemePointStyle? PostPoint { get; init; }
    public ThemePointStyle? HotelPoint { get; init; }
    public ThemePointStyle? ShopPoint { get; init; }
    public ThemePointStyle? TourismPoint { get; init; }
    public ThemePointStyle? Place { get; init; }

    internal static ThemeMapStyle Complete(ThemeMapStyle? style, string baseTheme) {
        var defaults = baseTheme == "dark" ? CreateDarkDefault() : CreateLightDefault();
        return new ThemeMapStyle {
            GenericArea = CompleteArea(style?.GenericArea, defaults.GenericArea!),
            Water = CompleteArea(style?.Water, defaults.Water!),
            Farmland = CompleteArea(style?.Farmland, defaults.Farmland!),
            Forest = CompleteArea(style?.Forest, defaults.Forest!),
            Park = CompleteArea(style?.Park, defaults.Park!),
            BuiltArea = CompleteArea(style?.BuiltArea, defaults.BuiltArea!),
            Building = CompleteArea(style?.Building, defaults.Building!),
            GenericLine = CompleteLine(style?.GenericLine, defaults.GenericLine!),
            Boundary = CompleteLine(style?.Boundary, defaults.Boundary!),
            Waterway = CompleteLine(style?.Waterway, defaults.Waterway!),
            Rail = CompleteLine(style?.Rail, defaults.Rail!),
            Path = CompleteLine(style?.Path, defaults.Path!),
            TrackRoad = CompleteLine(style?.TrackRoad, defaults.TrackRoad!),
            ServiceRoad = CompleteLine(style?.ServiceRoad, defaults.ServiceRoad!),
            ResidentialRoad = CompleteLine(style?.ResidentialRoad, defaults.ResidentialRoad!),
            LivingStreetRoad = CompleteLine(style?.LivingStreetRoad, defaults.LivingStreetRoad!),
            UnclassifiedRoad = CompleteLine(style?.UnclassifiedRoad, defaults.UnclassifiedRoad!),
            LocalRoad = CompleteLine(style?.LocalRoad, defaults.LocalRoad!),
            TertiaryRoad = CompleteLine(style?.TertiaryRoad, defaults.TertiaryRoad!),
            SecondaryRoad = CompleteLine(style?.SecondaryRoad, defaults.SecondaryRoad!),
            PrimaryRoad = CompleteLine(style?.PrimaryRoad, defaults.PrimaryRoad!),
            TrunkRoad = CompleteLine(style?.TrunkRoad, defaults.TrunkRoad!),
            Motorway = CompleteLine(style?.Motorway, defaults.Motorway!),
            GenericPoint = CompletePoint(style?.GenericPoint, defaults.GenericPoint!),
            Poi = CompletePoint(style?.Poi, defaults.Poi!),
            FoodPoint = CompletePoint(style?.FoodPoint, defaults.FoodPoint!),
            ParkingPoint = CompletePoint(style?.ParkingPoint, defaults.ParkingPoint!),
            MedicalPoint = CompletePoint(style?.MedicalPoint, defaults.MedicalPoint!),
            EducationPoint = CompletePoint(style?.EducationPoint, defaults.EducationPoint!),
            TransitPoint = CompletePoint(style?.TransitPoint, defaults.TransitPoint!),
            FuelPoint = CompletePoint(style?.FuelPoint, defaults.FuelPoint!),
            BankPoint = CompletePoint(style?.BankPoint, defaults.BankPoint!),
            ToiletPoint = CompletePoint(style?.ToiletPoint, defaults.ToiletPoint!),
            SafetyPoint = CompletePoint(style?.SafetyPoint, defaults.SafetyPoint!),
            PostPoint = CompletePoint(style?.PostPoint, defaults.PostPoint!),
            HotelPoint = CompletePoint(style?.HotelPoint, defaults.HotelPoint!),
            ShopPoint = CompletePoint(style?.ShopPoint, defaults.ShopPoint!),
            TourismPoint = CompletePoint(style?.TourismPoint, defaults.TourismPoint!),
            Place = CompletePoint(style?.Place, defaults.Place!)
        };
    }

    private static ThemeMapStyle CreateLightDefault() {
        return new ThemeMapStyle {
            GenericArea = new ThemeAreaStyle { Fill = "#E6E8EB", Stroke = "#AAB3BC", StrokeWidth = 0.8 },
            Water = new ThemeAreaStyle { Fill = "#AAD3DF", Stroke = "#72B4C7", StrokeWidth = 0.9 },
            Farmland = new ThemeAreaStyle { Fill = "#EEF0D5", Stroke = "#C7C9AE", StrokeWidth = 0.8 },
            Forest = new ThemeAreaStyle { Fill = "#ADD19E", Stroke = "#7FA36E", StrokeWidth = 0.8 },
            Park = new ThemeAreaStyle { Fill = "#C8FACC", Stroke = "#83B36A", StrokeWidth = 0.8 },
            BuiltArea = new ThemeAreaStyle { Fill = "#E0DFDF", Stroke = "#B9B9B9", StrokeWidth = 0.8 },
            Building = new ThemeAreaStyle { Fill = "#D9D0C9", Stroke = "#B5A99F", StrokeWidth = 0.9 },
            GenericLine = new ThemeLineStyle { Stroke = "#6F7A86", Casing = "#6F7A86", StrokeWidth = 1.2, CasingWidth = 1.2 },
            Boundary = new ThemeLineStyle { Stroke = "#8C78A8", Casing = "#8C78A8", StrokeWidth = 1.2, CasingWidth = 1.2, DashArray = [6, 4] },
            Waterway = new ThemeLineStyle { Stroke = "#5BA7C8", Casing = "#5BA7C8", StrokeWidth = 1.6, CasingWidth = 1.6 },
            Rail = new ThemeLineStyle { Stroke = "#777777", Casing = "#FFFFFF", StrokeWidth = 1.4, CasingWidth = 3.2, DashArray = [8, 3] },
            Path = new ThemeLineStyle { Stroke = "#D27C5D", Casing = "#D27C5D", StrokeWidth = 1.3, CasingWidth = 1.3, DashArray = [4, 3] },
            TrackRoad = new ThemeLineStyle { Stroke = "#A98B6C", Casing = "#D2C0AD", StrokeWidth = 0.9, CasingWidth = 1.6, DashArray = [6, 4] },
            ServiceRoad = new ThemeLineStyle { Stroke = "#FFFFFF", Casing = "#D0D6DD", StrokeWidth = 1.0, CasingWidth = 2.2 },
            ResidentialRoad = new ThemeLineStyle { Stroke = "#F7F7F7", Casing = "#C8CDD2", StrokeWidth = 1.8, CasingWidth = 3.4 },
            LivingStreetRoad = new ThemeLineStyle { Stroke = "#F8F5F0", Casing = "#C7C0B8", StrokeWidth = 1.8, CasingWidth = 3.2 },
            UnclassifiedRoad = new ThemeLineStyle { Stroke = "#F1E9DF", Casing = "#BDB4A8", StrokeWidth = 2.0, CasingWidth = 3.4 },
            LocalRoad = new ThemeLineStyle { Stroke = "#FFFFFF", Casing = "#B8B8B8", StrokeWidth = 2.2, CasingWidth = 4.0 },
            TertiaryRoad = new ThemeLineStyle { Stroke = "#F8F7D8", Casing = "#8F9B2A", StrokeWidth = 2.6, CasingWidth = 4.4 },
            SecondaryRoad = new ThemeLineStyle { Stroke = "#F7FABF", Casing = "#707D05", StrokeWidth = 3.0, CasingWidth = 5.0 },
            PrimaryRoad = new ThemeLineStyle { Stroke = "#FCD6A4", Casing = "#A06B00", StrokeWidth = 3.4, CasingWidth = 5.6 },
            TrunkRoad = new ThemeLineStyle { Stroke = "#F0B8C0", Casing = "#B44D72", StrokeWidth = 3.8, CasingWidth = 6.2 },
            Motorway = new ThemeLineStyle { Stroke = "#E892A2", Casing = "#DC2A67", StrokeWidth = 4.2, CasingWidth = 6.8 },
            GenericPoint = new ThemePointStyle { Fill = "#FFFFFF", Stroke = "#4A5562", Radius = 3.5, StrokeWidth = 1.0 },
            Poi = new ThemePointStyle { Fill = "#FFFFFF", Stroke = "#4A5562", Radius = 4.0, StrokeWidth = 1.0 },
            FoodPoint = new ThemePointStyle { Fill = "#FFF3BF", Stroke = "#8C5A00", Radius = 5.0, StrokeWidth = 1.0 },
            ParkingPoint = new ThemePointStyle { Fill = "#DEEFFF", Stroke = "#246BA7", Radius = 5.0, StrokeWidth = 1.0 },
            MedicalPoint = new ThemePointStyle { Fill = "#FFE3E3", Stroke = "#C92A2A", Radius = 5.0, StrokeWidth = 1.0 },
            EducationPoint = new ThemePointStyle { Fill = "#FFFFE5", Stroke = "#736C1D", Radius = 5.0, StrokeWidth = 1.0 },
            TransitPoint = new ThemePointStyle { Fill = "#E7F5FF", Stroke = "#1864AB", Radius = 5.0, StrokeWidth = 1.0 },
            FuelPoint = new ThemePointStyle { Fill = "#FFF4CC", Stroke = "#B26A00", Radius = 5.0, StrokeWidth = 1.0 },
            BankPoint = new ThemePointStyle { Fill = "#E8F0FF", Stroke = "#4263EB", Radius = 5.0, StrokeWidth = 1.0 },
            ToiletPoint = new ThemePointStyle { Fill = "#F1F3F5", Stroke = "#495057", Radius = 5.0, StrokeWidth = 1.0 },
            SafetyPoint = new ThemePointStyle { Fill = "#FFE3E3", Stroke = "#D9480F", Radius = 5.0, StrokeWidth = 1.0 },
            PostPoint = new ThemePointStyle { Fill = "#FFF0F6", Stroke = "#C2255C", Radius = 5.0, StrokeWidth = 1.0 },
            HotelPoint = new ThemePointStyle { Fill = "#F3E8FF", Stroke = "#7048E8", Radius = 5.0, StrokeWidth = 1.0 },
            ShopPoint = new ThemePointStyle { Fill = "#FFE8F0", Stroke = "#A61E4D", Radius = 5.0, StrokeWidth = 1.0 },
            TourismPoint = new ThemePointStyle { Fill = "#F3E8FF", Stroke = "#660033", Radius = 5.0, StrokeWidth = 1.0 },
            Place = new ThemePointStyle { Fill = "#3B4148", Stroke = "#FFFFFF", Radius = 4.5, StrokeWidth = 1.4 }
        };
    }

    private static ThemeMapStyle CreateDarkDefault() {
        return new ThemeMapStyle {
            GenericArea = new ThemeAreaStyle { Fill = "#343A42", Stroke = "#69717A", StrokeWidth = 0.8 },
            Water = new ThemeAreaStyle { Fill = "#264F67", Stroke = "#5DA7C8", StrokeWidth = 0.9 },
            Farmland = new ThemeAreaStyle { Fill = "#4C4930", Stroke = "#8B8754", StrokeWidth = 0.8 },
            Forest = new ThemeAreaStyle { Fill = "#1F3F2C", Stroke = "#4D8B58", StrokeWidth = 0.8 },
            Park = new ThemeAreaStyle { Fill = "#284B32", Stroke = "#62A36A", StrokeWidth = 0.8 },
            BuiltArea = new ThemeAreaStyle { Fill = "#3A3A38", Stroke = "#62625D", StrokeWidth = 0.8 },
            Building = new ThemeAreaStyle { Fill = "#6B5540", Stroke = "#B08A62", StrokeWidth = 0.9 },
            GenericLine = new ThemeLineStyle { Stroke = "#CBD5E1", Casing = "#CBD5E1", StrokeWidth = 1.2, CasingWidth = 1.2 },
            Boundary = new ThemeLineStyle { Stroke = "#C3A7FF", Casing = "#C3A7FF", StrokeWidth = 1.2, CasingWidth = 1.2, DashArray = [6, 4] },
            Waterway = new ThemeLineStyle { Stroke = "#66C2FF", Casing = "#66C2FF", StrokeWidth = 1.6, CasingWidth = 1.6 },
            Rail = new ThemeLineStyle { Stroke = "#C9CDD2", Casing = "#1B1E22", StrokeWidth = 1.4, CasingWidth = 3.2, DashArray = [8, 3] },
            Path = new ThemeLineStyle { Stroke = "#F0A070", Casing = "#F0A070", StrokeWidth = 1.3, CasingWidth = 1.3, DashArray = [4, 3] },
            TrackRoad = new ThemeLineStyle { Stroke = "#B09A7F", Casing = "#5A4A3A", StrokeWidth = 0.9, CasingWidth = 1.6, DashArray = [6, 4] },
            ServiceRoad = new ThemeLineStyle { Stroke = "#E8EBEE", Casing = "#5C646C", StrokeWidth = 1.0, CasingWidth = 2.2 },
            ResidentialRoad = new ThemeLineStyle { Stroke = "#EDEDED", Casing = "#60666D", StrokeWidth = 1.8, CasingWidth = 3.4 },
            LivingStreetRoad = new ThemeLineStyle { Stroke = "#E9E3DC", Casing = "#6B615A", StrokeWidth = 1.8, CasingWidth = 3.2 },
            UnclassifiedRoad = new ThemeLineStyle { Stroke = "#E2DDD4", Casing = "#6D655C", StrokeWidth = 2.0, CasingWidth = 3.4 },
            LocalRoad = new ThemeLineStyle { Stroke = "#EDEDED", Casing = "#60666D", StrokeWidth = 2.2, CasingWidth = 4.0 },
            TertiaryRoad = new ThemeLineStyle { Stroke = "#F0E1A6", Casing = "#84743D", StrokeWidth = 2.6, CasingWidth = 4.4 },
            SecondaryRoad = new ThemeLineStyle { Stroke = "#F2D46B", Casing = "#7A6943", StrokeWidth = 3.0, CasingWidth = 5.0 },
            PrimaryRoad = new ThemeLineStyle { Stroke = "#F3A65E", Casing = "#815837", StrokeWidth = 3.4, CasingWidth = 5.6 },
            TrunkRoad = new ThemeLineStyle { Stroke = "#FF99A8", Casing = "#8F515E", StrokeWidth = 3.8, CasingWidth = 6.2 },
            Motorway = new ThemeLineStyle { Stroke = "#FF7F92", Casing = "#894654", StrokeWidth = 4.2, CasingWidth = 6.8 },
            GenericPoint = new ThemePointStyle { Fill = "#1B1E22", Stroke = "#E5E7EB", Radius = 3.5, StrokeWidth = 1.0 },
            Poi = new ThemePointStyle { Fill = "#1B1E22", Stroke = "#E5E7EB", Radius = 4.0, StrokeWidth = 1.0 },
            FoodPoint = new ThemePointStyle { Fill = "#4B3B12", Stroke = "#FFD43B", Radius = 5.0, StrokeWidth = 1.0 },
            ParkingPoint = new ThemePointStyle { Fill = "#16324A", Stroke = "#74C0FC", Radius = 5.0, StrokeWidth = 1.0 },
            MedicalPoint = new ThemePointStyle { Fill = "#4A1D1D", Stroke = "#FF8787", Radius = 5.0, StrokeWidth = 1.0 },
            EducationPoint = new ThemePointStyle { Fill = "#47451A", Stroke = "#FFF3BF", Radius = 5.0, StrokeWidth = 1.0 },
            TransitPoint = new ThemePointStyle { Fill = "#12324A", Stroke = "#66D9E8", Radius = 5.0, StrokeWidth = 1.0 },
            FuelPoint = new ThemePointStyle { Fill = "#4B3B12", Stroke = "#FFD43B", Radius = 5.0, StrokeWidth = 1.0 },
            BankPoint = new ThemePointStyle { Fill = "#1B2D4A", Stroke = "#74C0FC", Radius = 5.0, StrokeWidth = 1.0 },
            ToiletPoint = new ThemePointStyle { Fill = "#2A2D31", Stroke = "#ADB5BD", Radius = 5.0, StrokeWidth = 1.0 },
            SafetyPoint = new ThemePointStyle { Fill = "#4A1D1D", Stroke = "#FF8787", Radius = 5.0, StrokeWidth = 1.0 },
            PostPoint = new ThemePointStyle { Fill = "#3F1730", Stroke = "#F783AC", Radius = 5.0, StrokeWidth = 1.0 },
            HotelPoint = new ThemePointStyle { Fill = "#2D1632", Stroke = "#E599F7", Radius = 5.0, StrokeWidth = 1.0 },
            ShopPoint = new ThemePointStyle { Fill = "#4A1F32", Stroke = "#F783AC", Radius = 5.0, StrokeWidth = 1.0 },
            TourismPoint = new ThemePointStyle { Fill = "#2D1632", Stroke = "#E599F7", Radius = 5.0, StrokeWidth = 1.0 },
            Place = new ThemePointStyle { Fill = "#F4F6F8", Stroke = "#1B1E22", Radius = 4.5, StrokeWidth = 1.4 }
        };
    }

    private static ThemeAreaStyle CompleteArea(ThemeAreaStyle? style, ThemeAreaStyle defaults) {
        return new ThemeAreaStyle {
            Fill = UseColor(style?.Fill, defaults.Fill),
            Stroke = UseColor(style?.Stroke, defaults.Stroke),
            StrokeWidth = style?.StrokeWidth ?? defaults.StrokeWidth
        };
    }

    private static ThemeLineStyle CompleteLine(ThemeLineStyle? style, ThemeLineStyle defaults) {
        return new ThemeLineStyle {
            Stroke = UseColor(style?.Stroke, defaults.Stroke),
            Casing = UseColor(style?.Casing, defaults.Casing),
            StrokeWidth = style?.StrokeWidth ?? defaults.StrokeWidth,
            CasingWidth = style?.CasingWidth ?? defaults.CasingWidth,
            DashArray = style?.DashArray is { Length: > 0 } ? style.DashArray : defaults.DashArray
        };
    }

    private static ThemePointStyle CompletePoint(ThemePointStyle? style, ThemePointStyle defaults) {
        return new ThemePointStyle {
            Fill = UseColor(style?.Fill, defaults.Fill),
            Stroke = UseColor(style?.Stroke, defaults.Stroke),
            Radius = style?.Radius ?? defaults.Radius,
            StrokeWidth = style?.StrokeWidth ?? defaults.StrokeWidth
        };
    }

    private static string UseColor(string? value, string? fallback) {
        return string.IsNullOrWhiteSpace(value) ? fallback ?? "#000000" : value.Trim();
    }
}

public sealed class ThemeAreaStyle {
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
    public double? StrokeWidth { get; init; }
}

public sealed class ThemeLineStyle {
    public string? Stroke { get; init; }
    public string? Casing { get; init; }
    public double? StrokeWidth { get; init; }
    public double? CasingWidth { get; init; }
    public double[]? DashArray { get; init; }
}

public sealed class ThemePointStyle {
    public string? Fill { get; init; }
    public string? Stroke { get; init; }
    public double? Radius { get; init; }
    public double? StrokeWidth { get; init; }
}
