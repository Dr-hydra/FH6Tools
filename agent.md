# FH6Tools Agent Context

## Project Goal
FH6Tools is a Windows desktop community tool launcher and control center for Forza Horizon 6. It provides curated tool download, installation, one-click launch, runtime status, local port checks, and future config file management.

## Current Product Direction
- Home page left side: FH6 one-click launch, game install detection, launch status, and related quick actions.
- Home page right side: installed/community tools with one-click launch, stop, open frontend, backend-only launch, status, port, health check, logs, and config entry.
- Tools are manually curated by the project owner.
- Users may add local tools manually; local tools should be marked separately from curated tools.

## Technical Baseline
- UI base: QING.UIKIT.
- Language/framework: VB.NET + WPF.
- First platform: Windows.
- Download/task implementation may reference the local PCL repository, especially network download, task state, and queue ideas.
- Core FH6Tools naming must not copy PCL/Minecraft business names.

## Current Implementation Snapshot
- The repository now contains `FH6Tools.slnx` with a QING.UIKIT-derived `FH6Tools` WPF app and `QING.Core` project references.
- FH6Tools-specific services live under `src/FH6Tools/Modules/FH6Tools`.
- `Data/sample-manifest.json` is the bundled curated manifest generated from the owner-approved `tools.md` list.
- The home page supports install/cancel download, launch/stop, frontend/backend start, folder/log opening, config open, backup, and restore actions.
- Install actions run through a simple in-app queue; cancel stops the active job and clears pending jobs.
- Backend port conflicts prompt for reusing an existing service, stopping the port owner, or changing the port.
- The FH6 launch card includes game data backup and backup-folder entry points; users can select a data folder when automatic candidates are not found.
- Runtime data defaults to `AppContext.BaseDirectory\FH6ToolsData`; smoke tests set `FH6TOOLS_APPDATA_ROOT` to a workspace `artifacts\smoke-data` path. Do not move tool state/config/download data back to `%APPDATA%`.
- Debug and Release builds are expected to compile with `dotnet build .\FH6Tools.slnx -c Debug` and `dotnet build .\FH6Tools.slnx -c Release`.
- Windows x64 Release builds are framework-dependent. Distributable builds are created with `scripts/Publish-FH6Tools.ps1`, which places a shared private .NET 10 Desktop and ASP.NET Core runtime under `dotnet` beside `FH6Tools.exe`.
- The published FH6Tools apphost searches the relative `dotnet` directory. Every launched tool receives `DOTNET_ROOT`, `DOTNET_ROOT_X64`, `DOTNET_MULTILEVEL_LOOKUP=0`, and the shared runtime at the front of `PATH`, so framework-dependent x64 .NET tools can use the same runtime.
- `tests/FH6Tools.SmokeTests` verifies manifest/local tools, resume/SHA/cancel download behavior, zip/exe/msi install paths, config backup/restore, runtime status for `single`/`backendOnly`/`frontendBackend`, health-check failure, and port conflict handling.
- `tools.md` is the owner-approved source list for curated tool candidates. The bundled manifest currently adapts OmniMix VB.NET Frontend, FH Language Combo Tool, Virtual TCU backend, FH6 Road Scanner, FH6 Subtitle Switcher, HorizonHaptics, and Forza Painter FH6.
- FH6 Auction House Sniper and FH6Auto are recorded in the manifest as `riskLevel: blocked`; FH6Tools must not install or launch them because they automate online/economy/gameplay advantage workflows.
- Manifest `downloadUrl` may use `github-release://owner/repo/pattern`; `ToolInstallService` resolves this against the latest GitHub release asset.
- `installType: portableExe` downloads a release exe into the tool install folder without running it as an installer.
- Endpoint `executable` values may use wildcards, and endpoint `environmentVariables` may redirect tool-specific state such as `APPDATA`, `USERPROFILE`, `XDG_CONFIG_HOME`, or `UV_CACHE_DIR` into `{toolRoot}`.
- Config entries may use `kind: directory`; directory snapshots are stored as zip files.
- UI was reimplemented against the updated sibling `E:\Dr.Hydra\QING.UIKIT` demo shell. `FormMain.xaml` now keeps the updated shell hosts (`PanTitle`, `PanMainLeft`, `PanMainRight`, `PanHint`, `PanMsg`, resizers, and back-to-top button).
- FH6Tools page code now lives under `src/FH6Tools/Pages/PageFH6Tools` with `PageFhToolsLeft` for navigation/status and `PageFhToolsRight` for the business dashboard. A single right-page instance switches between Home, Tools, Downloads, Config, and About.
- `src/FH6Tools/Modules/Shell/FhShell.vb` replaces the UI demo shell names with FH6Tools-native page enums/text/drop helpers.
- Right-page content should avoid fixed left/right inner columns for tool cards. Prefer vertical flow: information/status first, then launch/config buttons below in compact rows, so narrow windows do not cause overlap or offset layouts.
- The default UI language is Simplified Chinese. Users can switch between Simplified Chinese and English from the Config page; the selection is persisted in `FH6ToolsData\ui-language.txt`.
- On Home, the left sidebar contains only the FH6 icon, detected game path, and launch button. On other pages it becomes a narrow flat navigation list. Right-page labels and actions use vertical stacking instead of side-by-side grids.
- The Home right pane only shows installed tools. Each installed-tool card is compact and vertically stacks the tool name, health state, start, stop, config, and health-check actions. The Tools page remains the full catalog and management surface.
- All shell sections reuse one `PageFhToolsRight` instance. Title/left navigation must switch that instance's internal content directly; do not run the full page detach/exit/enter animation state machine when the target is already hosted.
- Keep future UI work aligned with the new QING.UIKIT demo shell instead of restoring the older flat `FormMain` dashboard.

