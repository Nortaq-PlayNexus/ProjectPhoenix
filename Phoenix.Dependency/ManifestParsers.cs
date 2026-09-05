using Phoenix.Core;

namespace Phoenix.Dependency;

public interface IManifestParser
{
    string Ecosystem { get; }
    string BuildSystemHint { get; }
    bool Matches(string fileName);
    DetectedManifest Parse(string absPath, string relPath);
}

public sealed class ManifestParserRegistry
{
    private readonly List<IManifestParser> _parsers = new();

    public ManifestParserRegistry()
    {
        foreach (var p in new IManifestParser[]
                 {
                     new RequirementsTxtParser(),
                     new PyProjectTomlParser(),
                     new PackageJsonParser(),
                     new CargoTomlParser(),
                     new GoModParser(),
                     new PomXmlParser(),
                     new BuildGradleParser(),
                     new CsProjParser(),
                     new ComposerJsonParser(),
                     new GemfileParser(),
                     new PubspecYamlParser(),
                     new DockerfileParser()
                 })
            _parsers.Add(p);
    }

    public IReadOnlyList<DetectedManifest> DetectAndParse(IReadOnlyList<string> relativePaths, string root)
    {
        var outList = new List<DetectedManifest>();
        foreach (var rel in relativePaths)
        {
            var name = Path.GetFileName(rel);
            var parser = _parsers.FirstOrDefault(p => p.Matches(name));
            if (parser == null) continue;
            try
            {
                var abs = Path.Combine(root, rel);
                outList.Add(parser.Parse(abs, rel));
            }
            catch
            {
                // untrusted/partial input: skip rather than crash the pipeline
            }
        }
        return outList;
    }
}

internal static class ParserUtil
{
    public static (string Name, string? Version) SplitSpec(string spec)
    {
        spec = spec.Trim();
        var idx = spec.IndexOfAny(new[] { '=', '>', '<', '~', '^', ' ' });
        if (idx < 0) return (spec, null);
        var name = spec[..idx].Trim();
        var ver = spec[idx..].Trim().Trim('"', '\'');
        // strip a leading equality operator so "==2.2.8" -> "2.2.8" (keep ranges like ">=2.0")
        while (ver.Length > 0 && ver[0] == '=')
            ver = ver[1..].Trim().Trim('"', '\'');
        return (name, string.IsNullOrWhiteSpace(ver) ? null : ver);
    }
}

