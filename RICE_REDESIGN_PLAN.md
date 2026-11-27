# Belie redesign plan (revised)

Branch: `redesign` (from `main` after merging `ui-polish`). Test with the dev build: `.\scripts\Run-Dev.ps1`.

Goal: **beautiful and streamlined.** It should look like a riced Linux desktop, but someone who has never heard of Hyprland can use it without learning anything. Every existing feature, setting, shortcut and data flow keeps working.

## Decisions

| # | Item | Decision |
|---|---|---|
| 1 | In-app workspaces (1–9, tiling, swap, float) | **Dropped.** Alt+1–9 stays with desktop workspaces. |
| 2 | Launcher on Ctrl+Space (+ Ctrl+K) | **Go.** |
| 3 | Alt+Q hide pane | **Dropped.** |
| 4 | Blur behind panes | **Go**, in WPF (see "Staying on WPF" below). |
| 5 | Bundled Inter + JetBrainsMono Nerd Font | **Not decided yet.** Until then, use Segoe UI Variable + Cascadia Mono, which ship with Windows 11. |
| 6 | Catppuccin Mocha / Tokyo Night / Gruvbox presets | **On hold.** The 13 existing themes stay as they are. |
| 7 | `shell.json` | **Go.** It holds the launcher keys and look preferences (glass strength, reduce motion), not layouts. |

## Staying on WPF

The blur limit is narrower than it sounded:

- **What WPF can do:** Windows 11 Acrylic or Mica on the whole window, through `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`. The thing behind Belie's window is your desktop, so the window shows **your real wallpaper, blurred**, live. That is exactly the riced look.
- **What WPF can't do:** live blur of *other app content* that sits under a pane, like CSS `backdrop-filter`. We don't need it, because the panes sit on the acrylic, not on other content. Where a pane sits on an image (for example the preview), use a pre-blurred copy of that image aligned under the pane. It looks the same and costs nothing per frame.
- **Porting would cost a rewrite and gain little:**
  - **WinUI 3** has in-app acrylic and the same C#/XAML, but every screen (about 15k lines of UI) would need rewriting. The desktop widgets, top bar and wallpaper hosts depend on WPF.
  - **Tauri/Electron** add a second language and runtime, plus a bridge to all the native code.
  - **Avalonia** only blurs the window backdrop, the same as WPF.
- **Recommendation:** stay on WPF. Step 1 below is a small proof (acrylic window + glass panes) that you test in the dev build before anything else gets built on it. If it disappoints, we revisit porting with evidence.

## The new shell

```
╭─────────────────────────────────────────────────────────────────────────────╮  acrylic window: your wallpaper, blurred
│  ◆ belie   [ 󰕮 Home ][ 󰸉 Profiles ][ 󱂬 Widgets ][ 󰋩 Library ][ ⋯ More ]   ⌕ Ctrl+Space   ● Auto  ⚙  ─ □ × │  one bar: nav pills, search, status
│                                                                             │
│  ╭━━━━━━━━━━━━━━━━━━━━━━━━╮  ╭───────────────────────────────────────────╮   │  12px outer gap, 6px between panes
│  ┃ pane with focus        ┃  │ pane                                       │   │  glass panes, 12px corners
│  ┃ (accent gradient edge) ┃  │ (thin dim edge)                            │   │
│  ╰━━━━━━━━━━━━━━━━━━━━━━━━╯  ╰───────────────────────────────────────────╯   │
│                                                          ┌ Saved ✓ ──┐     │  toasts slide in top-right
╰─────────────────────────────────────────────────────────────────────────────╯
```

- **One bar, no sidebar.** The title bar and navigation merge into one Waybar-style row of pills. The four everyday sections are always visible. **More** opens Top bar, Workspaces, Transitions, Team and Switcher as a small menu. The ⌕ pill opens the launcher. The ● pill shows the automation state (Auto, Paused, Override 0:42) and clicking it pauses or resumes. ⚙ opens settings.
- **Fixed layouts.** Each section has one sensible 1–3 pane arrangement that adapts to the window width. There is nothing to split, swap or learn.
- **Focus glow.** The pane that holds keyboard focus gets the 2px accent gradient edge. It's a nice detail that also shows where you are.
- **Plain words.** Labels use normal sentence case, and the short hints stay. Empty states show one clear button plus a muted hint such as `or press Ctrl+N`.

### Screens

