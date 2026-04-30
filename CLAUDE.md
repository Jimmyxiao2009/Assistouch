# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

**Normal development (Visual Studio):** Open `Assistouch1.slnx`, select platform (x64 recommended) and configuration, press F5.

**Release sideload package** (for local install via `Add-AppDevPackage.ps1`):
```powershell
.\build-release-sideload.ps1
# Builds, then copies the _Test folder to: Release_Output\Sideload\
# Install from there: PowerShell -ExecutionPolicy Bypass -File ".\Release_Output\Sideload\Add-AppDevPackage.ps1"
```

**Store upload package** (multi-arch bundle for Partner Center):
```powershell
.\build-store.ps1
# Output: output\*.msixupload  (x86 + x64 + ARM64 bundle)
```
The script lets the WAP project handle all three architectures in one pass (via `AppxBundlePlatforms=x86|x64|ARM64`). The WAP internally drives each arch through its publish profile (`Properties\PublishProfiles\win-<Platform>.pubxml`). Do **not** pre-build the csproj per-arch separately — WAP does it correctly on its own.

**Collect both into a submission folder:**
```powershell
.\collect-release.ps1
# Output: Release_Submit\Sideload\ + Release_Submit\StoreUpload\
```

MSBuild path used in scripts: `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`

**Install sideload package** (must use this script, not `Add-AppxPackage` directly):
```powershell
# If app is already running, kill it first (otherwise you get error 0x80073D02):
Stop-Process -Name 'Assistouch1' -Force -ErrorAction SilentlyContinue; Start-Sleep 2
PowerShell -ExecutionPolicy Bypass -File ".\Release_Output\Sideload\Add-AppDevPackage.ps1"
```

## Project Structure

Two projects in the solution:
- **`Assistouch1\Assistouch1.csproj`** — The app itself (WinUI 3, net8.0-windows10.0.19041.0)
- **`Assistouch1 (Package)\Assistouch1 (Package).wapproj`** — WAP packaging project (produces MSIX)

Namespace is `AssistiveTouch` throughout (historical mismatch with `RootNamespace=Assistouch1` in csproj — intentional, do not change).

## Architecture

### Singleton services on `App` (static properties, constructed at startup)
| Property | Type | Role |
|---|---|---|
| `App.Config` | `ConfigService` | Loads/saves `AppConfig` from `%LocalAppData%\AssistiveTouch\config.json` |
| `App.FgWatcher` | `ForegroundWindowService` | Polls foreground window every 500 ms; raises `ForegroundChanged` |
| `App.Rules` | `RuleEngine` | Matches `AppRule` matchers against foreground window info |
| `App.Executor` | `ActionExecutor` | Executes `ActionItem` via Win32 `SendInput`, `Process.Start`, etc. |

### Windows (all created/owned by `App`)
- **`FloatingButtonWindow`** — Always-on-top draggable button (`WS_EX_TOOLWINDOW`, `HWND_TOPMOST`). On tap (no drag), creates `ActionMenuWindow`. Position stored as normalized (0–1) ratios in `AppConfig.ButtonX/Y`.
- **`ActionMenuWindow`** — Frameless popup built imperatively in `BuildMenu()`. Closes on deactivation. Positions itself above/below/left/right of the floating button to avoid overlap. Passes captured `ForegroundWindowInfo` to `ActionExecutor` so hotkeys go to the right window.
- **`TrayMenuWindow`** — Popup shown when tray icon is left-clicked (toggle). Contains "Settings" and "Exit" items.
- **`SettingsWindow`** — NavigationView shell hosting four pages. Only one instance exists at a time (`App` keeps the reference).

### Menu construction flow
1. User taps floating button → `FloatingButtonWindow.ShowMenu()` captures `ForegroundWindowInfo` via `FgWatcher.CaptureContext()` (returns last external window if current focus is Assistouch itself)
2. `ActionMenuWindow` constructor calls `BuildMenu()` which queries `App.Rules.GetRecommendedActions(fg)` and `App.Config.Config.PinnedActions`
3. On item click: `ExecuteActionAndClose()` closes popup, waits 50 ms, calls `App.Executor.Execute(action, fg)`
4. Executor calls `RestoreForegroundWindow(fg)` before injecting keyboard/mouse input

### Action types (`ActionType` enum)
`Hotkey`, `SimulateClick`, `Script` (cmd/powershell/python), `ShowDesktop`, `OpenUrl`, `LaunchApp`, `KillProcess`, `MediaControl`, `SendText`, `LockScreen`

### Rule matching (`RuleEngine`)
Each `AppRule` has a list of `RuleMatcher` (AND logic). `MatchType` options: `ProcessName`, `ExeName`, `Pid`, `ProcessNameRegex`, `WindowTitleRegex`. Rules with no matchers never match.

### Config persistence
`ConfigService` serializes `AppConfig` as JSON using `System.Text.Json` source-generated context (`AppJsonContext`) — trim-safe, no reflection. Config version is bumped in `CurrentConfigVersion`; `MigrateIfNeeded()` merges new default rules without overwriting user-deleted ones.

### Tray icon
`TrayIconService` creates a hidden `HWND_MESSAGE` window and registers it with `Shell_NotifyIcon`. Icon is loaded via `ExtractIconEx` from the running exe (works in both packaged MSIX and unpackaged contexts). Left-click raises `MenuRequested`; right-click is ignored.

