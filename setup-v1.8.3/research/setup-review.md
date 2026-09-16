# Separate setup review: Tavern Launcher Client 1.8.3

Scope: independent read-only review of the existing `Install.ps1`, `Uninstall.ps1`, `tools/Integrate-Launcher.ps1`, `tools/Invoke-PythonPatch.ps1`, `tools/patch_launcher.py`, `tools/Play-TavernMenu.ps1`, and existing validation notes. This note is a new file; no existing code or user installation was changed by this review. Items below are recommendations and acceptance criteria, not claims that the new installer passed them.

## Findings from the existing installer

- `Install.ps1` copies the DLL and game helpers before checking launcher compatibility. A launcher validation or patching failure leaves a partial install. The new setup needs preflight and staging before mutation, followed by rollback if a later commit fails.
- The existing launcher integration only accepts the exact supplied **1.8.2** EXE hash. Do not call it against 1.8.3 or simply disable that guard. Review the 1.8.3 callback, config schema, embedded runtime, and game/TavernLib assemblies; put the resulting version-specific backend in the new setup folder.
- The Play helper accepts two exact `Root.Township.dll` hashes. Reusing the old mod/helper is only appropriate if the 1.8.3 assemblies are identical or separately verified compatible.
- Existing integration writes companion scripts and its record before atomically replacing the EXE. Treat these as one installation transaction, and do not leave a success record for a failed EXE replacement.
- Existing uninstall restores a verified original launcher only when the current launcher matches its recorded patched hash, which is a useful update-protection rule. Apply the same rule to the mod and companion scripts: preserve files users or updates changed after setup.
- Existing mod backup is made only once. A new setup should retain a manifest of the exact previous and installed hashes and the existence of every target. Reinstall should recognize its own installation and avoid corrupting the original backup chain.
- Existing uninstall leaves companion files. New Undo should explicitly report what it restores/removes/retains, and only remove a companion it installed that still matches its recorded hash.
- The old scripts often report output only after a large stage completes. Add progress before validation, archive extraction, patch verification, backup, copy, hash verification, and rollback so the live log demonstrates ongoing work.

## GUI and logging acceptance criteria

1. Double-clicking the setup EXE opens a small window without a developer SDK, external Python installation, or visible command shell. The release contains all installer-owned assets required at runtime. If using a BAT wrapper instead, missing dependencies produce a persistent, readable error.
2. Provide labeled pickers for the **game folder containing `A Township Tale.exe`** and **Tavern Launcher client EXE**. Auto-detected paths remain editable; validate before Install. No personal paths are compiled into the product or documentation.
3. Run installer work off the UI thread. Pipe stdout and stderr concurrently; an asynchronous event/queue updates the log on the UI thread. Do not sequentially call `ReadToEnd()` on both redirected streams or call `WaitForExit()` on the UI thread.
4. Keep line order sufficiently clear using timestamps/stage labels; distinguish stderr, failure, rollback, and success. Preserve the final trailing line when the process exits. Child exit code, not just the last printed message, decides success.
5. Use a hidden child process and structured/path-safe arguments. A command-string concatenation containing the selected path must not allow shell syntax. Test spaces, apostrophes, Unicode, ampersands, square brackets, and trailing directory separators.
6. Disable conflicting buttons while work runs. Do not allow closing/canceling to terminate a file transaction midway. Explain when setup is finishing a safe operation; keep the log visible afterward. Do not launch game or launcher automatically just to test installation.
7. Offer Copy log or Save log. Log stage details and exception messages; never print raw launcher profiles, tokens, passwords, access/refresh/identity token arguments, or environment dumps. Installation paths may appear in local logs, so documentation should advise reviewing logs before public sharing.
8. Installer success should explain the next steps: configure username/game/VR platform in Tavern Launcher, reopen it, press **Play Game**, select a server in the game's menu. Distinguish installation verification from VR/live-join verification.

## File safety and compatibility acceptance criteria

1. Require the game EXE, supported managed assemblies, Mono MelonLoader, TavernLib, payload DLL, helpers, expected exact 1.8.3 launcher hash, and necessary archive records. Check all guards before changing targets.
2. Detect a running game or launcher before staging/commit/undo and provide a readable close-and-retry message. Do not kill user processes. Recheck immediately before replacement to reduce races.
3. Resolve all targets against the selected game and launcher directories. Reject overlapping/inappropriate paths and unexpected reparse-point redirection where it could escape the selected location. Never recursively delete user-selected directories. Cleanup should target only a verified installer-owned temporary directory.
4. Preserve an original backup; refuse an unrelated or mismatching backup. A prior setup installation should be safely recognized. Never blindly overwrite a file updated after install or an unknown existing setup record.
5. Stage transformed launcher and companion files, verify all expected hashes/structural checks, and only then commit. Use same-directory temporary files/atomic replacement where practical. Roll back committed files in reverse order when later changes fail; retain useful recovery material if rollback also fails.
6. Journal installation ownership and exact hashes. Undo requires both correct original backups and unchanged installed targets; leave profile/tokens/saves/mod settings intact. Explain any blocked restore rather than partially claiming success.
7. Installation should not require administrator rights for ordinary writable folders. If writes are denied, show the actual failure and a practical action. Do not alter machine execution policy, install services, silently download runtimes, or mutate launcher/game settings as an installer side effect.

