# 📱 Tavern social tablet

**Menu mod 2.2.0 · Server companion 2.2.0 · Setup 2.2.1**

The game's physical social tablet now connects to the Tavern server and the same peer friends used by the main menu. It uses the existing tablet touch controls and player rows.

## Update without reinstalling

1. Close the game and launcher. Run **TavernHubSetup.exe**, select your existing game and Tavern Launcher Client 1.8.3 / 1.8.4, then click **Install / Update**.
2. The server host should stop the game server, run that same EXE, choose **Install server support...**, and select the patched game-server folder. Start the server again when setup finishes.
3. Join the server and summon the social tablet using the game's normal quick-access control. Open its player list and select a player.

Existing settings, peer identity, friends and original Undo backups are retained. Both players need the updated client for tablet friendship. Moderation acts on the server account even when the target does not have this mod.

## Controls

| Control | What it does |
| --- | --- |
| **Online players** | Lists people currently connected to this server and refreshes while the tablet is open. |
| **Add friend** | Verifies that player's peer identity, then sends a friend request. The recipient must accept. |
| **Accept friend / Decline** | Responds to an incoming request. Requests are also available on the main-menu friends board. |
| **Unfriend** | Removes the accepted friend from your local peer contacts. |
| **Mute / Unmute** | Uses the game's voice mute control. |
| **Block / Unblock** | Saves a personal block on this server account. A verified peer block also stops peer requests and invitations and removes that friendship. Unblocking does not automatically make the person a friend again. |
| **Blocked** | Lets you remove a saved personal block even after the player leaves. |
| **Kick** | Disconnects the selected player; they can join again if normal server checks allow it. |
| **Ban / Unban** | Persists a ban or removes it. Authorized users can select offline banned accounts from **Banned players**. |

Removal, blocking and moderation actions use a second touch to confirm. Choose **Cancel**, leave the action page, or let the confirmation expire to cancel it. The tablet shows progress and errors on its action page.

A personal block affects voice and social communication. It does not hide another player's avatar, prevent their world interactions, or ban them from the server. If peer verification is unavailable, the tablet reports that only the local server block was saved.

## Who can moderate?

Permissions are read from the host's Tavern account records. Tavern's **moderator** role provides the administrator controls in this integration; **owner** has higher authority. Client identity claims and display names do not grant permissions.

| Host-assigned role | Allowed targets |
| --- | --- |
| Player | No kick, ban or unban access. |
| Moderator | Ordinary players. |
| Owner | Ordinary players and moderators. |

Nobody can target themselves or an owner. The server checks authority again for every request. A ban is saved before disconnection and checked on subsequent joins, including joins with a previously valid login token.

The standalone `admin` role is not a Tavern moderation role in the inspected launcher build; assign `moderator` or `owner` through the host's existing account management.

## Files and compatibility

`<GameFolder>` means the folder containing the client's `A Township Tale.exe`. `<ServerFolder>` means the separate patched game-server folder selected in setup.

| File | Contents |
| --- | --- |
| `<GameFolder>\UserData\TavernTablet.json` | Personal server-account blocks and verified peer references. |
| `<GameFolder>\UserData\TavernMesh.json` | Existing private peer identity, accepted contacts, explicit peer blocks and invitations. Keep private and backed up. |
| `<ServerFolder>\UserData\TavernTabletServer.json` | This server's stable identity and persistent tablet bans. Back up with server data. |
| `<ServerFolder>\Mods\TavernNativeSocialServer.dll` | Combined card and tablet companion, installed by the same setup app. |

The companion uses the existing game connection on native message channel 34. It does not open a separate relay service. If another mod owns that channel, the integration reports the conflict instead of replacing its handler.

On an older server without tablet support, the current-player list and ordinary voice mute remain available. Peer actions and persistent account blocking require updated server support. A failed or malformed saved-state load is reported; the mod does not silently erase saved bans or blocks.

## Implementation map

The inspected game code includes `SocialTablet`, `Alta.SocialTablet.RecentPlayersPage`, `RecentPlayerElement`, `RecentPlayerActionPage`, `RecentPlayers`, `FriendshipManager`, `TouchScreenMenuBase` and the quick-access action-item spawn path. The original tablet's recent-player and account actions used Alta services. Its original ban handler did not enforce Tavern server bans, and the shipped tablet had no usable options/confirmation popup assigned.

The integration is in `mod-src/NativeSocialTablet.cs`, `MeshTabletTransport.cs`, `MeshSocialTransport.cs`, and `MeshSocialClient.cs`. Server enforcement is in `mesh-server/src/TabletService.cs` and `TabletPolicy.cs`. Peer verification and blocking live in `mesh-peer/src/MeshPeerRuntime.cs`. Native button clones use the mute-button template, avoiding old navigation callbacks, and are registered in the tablet's touch map. Game assemblies and decompiled game source are not included in the distribution.

## Verification

Automated checks cover server permissions, durable bans and old-token admission, peer consent and blocking, identity challenges, connection cleanup, and installer update/Undo. Compilation and binding checks use the supplied compatible game assemblies.

The final physical layout and end-to-end behavior still need a two-player VR session: touch each control, accept a tablet request, block/unblock voice, kick an ordinary player, ban and retry joining, restart the server, then unban. A successful automated check does not establish that those headset interactions have been tested.