```
Home        ╭━ Now on your desktop ━━━━━━━━━━━━╮ ╭─ About ─────────────────╮
            ┃ [wallpaper]  Dusk · applied 2h   ┃ │  ▄▀▀▄  belie 4.2.3      │  fetch-style card:
            ┃ [Edit] [Mute ambient]            ┃ │  █◉ █  6 profiles       │  version, counts,
            ╰━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╯ │   ▀▄▄▀ 9 widgets · 2 mon │  theme colour dots
            ╭─ Take over for a while ──────────╮ ╰─────────────────────────╯
            │ [Focus ▾] for [1 hour ▾] [Apply] │
            ╰──────────────────────────────────╯

Profiles    ╭─ Profiles ──╮ ╭━ Dusk ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╮
            │ ⌕ Search    │ ┃ ‹ [ big wallpaper preview ] ›          [Save] [Apply]  ┃
            │ ▌▣ Dusk  ✓  │ ┃ Wallpaper · Playback · Automation · Mood               ┃
            │  ▣ Focus    │ ┃ (current tab content)                                   ┃
            │ [+ New]     │ ╰━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╯
            ╰─────────────╯

Widgets     ╭─ Widgets ───╮ ╭━ Desktop preview ━━━━━━━━━━━━━━━━━━━╮ ╭─ Clock ──────────╮
            │ ▌󰥔 Clock  ◉ │ ┃ drag widgets where you want them    ┃ │ settings for the │
            │  󰎆 Music  ◉ │ ┃                                     ┃ │ selected widget  │
            │ [+ Add]     │ ╰━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╯ ╰──────────────────╯

Library     ╭━ My collection · Discover ━━━━━━━━━━━━━━━━━━━━━━━━━━╮ ╭─ Details ─────────╮
            ┃ ⌕ search  sort ▾  ★   [+ Import]                   ┃ │ preview, name,    │
            ┃ ▣ ▣ ▣ ▣ ▣ ▣ (thumbnail grid)                       ┃ │ tags, Use, Remove │
            ╰━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╯ ╰───────────────────╯

More ▸ Top bar      live bar preview across the top, then Looks | Parts | Options panes
More ▸ Workspaces   one pane: workspace list + behaviour toggles
More ▸ Transitions  preview pane + options pane
More ▸ Team         account pane + creature preview pane
More ▸ Switcher     hotkeys pane + switcher preview pane
Settings            floating glass card over the window (theme, startup, updates, glass strength, reduce motion)
```

### Launcher (Ctrl+Space or Ctrl+K)

```
╭──────────────────────────────────────────╮
│ ⌕ dusk_                                  │
│ ▌󰸉 Apply profile · Dusk           Enter  │
│  󰸉 Edit profile · Dusk                   │
│  󱂬 Widgets for Dusk                      │
│  ⚙ Theme · Dusk-coloured…                │
╰──────────────────────────────────────────╯
```

- It searches sections, actions, settings, profiles, widgets and library items, with fuzzy matching.
- Every entry runs the existing handler for that action, so the result is the same as clicking.
- The first time the app opens, the bar shows one hint: "Press Ctrl+Space to find anything". It disappears after you use the launcher once.

### Keys

| Keys | Action |
|---|---|
| Ctrl+Space / Ctrl+K | Launcher |
| Ctrl+S | Save the profile or widget you're editing |
| Ctrl+Enter | Apply profile to desktop |
| Ctrl+N | New profile |
| Esc | Close launcher, menu or dialog |

The keys can be changed in `shell.json`. Existing global keys don't change: Ctrl+Alt+W, Ctrl+Alt+PgUp/PgDn, desktop-workspace keys and profile shortcuts.

## Look

- **Glass:** acrylic window plus panes tinted with the theme surface at 80% (an adjustable "glass strength" setting: Off / Light / Strong). Off, Windows 10, or Windows transparency effects turned off all fall back to the current solid surfaces.
- **Edges:** 12px pane corners and 7px control corners. The focused pane gets a 2px gradient from the accent to a lighter, shifted accent. Other panes get a 1px dim edge. A soft shadow appears only on floating things: the launcher, menus, toasts and settings.
- **Mono accents:** numbers, times, keys and status pills use the mono font.
- **Motion:**
  - Panes pop in (scale 0.96→1 plus fade, 250 ms) on the curve `cubic-bezier(0.05, 0.9, 0.1, 1.05)`.
  - Switching sections cross-slides the content (200 ms).
  - Toasts slide in from the top right.
  - Windows' "Animation effects" setting and a "Reduce motion" toggle turn all of this off.
- **Scrollbars:** thin, accent-coloured, faint until hovered (already done in `ui-polish`).

## Build order (each step: build, all tests, dev build screenshot, click through every action on that screen)

1. **Glass proof.** Acrylic window, glass panes, focus glow, glass-strength setting, `shell.json`. You check it in the dev build before going further.
2. **Shell bar.** Nav pills plus More, status pill, settings card, toasts. The old sidebar stays behind `BELIE_CLASSIC_UI=1` until the end.
3. **Launcher.** Command registry and Ctrl+Space.
4. **Screens:** Profiles → Widgets → Home → Library → the five "More" screens → dialogs (profile rules/mood editor, release notes, confirmations as floating glass cards that keep their modal behaviour).
5. **Clean up.** Remove the classic shell; update tests, README and the user guide.

## Risks

- **Memory:** the "unload in tray" test must keep passing. No held animations; the acrylic and blurred images are released on unload.
- **Readability on busy wallpapers:** the minimum tint keeps text at 4.5:1 contrast. The proof in step 1 is where to judge this.
