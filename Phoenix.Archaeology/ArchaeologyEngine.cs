using Phoenix.Core;
using Phoenix.Dependency;

namespace Phoenix.Archaeology;

public sealed class ArchaeologyEngine
{
    private readonly FileSystemForensics _fs = new();
    private readonly ManifestParserRegistry _parsers = new();
    private readonly GitForensics _git = new();

    public ArchaeologyProfile Analyze(string root)
    {
        var files = _fs.Scan(root);
        var relPaths = files.Select(f => f.RelativePath).ToList();

        var manifests = _parsers.DetectAndParse(relPaths, root);
        var ecosystems = manifests.Select(m => m.Ecosystem).Distinct().ToList();

        var (language, buildSystem) = DetectLanguageAndBuild(relPaths, manifests);

        var (earliest, latest) = _fs.TimestampRange(root);
        var git = _git.Analyze(root);

        var notes = new List<string>();
        if (git?.HasGit == true)
            notes.Add($"Git: {git.CommitCount} commits, first {git.FirstCommit:yyyy-MM-dd}, last {git.LastCommit:yyyy-MM-dd}");
        if (manifests.Count > 0)
            notes.Add($"Detected {manifests.Count} dependency manifests across {ecosystems.Count} ecosystem(s).");
        var secretHits = files.Where(f => f.Classification == "secret").ToList();
        if (secretHits.Count > 0)
            notes.Add($"WARNING: {secretHits.Count} potential secret file(s) found (masked, not archived in plaintext).");

        var confidence = ComputeConfidence(manifests.Count, ecosystems.Count, language, buildSystem);

        return new ArchaeologyProfile(
            ProjectName: new DirectoryInfo(root).Name,
            DetectedLanguage: language,
            BuildSystem: buildSystem,
            Ecosystems: ecosystems,
            Manifests: manifests,
            FileCount: files.Count,
            TotalBytes: files.Sum(f => f.Size),
            Notes: notes,
            Confidence: confidence,
            EarliestTimestamp: earliest ?? git?.FirstCommit,
            LatestTimestamp: latest ?? git?.LastCommit);
    }

    private static (string?, string?) DetectLanguageAndBuild(IReadOnlyList<string> paths, IReadOnlyList<DetectedManifest> manifests)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        string? language = null, build = null;

        bool Has(string n) => set.Any(p => p.Equals(n, StringComparison.OrdinalIgnoreCase)
                                           || p.EndsWith("/" + n, StringComparison.OrdinalIgnoreCase));

        if (Has("Cargo.toml")) { language ??= "rust"; build = "cargo"; }
        if (Has("go.mod")) { language ??= "go"; build = "go-modules"; }
        if (Has("pyproject.toml") || Has("requirements.txt") || Has("setup.py")) { language ??= "python"; build ??= "pip"; }
        if (paths.Any(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))) { language ??= "c#"; build ??= "msbuild"; }
        if (Has("pom.xml")) { language ??= "java"; build = "maven"; }
        if (Has("build.gradle") || Has("build.gradle.kts")) { language ??= "java/kotlin"; build = "gradle"; }
        if (Has("composer.json")) { language ??= "php"; build = "composer"; }
        if (Has("Gemfile")) { language ??= "ruby"; build = "bundler"; }
        if (Has("pubspec.yaml")) { language ??= "dart"; build = "pub"; }
        if (Has("Package.swift")) { language ??= "swift"; build = "swift-pm"; }
        if (Has("CMakeLists.txt")) { build ??= "cmake"; language ??= language ?? "c/c++"; }
        if (Has("Makefile")) { build ??= build ?? "make"; language ??= language ?? "c/c++"; }
        // package.json is a weak signal (often co-exists with other toolchains) — check last.
        if (Has("package.json")) { language ??= "javascript/typescript"; build ??= "npm"; }
        if (Has("Dockerfile") || Has("Containerfile") || Has("docker-compose.yml")) build ??= build ?? "docker";

        return (language, build);
    }

    private static double ComputeConfidence(int manifestCount, int ecosystemCount, string? language, string? build)
    {
        double c = 0.4;
        if (manifestCount > 0) c += 0.2;
        c += Math.Min(0.2, ecosystemCount * 0.07);
        if (language != null) c += 0.1;
        if (build != null) c += 0.1;
        return Math.Min(0.99, c);
    }
}
