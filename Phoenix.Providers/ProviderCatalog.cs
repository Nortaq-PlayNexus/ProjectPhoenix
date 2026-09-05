namespace Phoenix.Providers;

/// <summary>Spec #57 — plugin surface for package registries / providers.</summary>
public interface IProviderCapability
{
    string Name { get; }
    bool Download { get; }
    bool Upload { get; }
    bool Metadata { get; }
    bool Checksums { get; }
    bool Signatures { get; }
    bool Archives { get; }
    bool PrivatePackages { get; }
    string Availability { get; }
}

public sealed record ProviderDefinition(
    string Name,
    string Kind,
    bool Download,
    bool Upload,
    bool Metadata,
    bool Checksums,
    bool Signatures,
    bool Archives,
    bool PrivatePackages,
    string Availability) : IProviderCapability;

public sealed class ProviderCatalog
{
    // Static, best-effort capability assertions (spec #59). A live client would
    // perform capability discovery per provider at runtime.
    public static readonly IReadOnlyList<ProviderDefinition> Providers = new List<ProviderDefinition>
    {
        new("npm", "registry", true, true, true, true, false, true, true, "public"),
        new("PyPI", "registry", true, true, true, true, true, false, true, "public"),
        new("crates.io", "registry", true, true, true, true, true, false, false, "public"),
        new("NuGet", "registry", true, true, true, true, true, true, true, "public"),
        new("Maven Central", "registry", true, false, true, true, true, true, false, "public"),
        new("Gradle", "registry", true, false, true, true, false, false, true, "public"),
        new("RubyGems", "registry", true, true, true, true, false, true, true, "public"),
        new("Packagist", "registry", true, true, true, true, false, false, true, "public"),
        new("Go modules", "registry", true, true, true, true, true, false, true, "public"),
        new("Docker Hub", "registry", true, true, true, true, true, true, true, "public"),
        new("GitHub", "repo", true, true, true, true, true, true, true, "public"),
        new("GitLab", "repo", true, true, true, true, true, true, true, "public"),
        new("Codeberg", "repo", true, true, true, true, true, true, true, "public")
    };

    public IReadOnlyList<ProviderDefinition> All() => Providers;

    public ProviderDefinition? ByName(string name) =>
        Providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Spec #60 — preferred provider resolution order for an ecosystem.</summary>
    public IReadOnlyList<ProviderDefinition> ResolveOrder(string ecosystem)
    {
        // Best-effort mapping of detected ecosystem to its canonical provider.
        var primary = ecosystem.ToLowerInvariant() switch
        {
            "python" => "PyPI",
            "node" => "npm",
            "rust" => "crates.io",
            "dotnet" => "NuGet",
            "java" or "kotlin" => "Maven Central",
            "ruby" => "RubyGems",
            "php" => "Packagist",
            "go" => "Go modules",
            "docker" => "Docker Hub",
            _ => null
        };
        int Score(ProviderDefinition p) =>
            (p.Download ? 1 : 0) + (p.Upload ? 1 : 0) + (p.Metadata ? 1 : 0) +
            (p.Checksums ? 2 : 0) + (p.Signatures ? 2 : 0) + (p.Archives ? 1 : 0) + (p.PrivatePackages ? 1 : 0);

        return Providers
            .OrderByDescending(p => (p.Name == primary ? 100 : 0) + Score(p))
            .ToList();
    }
}
