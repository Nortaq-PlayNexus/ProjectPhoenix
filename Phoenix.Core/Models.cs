namespace Phoenix.Core;

public enum DependencyState
{
    Unknown,
    Healthy,
    Aging,
    AtRisk,
    Abandoned,
    Missing,
    Unavailable,
    Corrupted,
    Unreproducible,
    Vulnerable
}

public enum RecoveryStatus
{
    Unresolved,
    BuildsButDoesNotRun,
    RunsButTestsFail,
    PartiallyResurrected,
    FullyResurrected
}

public enum InsuranceStatus
{
    Uninsured,
    PartiallyInsured,
    AtRisk,
    Insured
}

public record ProjectRecord(
    long Id,
    string Name,
    string? SourcePath,
    string? Fingerprint,
    string? EcoSystem,
    string? BuildSystem,
    string? DetectedLanguage,
    long? CreatedUtc,
    string Status);

public record FileRecord(
    long Id,
    long ProjectId,
    string RelativePath,
    string AbsolutePath,
    string Sha256,
    long Size,
    string? Classification);

public record DependencyRecord(
    long Id,
    long ProjectId,
    string Name,
    string? Version,
    string Ecosystem,
    string Scope,
    DependencyState State,
    string? Registry,
    string? SourceUrl,
    string? License,
    int RiskScore,
    string? LocalCacheStatus,
    string? ArchiveStatus);

public record ProjectRow(long Id, string Name, string? Status, string? SnapshotHash, string CreatedAt);

public record SnapshotRecord(
    long Id,
    long ProjectId,
    string SnapshotPath,
    string Sha256,
    long CreatedUtc,
    string Kind);

public record CapsuleRecord(
    long Id,
    long ProjectId,
    string CapsulePath,
    string ManifestSha256,
    string RecoveryStatus,
    long CreatedUtc);

public record EventRecord(
    long Id,
    long? ProjectId,
    long TimestampUtc,
    string Operation,
    string? Agent,
    string? Tool,
    string? InputHash,
    string? OutputHash,
    string Result);
