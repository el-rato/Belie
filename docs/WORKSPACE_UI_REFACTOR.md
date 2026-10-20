# Workspace UI refactor

Assumption: this is the existing WPF application; Windows resources replace CSS variables, and the selected theme, product name, routes, saved data and shortcuts stay intact. Ember supplies the requested warm peach identity; all existing theme choices remain available. The theme expansion adds Obsidian, Nord, Dracula, Rose, Ocean, Mocha, Tokyo and Sunset for 13 total themes.

## Design rationale

- Make previews the largest visual elements; give Profiles a larger, aspect-correct wallpaper view.
- Use one accent action per screen: Add widget or Apply to desktop. Keep Save and contextual actions quieter.
- Move studio utilities and the three quick-add shortcuts into one keyboard-accessible overflow.
- Replace gallery tiles with compact thumbnail rows, type badges and fixed 36px visibility buttons.
- Keep profile rows compact, with a thumbnail, name and Applied or Automated status; put combined status in the tooltip.
- Replace persistent instructional paragraphs with contextual help and short preview hints.
- Use one Segoe UI family, four shell text sizes, an 8px layout rhythm and shared surface/radius tokens.
- Collapse navigation labels below 1000px; stack the widget list/editor below the preview when its pane is narrower than 800px.
- Reveal preview overlays on hover or keyboard focus; preserve native focus navigation and Windows reduced-motion/high-contrast behavior.

## Tokens

Theme brushes remain dynamic so theme switching and per-profile scene accents still work. Values below describe Ember; Theme.xaml contains the fallback Sage palette matching the existing default selection.

| Token | Value / role |
| --- | --- |
| BgBrush | #121212 — app background |
| SurfaceBrush | #191919 — panels |
| SurfaceRaisedBrush | #232222 — raised surfaces |
| SurfaceHoverBrush | #302C29 — hover |
| BorderBrush | #3B3531 — subtle decorative separators |
| BorderStrongBrush | #85766B — stronger boundaries |
| TextPrimaryBrush | #F5F1ED |
| TextSecondaryBrush | #C8BFB8 |
| TextTertiaryBrush | #ABA19A |
| AccentBrush | #EFC1A0 |
| AccentHoverBrush | Accent mixed with 20% white, using the existing theme function |
| AccentPressedBrush | Accent mixed with 15% black, using the existing theme function |
| AccentSubtleBrush | #382C24 |
| AccentTextBrush | Existing contrast-adjusted accent |
| DangerBrush / GoodBrush / SignalBrush | #E49B94 / #98CAA5 / #D9AF6E in dark themes |
| Preview overlay | #EE191919 with white text |
| SpaceSmall / SpaceMedium / SpaceLarge | 8 / 16 / 24px Thickness resources |
| RadiusControl / RadiusPanel | 8 / 12px CornerRadius resources |
| AppFont | Segoe UI Variable Text, Segoe UI |
| TypeCaption / TypeBody / TypeSection / TypeTitle | 12 / 14 / 20 / 24px |
| IconFont | Segoe MDL2 Assets; distinct clock, spectrum, player, note, link, image and countdown glyphs |
| ControlTarget | 36px minimum height; visibility and preview buttons are 36 × 36px |
| PreviewElevation | Reserved resource; preview shadows are no longer applied |
| FocusRing | 2px primary-text outline, 12px radius |
| Motion | Immediate page, card and hover feedback; loading indicators and widget content retain their existing animations |

Widget typography and appearance remain user-controlled and are excluded from the shell type scale.

## Copy and location map

Unchanged labels are omitted. Dynamic string patterns are listed once rather than repeating each profile name or counter value.

