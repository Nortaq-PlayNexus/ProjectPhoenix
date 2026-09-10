# PROJECT PHOENIX :: AUTONOMOUS SOFTWARE RESURRECTION

<p align="center">
  <img src="https://img.shields.io/badge/PROJECT%20PHOENIX-RESURRECTION%20ENGINE-ff4d00?style=flat-square&labelColor=0a0e1a" alt="phoenix" />
</p>

## What is Project Phoenix?

Project Phoenix is an **Autonomous Software Resurrection & Dependency Insurance** system.
It discovers, diagnoses, and revives broken dependencies across your software stack.

## Architecture

- **Phoenix.Core** - Core engine, database, crypto, and shared models
- **Phoenix.Dependency** - Dependency manifest parsing, vault, and insurance
- **Phoenix.Environment** - Environment reconstruction and forensic analysis
- **Phoenix.Archaeology** - File system forensics and git archaeology
- **Phoenix.Archive** - Capsule building, dashboard generation, lock verification
- **Phoenix.Build** - Build runner and orchestration
- **Phoenix.Security** - Advisory database and security engine
- **Phoenix.Agents** - AI-powered agent orchestration
- **Phoenix.CLI** - Command-line interface
- **Phoenix.Repair** - Automated repair engine
- **Phoenix.Recovery** - Disaster recovery tools
- **Phoenix.Providers** - Cloud provider integration

## Quick Start

### Prerequisites
- .NET 6.0 SDK or later
- Python 3.10+ (for tooling)
- SQLite

### Build & Test
```bash
dotnet restore Phoenix.sln
dotnet build Phoenix.sln --configuration Release
dotnet test Phoenix.Tests\Phoenix.Tests.csproj --configuration Release
```

### Dependencies
All NuGet package dependencies are defined in the `.csproj` files:
- `Microsoft.Data.Sqlite` - SQLite database engine
- Project-to-project references - Modular architecture

## CI
GitHub Actions workflows are in `.github/workflows/ci.yml`.

## License
MIT License - see [LICENSE](LICENSE)
