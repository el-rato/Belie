# Belie patch notes

## [4.0.5] - 2026-11-26 - Tighter local security

### Fixed

- Belie's background signals, such as switching the wallpaper, applying a profile or closing the app, now accept only your own Windows account. Before, other processes in the same sign-in could send them too.
- Another program can no longer stop Belie from saving by holding on to its save lock. Belie waits a moment, then saves anyway, and the file is still written safely in one step.
- Opening a Wallpaper Engine wallpaper passes its folder path safely, and a path with quotes or line breaks in it is refused instead of being sent on.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained.

## [4.0.4] - 2026-11-26 - Top bar stays put

### Fixed

- The top bar no longer disappears when Belie's window is closed to the tray, or whenever the desktop has focus. Windows reports the desktop as a full-screen app, and the bar used to hide for it. Now the bar only steps aside for an app that really covers its monitor, and comes back as soon as you switch away.
- The last letter of an italic song title or artist in the **Now playing** widget is no longer cut off at the edge.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained.

## [4.0.3] - 2026-11-26 - Safer shared looks

### Fixed

- A top bar look from someone else is checked more carefully before it can do anything. A look can no longer carry a hidden command that runs without being listed in the **commands in this look** review first, and choosing to use a look without its commands now clears all of them. Only Belie's own built-in actions, such as play, mute and switching the wallpaper, count as safe. Looks you make yourself are unaffected.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained.

## [4.0.2] - 2026-11-26 - Choose the bar, workspaces or neither

### Changed

- The top bar and workspaces are separate choices. Use both, either one or neither: **Top bar > Show the top bar** and **Workspaces > Use workspaces**.
- Workspaces run without the bar, opened with their shortcuts. Turning on the bar no longer turns on workspaces and tiling, and hiding the bar no longer stops them.
- What you were using stays on. If your bar was off, workspaces stay off too.
- **Show the top bar** is the first switch on the Top bar page.

### Fixed

- Opening Belie's window no longer changes which copy starts with Windows. A build or second copy could take over, and only an installed or complete portable copy can update itself. The installed copy takes its place back when it starts.
- A copy that cannot update itself now says so, and where it runs from, when Belie is installed on the PC.
- If an update check fails, as it can while Windows is still connecting, Belie tries again after five minutes instead of six hours.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained.

## [4.0.1] - 2026-11-26 - Steadier profile loading

### Fixed

- Opening a profile loads its wallpapers once. On a slower PC the list could fill, empty and fill again.
- Pressing Play in the transition preview while its pictures are loading now plays it once they are ready.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained.

## [4.0.0] - 2026-11-25 - A desktop of your own

Belie now has a top bar, workspaces with tiling, a wallpaper switcher and animated wallpaper transitions. The bar is off until you turn it on under **Top bar**; workspaces and tiling come with it and can each be turned off. Transitions start on, and can be turned off under **Transitions**.

### Top bar

- A bar along the top or bottom of every monitor, with 20 kinds of module: workspaces, window title, clock, date, media, visualizer, processor, memory, disk, network, volume, battery, uptime, weather, text, launchers, scripts, separators, the island and the wallpaper switcher.
- Eight looks to start from: Island, Notch, Mocha pills, Blocks, Glass, Terminal, Minimal and Neon. Your own can be saved, shared and opened as one file. Commands in a look from someone else are shown before you use it.
- The island shows the clock and the music, and grows for a new track, the volume or a workspace.
- Every module and part can be restyled on its own: colors, font, size, padding, border, rounding and shape. Colors can follow the wallpaper.
- A stylesheet in the part of CSS that Waybar users know. The bar follows as you type or save the file.
- Clicks, right clicks and scrolls on a module can run a command or one of Belie's actions.
- Volume, media controls, a player and a calendar open from the bar.

### Workspaces and tiling

- Up to nine workspaces on each monitor. Open one with Alt and a number, and add Shift to send the window there.
- Automatic tiling with gaps, animation and resizing that neighbors follow. Focus, swap, float and split tiles with Ctrl+Alt and the arrows, Ctrl+Alt+Shift and the arrows, Ctrl+Alt+F and Ctrl+Alt+J.
- A workspace can open a profile, and share its shortcut with one.
- Windows hidden on another workspace are brought back if Belie ends unexpectedly.

### Wallpaper switcher

- Ctrl+Alt+W shows the wallpapers of the profile you are on. Pick one and the slideshow carries on from there; the profile remembers it. Tab looks through another profile.
- Ctrl+Alt+PageDown and PageUp step through wallpapers.
- Slanted, carousel, filmstrip and grid layouts, with size, slant, spacing, outline, backdrop and keys to choose.