| Location | Before | After / where the function lives |
| --- | --- | --- |
| Widgets title | Wallpaper studio | Widgets |
| Widgets subtitle | Compose a look. Each wallpaper remembers its widgets. | Removed; wallpaper-specific behavior remains unchanged and is explained by Layout information |
| Studio header | Styles catalogue | Styles, in the overflow |
| Studio quick-add row | Clock / Visualizer / Now playing chips | Moved to overflow → Quick add; the full Add widget picker remains |
| Studio counter | N on desktop · N widget(s) in gallery | N active · N total; same counts |
| Studio layout row | Editing: | Layout |
| Studio layout row | Follow active wallpaper | Follow wallpaper; original checkbox behavior and explanatory tooltip retained |
| Layout helper | Editing the shared layout for this profile. Wallpapers with their own layout are unaffected. | Layout information icon → tooltip and accessible help text; opens by keyboard activation too |
| Layout helper | Editing this wallpaper's own layout. It appears automatically when this wallpaper plays. | Layout information icon, updated for the current selection |
| Layout helper | This wallpaper shares the profile default. Choose Customize wallpaper before making changes just for this wallpaper. | Layout information icon; Customize wallpaper remains beside the layout selector |
| Preview footer | Drag widgets on the canvas. Save to keep your composition. Shuffle restores each wallpaper's layout automatically. | Drag to arrange · Save to keep; full meaning available in preview tooltip |
| Preview caption | Preview: [profile] · default layout / [filename] · widgets hidden | [profile] · Default / [filename] · Hidden, in floating preview caption |
| Empty preview caption | Add a clock, visualizer, or another widget to compose this look. | Removed; Add widget remains the primary action |
| Empty gallery | Drop an image, link, or note here. Or add your first widget. | Drop a file, link or note; drop handlers and Add widget remain |
| Widget row | Hide / Show | Eye / hidden-eye icon; original action names retained in tooltips and accessibility names |
| Widget row | Type · Hidden; profile scope on a separate line | Type badge; scope and Hidden state in row tooltip, visibility shown by eye icon |
| Widget editor | Delete widget | Delete; Delete widget tooltip retained |
| Sidebar context | Applied to desktop | Applied; editing the active profile still uses the same button |
| Profile rail heading | YOUR PROFILES | Profiles |
| Profile rail status | Not applied · Automated / Applied · Automated / Not applied | Automated / Applied / no extra sentence; full combined status in row tooltip |
| Profile status | Currently applied to your desktop | Applied |
| Profile status | Editing profile · Apply to use it on your desktop | Not applied; Apply to desktop remains prominent |
| Profile actions | More | Ellipsis icon; More profile actions tooltip; duplicate/delete menu unchanged |
| Wallpaper tab | Wallpaper source | Source |
| Wallpaper sources | Add from library | Library; same OpenLibrary_Click handler; full tooltip and accessibility name |
| Wallpaper sources | Compose widgets | Widgets; same ComposeWallpaper_Click handler; full tooltip and accessibility name |
| Wallpaper sources | Choose folder / Choose image / Choose video | Folder / Image / Video; same pickers; original names in tooltips and accessibility names |
| Wallpaper tab | Image fit | Fit; dropdown options, binding and accessibility name remain |
| Empty wallpaper preview | Drop an image or video here to get started. | Drop an image or video |
| Playback intro | Control how this scene moves and sounds. | Removed; all playback controls remain in the Playback tab |
| Playback | Shuffle the collection | Shuffle; randomized-order tooltip on the existing switch |
| Playback helper | Keep the next wallpaper a surprise. | Removed; randomized-order tooltip |
| Playback | Quiet playback | Mute video; existing switch and full accessibility name |
| Playback helper | Play video wallpapers without their audio. | Tooltip on Mute video |
| Playback | Keep your desktop in view | Keep icons visible; existing switch |
| Playback helper | Preserve icons and the taskbar over live wallpapers. | Existing icon-safe playback tooltip |
| Automation intro | Apply this profile on a schedule or when an event occurs. | Removed; schedules, triggers and their editor remain in Automation |
| Mood intro | Pair a color with an ambient soundtrack. | Removed; Edit mood and scene summary remain in Mood |
| Narrow sidebar | Overview / Widgets / Profiles / Library / Settings | Same icon buttons with full tooltips and accessibility names |
| Narrow sidebar | Workspace / Profiles headings | Hidden at narrow widths; navigation and profile rows remain |
| Narrow sidebar | Search profiles placeholder | Search icon; original editable search field, tooltip and accessibility name retained |
| Narrow sidebar | + New profile | +; same handler, New profile tooltip and accessibility name |
| Tray menu | Manage Profiles… | Open Belie; same open-window action |
| Tray menu | Pause Scheduling | Pause automation; same pause action and checked state |

Save as preset, Layout presets, Show/Hide widgets and Find widgets keep their original labels and handlers and move into the studio overflow. The unselected profile name never changes when a different profile is applied.

## Feature preservation checklist

The following controls retain their existing handlers, bindings and state flow. Test results and any environment limitations are recorded in the delivery message.

- [x] Overview, Widgets, Profiles, Library and Settings navigation.
- [x] Profile search, selection, thumbnails, active/automated indicators and new profile creation.
- [x] Profile rename, Save, Apply / Save & apply, duplicate and delete.
- [x] Assign, capture, remove and cancel global profile shortcuts; existing tray shortcuts.
- [x] Wallpaper preview, previous/next, counter, expand and drag/drop.
- [x] Wallpaper, Playback, Automation and Mood tabs.
- [x] Library import, widget composition, folder/image/video pickers, wallpaper ordering and fit modes.
- [x] Slideshow interval, shuffle, video mute and icon-friendly playback.
- [x] Schedule/trigger summaries and editing, scene accent and ambient audio.
- [x] Add widget picker for every existing kind and all three quick-add actions.
- [x] Profile/global scope filter, active/total counts, show/hide all and Find widgets.
- [x] Wallpaper layout selector, follow active wallpaper, customize and reset layout.
- [x] Save layout preset, browse layout presets and styles catalogue.
- [x] Widget selection, visibility toggle, duplicate, delete and Close editor.
- [x] Content, Style and Layout editor tabs, every existing editor field, skin tools and style preset.
- [x] Live preview, drag positioning, locking, saving and draft save/discard/cancel safeguards.
- [x] Widget/profile data persistence, slideshow layout restoration and desktop host calls unchanged.
- [x] Theme settings, high contrast, reduced motion, autostart, tray preferences and update/release-note actions retained.
- [x] Keyboard activation, focus rings, menu Escape dismissal and 36px primary control targets.

