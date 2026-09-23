# Tavern In-Game Hub Setup â€” Tavern Launcher Client 1.8.3 / 1.8.4

**Menu mod 2.2.0 Â· Setup 2.2.0 Â· Windows x64**

A small, separate Windows setup app for the Tavern In-Game Hub mod. Double-click the EXE, choose your game and launcher, and click **Install / Update**. You do not need to run commands, install Python, or compile anything.

Setup adds **Play Game** to Tavern Launcher's main window. Click it to open A Township Tale at its native server picker. The mod retrieves Tavern's community server list, includes saved/recent servers from your launcher profile, and performs Tavern authentication when you choose a server in the game. Server access rules still apply.

## Before you install

- Windows with .NET Framework 4.8 and Windows PowerShell 5.1 (included with supported Windows 10/11 installations).
- The supplied **Tavern Launcher Client 1.8.3 / 1.8.4** release. Keep its `addons` and `Patch` folders next to its EXE.
- The compatible managed Windows build of A Township Tale, with the Tavern client patch and Mono MelonLoader installed. Setup checks the launcher, game assembly, and TavernLib against the inspected builds.

If your game is not patched yet, open Tavern Launcher and use its **Patch** and **Mods** controls to complete its normal game setup first. This setup app installs the menu mod and launcher integration; it does not redistribute or install the game, Tavern Launcher, MelonLoader, or TavernLib.

## Install and play â€” no commands

1. Extract Tavern Launcher Client 1.8.3 / 1.8.4 into the folder where you will keep it.
2. Close A Township Tale and Tavern Launcher.
3. Double-click **TavernHubSetup.exe**. You may keep this EXE anywhere. If it is beside the launcher EXE, setup can detect that launcher automatically.
4. Beside **A Township Tale game**, click **Browse** and choose `A Township Tale.exe`.
5. Beside **Tavern Launcher client**, click **Browse** and choose `TavernLauncher - Client.exe` from version 1.8.3 or 1.8.4.
6. Click **Install / Update**. The live log displays checks, backups, copies, verification, and any errors. Wait for the completed status at the bottom.
7. Click **Open launcher**. In Tavern Launcher, set your username, game path, and platform: **SteamVR**, **Quest**, or **Fly**.
8. Click **Play Game** in Tavern Launcher's main window. Choose and join a server in the game's original menu.

**Check setup** checks compatibility without installing files. **Save log** saves the displayed output to a text file you choose. Logs can include local folder names; review them before sharing publicly.

The launcher integration uses versions 1.8.3 and 1.8.4's external add-on support to change the main button to **Play Game**. The launcher EXE itself stays original. The server list and other launcher tools remain available; choosing a server before launch is not required for Play Game.

## Updating an existing installation

**Launcher 1.8.4:** this setup accepts both 1.8.3 and 1.8.4. If updating the launcher too, close it and extract the 1.8.4 release into the **same launcher folder** you previously selected, retaining its `addons` folder. Apply the updated client patch using Tavern Launcher's normal controls, close the launcher, then run **Install / Update** here. Keeping the launcher at the same path preserves the existing installer record and Undo backups. The recovery folder still ends in `-1.8.3` for compatibility; do not rename it.

Close the game and launcher, open the new setup EXE, select the same game and launcher, and click **Install / Update**. There is no need to uninstall first. Setup verifies the previous installation, replaces changed mod/add-on files, and keeps the original Undo backups. Your launcher profile, login tokens, private server bookmarks and social identity remain in place. A failed update rolls back to the previously installed version.

If you installed by manually copying files and have no setup record, setup creates a baseline backup of those files on the first managed installation. If managed files have been edited outside setup, it reports the conflict instead of silently discarding those edits.

## Wheel, passwords and private servers

