<h1 align="center">Project Phoenix</h1>

<p align="center">
  <em>Autonomous Software Resurrection & Dependency Insurance</em>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/license-MIT-blue" alt="License">
  <img src="https://img.shields.io/badge/.NET-6.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 6">
  <img src="https://img.shields.io/badge/C%23-strict-239120" alt="C#">
  <img src="https://img.shields.io/badge/tests-passing-brightgreen" alt="Tests">
</p>

---

## Overview

**Project Phoenix** is a multi-agent autonomous system that discovers, analyzes, preserves, and resurrects software projects. It maps dependency trees, computes insurance status against supply-chain failures, simulates disaster scenarios, and generates recovery capsules — all from a single CLI.

When a project is "resurrected," Phoenix creates an immutable forensic snapshot, runs a coordinated pipeline of specialized agents (archaeologist, dependency analyst, environment engineer, security engineer, build engineer, repair engineer, judge, archivist), and produces a verifiable `.phoenix` capsule.

---

## Technology

- **Project type:** .NET 6 Console Application (CLI + class libraries)
- **Primary stack:** C#
- **Repository branch:** `main`

---

## Commands

```
phoenix resurrect <path> [--build]   Full resurrection pipeline
phoenix insure <path>               Map dependencies & compute insurance status
phoenix simulate <path>             Run the DOOMSDAY disaster simulator
phoenix cascade <depName>           Show blast radius if a dependency disappears
phoenix report                      Print the project health dashboard
phoenix dashboard                   Generate static HTML dashboard
phoenix providers                   Print the provider capability matrix
phoenix verify <capsule>            Verify a .phoenix capsule's integrity
```

---

## Project Structure

```
ProjectPhoenix/
├── Phoenix.CLI/              # CLI entry point & command routing
├── Phoenix.Core/             # Database, crypto, models, logging
├── Phoenix.Archaeology/      # Project analysis & file classification
├── Phoenix.Archive/          # Dashboard generation & capsule archival
├── Phoenix.Agents/           # Multi-agent coordinator & agent implementations
├── Phoenix.Build/            # Build system detection & sandboxed builds
├── Phoenix.Dependency/       # Dependency vault & insurance computation
├── Phoenix.Environment/      # Environment fingerprinting & drift detection
├── Phoenix.Providers/        # Provider capability matrix
├── Phoenix.Recovery/         # Recovery capsule creation & verification
├── Phoenix.Repair/           # Automated repair heuristics
├── Phoenix.Security/         # Secret scanning & vulnerability detection
├── Phoenix.Tests/            # Unit & integration tests
└── Phoenix.sln               # Solution file
```

---

## Getting Started

### Prerequisites

- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/Nortaq-PlayNexus/ProjectPhoenix.git
cd ProjectPhoenix

# Build
dotnet build Phoenix.sln

# Run
dotnet run --project Phoenix.CLI

# Run tests
dotnet test
```

---

## How It Works

1. **Forensic Snapshot** — An immutable, content-addressed copy of the source tree is created before any analysis.
2. **Multi-Agent Pipeline** — Specialized agents run in sequence: archaeology → dependency analysis → environment engineering → security → build → repair → judgment → archival.
3. **Insurance** — Dependencies are pinned, vaulted, and assigned an insurance status based on recoverability.
4. **Disaster Simulation** — The DOOMSDAY simulator models what happens if each dependency disappears, computing survival percentages.
5. **Cascade Analysis** — Shows the blast radius of losing any single dependency across all tracked projects.
6. **Capsule Verification** — `.phoenix` capsules are SHA-256 verified with locked dependency manifests.

---

## License

[MIT](LICENSE) — Copyright (c) 2026 PhantomTape
