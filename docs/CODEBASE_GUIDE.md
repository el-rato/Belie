# Finding your way around Belie

Belie is a Windows desktop app written in C# using WPF. Start with one user action and follow its calls. You do not need to read every file first.

## Start with Apply now

1. `UI/MainWindow.xaml` defines the button and its click handler.
2. `UI/MainWindow.Editing.cs` contains `ApplyNow_Click`, profile validation, saving, duplication, and deletion.
3. `Coordination/WallpaperCoordinator.cs` owns the active profile and coordinates manual choices, automatic rules, scenes, and playback.
4. `Resolution/ProfileResolver.cs` chooses a profile. Priority is manual override, active event, schedule, then fallback. Without a new choice, the coordinator can retain the current wallpaper.
5. `Engine/SlideshowController.cs` chooses media within that profile. `Engine/WallpaperEngine.cs` enumerates media and applies static wallpapers; the live controllers handle video and GIF playback.
6. `Persistence/SettingsStore.cs` saves the remembered profile. `StateChanged` and `WallpaperChanged` notify the UI and widget composition code.

Use your editor's **Go to definition** on each called method. **Find all references** answers who calls it. Search for a XAML button's `Click="..."` value when you start from the interface.

## Where to make a change

| Change | Start here |
| --- | --- |
| Startup, tray lifetime, single instance, command-line modes | `App.xaml.cs` |
| Page switching and opening the library or studio | `UI/MainWindow.Navigation.cs` |
| Overview, activity history, temporary overrides | `UI/MainWindow.Activity.cs` |
| Profile gallery, search, thumbnails | `UI/MainWindow.Gallery.cs` |
| Selected profile, unsaved edits, keyboard shortcuts | `UI/MainWindow.Profiles.cs` |
| Wallpaper preview, browsing, ordering, source pickers | `UI/MainWindow.Preview.cs` |
| Save, apply, duplicate, delete, profile editor launch | `UI/MainWindow.Editing.cs` |
| Themes, updates, settings dialog | `UI/MainWindow.Settings.cs` |
| Transition between wallpaper pictures | `Engine/WallpaperTransition.cs` (windows, frames, hand-over), `Engine/TransitionField.cs` (which cell changes when, subject outline), `Engine/TransitionPainter.cs` (drawing a frame, dimming). Skipped when windows together cover the desktop: `DesktopLayer.IsDesktopCoveredTogether` |
| Library organization | `UI/LibraryWindow.xaml.cs` |
| Online wallpaper discovery | `UI/LibraryWindow.Marketplace.cs`, `Infrastructure/WallhavenClient.cs` |
| Widget editor state, selection, saving, deletion | `UI/DesktopCanvasView.cs` |
| Widget editor controls and gallery construction | `UI/DesktopCanvasView.Workspace.cs` |
| Widget appearance and preview | `UI/DesktopCanvasView.Appearance.cs` |
| Per-wallpaper layouts, canvas interaction, presets, skins | `UI/DesktopCanvasView.Studio.cs` |
| Widget gallery (players, clocks, system, everyday) | `UI/WidgetStyleCatalog.cs` |
| Music player layouts | `UI/NowPlayingView.cs` |
| System, weather, calendar, checklist, timer widgets | `UI/InfoWidgetViews.cs` |
| Actual desktop widget rendering and dragging | `UI/DesktopWidgetWindow.cs` |
| Widget host lifecycle and visible windows | `Engine/DesktopCanvasHost.cs` |
| Starting/signaling the detached widget process | `Engine/DesktopCanvasProcess.cs` |
| One process for the widgets and the top bar | `Engine/DesktopHosts.cs` |
| Top bar process, refresh, shortcuts | `Engine/TopBarHost.cs`, `Engine/TopBarProcess.cs` |
| A workspace's profile, sent once its animation ends | `TopBarHost.SendProfileWhenStill` |
| Listening to the sound, shared by every visualizer | `Engine/DesktopAudioSpectrum.cs` (`Rent`, `Return`) |
| Top bar rendering and docking | `UI/TopBarWindow.cs`, `Infrastructure/AppBar.cs` |
| Top bar and Workspaces pages | `UI/TopBarPages.cs`, on the self-saving form in `UI/SettingsPage.cs`; values in `Models/TopBarSettings.cs` |
| Bar looks, module and part editors | `Engine/BarRice.cs`, `UI/TopBarPage.Looks.cs`, `UI/TopBarPage.Editing.cs`, `UI/BarPreview.cs` |
| Bar stylesheet (`topbar.css`), fonts, shapes | `Engine/BarStylesheet.cs`, `UI/BarFrame.cs` |
| Bar island, script modules | `UI/BarIsland.cs`, `Engine/BarScripts.cs` |
| Wallpaper switcher and its keys | `UI/WallpaperSwitcher.cs`, `UI/SwitcherDeck.cs`, `UI/SwitcherPage.cs`, `Engine/WallpaperKeys.cs` |
| Transitions page and its preview | `UI/TransitionsPage.cs` |
| Bar readings: system, weather, colors of the wallpaper | `Engine/SystemReadings.cs`, `Engine/WeatherSource.cs`, `Engine/WallpaperColors.cs` |
| Workspace and tiling keys, shared with profile shortcuts | `Engine/WorkspaceKeys.cs`, `Infrastructure/ProfileShortcutManager.cs` |
| Workspace rules | `Workspaces/WorkspaceManager.cs` |
| Automatic tiling | `Workspaces/TilingEngine.cs`, `Workspaces/DwindleTree.cs` |
| Workspace and window animations, their curves | `UI/SceneOverlay.cs`, `Workspaces/Motion.cs` |
| Which windows workspaces may touch | `Workspaces/NativeWindowSystem.cs` |
| Bringing hidden windows back after a crash | `Workspaces/HiddenWindowJournal.cs`, `TopBarProcess.RunGuard` |
| Widget fields, defaults, copy behavior | `Models/DesktopWidget.cs` |
| Composition, global/default/custom layout rules | `Models/DesktopCanvasDocument.cs` |
| JSON loading, migration, validation | `Persistence/*Store.cs` |
| Shared atomic JSON saving | `Persistence/AtomicJsonFile.cs` |
| Which profile should win | `Resolution/ProfileResolver.cs` |
| Battery, idle, and process detection | `Triggers/` |
| Ambient audio and accent application | `Engine/SceneController.cs` |

