using Phoenix.Core;
using Phoenix.Dependency;
using Phoenix.Security;
using Phoenix.Repair;
using Phoenix.Providers;
using Phoenix.Recovery;
using Phoenix.Environment;
using Phoenix.Agents;
using Phoenix.Archaeology;
using Phoenix.Archive;
using Xunit;

namespace Phoenix.Tests;

public class CryptoTests
{
    [Fact]
    public void Sha256_IsDeterministic()
    {
        var a = Crypto.Sha256("hello phoenix");
        var b = Crypto.Sha256("hello phoenix");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Sha256File_MatchesKnownVector()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phx-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "x.txt");
        File.WriteAllText(f, "abc");
        // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Crypto.Sha256File(f));
        Directory.Delete(dir, true);
    }
}

public class ManifestParserTests
{
    [Fact]
    public void RequirementsTxt_ParsesPins()
    {
        var root = TempWith("requirements.txt", "Django==2.2.8\nrequests>=2.22.0\n# comment\n");
        var reg = new ManifestParserRegistry();
        var manifests = reg.DetectAndParse(new[] { "requirements.txt" }, root);
        Assert.Single(manifests);
        Assert.Contains(manifests[0].Dependencies, d => d.Name == "Django" && d.Version == "2.2.8");
        Assert.Contains(manifests[0].Dependencies, d => d.Name == "requests" && d.Version == ">=2.22.0");
        Directory.Delete(root, true);
    }

    [Fact]
    public void PackageJson_ParsesDevAndProd()
    {
        var root = TempWith("package.json", "{\"dependencies\":{\"lodash\":\"4.17.21\"},\"devDependencies\":{\"jest\":\"29.0.0\"}}");
        var reg = new ManifestParserRegistry();
        var manifests = reg.DetectAndParse(new[] { "package.json" }, root);
        var deps = manifests.SelectMany(m => m.Dependencies).ToList();
        Assert.Contains(deps, d => d.Name == "lodash" && d.Scope == "direct");
        Assert.Contains(deps, d => d.Name == "jest" && d.Scope == "development");
        Directory.Delete(root, true);
    }

    [Fact]
    public void GoMod_ParsesRequire()
    {
        var root = TempWith("go.mod", "module example\n\ngo 1.21\n\nrequire github.com/gin-gonic/gin v1.9.1\n");
        var reg = new ManifestParserRegistry();
        var manifests = reg.DetectAndParse(new[] { "go.mod" }, root);
        Assert.Contains(manifests[0].Dependencies, d => d.Name == "github.com/gin-gonic/gin" && d.Version == "v1.9.1");
        Directory.Delete(root, true);
    }

    [Fact]
    public void CargoToml_ParsesDependencies()
    {
        var root = TempWith("Cargo.toml", "[package]\nname=\"x\"\n[dependencies]\nserde = \"1.0\"\ntokio = { version = \"1.0\", features=[\"full\"] }\n[dev-dependencies]\nrand = \"0.8\"\n");
        var reg = new ManifestParserRegistry();
        var manifests = reg.DetectAndParse(new[] { "Cargo.toml" }, root);
        var deps = manifests.SelectMany(m => m.Dependencies).ToList();
        Assert.Contains(deps, d => d.Name == "serde" && d.Scope == "direct");
        Assert.Contains(deps, d => d.Name == "rand" && d.Scope == "development");
        Directory.Delete(root, true);
    }

    [Fact]
    public void Dockerfile_ParsesBaseImagesAsSystem()
    {
        var root = TempWith("Dockerfile", "FROM python:3.11-slim\nRUN echo hi\n");
        var reg = new ManifestParserRegistry();
        var manifests = reg.DetectAndParse(new[] { "Dockerfile" }, root);
        Assert.Contains(manifests[0].Dependencies, d => d.Name == "python:3.11-slim" && d.Scope == "system");
        Directory.Delete(root, true);
    }

