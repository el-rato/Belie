# Belie UI audit and implementation handoff

## Objective

Make Belie feel like a coherent, polished Windows application: clear navigation, predictable editing, accurate previews, readable controls, and consistent visual hierarchy. Preserve the wallpaper, slideshow, scheduling, trigger, ambient audio, shortcut, library, tray, and widget features.

The largest weakness is inconsistent interaction design. Changing the palette alone will leave the duplicated editors, ambiguous states, and awkward content hierarchy intact.

## Evidence and boundaries

- Visually inspected the running app's **Profiles**, **Edit Profile → Wallpaper**, and **Widgets overview** screens in the Ember theme, approximately 1180 × 820 for the main window and 680 × 720 for the editor.
- Inspected the corresponding WPF source plus Activity, Library, settings, theme resources, and widget editor construction. Read only relevant files; no repository-wide scan.
- The running window belonged to `release-output/ui-shortcuts-refresh/Belie.exe`. The working tree contains existing modifications. Treat screenshots as evidence of that build and source findings as evidence of the current checkout; verify their agreement before implementing.
- Activity, Library, widget editing, other themes, and narrow-window behavior have **not** been visually verified. Findings about those surfaces are marked **source** or **risk** below.
- This handoff is the only file added by the audit. No app source was changed. Save/apply/delete actions were not exercised.

## Design direction

A calm Windows utility with wallpaper imagery as the main visual material. Keep Segoe, the existing theme choices, and the WPF stack. Use Fluent principles through existing resources and controls; a framework migration is outside scope.

Create shared values in `UI/Theme.xaml` and use them selectively in touched screens:

| Element | Proposed target |
|---|---|
| Page heading | 28 DIP, semibold |
| Section heading | 18–20 DIP, semibold |
| Body / control text | 14 DIP |
| Supporting text | 12 DIP; essential labels must not be 10–11 DIP |
| Spacing | 4, 8, 12, 16, 24, 32 DIP |
| Page inset | 24 DIP; 16 DIP in compact layouts |
| Normal control height | 36 DIP minimum; compact controls 32 DIP |
| Corners | 8 DIP controls; 12 DIP cards; 16 DIP large preview |
| Surfaces | Base workspace, one grouping surface, raised overlays |
| Accent | Primary command and selected state; status retains semantic colors |
| Focus | Visible outline independent of hover and selection |

These are proposed Belie design values, not mandatory Microsoft measurements. Wallpaper previews should retain their natural brightness. Borders and backgrounds should identify groups without surrounding every setting in its own card. Use plain, descriptive copy.

## Implementation sequence

### 1. Make profile editing predictable — P1

**F01 — Two competing editors. Visually confirmed.** The main workspace edits name, source, image fit, and playback; **Edit profile** opens a second editor repeating those settings. Main tabs are Wallpaper / Playback / Automation / Mood; dialog tabs are Wallpaper / Mood / Rules. The user must learn two arrangements for the same object.

**Fix:** Keep inline editing as the primary workflow. Make the existing dialog use the same four tabs and field order for creation and advanced editing. Remove the generic **Edit profile** header action once all settings have a clear inline destination. Keep targeted **Edit schedules**, **Edit triggers**, and **Edit mood** actions where they open the relevant dialog tab. Reuse existing handlers and view models; preserve every field and its validation. Start with aligned structure and labels rather than a large component extraction.

**Done:** Creating and editing a profile exposes the same terminology and grouping. Every existing setting has one discoverable destination in the main workspace. Opening advanced editing preserves pending inline changes.

**F02 — Unsaved changes are invisible and vulnerable to profile switching. Source.** `MainWindow.Select` overwrites the fields with the next profile's values; there is no dirty-state guard in that path. `Save_Click` refreshes the rail and selection without explicit success feedback. **Apply to desktop** also saves, but that behavior is only disclosed by its tooltip.

**Fix:** Track the UI draft against the selected profile. Show **Unsaved changes**, enable Save only when appropriate, and guard switching profiles or closing the editor with **Save / Discard / Cancel**. Keep existing save-then-apply behavior and make it explicit when the draft is dirty. Add unobtrusive **Changes saved** and **Applied to desktop** feedback. Use a shared inline message area for validation; focus the invalid field and switch to its tab. Retain error dialogs for failures that cannot be resolved inline.

**Done:** Editing profile A then selecting B never silently loses changes. Cancel leaves A and its draft visible. A failed save never reports success or applies the unsaved profile.

