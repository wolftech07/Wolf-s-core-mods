# Tavern Native Menu Setup — Tavern Launcher Client 1.8.3

A small, separate Windows setup app for the Tavern Native Menu mod. Double-click the EXE, choose your game and launcher, and click **Install / Update**. You do not need to run commands, install Python, or compile anything.

Setup adds **Play Game** to Tavern Launcher's main window. Click it to open A Township Tale at its native server picker. The mod retrieves Tavern's community server list, includes saved/recent servers from your launcher profile, and performs Tavern authentication when you choose a server in the game. Server access rules still apply.

## Before you install

- Windows with .NET Framework 4.8 and Windows PowerShell 5.1 (included with supported Windows 10/11 installations).
- The supplied **Tavern Launcher Client 1.8.3** release. Keep its `addons` and `Patch` folders next to its EXE.
- The compatible managed Windows build of A Township Tale, with the Tavern client patch and Mono MelonLoader installed. Setup checks the launcher, game assembly, and TavernLib against the inspected builds.

If your game is not patched yet, open Tavern Launcher and use its **Patch** and **Mods** controls to complete its normal game setup first. This setup app installs the menu mod and launcher integration; it does not redistribute or install the game, Tavern Launcher, MelonLoader, or TavernLib.

## Install and play — no commands

1. Extract Tavern Launcher Client 1.8.3 into the folder where you will keep it.
2. Close A Township Tale and Tavern Launcher.
3. Double-click **TavernNativeMenuSetup.exe**. You may keep this EXE anywhere. If it is beside the launcher EXE, setup can detect that launcher automatically.
4. Beside **A Township Tale game**, click **Browse** and choose `A Township Tale.exe`.
5. Beside **Tavern Launcher client**, click **Browse** and choose `TavernLauncher - Client.exe` from version 1.8.3.
6. Click **Install / Update**. The live log displays checks, backups, copies, verification, and any errors. Wait for the completed status at the bottom.
7. Click **Open launcher**. In Tavern Launcher, set your username, game path, and platform: **SteamVR**, **Quest**, or **Fly**.
8. Click **Play Game** in Tavern Launcher's main window. Choose and join a server in the game's original menu.

**Check setup** checks compatibility without installing files. **Save log** saves the displayed output to a text file you choose. Logs can include local folder names; review them before sharing publicly.

The launcher integration uses version 1.8.3's external add-on support to change the main button to **Play Game**. The launcher EXE itself stays original. The server list and other launcher tools remain available; choosing a server before launch is not required for Play Game.

## Updating an existing installation

Close the game and launcher, open the new setup EXE, select the same game and launcher, and click **Install / Update**. There is no need to uninstall first. Setup verifies the previous installation, replaces changed mod/add-on files, and keeps the original Undo backups. Your launcher profile, login tokens, private server bookmarks and social identity remain in place. A failed update rolls back to the previously installed version.

If you installed by manually copying files and have no setup record, setup creates a baseline backup of those files on the first managed installation. If managed files have been edited outside setup, it reports the conflict instead of silently discarding those edits.

## Wheel, passwords and private servers

- The wheel repair prevents a missing optional board lever from interrupting the game's startup before the wheel and ropes receive their input handlers. Scrolling still uses the native wheel and rope behavior.
- Password servers open the game's touch keyboard in front of you. Touch the keys and press Enter; close the dialog to cancel. Password text is masked unless you select reveal, and is isolated from the search field and suggestions.
- Select **Add a private server...** in the server picker. Use the VR keyboard to enter the hostname or IPv4 address, optional game port, friendly name and authentication port. Defaults are game port **1757** and authentication port **1762**. It appears in Favorites and Saved / Recent. This mod does not submit it to the community dashboard. The server owner's separate listing settings still apply.

## Friends and live invitations