- The wheel repair prevents a missing optional board lever from interrupting the game's startup before the wheel and ropes receive their input handlers. Scrolling still uses the native wheel and rope behavior.
- Password servers open the game's touch keyboard in front of you. Touch the keys and press Enter; close the dialog to cancel. Password text is masked unless you select reveal, and is isolated from the search field and suggestions.
- Select **Add a private server...** in the server picker. Use the VR keyboard to enter the hostname or IPv4 address, optional game port, friendly name and authentication port. Defaults are game port **1757** and authentication port **1762**. It appears in Favorites and Saved / Recent. This mod does not submit it to the community dashboard. The server owner's separate listing settings still apply.

## Friends and peer invitations

The mod starts its hidden networking helper automatically with the game. There is no friends-service address to configure and no separate relay app to run. Only accepted contacts appear in the friends list.

1. On the friends board, open **My friend code / network status**. Share the full 76-character code with the person you want to add. It is also available in `<GameFolder>\UserData\TavernFriendCode.txt` for desktop copying.
2. Use **Add a friend code...**. The recipient accepts in **Requests**. Alternatively, exchange the game's physical friendship cards while together on a server with current card support.
3. Accepted friends remain listed when offline or on another game server. The list belongs to your saved peer identity, not a relay or an editable display name.
4. Choose a friend and **Invite to Server**, then select a saved/private server. On the older board, use its accept control and the server wheel to select an orb, or **Cancel invitation** to return to normal joining.
5. Incoming invitations appear on the board and in **Requests**. Joining uses Tavern authentication and the native password keyboard. Invitations contain no passwords or login tokens. Undelivered invitations wait locally for up to seven days; both games must be online together for delivery.

Public Tox bootstrap nodes provide discovery, and public TCP helpers may assist connections. This removes your group's separate relay app; it is not infrastructure-free and some routers or firewalls can still block connectivity. See [MESH-FRIENDS.md](MESH-FRIENDS.md).

**Upgrading from the relay version:** friends need a one-time re-add through codes or updated cards. Old relay accounts did not contain peer public keys, so the updater cannot safely infer identity from names. The old `TavernSocial.json` is preserved. Alta/Oculus friends are not imported.

### Optional native-card support for server hosts

Close the game server, run the same setup EXE, click **Install server support...**, and select the patched game-server folder containing `A Township Tale.exe`, `MelonLoader`, and `Plugins\TavernLib.dll`. Wait for the live log to confirm success, then start the server normally.

This replaces `Mods\TavernNativeSocialServer.dll` at the old companion's path and keeps a verified backup. No relay configuration or separate listener is needed. Both clients need this release, and native message channel 34 must be available. **Undo server support...** restores the original companion or its absence. Friend codes work without this companion.

## Social tablet

The native in-game tablet now uses the current server roster and Tavern peer friends. Select a player to add or accept a friend, unfriend, mute, block or unblock. **Blocked** keeps local blocked players accessible when they leave; **Banned players** lets authorized server moderators and owners unban accounts. Kick, ban and removal actions ask for a second touch to confirm, with Cancel and an expiry.

Update the client using **Install / Update**. Hosts should stop the server and run **Install server support...** from the same EXE to install the companion that supplies the roster, identity checks and moderation. The host's Tavern `moderator` and `owner` roles control access; the client cannot grant itself permission. Bans survive server restarts and apply to existing login tokens.

Personal blocking mutes that server account and, when the player's peer identity can be verified, also blocks friend requests and invitations from that peer. It does not hide avatars or eject a player. See [SOCIAL-TABLET.md](SOCIAL-TABLET.md) for controls, data locations and the remaining VR checks.

## File locations

These are placeholders, not folders to create literally:

| Path | Meaning |
| --- | --- |
| `<GameFolder>` | The folder containing your `A Township Tale.exe`. |
| `<LauncherFolder>` | The folder containing your version 1.8.3 or 1.8.4 `TavernLauncher - Client.exe`. |
| `%APPDATA%` | Your Windows roaming application-data folder. Enter `%APPDATA%` in File Explorer's address bar to open it. |
| `<GameFolder>\Mods\TavernNativeMenu.dll` | The installed in-game menu mod. |
| `<GameFolder>\UserData\TavernNativeMenu.json` | Your local private/saved servers and menu settings. |
| `<GameFolder>\UserData\TavernSocial.json` | Legacy relay credentials, retained during migration; keep private. |
| `<LauncherFolder>\addons\tavern_native_menu\` | The Play Game launcher add-on. |
| `%APPDATA%\TheModdingTavern\client_enabled_addons.json` | Tavern's enabled-add-on list. Setup adds its name while retaining other add-ons. |
| `%APPDATA%\TheModdingTavern\tavern_launcher.json` | Tavern's existing username, game path, platform, and saved-server settings. |
| `<GameFolder>\TavernNativeMenuSetup-1.8.3\` | Verified backups, installation record, and recovery data. Keep this folder until you have finished using Undo. |
| `<GameFolder>\TavernNativeMenu\native\` | Automatic peer helper, native libraries, seeds, licenses and corresponding source archives. |
| `<GameFolder>\UserData\TavernMesh.json` | Private peer identity, contacts and outgoing invitations. Keep this and its `.bak` private and backed up. |
| `<GameFolder>\UserData\TavernFriendCode.txt` | Public friend code; safe to share with a prospective friend. |
| `<GameFolder>\UserData\TavernTablet.json` | Personal blocks, scoped to server accounts; retained during updates. |
| `<ServerFolder>\UserData\TavernTabletServer.json` | Server identity and tablet bans; back up with the server's saves. |
| `<ServerFolder>\TavernNativeMenuCardSetup\` | Optional server-card setup manifest and original backups. |

`<ServerFolder>` means the separate patched game-server folder selected for card support. Client updates preserve the original Undo backups while adding peer dependencies. Do not uninstall first.

Setup **2.2.0** includes menu mod **2.2.0**, automatic peer networking, the compatible launcher add-on, and optional server card companion **2.2.0**. The mod continues to honor `quest_scene_required` when selecting the join scene.

Launcher **1.8.4** was checked against its published client/server assets, TavernLib **1.5.2**, and CircuitsVoiceChat **1.0.8**. Its patched game assembly matches the already-supported game hash. Authentication, tablet bindings and voice mute remain compatible; the installer and Play Game compatibility lists now recognize the new launcher and TavernLib files.

### Menu polish retained from 1.0.5

- A visible **Cancel** key on the left of the VR keyboard dismisses password and address prompts without submitting them. It remains available after changing keyboard layouts, with the key and its touch area shifted 8 mm to the right in keyboard coordinates.
- Server-selection orbs no longer dim the menu before Tavern authentication. Cancelling or leaving authentication restores visibility without grabbing the orb again. The native loading fade still runs after an approved join, and native fade locks are respected.
- Refreshing a selected server also refreshes its details. Community descriptions are displayed when provided by the directory; otherwise the menu shows connection, player-count, and access information. Saved public servers reuse current directory metadata; private entries retain local details.
- The server-picker scene replaces the identified Alta artwork with a separate 1254Ã—1254 reconstruction of the T badge, positioned 3.5 cm beyond the original artwork surface. The original icon and launcher banner are unchanged. The replacement is embedded in the mod, so updating needs no extra asset-copy commands.
- The Discord sign displays only **https://discord.gg/jNQUUDAYSj**, without a community-name heading. The complete Discord sign and adjacent wall badge are moved 6 cm to the viewer's left to provide wall clearance; the badge on the filter board retains its own layout.
- The complete sponsor board and separate white sponsor panel are disabled, including frames, captions, backing surfaces and colliders. Vivox and Screen Queensland/NSW artwork is also hidden on matching materials and textures. Voice communication remains available. A few delayed passes also handle signs initialized after the menu opens.

To update an existing installation, close the game and launcher, open **TavernHubSetup.exe**, select the same game and launcher paths, and use **Install / Update**. Keep your existing settings and original Undo backup. No uninstall is needed.

The keyboard regression tests and builds cover code behavior; headset placement, touch reach, sign orientation, and legibility still require an in-game VR check. The high-resolution badge is an AI reconstruction of the supplied 32Ã—32 icon, rather than an original high-resolution brand asset.

## Undo or move the installation

Close the game and launcher, run this same setup EXE, select the same game and launcher files, and click **Undo setup**. Setup restores previous files, or removes files it added when no previous copy existed. If an earlier mod was already present, Undo restores that mod rather than removing it.

Undo preserves your launcher profile, login tokens, saves, and mod settings. It removes its own enabled-add-on entry only when setup originally added it, and retains other enabled add-ons. Verified backups remain for recovery.

If an installed file or a backup has changed since setup, Undo stops with an explanation instead of overwriting that change. If a write fails midway, setup attempts rollback. An interrupted operation keeps a journal; use the same EXE and selections, close the game and launcher, and click Install or Undo to recover.

Before moving the game or launcher to a new folder, undo this installation using the original paths, move the folders, and install again using the new paths. Another launcher version requires a compatible setup release.

## Troubleshooting

- **The launcher still shows Join Server:** fully close and reopen your supported launcher. In its Addons window, check that **Tavern In-Game Hub - Play Game** is enabled. If needed, close it and click Install / Update in this setup app to re-enable the add-on. Check that you reopened the launcher you selected during setup.
- **Missing MelonLoader or TavernLib:** finish the launcher's normal Patch/Mods setup, close it, and retry. A compatible older TavernLib is accepted with a warning; applying the 1.8.4 client patch updates it.
- **Unsupported launcher/game:** select the inspected version 1.8.3 or 1.8.4 launcher and compatible patched game. Do not rename another version to bypass the check.
- **Access denied:** use game and launcher folders writable by your Windows account, or run setup with the permissions those folders require. Use the same Windows account that runs Tavern Launcher so its add-on settings are updated.
- **Play Game reports a missing mod:** the game path currently selected in Tavern Launcher must match the game folder you installed into.
- **No community servers appear:** the community-list service and game server must be reachable. Setup does not host servers or make unavailable servers online.
- **A server requires a password or whitelist access:** complete the normal server requirements. The menu mod does not bypass them.
- **Friends are offline:** keep both games open, allow time for discovery, and check the board's network-status row. Ensure the automatic `TavernMeshPeer.exe` helper can reach the network. Closing the sender's game pauses outgoing invitation delivery.
- **Friend card fails:** update both clients and install current server-card support from the same setup EXE. The old relay companion is incompatible. Check the game server's MelonLoader log; message channel 34 must be available. Repeat the mutual card exchange once connected.

## Compatibility and testing

Setup accepts the inspected Tavern Launcher Client **1.8.3 and 1.8.4** executables, two compatible game assembly builds, and four inspected TavernLib builds. The game and TavernLib retain the menu hooks used by the mod. Unknown binaries stop before installation.

The Play Game add-on preserves the launcher's username and platform settings, debug-helper toggle, and MelonLoader console preference. It opens the game with a temporary menu identity. The in-game mod authenticates with the selected server before joining.

Fly mode opens the original menu scene, but its full controller interactions were designed for VR. This release does not add a separate desktop server picker.

**Add-ons that need a server-specific action before game launch, such as custom-model asset syncing, have not been integrated with the in-game selection flow.** Play Game does not run those post-authentication launcher hooks. The supplied macros add-on uses a separate registration mechanism and is retained.

Verified during development:

- Compiled the standalone EXE and checked its window rendering.
- Tested live stdout/stderr, output before process exit, trailing output, error exit status, and paths containing spaces, apostrophes, brackets, ampersands, and Unicode.
- Passed 108 installer fixture checks covering install, in-place updates, Undo, rollback, interrupted-operation recovery, damaged backups, locked files, and add-on settings preservation.
- Passed nine launcher add-on tests covering the Play Game button, menu-only launch arguments, neutral identity, profile selection, platforms, console/debug settings, and failure handling.
- Ran install/reinstall/check/Undo separately with copies of the real 1.8.3 and 1.8.4 launcher releases and their TavernLib dependencies.
- Passed 19 checks against the official 1.8.4 launcher source for add-on loading, controls, profile saving and token construction.
- Passed 78 native-method and field compatibility checks across the two supplied game assemblies after rebuilding the updated mod.
- Passed 22 wheel/rope checks executing the supplied game's method instructions with simulated Unity objects, including reproducing the original missing-lever failure.
- Passed 16 keyboard/artwork regression scenarios for password submission, masking, cancellation, touch positioning, focus restoration and targeted sponsor removal.
- Passed 15 join-visibility checks covering orb-fade suppression, cancellation recovery, native fade locks and preservation of the approved-join loading fade, including instruction checks in both supplied game assemblies.
- Passed 80 social/card binding checks, 29 card-consent checks, and 38 client native-transport checks. Invalid identities, wrong nonces, one-sided confirmation, expiry, replay, disconnects, paginated bans and handler collisions are covered.
- Passed 44 peer integration checks using production adapters that automatically start the real helper EXEs and native cores, plus 39 peer verification/blocking checks. These cover mutual consent, private invitations, saved offline delivery, persistence, simultaneous requests, explicit blocks, temporary identity verification and corrupted identity preservation.
- Passed 64 tablet binding checks, 29 actual tablet prefab/layout checks, 31 server permission/persistent-ban/stream tests, 20 server binding checks per TavernLib release, and nine CircuitsVoiceChat 1.0.8 mute-path checks. These do not replace live headset testing.
- Passed 13 tablet mute lifecycle checks covering server switches, disconnects, provider replacement, unblock, shutdown and preservation of existing manual mutes.
- Passed 27 optional server-card installer checks for updates, original backups, Undo, rollback and crash recovery. The retired relay tests remain available as legacy reference only.

**A live VR session, physical card exchange, or wide-area invitation between two real game clients has not been tested in this environment.** Successful compilation and automated checks do not verify headset physics, keyboard reach or all runtime mod combinations. Treat this as a build for in-game testing and include MelonLoader logs when reporting a failure.

## For maintainers

The single setup embeds the worker, menu DLL, launcher add-on, peer helper, native dependencies, corresponding source archives, license notices and optional server-card companion. End users do not need scripts or compilers. `social-server/` retains the old relay implementation only as reference.

From this project folder, replace `<GameFolder>` with your compatible patched game directory and `<LauncherFolder>` with your supported launcher directory. Run these commands from the `setup-v1.8.4` folder:

```powershell
.\mesh-native\Build-Native.ps1
.\mesh-native\Refresh-Seeds.ps1
.\mesh-native\Package-Native.ps1
.\mesh-peer\Build-Peer.ps1 -GameFolder '<GameFolder>'
.\mesh-server\Build-MeshServer.ps1 -GamePath '<GameFolder>'
.\Build-Mod.ps1 -GameFolder '<GameFolder>' -TavernLib '<LauncherFolder>\Patch\TavernLib.dll'
.\Build-Setup.ps1
.\Package-Setup.ps1
```

C# projects use Windows' .NET Framework compiler. Building the native library additionally requires Visual Studio Desktop development with C++, CMake and vcpkg. The standalone helper and c-toxcore are GPL-3.0-or-later; Newtonsoft.Json is MIT, libsodium ISC, and pthreads4w Apache-2.0. Keep the embedded notices and corresponding source archives with redistributed binaries. Unrelated project files retain their existing licensing.

Share `dist/TavernHubSetup.zip`, containing **one setup EXE**, the quick-start README, this advanced guide, the mesh friends and social tablet guides, and SHA256 checksums. Do not include game binaries, launcher executables, decompiled research or personal `UserData` in releases.
