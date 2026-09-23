# Mesh friends

The current mod starts its peer networking helper automatically while the game is running. Everyone installs from **TavernHubSetup.exe**. No one needs to choose, configure or run a separate friends relay app.

The friends board contains **accepted friends only**. Discovering another mod user does not make them your friend or expose them on your friends list. Friendship requests are separate until accepted.

## Add a friend

Open **My friend code / network status** on the native friends board, share your full 76-character code, and use **Add a friend code...** to enter theirs. A public-code copy is saved in `UserData/TavernFriendCode.txt` for desktop copying. The recipient accepts the incoming request. Display names are labels; the saved peer identity determines who you are communicating with.

You can also exchange the game's physical friendship cards while together on a server with the current card companion. Both players must use the current client mod. The card exchange requires mutual consent; grabbing a card alone does not permanently add somebody.

The in-game social tablet also supports friend requests, acceptance, and personal blocking. Its server companion must be current. See [SOCIAL-TABLET.md](SOCIAL-TABLET.md) for tablet controls and moderation permissions.

Accepted contacts remain saved on your computer when either player closes the game. Only their online status changes. Keep `UserData/TavernMesh.json` and its `.bak`, along with your other game `UserData` and backups when moving the installation; it contains your private identity and friends data. Do not distribute it with the mod.

## Invite a friend

Choose an accepted friend, select **Invite to Server**, and choose a saved or private server. On the older friends board, the invitation action opens the server wheel; choose an orb or **Cancel invitation**.

Incoming invitations appear on the native board and in **Requests**. Joining still uses Tavern authentication, the password keyboard and normal server access rules. Invitations carry the server address and connection settings to the selected friend; they do not contain server passwords or login tokens.

If delivery is unavailable, outgoing invitations are saved locally for up to seven days. Both clients must be online together to deliver them. There is no separately hosted offline mailbox; closing the sender's game pauses delivery.

## Optional: enable native cards on a game server

Friend codes work without a server companion. To support physical cards:

1. Close the game server.
2. Run the same **TavernHubSetup.exe** used for the client.
3. Click **Install server support...** and choose the patched game server folder containing `A Township Tale.exe`, `MelonLoader`, and `Plugins\TavernLib.dll`.
4. Wait for the live setup log to confirm success, then start the server normally.

This installs `Mods\TavernNativeSocialServer.dll`. It replaces the legacy relay companion at that same path and backs up any previous copy. It needs no relay address or separate listener. The game server's normal connection carries the authenticated card exchange; message channel 34 must be available.

Use **Undo server support...** with the same server folder to restore the original companion or its original absence. Server saves and configuration files are retained. Backups and recovery state are kept in `TavernNativeMenuCardSetup` inside the server folder. If Undo restores the old relay companion, that older companion still has its original relay requirements.

## Update from the relay release

Close the game and launcher and click **Install / Update** in the new setup. Setup adds the networking dependency files while retaining the original Undo backups. There is no need to uninstall.

Legacy `UserData\TavernSocial.json` is preserved. The old relay identities did not include peer public keys, so friends must be added once again using friend codes or the updated cards. The updater cannot safely infer a peer identity from an editable name or a server-local player number.

The old relay host is no longer included in the distribution. You can retain its data separately if you still use an older mod release; the updater does not delete that data.

## How connections work

The mod launches a small hidden networking helper with the game and stops it when the game exits. You do not launch that helper yourself. It uses Tox peer networking, public bootstrap nodes to find the network, and public Tox TCP helpers when needed to establish connections. The dependency notices and corresponding native source are bundled with the release.

This removes the requirement to operate your own friends service. It does not remove the need for network discovery infrastructure, and direct connections are not guaranteed through every router or firewall. Public bootstrap/helper operators can observe connecting network addresses. Your identity file and friend list stay on your computer; only accepted peers receive social messages intended for them.

If a friend stays offline, confirm both games are open, both players installed the current release and neither connection blocks the networking helper. Check the game log for the exact failure. Keep keys, saved-state files and private server addresses out of public bug reports.

Automated fixtures exercise identity, consent, persistence and setup recovery. Wide-area connections and physical card exchanges between two VR players still require live testing; a successful local test does not guarantee every internet route or headset interaction.
