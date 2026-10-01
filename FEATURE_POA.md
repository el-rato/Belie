# Belie — feature plan of action

Turn profiles into complete desktop environments, with a library for discovery and an activity view for control. Keep existing profiles, schedules, triggers, tray controls, and live playback working throughout.

## Delivery order

| Phase | Feature | Scope | Completion check |
| --- | --- | --- | --- |
| 1 | Wallpaper library | Image/video thumbnails, file and folder imports, search, favorites, tags, named collections, and choosing media for a profile. Store metadata separately from profiles. | Import, organize, reopen, and choose a wallpaper; duplicates are ignored and missing files remain recoverable. |
| 2 | Activity dashboard | Active profile, activation reason, upcoming schedule change, recent switches, and timed manual overrides. | Displayed state matches the resolver; an override expires and automation resumes predictably. |
| 3 | Desktop scenes | Extend profiles with an optional accent color and ambient audio track, volume, and mute. Provide Focus, Gaming, and Evening starter scenes. | Scene switches apply all enabled settings together; stopping or disabling a scene restores settings it owns. Existing profiles retain their behavior. |
| 4 | Per-monitor setups | Visual monitor picker, individual media and fit settings, spanning option, stable monitor identity, and disconnected-monitor fallback. | Two displays can show different media; reconnecting, resolution changes, and mixed DPI retain assignments. |
| 5 | Appearance controls | Brightness, tint, blur, and crop with preview and reset. Start with static images; extend to live rendering afterward. Preserve original media. | Applied wallpaper matches preview; reset returns the original appearance; old profiles use neutral settings. |
| 6 | Day-to-night transitions | Pair matching day/night assets, set transition windows, and gradually blend lighting or media. Use explicit local times first. | Transitions respect schedules, manual overrides, and sleep/resume without flickering or excessive resource use. |
| 7 | Interactive live wallpapers | Add an optional scene renderer for mouse parallax, cursor response, and audio-reactive effects. Expose intensity and motion controls. | Effects work on supported scenes, pause for fullscreen apps and battery-saving mode, and leave ordinary video playback working. |
| 8 | Shareable scene packs | Versioned pack format containing scene settings, collections, and selected media. Import preview, duplicate handling, asset size limits, and safe extraction. | A pack reproduces a scene on another machine; missing optional media is reported; unsafe archive paths are rejected. |

## Implementation boundaries

- Reuse the current WPF theme, preview loader, profile editor, persistence conventions, coordinator, and resolver.
- Keep media-library metadata in `library.json`; profiles continue to reference media paths.
- Add backward-compatible profile fields only when their feature ships.
- Apply scene effects through the coordinator so manual, scheduled, and triggered switches behave alike.
- Establish per-monitor rendering before adding effects that must render separately on each display.
- Keep scene packs versioned so later appearance and interaction settings can be shared without breaking older imports.
- Verify persistence and resolver behavior with automated tests; verify display, playback, and effects on Windows.

## Phase 1 checklist

- [x] Persistent library metadata and duplicate-safe imports.
- [x] Thumbnail gallery with search, favorites, and collection filters.
- [x] Edit names, tags, and collection membership.
- [x] Choose library media for an existing or new profile.
- [x] Missing-file handling, bounded thumbnail loading, and persistence tests.
- [x] Build and run automated checks (25 passing tests, including the WPF gallery flow).
- [ ] Manual Windows gallery and profile-selection check.

Phase 1 is implemented. The rendered gallery layout was inspected, and the automated WPF check covers paging, tag search, collection and favorite filters, editing and saving metadata, missing-file selection, thumbnail loading, and choosing media. A manual check in the running app remains. Phase 2 is next.

## Later acceptance scenarios

- Open existing data after upgrading without losing or rewriting profile rules.
- Switch scenes rapidly while a slideshow or live wallpaper is running.
- Unplug a media drive or a second monitor while its scene is active.
- Sleep through a schedule or transition boundary and resume into the correct scene.
- Fail a save without reporting success or losing previously saved data.
- Import a shared pack without executing content or writing outside its destination.