## Bounded tests before release

- Snapshot SHA256 for the existing project source/scripts and release ZIP; verify unchanged after building the separate setup.
- Build the EXE using the existing .NET Framework compiler and test startup plus the log/progress UI with a harmless self-test or fixture worker.
- Exercise worker output on both streams, delayed messages, a final line without a newline, nonzero exit, and invalid inputs; verify the UI remains responsive and reports accurate status.
- Run installation/undo only against **isolated fixture/copy directories**, including an exact copy of the supplied 1.8.3 EXE and required dependencies. Assert original EXE restored exactly and unrelated archive entries unchanged.
- Reject 1.8.2/unknown launchers, missing payload/game dependencies, unsupported game hashes, stale/mismatched backups, changed installed files, and locked files without clobbering them.
- Inject failure after at least one file commit; assert rollback restores both original bytes and original file absence. Verify recovery information when rollback cannot complete.
- Test second install, undo before install, undo after successful install, and second undo; none should destroy an original backup or delete unrelated files.
- Exercise paths with spaces and common shell-special characters. Ensure new docs/package contents contain no author-specific absolute paths or private config/token data.

VR rendering, controller selection, a live server join, and correct character identity remain manual runtime acceptance checks. A compiled and fixture-tested installer cannot establish those game behaviors.

## Implemented worker and completed checks

The new `payload/Setup-Worker.ps1` is independent of the old installer. The GUI calls it using Windows PowerShell with `-Operation Validate|Install|Uninstall`, `-GameExe`, `-LauncherExe`, and `-PayloadRoot`. Paths are complete paths to the selected executable files and to the extracted embedded payload. `Validate` is read-only. All output uses UTF-8; failures print an `[ERROR]` line and exit nonzero.

The payload's `integration-plan.json` is a full `Entries` list. Each entry has `Source` relative to the payload, `Scope` equal to `game` or `launcher`, and `Target` relative to that selected folder. It must explicitly include `TavernNativeMenu.dll` targeting `Mods/TavernNativeMenu.dll`. Allowed addon targets are under `addons/tavern_native_menu/`; launcher EXE replacement is refused.

The worker checks the supplied 1.8.3 launcher hash, the two supported game assembly hashes, and three reviewed TavernLib hashes. It merges `tavern_native_menu` into `%APPDATA%/TheModdingTavern/client_enabled_addons.json` as a BOM-free UTF-8 JSON list. Undo preserves unrelated addon changes made afterward and removes this name only when setup originally added it. No profile, token, password, or save files are read or rewritten by the worker.

Files are staged and verified before commit. The game folder's `TavernNativeMenuSetup-1.8.3` directory retains original files, transaction snapshots, and an ownership record. A pending journal enables recovery after interruption. Repeat install verifies existing files and backups; it can restore disabled addon enablement. Undo refuses changed installed files or damaged backups. A per-game named mutex prevents concurrent setup transactions. Log messages describe checks, copies, verification, and recovery.

`tests/Run-WorkerTests.ps1` completed **43 checks under Windows PowerShell 5.1**, using only isolated temporary fixtures. Those checks cover install/undo, absent and preexisting files, idempotence, addon settings preservation, re-enabling, UTF-8 without BOM, spaces/apostrophes/ampersands/brackets/Unicode paths, corrupt backups, locked files, changed installed files, invalid settings and traversal, missing dependencies, failure rollback, and automatic recovery of an interrupted commit. Tests override dependency checks only inside the test harness; the production worker has no compatibility-bypass switch. The production guard was also checked to reject a fake launcher.

The added Unicode test found and fixed a Windows PowerShell 5.1 encoding bug: reading a BOM-free UTF-8 installation record with default `Get-Content` corrupts non-ASCII paths. The worker now reads JSON with `File.ReadAllText`. Its console encoding also explicitly matches the GUI's UTF-8 reader.

## GUI and addon review notes

The reviewed `src/Setup.cs` runs the child worker off the UI thread, redirects both streams asynchronously, drains trailing output, quotes Windows arguments without a shell, blocks conflicting actions while busy, and verifies ownership before temporary-folder cleanup. No additional blocking defect was found in that read-only review. The GUI's own visual and process tests are separate from the 43 worker checks.

The new addon's referenced 1.8.3 fields, methods, platform mapping, `CONFIG_FILE`, and `build_tokens` all exist in the extracted launcher disassembly. The old mod's two targeted TavernLib prefix method names are still present in the new library.

One compatibility limit needs user-facing documentation: the stock 1.8.3 `_do_launch` calls `run_post_auth_hooks(host, log_fn)` for per-server work such as custom-model asset syncing before game launch. Entering the game before choosing a server skips that server-specific launch hook. Full compatibility with such addons has not been established; do not describe all launcher addons as supported without handling that separate path.