    private static string TempWith(string name, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "phx-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }
}

public class SecurityEngineTests
{
    [Fact]
    public void DetectsTyposquat()
    {
        var deps = new List<DependencyRecord>
        {
            new(0, 0, "requets", "2.31.0", "node", "direct", DependencyState.Unknown, null, null, null, 0, null, null),
            new(0, 0, "lodash", "4.17.21", "node", "direct", DependencyState.Unknown, null, null, null, 0, null, null)
        };
        var findings = new SecurityEngine().Analyze(deps);
        Assert.Contains(findings, f => f.DependencyName == "requets" && f.Kind == "typosquat");
        Assert.DoesNotContain(findings, f => f.DependencyName == "lodash");
    }
}

public class InsuranceAndDisasterTests
{
    [Fact]
    public void Insurance_Insured_WhenVersionsPinned()
    {
        var deps = new List<DependencyRecord>
        {
            new(0,0,"a","1.0","python","direct",DependencyState.Healthy,null,null,null,0,null,null),
            new(0,0,"b","2.0","python","direct",DependencyState.Healthy,null,null,null,0,null,null)
        };
        var status = new DependencyVault(Path.Combine(Path.GetTempPath(), "vault-" + Guid.NewGuid().ToString("N"))).ComputeInsurance(deps);
        Assert.Equal(InsuranceStatus.Insured, status);
    }

    [Fact]
    public void Disaster_InternetDown_KillsUncachedDeps()
    {
        var deps = new List<DependencyRecord>
        {
            new(0,0,"a",null,"python","direct",DependencyState.Unknown,null,null,null,0,"pending","pending"),
            new(0,0,"b","1.0","python","direct",DependencyState.Healthy,null,null,null,0,"stored","stored")
        };
        var scenarios = new DisasterSimulator().Simulate(deps, InsuranceStatus.PartiallyInsured);
        var internet = scenarios.First(s => s.Scenario.Contains("Internet"));
        Assert.Equal(50.0, internet.SurvivalPct);
    }

    [Fact]
    public void Disaster_RegistryDown_KillsThatEcosystem()
    {
        var deps = new List<DependencyRecord>
        {
            new(0,0,"a","1.0","python","direct",DependencyState.Unknown,null,null,null,0,"pending","pending"),
            new(0,0,"b","1.0","node","direct",DependencyState.Unknown,null,null,null,0,"pending","pending")
        };
        var scenarios = new DisasterSimulator().Simulate(deps, InsuranceStatus.PartiallyInsured);
        var pyDown = scenarios.First(s => s.Scenario == "Registry down: python");
        // python dep is lost (not cached), node dep survives -> 50%
        Assert.Equal(50.0, pyDown.SurvivalPct);
    }
}

public class EnvironmentEngineTests
{
    [Fact]
    public void ReconstructsPythonFromPyproject()
    {
        var root = Path.Combine(Path.GetTempPath(), "phx-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "pyproject.toml"), "[tool.poetry.dependencies]\npython = \"^3.9\"\n");
        var env = new EnvironmentReconstructionEngine().Reconstruct(root);
        Assert.Contains(env.Findings, f => f.Kind == "runtime" && f.Value.Contains("3.9"));
        Directory.Delete(root, true);
    }
}

public class DatabaseTests
{
    [Fact]
    public void Aggregates_ReflectInserts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "phx-db-" + Guid.NewGuid().ToString("N") + ".db");
        using (var db = new PhoenixDb(dbPath))
        {
            var pid = db.InsertProject("p1", null, "fp", null, null, null);
            db.InsertDependency(pid, "a", "1.0", "python", "direct", DependencyState.Vulnerable, null, null, null, 90, null, null);
            db.InsertDependency(pid, "b", "2.0", "python", "direct", DependencyState.Healthy, null, null, null, 10, null, null);
            db.UpdateProjectStatus(pid, "FULLY RESURRECTED");
            db.InsertRecoveryTest(pid, null, "build", "PASS");
        }
        using (var db = new PhoenixDb(dbPath))
        {
            Assert.Equal(1, db.CountProjects());
            Assert.Equal(1, db.CountProjectsByStatus("FULLY RESURRECTED"));
            Assert.Equal(2, db.CountDependencies());
            Assert.Equal(1, db.CountDependenciesByState(DependencyState.Vulnerable));
            Assert.Equal(1, db.CountRecoveryTestsByResult("PASS"));
            Assert.Equal(2, db.CountDependenciesWithVersion());
        }
        try { File.Delete(dbPath); } catch { /* best-effort cleanup */ }
    }
}