## Updated source files

The accompanying archive contains the complete current contents of every file changed for this refactor, including existing in-progress work in those files. It is a source delivery, not a standalone application checkout.

| File | Change |
| --- | --- |
| UI/Theme.xaml | Shared type/space/radius/elevation/focus tokens, control sizing and preview overlay style |
| UI/UiAppearance.cs | Ember palette values only; theme selection logic unchanged |
| UI/MainWindow.xaml | Profile layout, short labels, floating preview controls, sidebar hooks |
| UI/MainWindow.Navigation.cs | Presentation-only sidebar adaptation |
| UI/MainWindow.Profiles.cs | Short status copy, larger aspect-correct preview, shell layout refresh |
| UI/MainWindow.Gallery.cs | Compact profile rail rows and full status tooltips |
| UI/DesktopCanvasView.cs | Short counter/caption copy and unanimated picker |
| UI/DesktopCanvasView.Studio.cs | Compact layout toolbar and contextual information tooltip |
| UI/DesktopCanvasView.Workspace.cs | Overflow, compact rows, meaningful glyphs, preview hero and responsive stacking |
| WallpaperProfiles.Tests/LibraryWindowTests.Studio.cs | Overflow reachability, click-target and compact geometry assertions; verifies layout/menu interaction does not save data |
| WallpaperProfiles.Tests/LibraryWindowTests.Widgets.cs | Verify the visible hero preview in compact mode, where the duplicate editor thumbnail is hidden |
| WallpaperProfiles.Tests/LibraryWindowTests.Snapshots.cs | Render the compact library details case at 640px after the sidebar gains an icon mode |

No dependencies, application services, model files, persistence, APIs, routes or shortcut definitions were changed by this refactor.

## Verification

- Release build succeeds with zero compiler errors. NuGet vulnerability metadata could not be fetched on this restricted host (NU1900).
- Existing regression suite: 121 passed, 1 failed, 122 total.
- The failure is the pre-existing WindowsMediaSession_CanBeReadWithoutChangingPlayback integration test: Windows cannot share the current playback session in this environment. The same failure occurred in the baseline run before these edits.
- The WPF UI integration test passes through navigation, profile sources and ordering, save/apply/draft handling, shortcuts, widgets, styles and layouts, themes and update dialogs.
- Added assertions confirm the overflow retains its actions, targets are at least 36px tall, compact preview/list geometry does not overlap, and resizing/opening the menu does not save layout data.
- Rendered wide and compact layouts, four profile tabs, widget editor tabs, both dark and light themes, and portrait/ultrawide previews at multiple DPI scales. Existing text-contrast assertions pass at 4.5:1 or better for tested text brushes against the main surfaces in Ember and Daylight.
- Existing per-profile scene accents can override the theme accent; this behavior is retained. Theme selection is not forced or migrated.
- Checklist ticks above mean the feature remains connected to its original implementation; external services and live desktop media access are limited by the host environment.


## Lightweight UI follow-up

The interactive shell now uses WPF's default hardware-capable rendering path. The separate background widget process keeps its existing software setting. This removes the earlier blanket CPU-only setting from the main window without changing wallpaper playback or widget data.

The desktop composition retains each widget renderer and visual tree while editing. Changes update the edited widget; switching compositions reconciles additions/removals, and unloading the view releases retained renderers. A regression check exercises 101 position changes, verifies that the widget container/content are the same objects, and confirms the saved canvas file is unchanged. This is an interaction check, not a frame-rate benchmark.

Page slide/fade and moving card effects are removed. The sidebar is a fixed flat rail, settings use separator rows, tabs use a plain baseline, preview shadows are removed, and the studio controls share a compact row when space allows. Repeated size events no longer reapply the same shell layout. All themes, shortcuts, draft safeguards and actions remain available; Customize wallpaper and Use profile default are in the studio overflow.

The full regression run passed 121 of 122 tests; the remaining Windows media-session failure also occurred before this pass. The final focused UI flow additionally checks the interactive rendering policy and background-host fallback.


## Tray menu follow-up

The existing native tray menu now reads the same WPF surface, text, border, hover and accent resources as the app. Its renderer uses Segoe UI, spaced rows and accent checkmarks; theme changes update the menu as well as the icon. Native keyboard navigation, profile switching, pause state, opening and exit handlers remain unchanged. Windows high contrast uses the system renderer. The UI flow verifies palette matching and row sizing and renders the menu for all 13 themes.
