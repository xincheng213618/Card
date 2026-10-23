# Sanguosha.NET · 群雄逐鹿

[简体中文](README.md) · [Developer quickstart](docs/GETTING_STARTED.md) · [Documentation](docs/README.md) · [Contributing](CONTRIBUTING.md)

[![CI](https://github.com/xincheng213618/sanguosha-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/xincheng213618/sanguosha-dotnet/actions/workflows/ci.yml)

**A Sanguosha (三国杀) rules engine and Windows WPF single-player client written in C# / .NET 8.** The project aims to provide runnable source for studying card-game rules, adding custom characters, experimenting with AI, and building other interfaces.

This is an independent community implementation, without affiliation with or endorsement by the official game. Rules follow the editions documented for each mode and character. See [modes and rulesets](docs/MODES_AND_RULESETS.md), [activity coverage](docs/ACTIVITY_MODES.md), and [known capability gaps](docs/SANGUOSHA_GAP_PLAN.md). Much of the detailed game documentation is currently in Chinese.

## What you can build on

| Goal | Starting point |
| --- | --- |
| Play locally against AI | WPF client with identity, 2v2, national-war, activity and campaign modes; [player guide](docs/PLAYER_GUIDE.md) |
| Connect another interface | Core has no WPF dependency; submit commands and display seat-specific snapshots; [headless example](samples/CardGame.Headless/README.md) |
| Add characters and skills | Character manifests, skill JSON and compiled runtime modules; [developer quickstart](docs/GETTING_STARTED.md) and [module conventions](docs/CHARACTER_MODULES.md) |
| Study resolution and replay | Command validation, nested resolution frames, atomic card movement, private player views and checkpoint replay; [architecture](docs/ARCHITECTURE.md) |
| Explore AI decisions | Existing rule queries and [SimpleAi.cs](src/CardGame.Core/SimpleAi.cs); [AI documentation](docs/TACTICAL_AI.md) |

Use the project from source. Content extensions are compiled into the application; a stable external SDK, binary plugin protocol and public online service are not currently promised. A [LAN room prototype](docs/LAN_PROTOTYPE.md) supports five/eight-seat identity matches with human players and AI. The desktop client requires Windows. Core, the LAN server and the headless example target `net8.0`; CI validates them on Windows.

## Quick start

Install Git and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). SDK selection is defined by [global.json](global.json). Run these commands in PowerShell:

```powershell
git clone https://github.com/xincheng213618/sanguosha-dotnet.git
Set-Location sanguosha-dotnet

# Submit real commands, read player views, serialize and restore a checkpoint.
dotnet run --project .\samples\CardGame.Headless\CardGame.Headless.csproj

# Windows only: launch the desktop game.
dotnet run --project .\src\CardGame.Wpf\CardGame.Wpf.csproj
```

On Windows, [Play.cmd](Play.cmd) builds and starts the current development client in a source checkout. Distribution-only folders start the existing package; use `tools\Start-Game.ps1 -UsePackage` to explicitly open a package from a checkout. The headless example does not read desktop saves or require artwork, audio or WPF.

## Source map

| Component | Responsibility |
| --- | --- |
| [CardGame.Core](src/CardGame.Core) | Commands, legal choices, resolution, card zones, player snapshots, AI and replay |
| [CardGame.Content.Standard](src/CardGame.Content.Standard) | Built-in content loading and registration |
| [Characters](src/Characters/README.md) | Character manifests, skill definitions and character-specific runtime modules |
| [CardGame.Wpf](src/CardGame.Wpf) | Desktop interaction, presentation, local persistence and audiovisual feedback |
| [CardGame.Headless](samples/CardGame.Headless) | Small executable example of hosting Core without WPF |

For a player-facing interface, use `CreateSnapshot(viewerSeat)`. Resolution stacks and checkpoints are trusted host data and can include private state; do not expose them as player views. See the [command and privacy contracts](docs/CORE_CONTROL_DESIGN.md), [card lifecycle](docs/CARD_LIFECYCLE_CONTRACT.md), and [save/replay boundaries](docs/SAVE_AND_RESUME.md).

## Verification and contribution

For the routine scope, use Windows, PowerShell 7 and Python 3.10 or newer on PATH. Python registers temporary outputs and needs no additional packages. Run from the repository root:

```powershell
pwsh -File .\tools\Test-Changed.ps1
```

CI runs that same [routine scope](tools/VerificationScopes.psd1): four smoke checks targeting about one second of test-process time, with build time measured separately. It then builds and runs the headless example. Use existing name filters for relevant engine and WPF regressions while developing. Full verification is manual only and requires an explicit request; it is not the default CI lane. See [CONTRIBUTING.md](CONTRIBUTING.md) and the [development gates](docs/DEVELOPMENT_GATES.md).

Reproducible bug reports, sourced rule corrections, documentation improvements and small extensions are welcome. Use [Issues](https://github.com/xincheng213618/sanguosha-dotnet/issues) for questions and reports, or start with a [good first issue](https://github.com/xincheng213618/sanguosha-dotnet/issues?q=is%3Aissue%20is%3Aopen%20label%3A%22good%20first%20issue%22).

## License and third-party material

Original software code, scripts and project-authored documentation are available under the [MIT License](LICENSE). Third-party images, audio, trademarks, scraped pages and quotations, including text embedded in source or content files, are outside that grant; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for scope and source records. Core and the headless example can be used without the desktop assets.