### Wallpaper transitions

- Ten ways for one wallpaper to give way to the next. Automatic finds a picture's subject, dims the old wallpaper, draws the subject's outline and spreads from it.
- Length, square size, direction, dimming and glow can be changed, with a preview.

### Widgets

- Nine music players, among them a turning record, a progress ring and a slim capsule.
- New widgets: system readings, weather, calendar, checklist and focus timer.

### Smoother and lighter

- Opening a workspace that has a profile no longer changes the wallpaper in the middle of the window animation; it waits until the windows are still.
- Window animations run ahead of the apps that are redrawing, and Windows no longer slows them down to save power.
- Neighboring tiles follow a dragged edge at your display's refresh rate.
- Visualizers share one listener, rest when hidden or silent, and stop behind full-screen apps.
- Reading the network and the volume no longer holds up the bar, and apps with changing titles cause far less work.
- Transitions are planned in the background, use fewer processor cores, and are skipped when windows cover the desktop.
- The bar and widgets share one process, and memory that was only needed for a moment is returned.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained. If a profile's shortcut is also a workspace's, such as Alt+1, both happen at once.

## [3.4.3] - 2026-11-22 - Widgets on every wallpaper

### Widgets

- One widget, every wallpaper: a widget now shows on every wallpaper in its profile and remembers its own spot on each wallpaper where you move it. Drag a widget on the desktop and that spot is kept for the wallpaper on screen; when the slideshow moves on, the widget moves to its spot for the next wallpaper.
- In the widget studio, **Arrange on** picks the profile's default spots or one wallpaper to arrange. The Layout tab shows whether a widget has its own spot there, and **Use default position** puts it back.
- Replaces per-wallpaper layouts (**Customize wallpaper** / **Use profile default**). Existing wallpaper layouts are converted automatically: each widget keeps the spot it had on that wallpaper and shows on the profile's other wallpapers again.
- Fixed a crash when pointing at the Layout information button in the widget studio.

### Discover

- New content rating filter: Safe only, Safe + sketchy, Sketchy only, NSFW only, Sketchy + NSFW, or Everything. Safe only remains the default.
- NSFW results use your own Wallhaven API key (**Wallhaven API key** button). Belie stores it encrypted for your Windows account and sends it only to wallhaven.cc. Downloaded sketchy or NSFW wallpapers are tagged in your library.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widgets, and shortcuts are retained.

## [3.4.2] - 2026-10-20 - A lighter Workspace

### Appearance and usability

- Larger wallpaper and desktop previews, compact profile/widget rows, and quieter panels with fewer borders and shadows.
- Secondary studio actions grouped in the More menu; widget visibility uses eye buttons, with existing controls and shortcuts retained.
- Responsive sidebar and stacked studio layout for narrow windows, visible keyboard focus, and reduced-motion support.
- Eight new themes: Obsidian, Nord, Dracula, Rose, Ocean, Mocha, Tokyo, and Sunset (13 themes total).
- Library tags now open from a compact Tags button instead of reducing the preview area.
- Tray menu follows the app's theme, font, hover colors, and accent checkmarks. Open Belie, profile switching, Pause automation, and Exit remain available.

### Rendering and reliability

- The main window uses WPF's hardware-capable rendering path; background widgets retain their existing rendering configuration.
- Widget editing reuses preview visuals instead of rebuilding each widget on every adjustment. Saved layouts change only through the existing save actions.
- Removed pane slide/fade and moving hover effects for steadier navigation.
- Retry brief Windows file-replacement contention without rewriting or discarding the previous saved data.
- Consolidated atomic JSON writes, preserved profiles after temporary read failures, and reduced repeated resolver work.
- Split large UI, model, and engine files into focused partials; added a codebase guide and nine stress scenarios.

### Updating

Read these notes offline at **Settings → App updates → What’s new**.

Open **Settings > App updates > Check for updates**, then choose **Update and restart**. Profiles, settings, schedules, widget layouts, and shortcuts are retained. Alternatively, install **Belie-win-Setup.exe** or extract the entire **Belie-win-Portable.zip**. Exit an older running instance before launching a portable build.

### Validation

Release compilation and the UI integration flow pass, including tray palette/target checks across all 13 themes. Regression and stress coverage includes persistence, profile selection, widget editing, layouts, and update controls. This restricted host cannot read the Windows desktop media session; that existing integration check still requires an interactive Windows session.

## [3.4.1] - Patch notes inside Belie

### What changed

- Added an in-app **What’s new** window with release notes and instructions for using Wallpaper Studio.
- Bundled the notes with the app so they remain readable offline.
- Added **Release notes** beside an available update, so future updates can explain their changes before installation.
- Release packages now include the same versioned patch notes used on GitHub.