public class CoordinatorIntegrationTests
{
    [Fact]
    public void ResurrectionPipeline_ProducesCapsuleAndDeps()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "phx-int-" + Guid.NewGuid().ToString("N") + ".db");
        var capsRoot = Path.Combine(Path.GetTempPath(), "phx-caps-" + Guid.NewGuid().ToString("N"));
        var srcRoot = Path.Combine(Path.GetTempPath(), "phx-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(srcRoot);
        File.WriteAllText(Path.Combine(srcRoot, "requirements.txt"), "flask==2.0.0\n");

        try
        {
            System.Environment.SetEnvironmentVariable("PHOENIX_CAPSULE_ROOT", capsRoot);
            using var db = new PhoenixDb(dbPath);
            var ctx = new ResurrectionContext { ProjectId = db.InsertProject("it", srcRoot, "fp", null, null, null), SnapshotPath = srcRoot, SnapshotHash = "h", Db = db, Log = new ConsoleLogger() };
            new Coordinator()
                .Add(new ArchaeologistAgent())
                .Add(new DependencyAnalystAgent())
                .Add(new EnvironmentEngineerAgent())
                .Add(new SecurityEngineerAgent())
                .Add(new JudgeAgentWrapper())
                .Add(new ArchivistAgent())
                .Run(ctx);

            Assert.Equal(1, db.CountDependencies());
            Assert.False(string.IsNullOrEmpty(ctx.CapsulePath));
            Assert.True(Directory.Exists(ctx.CapsulePath));
            Assert.True(File.Exists(Path.Combine(ctx.CapsulePath!, "metadata", "phoenix.manifest.json")));
            Assert.True(File.Exists(Path.Combine(ctx.CapsulePath!, "metadata", "PROJECT_FORENSICS.md")));
            Assert.True(File.Exists(Path.Combine(ctx.CapsulePath!, "metadata", "RESURRECTION_REPORT.md")));
        }
        finally
        {
            try { File.Delete(dbPath); Directory.Delete(capsRoot, true); Directory.Delete(srcRoot, true); } catch { }
        }
    }

    [Fact]
    public void ResurrectionPipeline_WithBuildAttempt_CompletesWithoutThrowing()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "phx-build-" + Guid.NewGuid().ToString("N") + ".db");
        var capsRoot = Path.Combine(Path.GetTempPath(), "phx-bcaps-" + Guid.NewGuid().ToString("N"));
        var srcRoot = Path.Combine(Path.GetTempPath(), "phx-bsrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(srcRoot);
        File.WriteAllText(Path.Combine(srcRoot, "requirements.txt"), "flask==2.0.0\n");
        try
        {
            System.Environment.SetEnvironmentVariable("PHOENIX_CAPSULE_ROOT", capsRoot);
            using var db = new PhoenixDb(dbPath);
            var ctx = new ResurrectionContext { ProjectId = db.InsertProject("b", srcRoot, "fp", null, null, null), SnapshotPath = srcRoot, SnapshotHash = "h", AllowBuild = true, Db = db, Log = new ConsoleLogger() };
            var ex = Record.Exception(() => new Coordinator()
                .Add(new ArchaeologistAgent()).Add(new DependencyAnalystAgent()).Add(new EnvironmentEngineerAgent())
                .Add(new SecurityEngineerAgent()).Add(new BuildEngineerAgent()).Add(new JudgeAgentWrapper()).Add(new ArchivistAgent())
                .Run(ctx));
            Assert.Null(ex);
            Assert.False(string.IsNullOrEmpty(ctx.CapsulePath));
        }
        finally
        {
            try { File.Delete(dbPath); Directory.Delete(capsRoot, true); Directory.Delete(srcRoot, true); } catch { }
        }
    }
}

