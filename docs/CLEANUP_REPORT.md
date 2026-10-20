# Belie cleanup and stress-test results

Verified on Windows on 20 October 2026, using a Release build.

## Changes

- Split the main window into navigation, activity, gallery, profile selection, preview, editing, and settings files. Its entry file went from 1,363 to 198 lines.
- Split widget process management, host lifecycle, and window rendering. `DesktopCanvasHost.cs` went from 727 to 107 lines.
- Separate widget data and composition rules into `Models/DesktopWidget.cs` and `Models/DesktopCanvasDocument.cs`. Persistence retains loading, migration, validation, and saving.
- Group widget editor construction and appearance logic into separate files. Group the UI integration test helpers by feature while preserving their shared WPF application.
- Replace four JSON save implementations with `AtomicJsonFile`: streamed serialization, unique temporary files, coordinated readers/writers across store instances, atomic publication, and failure cleanup. Keep the canvas read-modify-write lock and legacy migration backups.
- Quarantine malformed profile JSON only. A temporary read/access failure now leaves the profile in place; a new regression test verifies recovery.
- Share duplicated media pickers, page switching, profile draft cloning, and widget copying.
- Replace repeated event/profile searches and event sorting with membership lookup and a single winner scan. Stop schedule-boundary scanning after the earliest qualifying day and remove per-rule transition-array allocations. Preserve priority, timestamp, tie order, overnight behavior, and manual override semantics.
- Add a [source walkthrough and feature map](CODEBASE_GUIDE.md), linked from the README.

The existing Studio labels/tooltips and changelog edits were preserved. Both live rendering backends, detached hosts, and old-data migration remain because they have distinct active uses. File splits move existing code; their smaller sizes do not represent deleted features.

## Verification

| Check | Result |
| --- | --- |
| Release build of app and tests | Passed; zero compiler errors |
| Dedicated stress suite | 9 passed, 0 failed |
| Final full suite | 130 passed, 1 failed, 0 skipped; 22.56 seconds |
| WPF gallery, navigation, editor, Studio, widgets integration | Passed |
| Temporary profile-read failure recovery | Passed |
| Whitespace/error-marker check | `git diff --check` passed |

The full-suite failure is `NowPlayingTests.WindowsMediaSession_CanBeReadWithoutChangingPlayback`: Windows media-session access is unavailable to this restricted process. Its assertion remains unchanged. Desktop media-session access still needs verification outside this host.

VSTest itself could not connect to its test process because localhost socket access is denied. The installed xUnit runner executed the suite in-process via `scripts/Test-InProcess.ps1`; no tests were silently filtered from the full run. The script redirects temporary files into the writable test-results directory because this host also denies file renames in its redirected system temporary folder.

The build emitted NU1900 warnings because NuGet's vulnerability-data endpoint was unreachable. This run does not establish dependency vulnerability status. `dotnet format` could not start its workspace host because named-pipe access is restricted; compilation and `git diff --check` completed separately.

## Dedicated stress measurements

These are observations from this machine, not performance guarantees or timing assertions.

| Scenario | Result | Time |
| --- | --- | ---: |
| 200 profile replacements with concurrent readers, 8 workers | Complete profiles; no valid files quarantined | 493 ms |
| 400 settings saves with concurrent readers, 8 workers | Complete, internally consistent snapshots | 842 ms |
| 200 library replacements of 64 assets, 8 workers | Names, collections, and tags stayed consistent | 473 ms |
| 400 canvas updates across 8 store instances | Every edit preserved | 2,307 ms |
| 20,000 library records | 10,000 unique assets; metadata preserved | 287 ms |
| 3,201 widgets and 150 layouts | Profile/wallpaper isolation and 50 resets preserved | 191 ms |
| 250 resolutions over 2,000 profiles and 4,003 events | Correct priority, timestamp, and stable ties | 86 ms |
| 48 randomized schedule sets | Matched a minute-by-minute reference | 19 ms |
| Serialization fails after a partial temporary write | Previous file preserved; temporary file removed | Passed |

The dedicated stress run finished in 4.83 seconds. Tests use disposable fixtures. They do not measure a long-running GPU/video soak, physical multi-monitor changes, or independent writer processes.

Machine-readable results and detailed logs are under `TestResults/cleanup/`. Reproduction commands are in the codebase guide.


## 3.4.2 release validation follow-up

The first GitHub release gate reproduced a transient Windows file-replacement failure in the library stress scenario (130 passed, 1 failed). Atomic publication now retries access/sharing violations at most four times over 300 ms, without reserializing, deleting the destination, or swallowing permanent failures. Added a regression scenario with an external read lock that is released during a save. The existing permanent-lock check still verifies preservation and temporary-file cleanup.

The updated local suite has 132 tests: 131 passed and the same restricted-host desktop media-session check failed. All ten stress scenarios, UI integration, and updater checks pass. Publishing remains gated on the complete GitHub Windows test run.
