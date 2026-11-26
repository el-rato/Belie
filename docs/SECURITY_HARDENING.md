# Security hardening after the 4.0.2 audit

The red-team audit of Belie 4.0.2 found seven issues. #1 and #2 (a hidden `belie:` command in a
shared look running through `cmd`) were fixed in 4.0.3. This note covers the rest.

| # | Finding | Status |
|---|---------|--------|
| 3 | Update trust rests only on the GitHub repo | Recommendation below; no code change |
| 4 | `Local\Belie.*` events and mutexes use default ACLs | Fixed: `Infrastructure/LocalIpc.cs` |
| 5 | Wallhaven key: DPAPI `CurrentUser` with public entropy | Accepted; see below |
| 6 | Wallpaper Engine arguments built by quote concatenation | Fixed: `WallpaperEngineWorkshop.OpenWallpaperStartInfo` |
| 7 | Another process can hold the save mutex and block every save | Fixed: `AtomicJsonFile.WithLock` |

Tests: `WallpaperProfiles.Tests/SecurityHardeningTests.cs`.

## #4 Named IPC objects

Every named event and mutex Belie creates (`Local\Belie.Exit`, `.Activate`, `.Canvas.Open`, the top
bar's `.Stop`/`.Changed`/`.Command`/`.Closed`, the wallpaper/profile/settings events, the single-instance,
canvas, top bar and desktop-host mutexes, the live host's stop event and the JSON save locks) now goes
through `LocalIpc`, which sets a protected DACL with one entry: full control for the current user's SID.
The token default also lets in the logon-session SID and SYSTEM.

What this does not do: another process running as the same user can still open and signal these
objects, because that process already has the user's rights (it could edit `%APPDATA%\Belie` directly).
The sidecar files read after a signal (`*.command.json`, `*.profile.json`) are still checked against
known workspaces and profiles, as before.

## #5 Wallhaven key entropy — not changed

DPAPI `CurrentUser` already stops other Windows accounts from reading `wallhaven-key.bin`. Any secret
that Belie can decrypt without asking the person can also be decrypted by other code running as that
person, whatever entropy is used: a random per-install entropy would have to be stored where Belie (and
so that code) can read it. Real protection would need a passphrase typed at every start, which is out
of proportion for a free, read-only image-search key. The risk is accepted.

## #6 Wallpaper Engine arguments

The command line is built with `ProcessStartInfo.ArgumentList` (`-control`, `openWallpaper`, `-file`,
`<path>`), so .NET quotes and escapes the path. `TryOpenViaWallpaperEngine` also refuses paths with
`"`, NUL or line breaks; Windows file names cannot contain them anyway. (The method currently has no
callers, but it stays safe if one is added.)

## #7 Save lock

`AtomicJsonFile` used to wait 5 s for `Local\Belie.Json.<hash>` and then throw "The file is busy". Now
it waits up to 2 s, logs a warning and saves without the lock. If the mutex can't be opened at all
(for example, someone created the name first with a DACL that blocks Belie), it also saves without the
lock. This is safe because the lock only smooths contention: every save writes a complete temp file
and publishes it with an atomic rename, retrying on sharing violations.

`DesktopCanvasStore.WithWriteLock` has a second lock (`...Store`) that guards a read-modify-write, so
it can't safely fall back to running without the lock. It now has the owner-only DACL. A same-user
process could still hold it and make canvas edits fail with "The canvas is busy"; that is a nuisance,
not a privilege issue, and it is left as is.

## #3 Recommendation: update trust

Today `VelopackUpdateClient` uses `GithubSource("https://github.com/el-rato/Belie")`. Velopack checks
each package's SHA against the `releases.*.json` feed, but the feed comes from the same GitHub release,
so it only catches corruption, not a malicious release. Anyone who can publish a release (a stolen
maintainer token, a compromised Actions workflow, a leaked `VPK_TOKEN`) can push code to every client
that updates. TLS covers network attackers. Velopack does not allow downgrades by default.

In order of value for the effort:

1. **Authenticode-sign the release.** Pass `--signParams` (signtool) or `--azureTrustedSignFile`
   (Azure Trusted Signing) to `vpk pack` in `.github/workflows/release.yml`, so `Belie.exe`,
   `Update.exe` and `Belie-win-Setup.exe` are signed. This also removes the SmartScreen warnings.
   Keep the signing credential in a protected GitHub Environment that needs manual approval, apart
   from the `contents: write` token that uploads releases.
2. **Check the publisher before applying an update.** After `DownloadUpdatesAsync`, and before
   `WaitExitThenApplyUpdates`, open the downloaded `.nupkg` (a zip) from Velopack's packages folder.
   Run `WinVerifyTrust` on the `Belie.exe` inside it, and compare the signer's certificate (subject
   plus public key or thumbprint) with values compiled into the running build. If they don't match,
   refuse the update. Then a GitHub-only compromise can no longer ship code: the attacker also needs
   the signing key. Velopack doesn't do this on its own, so it would live in `AppUpdater` behind
   `IAppUpdateClient` and could be unit-tested with a fake client.
3. **Lock down the publishing path.** Require 2FA/passkeys on the maintainer account, protect `main`
   and release tags, pin third-party Actions to commit SHAs, and limit `VPK_TOKEN`'s scope.
4. **Keep downgrade protection explicit.** Leave `AllowVersionDowngrade` off and add a test that
   `AppUpdater` never applies an update whose version is not newer than `CurrentVersion`.

Do step 1 first. Step 2 is the one that actually moves trust away from GitHub, but it depends on
step 1 and needs care with certificate renewal: pin the publisher name plus the issuing CA, or ship
the next certificate's key ahead of time.