## Tool Runtime Model
Supported tool types:
- `single`: one executable/process.
- `backendOnly`: backend service with local port binding; can run without frontend.
- `frontendBackend`: backend starts first, then frontend starts or opens after backend health check passes.

Default status detection:
- Backend: process alive + port listening + health URL success.
- Frontend: process alive, or URL reachable for browser-style frontends.

Default port behavior:
- Fixed port first.
- If occupied, prompt user to reuse existing service, stop occupying process, change port, or cancel.

## Manifest Direction
Remote tools are described by JSON manifest entries including:
- `id`, `name`, `version`, `category`, `description`
- `downloadUrl`, `sha256`, `installType`
- `toolType`
- `backend`, `frontend`, or `single`
- `configFiles`
- `requiresAdmin`, `riskLevel`, `notes`

Local user tools are stored separately in `localTools.json`.

## Config Management Direction
First version is file-level config management:
- Open config file.
- Open containing directory.
- Backup.
- Restore.
- Snapshot list.

Field-level schema editing is a later extension.

## Safety Boundaries
Do not build or curate tools for:
- Piracy or license bypass.
- Anti-cheat bypass.
- Online cheating.
- Account, currency, leaderboard, or online save tampering.
- Redistribution of files without permission.

## Naming Guidance
Use project-native names, for example:
- `FhNet`
- `FhTask`
- `FhDownload`
- `ToolManifestService`
- `ToolInstallService`
- `ToolRuntimeService`
- `GameLaunchService`
- `ConfigSnapshotService`

Avoid PCL/Minecraft names such as:
- `Mc*`
- `PCL*`
- `DlClient*`
- `ModDownload*`

## Maintenance Rules For Future Agents
Update this file when:
- The product goal changes.
- A major feature is added or removed.
- Manifest/schema fields change.
- Tool runtime behavior changes.
- Download/install/config strategy changes.
- A new important project constraint appears.

Keep updates short, factual, and current. Do not turn this file into a changelog; record only durable context that future work needs.
