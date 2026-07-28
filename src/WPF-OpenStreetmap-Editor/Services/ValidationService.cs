using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record ValidationContext(
    IReadOnlyList<MapFeature> Features,
    OsmDataset? OsmDataset = null);

public interface IValidationRule {
    string Id { get; }
    IEnumerable<ValidationIssue> Validate(ValidationContext context);
}

public sealed class ValidationService {
    private readonly IReadOnlyList<IValidationRule> _rules;

    public ValidationService(IEnumerable<IValidationRule>? rules = null) {
        _rules = (rules ?? CreateDefaultRules()).ToList().AsReadOnly();
    }

    public IReadOnlyList<IValidationRule> Rules => _rules;

    public IReadOnlyList<ValidationIssue> Validate(ValidationContext context) {
        ArgumentNullException.ThrowIfNull(context);

        return _rules
            .SelectMany(rule => rule.Validate(context))
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.RuleId, StringComparer.Ordinal)
            .ThenBy(static issue => issue.Target.Type)
            .ThenBy(static issue => issue.Target.Id, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<ValidationIssue> ValidateFeatures(IEnumerable<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(features);
        return Validate(new ValidationContext(features.ToList()));
    }

    public IReadOnlyList<ValidationIssue> ValidateDataset(OsmDataset dataset) {
        ArgumentNullException.ThrowIfNull(dataset);
        return Validate(new ValidationContext([], dataset));
    }

    private static IEnumerable<IValidationRule> CreateDefaultRules() {
        yield return new TagValidationRule();
        yield return new GeometryValidationRule();
        yield return new RelationValidationRule();
    }
}