**F03 — Primary actions leave the viewport. Visually confirmed / source.** Profile name, Save, and Apply are inside the outer scrolling panel. Large previews and lists push essential controls away while editing lower sections.

**Fix:** Move the profile header and command area into a fixed row above the scrolling details. Keep **Apply to desktop** primary and Save secondary. Put Duplicate / Delete in a clearly named **More** menu beside the header; retain existing delete confirmation. Keep dialog Save / Cancel in their existing fixed footer.

**Done:** Save and Apply remain reachable at the minimum supported window height and on every tab. The preview can scroll without moving the command row.

**Files:** `UI/MainWindow.xaml`, `UI/MainWindow.xaml.cs`, `UI/ProfileEditorWindow.xaml`, `UI/ProfileEditorWindow.xaml.cs`. Reuse `UI/EditorViewModels.cs` where applicable. Adapt tab selection currently mapped by numeric indices in `EditRules_Click`.

### 2. Correct preview and source presentation — P1

**F04 — Preview composition changes between surfaces. Visually confirmed.** The same wallpaper is contained with broad side gutters in the main workspace but tightly cropped in the dialog. This makes the dialog's large image look like a banner rather than an accurate desktop preview.

**Fix:** Distinguish a browsing thumbnail from a desktop-fit preview. Give main and dialog previews the same desktop-aspect-ratio viewport and render the selected Fit mode inside it. Use a compact contained preview during editing, with the existing expand action for inspection. Limit preview height when vertical space is scarce. Keep slideshow Previous / Next / position controls inside a consistently sized toolbar; disable navigation when unavailable.

**Done:** Fill, Fit, and Stretch previews correspond to their actual wallpaper behavior. Wide, portrait, and ultrawide images retain correct geometry in both editors. Previewing a file never applies it implicitly.

**F05 — Filesystem presentation dominates wallpaper management. Visually confirmed.** The profile shows a full local path and an “additional wallpaper(s)” list of generated filenames. Folder / Image / Video / Library commands are small, loosely grouped text actions. The editor squeezes a path field and three browsing buttons into one row.

**Fix:** Present **Wallpaper source** with a readable filename or folder name, a clear source type, and **Change source**. Keep the full path accessible through a secondary field or tooltip for users who need it. Present all profile wallpapers, including the primary one, as a consistent compact list with thumbnail, name, media type, and removal action. Label counts correctly: **1 wallpaper / 3 wallpapers**. Group Add from library / Choose file / Choose folder in one coherent source-command area. Preserve drag and drop and editable paths.

**Done:** A user can identify the source and add/remove wallpapers without interpreting filenames or paths. Removing an additional item does not remove the source file. Long paths remain inspectable without compressing browsing controls.

**Files:** Main and profile editor XAML/code, existing `UI/MonitorThumbnail.xaml` and `.xaml.cs`. Reuse `ThumbnailLoader`; avoid changes to wallpaper rendering engines unless an actual preview defect requires them.

### 3. Normalize the shell and visual hierarchy — P2

**F06 — Global navigation mixes destinations and profile context. Visually confirmed.** Profile search, profile thumbnails, and **New profile** remain in the sidebar on Widgets, alongside a second widget gallery and its own filter. The selected profile looks important, but its relationship to the widget preview is not obvious.

**Fix:** Keep one shell with global navigation. Show profile-specific search/list actions in Profiles; in Widgets expose the profile scope explicitly in the workspace filter, including global widgets. If a compact profile switcher is needed across destinations, label it **Profile context**. Distinguish **selected profile** from **currently applied profile** using visible text/status, not two similar accent outlines. Label the existing rules dot or replace it with a readable automation indicator.

**Done:** On each destination the sidebar's controls have an obvious purpose. Selecting a profile and applying a profile are visibly different actions and states.

**F07 — Scale, shapes, and icons vary without a clear system. Visually confirmed / source.** Headings range from 21 to 38 DIP, utility actions use 10–11 DIP text, corners range from 5 to 20 DIP, and icons mix Segoe glyphs with `⧉`, `⌖`, `×`, and arrows. Main close/minimize use paths while dialog close uses font glyphs.

**Fix:** Apply the shared type/spacing/geometry values above. Reuse the existing Windows icon font for a consistent icon family. Standardize icon size and alignment. Give icon-only controls a meaningful automation name and tooltip. Remove global button scale-on-hover; use quiet color feedback and retain reduced-animation support. Keep primary and secondary commands visually distinct.