### Crash logging
`CrashLogger.Write(ex, context)` appends to `%LocalAppData%\AssistiveTouch\crash.log`. Hooked into `Application.UnhandledException`, `TaskScheduler.UnobservedTaskException`, and `AppDomain.UnhandledException`.

## Key Patterns and Pitfalls

**MSIX packaging:** The csproj defines `MICROSOFT_WINDOWSAPPSDK_BOOTSTRAP_AUTO_INITIALIZE_OPTIONS_ONPACKAGEIDENTITY_NOOP` so the app runs in both unpackaged (direct exe) and packaged (MSIX/Store) contexts. Without this, packaged launch crashes immediately.

**DataTemplates must be in App.xaml:** Building UI in code-behind via `XamlReader.Load()` with bindings causes crashes. All `DataTemplate` resources live in `App.xaml` static resources.

**ContentDialog requires XamlRoot:** Always pass `XamlRoot` from the page's property, never null.

**DispatcherQueue:** Always use the window/page instance's `DispatcherQueue`, never `GetForCurrentThread()` from a background thread.

**SystemBackdrop must be set after first Activated event:** Both `FloatingButtonWindow` and `ActionMenuWindow` subscribe to `Activated` and apply backdrop on the first call.

**Win32 style changes order matters:** Strip caption/frame → set `WS_EX_TOOLWINDOW` → `SetWindowPos` with `SWP_FRAMECHANGED` → then apply DWM attributes → then resize. Changing order causes border artifacts.

**Foreground window capture timing:** `FloatingButtonWindow` captures `ForegroundWindowInfo` on `PointerPressed` (before this window steals focus) and passes it to `ActionMenuWindow`. This ensures hotkeys land on the user's previous app, not on Assistouch.

**AppxPackageDir trailing backslash:** In PowerShell, `"/p:AppxPackageDir=C:\path\"` escapes the closing quote. Never pass a trailing `\` in MSBuild property strings from PowerShell. The build scripts avoid this by omitting `AppxPackageDir` and using a post-build `Copy-Item` instead.

**Adding new XAML pages:** The SDK auto-discovers `*.xaml` files; do not add explicit `<Page Include="..."/>` entries in the csproj — this causes NETSDK1022 duplicate item errors.

**Imperative drag-and-drop reorder (StackPanel, not ListView):** `ListView.CanReorderItems` is unreliable when DataTemplate children (e.g. `Border` with `Background`, inner `Button`s) consume pointer events. Use a plain `StackPanel` instead: set `CanDrag=true` on the grip element + handle `DragStarting` to set `_dragging`; set `AllowDrop=true` on each card + handle `DragOver`/`DragLeave`/`Drop` to reorder in the list and call `RebuildItems()`. Requires `using Windows.ApplicationModel.DataTransfer;`.

**OS-intercepted shortcuts:** `SendInput` cannot inject Win+L, Win+D, Ctrl+Alt+Del — the OS/shell intercepts them. `ActionExecutor` has a `_blockedOverrides` dictionary keyed by normalised hotkey string (spaces stripped, OrdinalIgnoreCase). Entries: `Win+L` → `LockWorkStation()`, `Win+D` → COM `Shell.Application.ToggleDesktop()`, `Win+E` → `Process.Start("explorer.exe")`, `Win+I` → `Process.Start("ms-settings:")`, `Ctrl+Alt+Del` → no-op. Add new overrides there for any other shell-intercepted keys.

**`FgWatcher.CaptureContext()` vs `Snapshot()`:** In test buttons inside SettingsWindow pages (RulesPage, etc.), always call `App.FgWatcher.CaptureContext()` — it returns the last non-Assistouch foreground window. `ForegroundWindowService.Snapshot()` returns the *current* foreground window, which will be the Settings window itself.

**Window icon:** Call `AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"))` in the window constructor (after `InitializeComponent()`). Works in both packaged and unpackaged contexts.

**WinUI 3 missing WPF APIs:** `Border` has no public `Cursor` property (WPF-only). `UIElement.ProtectedCursor` is protected. To change cursor, derive a custom control or omit it. `ToggleButton` is in `Microsoft.UI.Xaml.Controls.Primitives`, not `Microsoft.UI.Xaml.Controls`.

**C# definite assignment with captured locals:** If a local variable is captured inside a lambda/local function that is registered as an event handler, the compiler requires the variable to be declared *before* the handler registration — even if the event cannot fire until after assignment. Always declare the captured variable first, then wire up the handlers.

**Deleting XAML files requires a Clean build:** The XAML compiler caches type registrations in `obj\...\XamlTypeInfo.g.cs`. If you delete a `.xaml` file (e.g. `MainWindow.xaml`), the stale generated file will still reference the removed type and cause `CS0234` errors on the next build. Fix: run MSBuild `/t:Clean` for all configurations (x86/x64/ARM64) before rebuilding, or delete the relevant `obj\` subdirectory manually.

**Trimming disabled:** `PublishTrimmed=False` — MSIX packaged apps use framework-dependent deployment; trimming is not applicable and causes false-positive IL warnings with COM interop (`dynamic`, `Activator.CreateInstance`, `Type.GetTypeFromProgID`).

**COM interop for Shell.Application:** Use the `IShellApplication` `[ComImport]` interface (defined in `ActionExecutor.cs`) instead of `dynamic` to call `ToggleDesktop()`. This is trim-safe and avoids IL2096/IL2026 warnings.

**`AllowUnsafeBlocks`:** Enabled in the csproj — required by `Win32/NativeMethods.cs`.