public class GitForensicsTests
{
    [Fact]
    public void Snapshot_PreservesGitHistory()
    {
        var src = Path.Combine(Path.GetTempPath(), "phx-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "x");
        RunGit(src, "init -q");
        RunGit(src, "config user.email t@t.t");
        RunGit(src, "config user.name t");
        RunGit(src, "add -A");
        RunGit(src, "commit -qm init");

        var snap = Path.Combine(Path.GetTempPath(), "phx-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snap);
        var snapDir = new ForensicSnapshot(new ConsoleLogger()).Create(src, snap);

        Assert.True(Directory.Exists(Path.Combine(snapDir, ".git")), "git history must be preserved in snapshot");
        var profile = new GitForensics().Analyze(snapDir);
        Assert.True(profile!.HasGit);
        Assert.True(profile.CommitCount >= 1);

        try { Directory.Delete(src, true); Directory.Delete(snap, true); } catch { }
    }

    [Fact]
    public void Lockdown_CapturesAndDetectsDrift()
    {
        var src = Path.Combine(Path.GetTempPath(), "phx-lock-" + Guid.NewGuid().ToString("N"));
        var cap = Path.Combine(Path.GetTempPath(), "phx-lockcaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src); Directory.CreateDirectory(cap);
        var deps = new List<DependencyRecord>
        {
            new(0,0,"flask","2.0.0","python","direct",DependencyState.Healthy,null,null,null,0,null,null)
        };
        var input = new CapsuleBuilder.CapsuleInput(1, "p", src, "python", "pip", "python", deps, Array.Empty<string>(), "Unresolved", 0.9, null, null);
        var capsulePath = new CapsuleBuilder().Build(input, cap);
        var verifier = new LockVerifier();

        var locked = verifier.ReadLock(capsulePath);
        Assert.Contains(locked, e => e.Name == "flask" && e.Version == "2.0.0");

        var drift = verifier.DetectDrift(capsulePath, new (string, string?, string)[] { ("flask", "2.1.0", "python") });
        Assert.Contains(drift, d => d.Contains("DRIFT") && d.Contains("2.1.0"));

        var noDrift = verifier.DetectDrift(capsulePath, new (string, string?, string)[] { ("flask", "2.0.0", "python") });
        Assert.Empty(noDrift);

        try { Directory.Delete(src, true); Directory.Delete(cap, true); } catch { }
    }

    [Fact]
    public void Capsule_ExcludesSecretFiles()
    {
        var src = Path.Combine(Path.GetTempPath(), "phx-sec-" + Guid.NewGuid().ToString("N"));
        var cap = Path.Combine(Path.GetTempPath(), "phx-seccaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src); Directory.CreateDirectory(cap);
        File.WriteAllText(Path.Combine(src, "app.py"), "x");
        File.WriteAllText(Path.Combine(src, ".env"), "SUPER_SECRET=1");
        File.WriteAllText(Path.Combine(src, "key.pem"), "PRIVATEKEY");
        var input = new CapsuleBuilder.CapsuleInput(1, "p", src, "python", "pip", "python",
            Array.Empty<DependencyRecord>(), Array.Empty<string>(), "Unresolved", 0.9, null, null);
        var capsulePath = new CapsuleBuilder().Build(input, cap);
        var srcCopy = Path.Combine(capsulePath, "source");
        Assert.False(File.Exists(Path.Combine(srcCopy, ".env")), ".env must not be archived");
        Assert.False(File.Exists(Path.Combine(srcCopy, "key.pem")), "*.pem must not be archived");
        Assert.True(File.Exists(Path.Combine(srcCopy, "app.py")));
        try { Directory.Delete(src, true); Directory.Delete(cap, true); } catch { }
    }

    [Fact]
    public void DetectsDotNet_WhenPackageJsonAlsoPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "phx-det-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Sample.csproj"), "<Project><ItemGroup><PackageReference Include=\"X\" Version=\"1.0\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(root, "package.json"), "{\"name\":\"x\",\"version\":\"1.0.0\"}");
        var profile = new ArchaeologyEngine().Analyze(root);
        Assert.Equal("c#", profile.DetectedLanguage);
        Assert.Equal("msbuild", profile.BuildSystem);
        Directory.Delete(root, true);
    }

    private static void RunGit(string dir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args) { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(15000);
    }
}

public class SecurityAdvisoryTests
{
    [Fact]
    public void FlagsVulnerableDjango()
    {
        var findings = new SecurityEngine().Analyze(new List<DependencyRecord>
        {
            new(0,0,"django","2.1.0","python","transitive", DependencyState.Healthy, null, null, null, 0, null, null)
        });
        Assert.Contains(findings, f => f.Kind == "vulnerability" && f.Severity == "HIGH");
    }

    [Fact]
    public void IgnoresPatchedVersion()
    {
        var findings = new SecurityEngine().Analyze(new List<DependencyRecord>
        {
            new(0,0,"django","2.2.28","python","transitive", DependencyState.Healthy, null, null, null, 0, null, null)
        });
        Assert.DoesNotContain(findings, f => f.Kind == "vulnerability");
    }

    [Fact]
    public void VersionRangeCompare()
    {
        Assert.True(AdvisoryDatabase.IsVulnerable("1.26.4", "<1.26.5"));
        Assert.False(AdvisoryDatabase.IsVulnerable("1.26.5", "<1.26.5"));
        Assert.True(AdvisoryDatabase.IsVulnerable("4.17.20", "<4.17.21"));
    }
}

public class RepairTests
{
    [Fact]
    public void ProposesVulnerabilityUpgrade()
    {
        var proposals = new RepairEngine().Propose(
            Path.GetTempPath(), "python", "pip",
            new List<SecurityFinding> { new("django", "python", "vulnerability", "HIGH", "fixed in 2.2.28") },
            Array.Empty<EnvironmentFinding>());
        Assert.Contains(proposals, p => p.SuggestedAction.Contains("2.2.28"));
    }

    [Fact]
    public void CreatesRepairAttemptWithProvenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "phx_repair_" + Guid.NewGuid().ToString("N")[..8]);
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.py"), "x=1");
        var proposals = new RepairEngine().Propose(src, "python", "pip",
            Array.Empty<SecurityFinding>(), Array.Empty<EnvironmentFinding>());
        var attempt = new RepairStore().CreateAttempt(root, "MyProj", src, proposals);
        var log = Path.Combine(attempt, "REPAIR_LOG.json");
        Assert.True(File.Exists(log));
        var json = File.ReadAllText(log);
        Assert.Contains("original_snapshot_hash", json);
        Assert.Contains("proposals", json);
        Directory.Delete(root, true);
    }