**Done:** Same-purpose controls match across screens. Content labels no longer look like microcopy. Hover does not make toolbar buttons swell or collide.

**F08 — Activity behaves like a promotional page. Source.** “Set the scene,” “Your scenes,” “Quick moods,” “Play for a while,” and large 38 DIP titles coexist with actual automation status and recent activity. Profiles are called scenes here and profiles elsewhere; applying a still image is called Play.

**Fix:** Use **Overview** for the destination if it continues to contain live status and commands. Put active wallpaper, activation reason, automation state, and next scheduled change first. Follow with profile switching, the existing temporary override, and recent activity. Label commands **Apply**, **Temporary override**, and **Resume automation**. Move mood presets into profile creation. Keep ambient audio controls visible only where meaningful. Treat “Now playing” as a media-specific label, not a universal profile state.

**Done:** The initial viewport answers “What is applied?”, “Why?”, and “What happens next?” with actual app data. A no-active-profile state cannot resemble successful playback.

**F09 — Settings behaves like a small floating theme chooser. Source.** A transient popup combines appearance and startup options while other destinations are full workspaces.

**Fix:** Reuse the existing settings controls in a stable, clearly titled surface. A modest owned dialog is sufficient. Group **Appearance** and **Startup**; show named theme choices with readable previews and selected state. Preserve immediate theme updates and persisted startup behavior.

**Done:** Settings stays usable at the minimum window size and with keyboard navigation. Selecting a theme provides an unambiguous selected state.

**Files:** `UI/Theme.xaml`, `UI/MainWindow.xaml`, `.xaml.cs`, `UI/UiAppearance.cs`; touch widget construction only for shell-related alignment.

### 4. Finish Library and Widgets as product workspaces — P2

**F10 — Library carries dialog chrome into an embedded destination. Source.** `CreateInlineContent` retains a 46 DIP header row with **← Profiles** above another Library heading. Cards have fixed 194 DIP widths, and selecting a local item reserves a fixed 260 DIP details column. This risks a narrow gallery and wasted space inside the 880 DIP main window.

**Fix:** Hide the dialog title row in embedded mode and keep it in standalone mode. Use one Library heading and a stable collection/discovery tab treatment. Let the gallery choose columns from available width. At compact width, open details as a replacement view with **Back to collection**, rather than squeezing both columns. Clearly distinguish card inspection from checkbox selection and keep the selected count and target profile visible beside **Add to [profile]**. Preserve multi-selection, pagination, import, filtering, discovery, and download cancellation.

**Done:** Both embedded and standalone Library modes remain usable. No card or details field clips at the supported minimum width. Users can identify which profile will receive selected wallpapers before committing.

**F11 — Widget overview hides scope and command meaning. Visually confirmed.** With **All widgets** selected, the gallery showed a widget assigned to Dark while the preview said **No widgets for Chill**. The toolbar showed **0 visible · 1 widgets**, **Hide**, and an unlabeled crosshair. Gallery and preview refer to different scopes without making that distinction explicit.

**Fix:** Label the gallery scope and preview scope independently if they differ: **All widgets** and **Preview: Chill**. State why a widget is not visible in that preview. Use correct plurals and explicit commands **Show widgets**, **Hide widgets**, **Find widgets**. Choose one primary Add widget action; keep quick types as a secondary menu or compact shortcuts with consistent sizing. In the editor, use a trash icon or **Delete widget** rather than `×`, which also means close editor. Retain Content / Style / Layout organization and pinned Save.

**Done:** A widget's profile assignment, enabled state, and actual visibility are distinguishable. Closing its editor and deleting it cannot be mistaken for one another. Scope changes preserve pending edits using the same draft rules as profile editing.

**Files:** `UI/LibraryWindow.xaml`, `.xaml.cs`, `UI/LibraryWindow.Marketplace.cs`, `UI/DesktopCanvasView.cs`.

### 5. Complete interaction states and adaptability — P1 for access; P2 for polish

**F12 — Custom controls have uneven focus and theme behavior. Source / risk.** `SwitchStyle` has no explicit focused trigger; Slider indicates focus by reducing opacity; DangerButton hardcodes dark hover colors. Some composite navigation buttons appear unnamed at the parent level in the observed UI Automation tree. High-contrast behavior has not been verified.

