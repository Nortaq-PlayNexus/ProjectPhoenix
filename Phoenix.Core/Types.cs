namespace Phoenix.Core;

public record DetectedManifest(
    string Path,
    string Ecosystem,
    string BuildSystemHint,
    IReadOnlyList<RawDependency> Dependencies);

public record RawDependency(
    string Name,
    string? Version,
    string Scope,
    string Ecosystem);

public record ArchaeologyProfile(
    string ProjectName,
    string? DetectedLanguage,
    string? BuildSystem,
    IReadOnlyList<string> Ecosystems,
    IReadOnlyList<DetectedManifest> Manifests,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> Notes,
    double Confidence,
    DateTime? EarliestTimestamp,
    DateTime? LatestTimestamp);

public record DependencyNode(
    string Name,
    string Ecosystem,
    string? Version,
    string Scope,
    IReadOnlyList<DependencyNode> Children);

public record ProjectTimelineEntry(DateTime When, string Event);

public static class KnownEcosystems
{
    public const string Python = "python";
    public const string Node = "node";
    public const string Rust = "rust";
    public const string Go = "go";
    public const string Java = "java";
    public const string DotNet = "dotnet";
    public const string Cpp = "cpp";
    public const string Php = "php";
    public const string Ruby = "ruby";
    public const string Dart = "dart";
    public const string Swift = "swift";
    public const string Kotlin = "kotlin";
    public const string Container = "container";
    public const string Generic = "generic";
}
