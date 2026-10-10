# Belie

A small Windows app (Windows 10/11) that lets you create **profiles**, each linked to a folder
of images, a single image, or a **video/GIF**, and switch your desktop wallpaper **manually**
(tray menu), on a **schedule** (days/times), or **automatically** when something happens
(battery, idle, or a program launches).

It lives quietly in the **system tray** (bottom-right corner of the taskbar). You never need to
know C# or .NET to use it. Choose from four dark themes and the light Daylight theme.

## Download

[Download the latest Windows release](https://github.com/el-rato/Wallpaper-Profile-App/releases/latest/download/Belie.exe).

### New in 3.1.0

- Polished navigation, larger wallpaper previews, responsive galleries, and subtle interaction feedback.
- Profile keyboard shortcuts for quick switching, including while Belie is in the tray.
- Customizable Now playing widgets with a transparent Rainmeter style, song details, and a thin progress line.
- Music-player filtering that ignores browser and video playback, plus optional album art and playback controls.
- Drag the song text to position an unlocked music widget on the desktop.
- Landscape-only wallpaper discovery and clearer save, apply, and unsaved-change feedback.

---

## How to build (developer only)

You need the **.NET 8 SDK**. Open a terminal **in this folder** and run:

```
dotnet build -c Debug
```

The compiled app appears in `bin\Debug\net8.0-windows10.0.19041.0\Belie.exe`.

Unit tests (resolver, schedule rules, profile storage):

```
dotnet test WallpaperProfiles.Tests\WallpaperProfiles.Tests.csproj
```

## How to make a single, self-contained .exe (no install needed for other users)

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Result: `publish\Belie.exe` (about 140 MB). This one file contains everything and runs
on any Windows 10/11 PC with no .NET install. Copy it to a USB stick or share it directly.

> **One-time security prompt:** the first time you run a freshly published `.exe`, Windows
> SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**. This is
> normal for any program that is not code-signed; it does not mean the app is harmful.

---

## First run, in plain language

1. Double-click `Belie.exe`.
2. A small icon appears in the **system tray** (near the clock, bottom-right).
3. A window also opens. If it doesn't, **left-click or double-click the tray icon**.
4. Select a profile (or use the dashed **+ Add profile** card), type a name, then either click
   **Folder** to pick a folder of pictures, **Image** to pick one picture, **Video** to pick a
   live wallpaper video, or simply **drag & drop an image/folder onto the big preview**.
   Choose a **Fit mode** and click **Save**.
5. With your profile selected, click **Apply now** — your wallpaper changes instantly.
6. Close the window: the app keeps running in the tray. To fully quit, **right-click the tray
   icon → Exit**.

Starting the app a second time (e.g. double-clicking the exe again) simply brings the existing
window to the front — there is only ever one instance.

---

## Tray menu (right-click the icon)

- **List of profiles** — click any one to switch right now.
- **Manage Profiles…** — opens the main window.
- **Pause Scheduling** — freezes automatic changes (manual switches still work). Toggle on/off.
- **Exit** — closes the app completely.

---

## Features

### Selecting images
- **Modern file dialogs** — the folder picker is the same fluent Explorer dialog you use daily.
- **Drag & drop** — drop an image, a video, or a whole folder onto the preview (or the path box).
- **Live validation** — a status dot under the path shows how many usable images were found, or
  warns when a path no longer exists. Previews load in the background so the UI never stalls,
  even on network shares or folders with hundreds of pictures.

### Live wallpaper (video & GIF)
Pick a **video** with the **Video** button (or drop one onto the preview) and the app plays it
behind your desktop icons — a real animated desktop, just like the popular live-wallpaper apps:

- **Formats:** MP4, MOV, WMV, AVI, MPG/MPEG, M4V, M2V, MKV, WebM, 3GP — anything Windows Media
  Foundation can decode on a stock Windows install (H.264 MP4 is the safest choice). Animated
  **GIFs** work too: pick a `.gif` file as the profile's single source.
- **How it works:** a hidden window is re-parented onto the desktop's `WorkerW` layer, so the
  video loops silently behind your icons while Windows' normal static wallpaper stays underneath
  it as a fallback.
- **Controls:** the **Fit mode** applies to video too (Fill = cover, Fit = letterboxed,
  Stretch = distort). **Mute live video audio** (Advanced section) keeps videos silent —
  untick it for sound.
- **Keep desktop icons visible** (Advanced section): on Windows 11 24H2+ the smooth overlay
  covers the desktop icons (see below). Tick this to play the video as the **real desktop
  wallpaper** instead — icons and taskbar stay fully visible and normal, at a reduced
  update rate (~4 fps). Untick it for the smooth full-frame-rate overlay.
- **Everything works together:** video profiles honor schedules, event triggers, manual
  switches, and the tray menu exactly like image profiles. Switching to an image profile
  (or pausing/deleting) stops the video and returns you to the static wallpaper.
- **Survives closing the app:** live wallpapers render on a dedicated thread, and when you
  **Exit** the app it hands the playing video/GIF to a small detached helper process
  (a hidden copy of Belie running `--live-host`), so your animated wallpaper **keeps playing
  after the app is gone**. The moment you start Belie again or switch profiles, the helper
  steps aside and the app takes over.
- **Notes:** 4K videos use noticeable CPU/GPU; 1080p is a comfortable sweet spot. The video
  plays only while *something* is alive: the app itself, or its detached helper after you
  exit. Videos inside *folders* are ignored by slideshows — select a video file directly.
- **Windows 11 24H2+ rendering mode:** newer Windows builds no longer allow apps to draw
  between the wallpaper and the desktop icons, so on those builds the live wallpaper runs as
  a click-through overlay above the desktop: the video is fully visible and interactive
  elements pass through, but the desktop icons are covered while a live profile is active
  (they come back the moment you switch to an image profile) — the window is also kept one
  pixel short of the screen so Windows doesn't mistake it for a fullscreen app and hide the
  taskbar. On Windows 10 and earlier Windows 11 builds the classic behind-the-icons mode is
  used automatically. Use the **Keep desktop icons visible** option if you prefer icons over
  smoothness.

### Free live wallpapers — no Wallpaper Engine needed
This app's live wallpaper engine replaces what most people use Wallpaper Engine for, at no cost:

- **Pixabay** (pixabay.com/videos) and **Pexels** (pexels.com/videos) — huge libraries of
  high-quality, royalty-free videos you can download as MP4 and use directly. Loop-friendly
  picks: ocean/space/rain/city timelapses.
- **WallpaperHub** (wallpaperhub.app) — free official-style motion wallpapers for Windows.
- **Animated GIFs** — any GIF from Tenor/Gfycat or saved from the web works; pick the `.gif`
  file directly in the app.
- **Your own clips** — game replays, drone footage, screen recordings: any MP4 you have.
- Tip: for a seamless loop, pick videos that start and end on a similar frame — the app loops
  them endlessly.

If you *do* own Wallpaper Engine on Steam, the app can also read its workshop library
(`Engine\WallpaperEngineWorkshop.cs`): video wallpapers can be added to profiles directly,
and scene/web/application types are handed over to the Wallpaper Engine app itself. Without a
Wallpaper Engine license there is no workshop to read — the free sources above are the way to go.

### Slideshow
Set **Slideshow interval (min, 0 = off)** to a number (e.g. `5`). The app cycles through the
folder's images on that timer. Tick **Random slideshow order** for a shuffle. Slideshow pauses
when the profile is deactivated and resumes when it is reactivated.

### Schedule
Add rules: pick the **days** (Mo–Su), a **start** and **end** time. When the current time falls
inside a rule, that profile becomes active. You can add several rules to one profile.

### Event triggers
- **On battery** — laptop unplugged.
- **Plugged in / charging** — on AC power.
- **Battery below N%** — enter a number like `20`.
- **User idle ≥ N minutes** — enter minutes like `15` (no mouse/keyboard for that long).
- **A program starts** — enter a program name like `notepad.exe`. Activates while that program
  is running; reverts when it closes.

Each trigger has a **Priority** (1–99, higher wins) so you can decide what dominates when several
things are true at once.

### Reliability details
- Wallpapers are applied through a **local staging copy** (`cache\` folder — see below), so your
  desktop keeps its wallpaper even if the source image lived on a USB stick or network share that
  later disconnects. It also sidesteps the classic wallpaper API's long-path limits.
- Profile and settings files are written **atomically** (temp file + rename) — a crash or power
  loss mid-write can never corrupt them. Unreadable profile files are quarantined with a
  `.corrupt-…` suffix instead of breaking startup.
- Wallpaper changes are **serialized and generation-guarded**: rapid profile switches can never
  apply out of order, and a slow apply for an old profile is skipped instead of overwriting the
  new one.
- Everything is logged to the rolling `logs\` folder, including background task failures.

---

## How the app decides which profile is active (priority order)

Highest to lowest:

1. **Manual switch** — always wins immediately and holds until the **next scheduled boundary**
   (the next time any schedule rule starts or ends). After that, automatic rules take over again.
2. **Event trigger** — wins over the schedule while its condition is true. If several triggers are
   true at once, the one with the **highest priority** wins; if tied, the one that became true most
   recently wins.
3. **Scheduled profile** — the normal baseline/default.

If nothing matches (no schedule, no active trigger, no manual switch) the wallpaper is simply left
as it is.

### Documented tie-breaks

- **Overlapping schedule rules:** when two rules match at the same moment, the rule that was
  **created most recently** wins (each rule stores a creation timestamp).
- **Overlapping event triggers:** highest `Priority` wins; ties broken by most-recently-activated.
- **Manual override expiry:** it ends at the next scheduled boundary across *all* profiles' rules.
  If you have no schedule rules at all, a manual switch simply stays until you pick another profile
  or restart the app.
- **Missing profiles:** if a resolved profile was deleted, the manual override is cleared for that
  round and the app falls back to the next automatic choice.

All of this logic lives in one file, `Resolution\ProfileResolver.cs`, and is covered by unit tests
in `WallpaperProfiles.Tests\`.

---

## Data — where things are stored

Everything lives under:

```
C:\Users\YOURNAME\AppData\Roaming\Belie\
```

(Old `WallpaperProfiles` data is migrated here automatically on first run.)

- `profiles\` — one JSON file per profile.
- `settings.json` — minimized/last-active-profile preferences.
- `cache\` — the staged copy of the currently applied wallpaper (kept short-lived and small;
  safe to clear while the app is closed).
- `logs\` — rolling log files (old ones deleted after 14 days).

**To uninstall / reset:** close the app (tray → Exit), then delete that `Belie` folder. Also
uncheck "Start with Windows" first if you enabled it (or delete the `Belie` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).

---

## Settings (bottom of the main window)

- **Start with Windows** — adds the app to your login startup (writes a `Run` registry key).
- **Minimized to tray** — the window stays hidden on launch; use the tray icon.

---

## Command-line (optional, for testing)

```
Belie.exe --set-wallpaper "C:\path\to\pic.png" [--fit fill]
Belie.exe --set-wallpaper "C:\path\to\clip.mp4"   (live wallpaper until you press OK)
Belie.exe --apply-profile "<profile name>"
Belie.exe --minimized
Belie.exe --help
```

---

## Troubleshooting

- **Nothing happens / wallpaper didn't change:** read the latest `.log` file in
  `%APPDATA%\WallpaperProfiles\logs`. Missing folders, unreadable images, and failures are logged
  there.
- **Wallpaper reverted to a plain image / Windows default:** check whether the source folder is on
  a disconnected drive; the app applies from its local `cache\` copy, so this should no longer
  happen — if it does, the log will say so.
- **Multi-monitor:** the app sets one image using Windows' classic wallpaper engine
  (Fill/Fit/Stretch/Tile/Center/Span). "Span" stretches one image across all monitors. True
  per-monitor different images are not supported yet.
- **Process-launch trigger never fires:** the program name must match exactly, e.g. `notepad.exe`.
  `.exe` is added automatically if you omit it. Detection uses the WMI service (always on in Windows
  10/11) and does not require administrator rights.
- **A video wallpaper doesn't play:** the file may use a codec your Windows lacks (some HEVC/MKV
  variants). The app logs it, closes the live host, and keeps your previous static wallpaper —
  re-encode to H.264 MP4 for guaranteed playback.
- **The live wallpaper stays after I exit the app:** that's the detached helper doing its job —
  it stops automatically the next time Belie starts or you switch to a static wallpaper. To stop
  it right away, start Belie once (it takes over immediately) or kill the `Belie.exe` process.
- **Antivirus blocking:** a few scanners flag the single-file `.exe` because it's a bundled
  self-extracting app. Add an exception or use the `bin\Debug` build from your own machine.

---

## Build order (how it was implemented, stage by stage)

1. Wallpaper engine — P/Invoke `SystemParametersInfo` + registry fit mode, with local staging
   and serialized background applies.
2. Profile model + JSON persistence (atomic writes, corrupt-file quarantine).
3. Tray icon + context menu wired to profiles.
4. Slideshow timer for the active profile (background scans/applies).
5. Scheduler (time/day switching every ~20 s).
6. Event triggers: battery, idle, process-launch.
7. Main window UI (profiles + editor + settings), modern dialogs, drag & drop.
8. Autostart + start-minimized.
9. Single-instance activation + CLI.
10. Unit tests + single self-contained `.exe` publish.
11. Live wallpapers — `WorkerW`-hosted window playing videos (MediaElement) and animated GIFs
    (frame stepping), wired into profiles, schedules, triggers, UI, and CLI.