**Fix:** Give every interactive template a conspicuous focus indicator. Name navigation destinations and associate field labels with their controls. Use theme resources for destructive hover/pressed states and provide an intentional Windows high-contrast treatment. Check primary action text against arbitrary scene accents; adjust its foreground if needed. Keep status explanations in text alongside color.

**Done:** Every essential workflow works using Tab / Shift+Tab / Enter / Space / arrow keys. Narrator reads destination, field name, and state without raw icon glyphs. Focus remains visible in each theme and high contrast.

**F13 — Fixed dimensions and horizontal command groups risk clipping. Source / risk.** The main window supports 880 × 650, while the editor supports 620 × 600. Auto-width header buttons, fixed gallery/details widths, and multi-column input rows compete for this limited space. Actual narrow/DPI clipping remains unverified.

**Fix:** Reflow based on available window width in WPF DIPs. Stack secondary actions and source controls when necessary; keep essential commands accessible. Adapt the preview height and card columns. Use vertical scrolling for long forms while retaining fixed command rows. Preserve current window resizing, maximize/work-area behavior, and close-to-tray behavior.

**Done:** No overlapping text, unreachable command, or horizontal form scrolling at existing minimum sizes, standard size, and maximized size. Validate display scaling at 100%, 150%, and 200% without changing the user's OS settings during automated inspection.

**F14 — Loading, empty, missing, and error states need a shared presentation. Source / risk.** Library has “Preview unavailable” fallbacks and several status fields, but their treatment differs from profile validation and widget messages. A fallback thumbnail can be shown while loading and look like a failure.

**Fix:** Separate **Loading preview**, **Preview unavailable**, and **Source missing**. Give empty collection and zero-search-result states different messages and appropriate actions. Use one inline status treatment with severity, explanation, and useful recovery action. Preserve cancellation and the existing missing-file handling.

**Done:** Each state identifies what happened and what the user can do. Normal loading cannot be mistaken for a missing source or a successful download.

## Validation and completion

Implement in the sequence above with the smallest coherent file changes. Preserve existing working-tree modifications. Limit investigation to the affected UI and its direct dependencies. Avoid new feature work, engine rewrites, or a framework migration.

Before declaring completion:

1. Build `WallpaperProfiles.csproj` and run the existing `WallpaperProfiles.Tests` suite. Add focused behavioral tests only for new draft guards, validation routing, or selection behavior; avoid tests that merely restate style values.
2. Capture before/after views of Overview, Profiles and every tab, creation/editing, Library collection/discovery/details, Widgets overview/editor tabs, and Settings. Include a dark theme and Daylight.
3. Check minimum/default/maximized sizes and supported scaling; exercise long names, long paths, portrait/ultrawide media, empty data, many profiles, missing sources, and unavailable thumbnails.
4. Verify creation, rename, add/remove wallpaper, Save, apply, cancel/discard, shortcuts, schedules, triggers, ambient audio, local library multi-selection, discovery/download/cancel, and widget assignment/visibility/editing. Verify on fixtures where changing wallpaper or adding data would affect the user's real setup.
5. Verify keyboard-only operation and automation names. Measure text contrast; use at least 4.5:1 for normal text and 3:1 for large text. Ensure focus remains recognizable beside selected state.
6. Confirm tray persistence, startup preferences, and window behavior still work. Report the exact surfaces checked and any remaining unverified risk.

The result is complete when F01–F14 meet their local criteria and the checks above pass. Cosmetic similarity alone is insufficient.

## Reference guidance

The proposed values and screen arrangements are design judgments for Belie. These primary sources support the underlying consistency, layout, and accessibility principles; their WinUI examples should be adapted to the existing WPF implementation:

- [Microsoft Windows design guidelines](https://learn.microsoft.com/en-us/windows/apps/design/guidelines-overview)
- [Microsoft content layout and spacing](https://learn.microsoft.com/en-us/windows/apps/design/style/spacing)
- [Microsoft accessible text requirements](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements)

## Prompt for the implementing agent

Implement the Belie UI improvements in `UI_REDESIGN_POA.md`, following its ordered work packages and completion criteria. First verify the current build against the audit's limited screenshot coverage. Preserve existing modifications and all product functionality. Reuse existing WPF styles, controls, handlers, and view models, and keep changes restricted to the required UI and directly related state handling. Complete the visual, keyboard, behavior, and build checks, then report changes and any remaining limitations. Stop after the requested UI work is complete.
