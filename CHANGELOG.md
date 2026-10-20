# Belie patch notes

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