The included **TavernNativeSocialHost.exe** supplies the small shared relay. One person hosts it for the group; ordinary players only install the client mod. Read **SOCIAL-HOSTING.md** for the host setup, server companion and internet connection requirements. A reachable HTTPS relay must be configured before friends on different computers can use this feature; this package does not include a pre-hosted public service.

1. On the native friends board, choose **Connect friends service...** and enter the HTTPS URL supplied by the relay operator. Everyone uses the same relay. A blank address disconnects while preserving your saved identity.
2. Both players join a game server with the companion installed and exchange the game's physical friendship cards. The companion verifies both players and both sides of the exchange before saving the friendship.
3. Your friends appear on the board with online/offline status. Your game client sends presence heartbeats; after it closes or disconnects, you become offline within about a minute. The relay polls for updates, so delivery is not instantaneous.
4. Choose a friend and **Invite to Server**, then select a saved/private server. On the older card-style friends board, its accept control opens invitation selection on the server wheel: select a saved server's orb to send, or **Cancel invitation** to return to normal joining. The reject control removes a friend.
5. Incoming invitations appear in the friends list and the **Requests** tab. Join uses the regular Tavern authentication and native password keyboard. The newer panel also offers Save Server and Dismiss; the older board uses its accept/reject controls. A private address goes only to the chosen recipient, and passwords and login tokens are never included. Pending invitations expire after seven days.

Friends are attached to a saved relay identity, not to a display name or a server's numeric player ID. Keep `UserData\TavernSocial.json` and its backup to retain your identity. Changing your Tavern name does not create a new identity. Existing Alta/Oculus account friend lists are not imported; the mod's native board uses relay friendships established through cards. Social features do not require the companion on every server you invite someone to, but adding friends through cards requires it on the server where you exchange them.

## File locations

These are placeholders, not folders to create literally:

| Path | Meaning |
| --- | --- |
| `<GameFolder>` | The folder containing your `A Township Tale.exe`. |
| `<LauncherFolder>` | The folder containing your version 1.8.3 `TavernLauncher - Client.exe`. |
| `%APPDATA%` | Your Windows roaming application-data folder. Enter `%APPDATA%` in File Explorer's address bar to open it. |
| `<GameFolder>\Mods\TavernNativeMenu.dll` | The installed in-game menu mod. |
| `<GameFolder>\UserData\TavernNativeMenu.json` | Your local private/saved servers and menu settings. |
| `<GameFolder>\UserData\TavernSocial.json` | Your selected relay and private social credentials. Keep this file; do not post it with the mod. |
| `<LauncherFolder>\addons\tavern_native_menu\` | The Play Game launcher add-on. |
| `%APPDATA%\TheModdingTavern\client_enabled_addons.json` | Tavern's enabled-add-on list. Setup adds its name while retaining other add-ons. |
| `%APPDATA%\TheModdingTavern\tavern_launcher.json` | Tavern's existing username, game path, platform, and saved-server settings. |
| `<GameFolder>\TavernNativeMenuSetup-1.8.3\` | Verified backups, installation record, and recovery data. Keep this folder until you have finished using Undo. |

Setup **1.1.4** includes TavernNativeMenu **1.0.5** and a version 1.8.3 add-on. The bundled mod also honors version 1.8.3's `quest_scene_required` authentication response by choosing the corresponding scene for that join. The optional social host embeds TavernNativeSocialServer **1.0.0**.

### Menu polish in 1.0.5

- A visible **Cancel** key on the left of the VR keyboard dismisses password and address prompts without submitting them. It remains available after changing keyboard layouts, with the key and its touch area shifted 8 mm to the right in keyboard coordinates.
- Server-selection orbs no longer dim the menu before Tavern authentication. Cancelling or leaving authentication restores visibility without grabbing the orb again. The native loading fade still runs after an approved join, and native fade locks are respected.
- Refreshing a selected server also refreshes its details. Community descriptions are displayed when provided by the directory; otherwise the menu shows connection, player-count, and access information. Saved public servers reuse current directory metadata; private entries retain local details.
- The server-picker scene replaces the identified Alta artwork with a separate 1254×1254 reconstruction of the T badge, positioned 3.5 cm beyond the original artwork surface. The original icon and launcher banner are unchanged. The replacement is embedded in the mod, so updating needs no extra asset-copy commands.
- The Discord sign displays only **https://discord.gg/jNQUUDAYSj**, without a community-name heading. The complete Discord sign and adjacent wall badge are moved 6 cm to the viewer's left to provide wall clearance; the badge on the filter board retains its own layout.
- The complete sponsor board and separate white sponsor panel are disabled, including frames, captions, backing surfaces and colliders. Vivox and Screen Queensland/NSW artwork is also hidden on matching materials and textures. Voice communication remains available. A few delayed passes also handle signs initialized after the menu opens.

To update an existing installation, close the game and launcher, open **TavernNativeMenuSetup.exe**, select the same game and launcher paths, and use **Install / Update**. Keep your existing settings and original Undo backup. No uninstall is needed.

The keyboard regression tests and builds cover code behavior; headset placement, touch reach, sign orientation, and legibility still require an in-game VR check. The high-resolution badge is an AI reconstruction of the supplied 32×32 icon, rather than an original high-resolution brand asset.

## Undo or move the installation

Close the game and launcher, run this same setup EXE, select the same game and launcher files, and click **Undo setup**. Setup restores previous files, or removes files it added when no previous copy existed. If an earlier mod was already present, Undo restores that mod rather than removing it.

Undo preserves your launcher profile, login tokens, saves, and mod settings. It removes its own enabled-add-on entry only when setup originally added it, and retains other enabled add-ons. Verified backups remain for recovery.

If an installed file or a backup has changed since setup, Undo stops with an explanation instead of overwriting that change. If a write fails midway, setup attempts rollback. An interrupted operation keeps a journal; use the same EXE and selections, close the game and launcher, and click Install or Undo to recover.

Before moving the game or launcher to a new folder, undo this installation using the original paths, move the folders, and install again using the new paths. Another launcher version requires a compatible setup release.

## Troubleshooting

- **The launcher still shows Join Server:** fully close and reopen version 1.8.3. In its Addons window, check that **Tavern Native Menu - Play Game** is enabled. If needed, close it and click Install again in this setup app to re-enable the add-on. Check that you reopened the launcher you selected during setup.
- **Missing MelonLoader or TavernLib:** finish the launcher's normal Patch/Mods setup, close it, and retry. A compatible older TavernLib is accepted with a warning; applying the supplied 1.8.3 client patch updates it.
- **Unsupported launcher/game:** select the inspected version 1.8.3 launcher and compatible patched game. Do not rename another version to bypass the check.
- **Access denied:** use game and launcher folders writable by your Windows account, or run setup with the permissions those folders require. Use the same Windows account that runs Tavern Launcher so its add-on settings are updated.
- **Play Game reports a missing mod:** the game path currently selected in Tavern Launcher must match the game folder you installed into.
- **No community servers appear:** the community-list service and game server must be reachable. Setup does not host servers or make unavailable servers online.
- **A server requires a password or whitelist access:** complete the normal server requirements. The menu mod does not bypass them.
- **Relay error:** in the social host, click **Start relay**, then **Test connection**. Its live log explains the outcome. An automatic diagnostic log is also saved at `%LOCALAPPDATA%\TavernNativeSocialRelay\host.log`. See the hosting guide for port conflicts, HTTPS requirements and log locations. `127.0.0.1` works only on the computer running the relay.
- **Friend card fails:** both players need this client mod and the same relay; the game server needs its configured companion. Check its MelonLoader log and relay log. Message channel 34 must be available. Repeat the two-player card handoff after the service reconnects.

## Compatibility and testing

Setup targets the supplied Tavern Launcher Client **1.8.3** executable. The inspected game assembly and TavernLib retain the menu hooks used by the existing mod. It accepts the two inspected game assembly builds and three previously inspected TavernLib builds; unsupported binaries stop before installation.

The Play Game add-on preserves the launcher's username and platform settings, debug-helper toggle, and MelonLoader console preference. It opens the game with a temporary menu identity. The in-game mod authenticates with the selected server before joining.

Fly mode opens the original menu scene, but its full controller interactions were designed for VR. This release does not add a separate desktop server picker.

**Add-ons that need a server-specific action before game launch, such as custom-model asset syncing, have not been integrated with the in-game selection flow.** Play Game does not run those post-authentication launcher hooks. The supplied macros add-on uses a separate registration mechanism and is retained.

Verified during development:

- Compiled the standalone EXE and checked its window rendering.
- Tested live stdout/stderr, output before process exit, trailing output, error exit status, and paths containing spaces, apostrophes, brackets, ampersands, and Unicode.
- Passed 70 installer fixture checks covering install, in-place updates, Undo, rollback, interrupted-operation recovery, damaged backups, locked files, and add-on settings preservation.
- Passed nine launcher add-on tests covering the Play Game button, menu-only launch arguments, neutral identity, profile selection, platforms, console/debug settings, and failure handling.
- Ran install/reinstall/check/Undo with copies of the real supplied launcher and compatible game dependencies.
- Passed 78 native-method and field compatibility checks across the two supplied game assemblies after rebuilding the updated mod.
- Passed 22 wheel/rope checks executing the supplied game's method instructions with simulated Unity objects, including reproducing the original missing-lever failure.
- Passed 16 keyboard/artwork regression scenarios for password submission, masking, cancellation, touch positioning, focus restoration and targeted sponsor removal.
- Passed 15 join-visibility checks covering orb-fade suppression, cancellation recovery, native fade locks and preservation of the approved-join loading fade, including instruction checks in both supplied game assemblies.
- Passed 80 social/card/transport binding checks, 46 relay/card-consent checks and 29 client HTTP checks. Tested credentials, private invitation recipients, one-use server tickets, persistence, reconnect identity and failed requests.
- Passed eight integration checks using two isolated instances of the production social client against the real relay over local HTTP, including invitation acceptance and identity recovery after restart.
- Started and stopped the actual host EXE in an isolated fixture, exercised its connection button and automatic log, and rendered both setup windows for layout review.

**A live VR session, physical card exchange and invitation between two real game clients have not been tested in this environment.** Successful compilation and automated checks do not verify headset physics, keyboard reach or all runtime mod combinations. Treat this as a build for in-game testing and include MelonLoader logs when reporting a failure.

## For maintainers

The release EXE embeds the worker, menu-mod DLL, add-on, and file plan. No external scripts are needed by users. `Build-Setup.ps1`, `Build-Mod.ps1`, `src/Setup.cs`, `mod-src/`, `payload/`, and `tests/` contain the separate setup project. The build uses Windows' .NET Framework compiler and does not require NuGet or a .NET SDK. `mod-src/` contains the current menu-mod source. Superseded version 1.8.2 project files have been removed.

To rebuild the setup from this project folder, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Setup.ps1`. It embeds the existing `payload\TavernNativeMenu.dll`. To rebuild that mod first, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Mod.ps1 -GameFolder "<GameFolder>"`, replacing the placeholder with the compatible game directory. This process-local execution-policy flag does not change the machine policy. The setup result is `dist\TavernNativeMenuSetup.exe`.

The social host is built separately with `social-server\Build-Social.ps1 -GamePath "<GameFolder>"`. It embeds the compiled server companion and uses the Windows .NET Framework compiler. `social-server\src\` contains the relay, GUI and companion sources.

For distribution, share `TavernNativeMenuSetup-for-1.8.3.zip`, which includes both EXEs and their guides, or give ordinary players just the client setup EXE and this README. No personal absolute paths, game binaries, launcher executable, credentials or extracted game code are included in the release package.