    [Fact]
    public void ApplyBumpsVulnerableVersionInManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "phx_apply_" + Guid.NewGuid().ToString("N")[..8]);
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "requirements.txt"), "django==2.1.0\nflask==2.0.0\n");
        var proposals = new RepairEngine().Propose(src, "python", "pip",
            new List<SecurityFinding> { new("django", "python", "vulnerability", "HIGH", "fixed in 2.2.28") },
            Array.Empty<EnvironmentFinding>());
        var attempt = new RepairStore().CreateAttempt(root, "P", src, proposals);
        var mods = new RepairEngine().Apply(attempt, src, proposals);
        var applied = File.ReadAllText(Path.Combine(attempt, "source", "requirements.txt"));
        Assert.Contains("django==2.2.28", applied);
        Assert.Contains("flask==2.0.0", applied); // untouched
        Assert.NotEmpty(mods);
        new RepairStore().RecordModifications(attempt, proposals, mods);
        Assert.Contains("Applied", File.ReadAllText(Path.Combine(attempt, "REPAIR_LOG.json")));
        Directory.Delete(root, true);
    }
}

public class ProviderTests
{
    [Fact]
    public void CatalogHasKnownProviders()
    {
        var catalog = new ProviderCatalog();
        Assert.Contains(catalog.All(), p => p.Name == "npm" && p.Download);
        Assert.Contains(catalog.All(), p => p.Name == "PyPI" && p.Checksums);
        Assert.NotNull(catalog.ByName("NuGet"));
    }

    [Fact]
    public void ResolveOrder_PutsEcosystemPrimaryFirst()
    {
        var order = new ProviderCatalog().ResolveOrder("python");
        Assert.Equal("PyPI", order[0].Name);
        Assert.Contains(order, p => p.Name == "PyPI");
    }
}

public class DashboardTests
{
    [Fact]
    public void GeneratesHtmlAggregatingDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "phx-dash-" + Guid.NewGuid().ToString("N") + ".db");
        var outPath = Path.Combine(Path.GetTempPath(), "phx-dash-" + Guid.NewGuid().ToString("N") + ".html");
        using (var db = new PhoenixDb(dbPath))
        {
            var pid = db.InsertProject("Widget", null, "fp", null, null, null);
            db.UpdateProjectStatus(pid, "FULLY RESURRECTED");
            db.InsertDependency(pid, "django", "2.1.0", "python", "direct", DependencyState.Vulnerable, null, null, null, 90, null, null);
            DashboardGenerator.Generate(db, outPath);
        }
        var html = File.ReadAllText(outPath);
        Assert.Contains("Widget", html);
        Assert.Contains("Vulnerable", html);
        Assert.Contains("django", html);
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(outPath); } catch { }
    }
}



