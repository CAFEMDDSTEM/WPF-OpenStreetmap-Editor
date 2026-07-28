namespace WPF_OpenStreetmap_Editor.Models;

public enum ValidationSeverity {
    Information,
    Warning,
    Error
}

public enum ValidationObjectType {
    Feature,
    Node,
    Way,
    Relation
}

public enum ValidationFixKind {
    RemoveTag,
    RemoveConsecutiveDuplicatePoints,
    ClosePolygon
}

public sealed record ValidationObjectReference(ValidationObjectType Type, string Id);

public sealed record ValidationFixSuggestion(
    ValidationFixKind Kind,
    string Description,
    string? TagKey = null);

public sealed record ValidationIssue(
    string RuleId,
    ValidationSeverity Severity,
    string Message,
    ValidationObjectReference Target,
    ValidationFixSuggestion? SuggestedFix = null);