### Where to find it

Update to 3.4.1, then open **Settings → App updates → What’s new**. The window also includes the full Wallpaper Studio guide from 3.4.0 below.

When another update is available, choose **Release notes** in the update notice or Settings to read the notes supplied with that update. **What’s new** continues to show the notes bundled with your installed version.

### Getting started with Wallpaper Studio

1. Open a profile, preview a wallpaper, and choose **Compose widgets**. You can also open **Widgets** from the sidebar and select a profile.
2. Select a wallpaper and choose **Customize wallpaper** to give it an independent widget layout.
3. Open **Styles catalogue**, choose a clock or visualizer, customize it, and press **Create**. For an existing widget, press **Save** after changing it.
4. Drag unlocked widgets directly on the canvas. Save each edited widget to keep its new position.
5. Turn on the profile’s slideshow and shuffle as usual. Belie automatically restores each wallpaper’s matching layout when it becomes active.

### Updating

Open **Settings → App updates → Check for updates**, then choose **Update and restart**. Existing profiles and settings are retained. For a new installation, use **Belie-win-Setup.exe**, or extract the complete **Belie-win-Portable.zip** for a portable installation.

## [3.4.0] - Wallpaper Studio and styles catalogue

### What changed

- A wallpaper canvas for composing a complete desktop look with clocks, visualizers, and other widgets.
- Independent widget layouts for individual wallpapers, with automatic switching during slideshows and shuffle.
- A visual catalogue with **10 clock designs** and **8 visualizer styles**, plus a standalone audio visualizer widget.
- Customizable fonts, colors, opacity, widget sizes, visualizer density, line thickness, audio sensitivity, and motion smoothing.
- Optional wallpaper color matching, saved layout presets, personal skins, and skin import/export.
- Background loading for wallpaper previews and a more compact widget editor.

### Open the studio

From **Profiles**, select a profile and preview the wallpaper you want to work with, then choose **Compose widgets**. Alternatively, open **Widgets** from the sidebar and choose a profile in the top selector.

### Profile defaults and wallpaper-specific layouts

1. Choose **Profile default** beside **Editing:** to edit the profile’s shared layout. Wallpapers with their own layouts are unaffected.
2. To make a particular wallpaper look different, select it and choose **Customize wallpaper**. Belie starts with an independent copy of the profile’s default widgets.
3. Add widgets, change their styles, or move unlocked widgets on the wallpaper canvas. Press **Create** for a new widget or **Save** for each edited widget.
4. Repeat for other wallpapers in the profile. Choose **Use profile default** to remove a wallpaper’s custom composition and return to the shared default.

Wallpapers without a custom layout use the profile default. Widgets assigned to **General · all profiles** remain visible across changes. Custom layouts belong to their profile, so the same image can have a different look in another profile.

### How slideshow and shuffle work

Keep your usual slideshow interval and **Shuffle the collection** setting under the profile’s **Playback** tab. Each time a wallpaper is applied successfully, Belie switches to that wallpaper’s saved composition. There is no need to reassign widgets after each shuffle.

Enable **Follow active wallpaper** in the studio to keep the preview on your desktop’s current wallpaper while no widget is being edited. This only controls the studio preview; it does not change your slideshow settings. While a widget is open in the editor, the canvas stays on that composition so a slideshow change does not interrupt your work.

### Clock and visualizer skins

Open **Styles catalogue** and choose **Clocks** or **Visualizers** to browse previews against your wallpaper. Selecting a skin from the studio creates a widget draft; press **Create** to keep it.

To restyle an existing clock, visualizer, or compatible music widget, select it and open **Style → Browse skins**. You can pair a clock with a visualizer or use them as separate widgets. Visualizers react to desktop audio and settle when playback is silent; catalogue previews demonstrate their motion without capturing audio.

Use the Style controls to adjust the design, shape, fonts, colors, and motion. **Use wallpaper colors** samples the selected wallpaper for a coordinated palette. Press **Save** to apply your widget edits.

### Reuse a look

- **Save as preset** stores the selected canvas’s saved arrangement as a named preset. Save each widget’s edits first. Find presets under **Layout presets** or the catalogue’s **Saved layouts** tab. Applying one replaces that canvas’s local widgets with an independent copy.
- **Style → Save skin** stores a customized appearance under **Styles catalogue → My styles**. Saving a skin does not apply an unsaved widget draft; save the widget separately.
- **Export skin** writes a skin file you can share. **Import skin…** in the catalogue adds a skin file to **My styles**.

Changes to a copied layout do not automatically change other wallpapers or the saved preset.