## C# details that explain the file structure

`partial class MainWindow` means that `MainWindow.xaml.cs`, `MainWindow.Navigation.cs`, and the other `MainWindow.*.cs` files compile into **one class**. They share fields and methods. Splitting the files makes responsibilities easier to find; it does not add new windows or change XAML event wiring. The same convention applies to `DesktopCanvasView` and the UI integration tests.

`*.xaml` describes controls, styles, and event wiring. Its companion C# files provide behavior. A `using` imports a namespace; it does not execute another file. `async` methods return control while waiting, and `await` resumes them later. Dispatcher calls return work to the WPF UI thread.

`Models/` describes data. `Persistence/` loads and saves it. `Resolution/` makes selection decisions. `Coordination/` applies those decisions. `Engine/` handles desktop/media integration. `UI/` displays and edits state. Follow those boundaries when adding code.

## What is intentionally separate

- Smooth live playback and icon-safe playback use different Windows rendering mechanisms. Both implementations are used.
- The detached live host and desktop host (widgets and top bar) keep rendering alive independently of the tray app.
- The wallpaper switcher belongs to the tray app; the bar asks for it through named events (`TopBarProcess.WallpaperEventName`). Choosing a wallpaper never stops a slideshow: it carries on from there.
- A look (`BarRice`) changes what the bar shows and how it is drawn, never workspaces, profile links or tiling. Looks from a file may hold commands, so importing one lists them and asks.
- The full profile editor supports rules and scene settings; the inline profile editor supports common daily edits.
- Legacy canvas migration preserves existing user data, including saved boards. Its backup and validation paths are still needed.

Do not delete these as duplicates based on similar names alone.

## Tests

Run the usual suite:

```powershell
dotnet test WallpaperProfiles.Tests/WallpaperProfiles.Tests.csproj -c Release
```

Run only the bounded stress tests:

```powershell
dotnet test WallpaperProfiles.Tests/WallpaperProfiles.Tests.csproj -c Release --filter Category=Stress
```

When VSTest's localhost connection is unavailable, use the installed xUnit runner directly:

```powershell
./scripts/Test-InProcess.ps1 -Suite All
./scripts/Test-InProcess.ps1 -Suite Stress -NoBuild
```

Tests in the `Desktop` category dock a real top bar for a few seconds and hide windows of a helper process they start. They do nothing unless `BELIE_DESKTOP_TESTS` is `1`.

The script records test names, failures, elapsed times, and stress measurements in `TestResults/cleanup/All.json` or `Stress.json`. It keeps temporary files and logs under that ignored test-results directory. The first command requires restored dependencies; run `dotnet restore WallpaperProfiles.Tests/WallpaperProfiles.Tests.csproj` on a fresh checkout.

The UI integration test helpers are grouped into `LibraryWindowTests.*.cs` files. They deliberately share one WPF application and one test entry point because WPF permits only one application per process. The Windows media-session test needs access to the user's desktop media services; a restricted host can deny it.

Ignore `bin/`, `obj/`, `release-output/`, and `TestResults/` while learning the source. They contain generated code, packages, caches, and test output.
