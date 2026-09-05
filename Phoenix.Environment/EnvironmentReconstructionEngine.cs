using System.Text.RegularExpressions;
using Phoenix.Core;

namespace Phoenix.Environment;

public sealed record EnvironmentFinding(
    string Kind,
    string Value,
    double Confidence,
    string Evidence);

public sealed record ReconstructedEnvironment(
    IReadOnlyList<EnvironmentFinding> Findings,
    string? RecommendedRuntime,
    string? RecommendedCompiler,
    string? RecommendedOs,
    string? RecommendedArchitecture);

public sealed class EnvironmentReconstructionEngine
{
    public ReconstructedEnvironment Reconstruct(string root)
    {
        var findings = new List<EnvironmentFinding>();

        // Python
        DetectPython(root, findings);
        // Node
        DetectNode(root, findings);
        // Rust
        DetectRust(root, findings);
        // Go
        DetectGo(root, findings);
        // .NET
        DetectDotNet(root, findings);
        // Java / JVM
        DetectJvm(root, findings);
        // Container base image
        DetectContainer(root, findings);

        var runtime = Highest(findings, "runtime")?.Value;
        var compiler = Highest(findings, "compiler")?.Value;
        var os = Highest(findings, "os")?.Value;
        var arch = Highest(findings, "architecture")?.Value
                   ?? (System.Environment.Is64BitOperatingSystem ? "x64" : "x86");

        return new ReconstructedEnvironment(findings, runtime, compiler, os, arch);
    }

    private static EnvironmentFinding? Highest(IEnumerable<EnvironmentFinding> f, string kind)
        => f.Where(x => x.Kind == kind).OrderByDescending(x => x.Confidence).FirstOrDefault();

    private static void Add(IList<EnvironmentFinding> list, string kind, string value, double conf, string evidence)
        => list.Add(new EnvironmentFinding(kind, value, conf, evidence));

    private void DetectPython(string root, IList<EnvironmentFinding> f)
    {
        var py = Path.Combine(root, "pyproject.toml");
        if (File.Exists(py))
        {
            var t = File.ReadAllText(py);
            var m = Regex.Match(t, "python\\s*=\\s*[\"']?\\^?(?<v>[0-9]+\\.[0-9]+)");
            if (m.Success) Add(f, "runtime", "Python " + m.Groups["v"].Value, 0.9, "pyproject.toml [tool.poetry.dependencies] python");
        }
        foreach (var req in Directory.EnumerateFiles(root, "*.txt", SearchOption.TopDirectoryOnly)
                     .Where(p => Path.GetFileName(p).Contains("requirement", StringComparison.OrdinalIgnoreCase)))
        {
            var t = File.ReadAllText(req);
            var m = Regex.Match(t, "python_requires\\s*[=~>=<]+\\s*[\"']?(?<v>[0-9]+\\.[0-9]+)");
            if (m.Success) Add(f, "runtime", "Python " + m.Groups["v"].Value, 0.7, Path.GetFileName(req));
        }
        var docker = Path.Combine(root, "Dockerfile");
        if (File.Exists(docker))
        {
            var m = Regex.Match(File.ReadAllText(docker), "FROM\\s+(?<img>python:(?<v>[0-9]+\\.[0-9]+))");
            if (m.Success)
            {
                Add(f, "runtime", "Python " + m.Groups["v"].Value, 0.85, "Dockerfile FROM " + m.Groups["img"].Value);
                Add(f, "os", "Linux", 0.6, "Dockerfile base image");
            }
        }
    }

    private void DetectNode(string root, IList<EnvironmentFinding> f)
    {
        var nvm = Path.Combine(root, ".nvmrc");
        if (File.Exists(nvm))
            Add(f, "runtime", "Node " + File.ReadAllText(nvm).Trim(), 0.9, ".nvmrc");
        var pkg = Path.Combine(root, "package.json");
        if (File.Exists(pkg))
        {
            var m = Regex.Match(File.ReadAllText(pkg), "\"engines\"\\s*:\\s*\\{[^}]*\"node\"\\s*:\\s*\"(?<v>[^\"]+)\"");
            if (m.Success) Add(f, "runtime", "Node " + m.Groups["v"].Value, 0.8, "package.json engines.node");
        }
    }

    private void DetectRust(string root, IList<EnvironmentFinding> f)
    {
        foreach (var name in new[] { "rust-toolchain", "rust-toolchain.toml" })
        {
            var p = Path.Combine(root, name);
            if (File.Exists(p))
            {
                var t = File.ReadAllText(p);
                var m = Regex.Match(t, "channel\\s*=\\s*[\"']?(?<v>[0-9]+\\.[0-9]+(\\.[0-9]+)?)");
                Add(f, "compiler", "Rust " + (m.Success ? m.Groups["v"].Value : "pinned"), 0.9, name);
            }
        }
        if (File.Exists(Path.Combine(root, "Cargo.toml")))
            Add(f, "compiler", "Rust (cargo)", 0.6, "Cargo.toml present");
    }

    private void DetectGo(string root, IList<EnvironmentFinding> f)
    {
        var g = Path.Combine(root, "go.mod");
        if (File.Exists(g))
        {
            var m = Regex.Match(File.ReadAllText(g), "^go\\s+(?<v>[0-9]+\\.[0-9]+)", RegexOptions.Multiline);
            if (m.Success) Add(f, "runtime", "Go " + m.Groups["v"].Value, 0.9, "go.mod go directive");
        }
    }

    private void DetectDotNet(string root, IList<EnvironmentFinding> f)
    {
        var g = Path.Combine(root, "global.json");
        if (File.Exists(g))
        {
            var m = Regex.Match(File.ReadAllText(g), "\"version\"\\s*:\\s*\"(?<v>[0-9]+\\.[0-9]+)");
            if (m.Success) Add(f, "runtime", ".NET " + m.Groups["v"].Value, 0.9, "global.json");
        }
        if (Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Any())
            Add(f, "compiler", "MSBuild/Roslyn", 0.6, "*.csproj present");
    }

    private void DetectJvm(string root, IList<EnvironmentFinding> f)
    {
        var pom = Path.Combine(root, "pom.xml");
        if (File.Exists(pom))
        {
            var m = Regex.Match(File.ReadAllText(pom), "<maven.compiler.(?:source|release)>(?<v>[0-9]+)");
            if (m.Success) Add(f, "runtime", "Java " + m.Groups["v"].Value, 0.8, "pom.xml maven.compiler");
            Add(f, "compiler", "Maven", 0.6, "pom.xml");
        }
        var gradle = Path.Combine(root, "build.gradle");
        if (File.Exists(gradle))
            Add(f, "compiler", "Gradle", 0.6, "build.gradle");
    }

    private void DetectContainer(string root, IList<EnvironmentFinding> f)
    {
        var d = Path.Combine(root, "Dockerfile");
        if (!File.Exists(d)) return;
        foreach (var line in File.ReadAllLines(d))
        {
            var m = Regex.Match(line, "FROM\\s+(?<img>[^\\s]+)");
            if (m.Success) Add(f, "system", "container:" + m.Groups["img"].Value, 0.7, "Dockerfile FROM");
        }
    }
}
