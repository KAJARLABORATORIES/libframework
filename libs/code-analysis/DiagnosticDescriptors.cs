using Microsoft.CodeAnalysis;

namespace kajarlabs.Framework.CodeAnalysis;

internal static class DiagnosticDescriptors
{
    private const string category = "DependencyInjection";

    public static readonly DiagnosticDescriptor MISSING_PARTIAL = new(
        id: "KJDI001",
        title: "Missing partial modifier",
        messageFormat: "Type '{0}' uses [Cached] or [Resolved] and must be declared partial along with any enclosing types",
        category: category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor INVALID_RESOLVED_TARGET = new(
        id: "KJDI002",
        title: "Invalid [Resolved] target",
        messageFormat: "[Resolved] cannot be applied to '{0}': {1}",
        category: category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor INVALID_CACHED_TARGET = new(
        id: "KJDI003",
        title: "Invalid [Cached] target",
        messageFormat: "[Cached] cannot be applied to '{0}': {1}",
        category: category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor INVALID_CACHED_AS_TYPE = new(
        id: "KJDI004",
        title: "Invalid [Cached] registration type",
        messageFormat: "Cannot cache '{0}' as '{1}': {2}",
        category: category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UNSUPPORTED_CONTAINING_TYPE = new(
        id: "KJDI005",
        title: "Unsupported containing type for dependency injection",
        messageFormat: "Type '{0}' uses [Cached] or [Resolved] and must derive from Drawable or Game",
        category: category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor GetDescriptor(DiagnosticRule rule)
    {
        return rule switch
        {
            DiagnosticRule.MissingPartial => MISSING_PARTIAL,
            DiagnosticRule.InvalidResolvedTarget => INVALID_RESOLVED_TARGET,
            DiagnosticRule.InvalidCachedTarget => INVALID_CACHED_TARGET,
            DiagnosticRule.InvalidCachedAsType => INVALID_CACHED_AS_TYPE,
            DiagnosticRule.UnsupportedContainingType => UNSUPPORTED_CONTAINING_TYPE,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
        };
    }
}

internal enum DiagnosticRule : byte
{
    MissingPartial = 1,
    InvalidResolvedTarget = 2,
    InvalidCachedTarget = 3,
    InvalidCachedAsType = 4,
    UnsupportedContainingType = 5,
}