internal sealed class RequirementsTxtParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Python;
    public string BuildSystemHint => "pip";
    public bool Matches(string f) => f.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && f.Contains("requirement");
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        foreach (var raw in File.ReadLines(abs))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0 || line.StartsWith("-")) continue;
            var (n, v) = ParserUtil.SplitSpec(line);
            deps.Add(new RawDependency(n, v, "direct", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class PyProjectTomlParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Python;
    public string BuildSystemHint => "pyproject";
    public bool Matches(string f) => f.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var lines = File.ReadAllText(abs).Split('\n');
        string? section = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                section = line.Trim('[', ']').Split(']')[0];
                continue;
            }
            if (section == null) continue;
            var inDeps = section is "tool.poetry.dependencies" or "project.dependencies" or "dependencies"
                || section.EndsWith(".dependencies", StringComparison.OrdinalIgnoreCase);
            if (!inDeps) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var name = line[..eq].Trim();
            if (name is "python") continue; // python version constraint, not a package
            var val = line[(eq + 1)..].Trim().Trim('"', '\'');
            deps.Add(new RawDependency(name, val.Length == 0 ? null : val, "direct", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class PackageJsonParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Node;
    public string BuildSystemHint => "npm";
    public bool Matches(string f) => f.Equals("package.json", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var text = File.ReadAllText(abs);
        foreach (var prop in new[] { "\"dependencies\"", "\"devDependencies\"", "\"peerDependencies\"", "\"optionalDependencies\"" })
        {
            var i = text.IndexOf(prop, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var brace = text.IndexOf('{', i);
            var end = text.IndexOf('}', brace);
            if (brace < 0 || end < 0) continue;
            var block = text.Substring(brace + 1, end - brace - 1);
            var scope = prop.Contains("dev", StringComparison.OrdinalIgnoreCase) ? "development"
                : prop.Contains("peer", StringComparison.OrdinalIgnoreCase) ? "peer"
                : prop.Contains("optional", StringComparison.OrdinalIgnoreCase) ? "optional" : "direct";
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(block, "\"(?<n>[^\"]+)\"\\s*:\\s*\"(?<v>[^\"]+)\""))
            {
                var g = (System.Text.RegularExpressions.Match)m;
                deps.Add(new RawDependency(g.Groups["n"].Value, g.Groups["v"].Value, scope, Ecosystem));
            }
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class CargoTomlParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Rust;
    public string BuildSystemHint => "cargo";
    public bool Matches(string f) => f.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var text = File.ReadAllText(abs);
        var sections = new[] { "[dependencies]", "[dev-dependencies]" };
        var lines = text.Split('\n');
        var active = false;
        var scope = "direct";
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith('['))
            {
                active = sections.Any(s => t.StartsWith(s, StringComparison.OrdinalIgnoreCase));
                scope = t.Contains("dev", StringComparison.OrdinalIgnoreCase) ? "development" : "direct";
                continue;
            }
            if (!active || t.Length == 0 || t.StartsWith('#')) continue;
            var eq = t.IndexOf('=');
            if (eq < 0) continue;
            deps.Add(new RawDependency(t[..eq].Trim(), t[(eq + 1)..].Trim().Trim('"', '\''), scope, Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class GoModParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Go;
    public string BuildSystemHint => "go-modules";
    public bool Matches(string f) => f.Equals("go.mod", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        foreach (var line in File.ReadLines(abs))
        {
            var t = line.Trim();
            if (!t.StartsWith("require")) continue;
            var body = t["require".Length..].Trim();
            body = body.Trim('(', ')');
            foreach (var b in body.Split('\n'))
            {
                var parts = b.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    deps.Add(new RawDependency(parts[0], parts[1], "direct", Ecosystem));
            }
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class PomXmlParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Java;
    public string BuildSystemHint => "maven";
    public bool Matches(string f) => f.Equals("pom.xml", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var text = File.ReadAllText(abs);
        foreach (var m in System.Text.RegularExpressions.Regex.Matches(text, "<dependency>.*?<artifactId>(?<n>[^<]+)</artifactId>.*?<version>(?<v>[^<]+)</version>.*?</dependency>", System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var g = (System.Text.RegularExpressions.Match)m;
            deps.Add(new RawDependency(g.Groups["n"].Value.Trim(), g.Groups["v"].Value.Trim(), "direct", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class BuildGradleParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Kotlin;
    public string BuildSystemHint => "gradle";
    public bool Matches(string f) => f.Equals("build.gradle", StringComparison.OrdinalIgnoreCase) || f.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        foreach (var line in File.ReadLines(abs))
        {
            var t = line.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(t, "(implementation|api|testImplementation|compileOnly)\\s*[(]?['\"](?<g>[^'\"]+):(?<a>[^'\"]+):(?<v>[^'\"]+)['\"]");
            if (!m.Success) continue;
            deps.Add(new RawDependency(m.Groups["a"].Value, m.Groups["v"].Value, "direct", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class CsProjParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.DotNet;
    public string BuildSystemHint => "msbuild";
    public bool Matches(string f) => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var text = File.ReadAllText(abs);
        foreach (var m in System.Text.RegularExpressions.Regex.Matches(text, "<PackageReference\\s+Include=\"([^\"]+)\"\\s+Version=\"([^\"]+)\""))
        {
            var g = (System.Text.RegularExpressions.Match)m;
            deps.Add(new RawDependency(g.Groups[1].Value, g.Groups[2].Value, "direct", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class ComposerJsonParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Php;
    public string BuildSystemHint => "composer";
    public bool Matches(string f) => f.Equals("composer.json", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var text = File.ReadAllText(abs);
        foreach (var prop in new[] { "\"require\"", "\"require-dev\"" })
        {
            var i = text.IndexOf(prop, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var brace = text.IndexOf('{', i);
            var end = text.IndexOf('}', brace);
            if (brace < 0 || end < 0) continue;
            var scope = prop.Contains("dev", StringComparison.OrdinalIgnoreCase) ? "development" : "direct";
            var block = text.Substring(brace + 1, end - brace - 1);
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(block, "\"(?<n>[^\"]+)\"\\s*:\\s*\"(?<v>[^\"]+)\""))
            {
                var g = (System.Text.RegularExpressions.Match)m;
                deps.Add(new RawDependency(g.Groups["n"].Value, g.Groups["v"].Value, scope, Ecosystem));
            }
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class GemfileParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Ruby;
    public string BuildSystemHint => "bundler";
    public bool Matches(string f) => f.Equals("Gemfile", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        foreach (var line in File.ReadLines(abs))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, "gem\\s+['\"](?<n>[^'\"]+)['\"]\\s*,?\\s*['\"]?(?<v>[^'\"]*)['\"]?");
            if (m.Success)
                deps.Add(new RawDependency(m.Groups["n"].Value, m.Groups["v"].Value.Length == 0 ? null : m.Groups["v"].Value, "direct", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class PubspecYamlParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Dart;
    public string BuildSystemHint => "pub";
    public bool Matches(string f) => f.Equals("pubspec.yaml", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        var text = File.ReadAllText(abs);
        foreach (var section in new[] { "dependencies:", "dev_dependencies:" })
        {
            var i = text.IndexOf(section, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var scope = section.StartsWith("dev", StringComparison.OrdinalIgnoreCase) ? "development" : "direct";
            var lines = text.Substring(i + section.Length).Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith(' ') == false && line.StartsWith('\t') == false) break;
                var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), "^(?<n>[a-zA-Z0-9_]+):\\s*(?<v>.+)?$");
                if (m.Success && m.Groups["n"].Value != "flutter")
                    deps.Add(new RawDependency(m.Groups["n"].Value, string.IsNullOrWhiteSpace(m.Groups["v"].Value) ? null : m.Groups["v"].Value.Trim(), scope, Ecosystem));
            }
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}

internal sealed class DockerfileParser : IManifestParser
{
    public string Ecosystem => KnownEcosystems.Container;
    public string BuildSystemHint => "docker";
    public bool Matches(string f) => f.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || f.Equals("Containerfile", StringComparison.OrdinalIgnoreCase);
    public DetectedManifest Parse(string abs, string rel)
    {
        var deps = new List<RawDependency>();
        foreach (var line in File.ReadLines(abs))
        {
            var t = line.Trim();
            if (!t.StartsWith("FROM", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                deps.Add(new RawDependency(parts[1], null, "system", Ecosystem));
        }
        return new DetectedManifest(rel, Ecosystem, BuildSystemHint, deps);
    }
}
