# Tavern In-Game Hub

**v2.3.1 · A Township Tale · Tavern Launcher 1.8.3 / 1.8.4 · Windows PC VR**

Choose a server, invite friends, and manage players from inside the game.

**[Download setup](setup-v1.8.4/dist/TavernHubSetup.zip)** · **[Readable source](setup-v1.8.4/dist/TavernHubSource.zip)** · [Installation guide](setup-v1.8.4/README.md) · [Report a bug](https://github.com/wolftech07/Wolf-s-core-mods/issues)

## What it does

- Adds **Play Game** to Tavern Launcher so you can choose your server in VR.
- Shows community servers and your saved or private servers in the original server picker.
- Lets you add servers on the PC launcher; the open VR menu reloads them automatically.
- Uses the game's VR keyboard for passwords, with a Cancel button.
- Adds friends and invitations through friend codes, friendship cards, and the social tablet.
- Lets you view players, mute or block people, and use **Kick**, **Ban**, and **Unban** when your server role allows it.

## Install and play

You need **Tavern Launcher 1.8.3 or 1.8.4**, a compatible Windows copy of A Township Tale patched through Tavern Launcher, and **Mono MelonLoader**. Complete the launcher's normal **Patch** and **Mods** setup first.

1. Download and extract [TavernHubSetup.zip](setup-v1.8.4/dist/TavernHubSetup.zip).
2. Close the game and launcher, then open **TavernHubSetup.exe**.
3. Select your **A Township Tale.exe** and **TavernLauncher - Client.exe**.
4. Click **Install / Update** and wait for the log to confirm completion.
5. Open Tavern Launcher and click **Play Game**. Choose your server inside the game.

No commands or compiling required. The game, launcher, and MelonLoader are not included.

## Server hosts

For friendship cards and the full social tablet, stop your game server and choose **Install server support...** in the same setup EXE. Select the patched game-server folder, then restart the server. Both players need the current client mod for friendship features.

Tavern **moderators** and **owners** get the appropriate moderation controls. Friend codes work without the server companion. Your group does not need to run a separate friends relay app.

## Update or remove

Run the latest setup and choose **Install / Update**. Select your existing game and the launcher you want to use, even if the new launcher is in a different folder. Your settings, friends, and original game backups are kept. If upgrading Tavern Launcher too, apply its new patch before updating this mod.

To remove the integration, close the game and launcher and choose **Undo setup**. Hosts can use **Undo server support...**.

## Friends and private servers

On PC, open Tavern Launcher's **Saved & Recent Servers** panel, choose **Favourites → + Add Server**, enter the address and ports supplied by the host, then click **Add**. The server appears in VR **Tavern Favorites** and **Tavern Saved / Recent** on the next refresh, even if the game is already open. Choose **Tavern authentication** for an ordinary Tavern server; use **Direct game server** only if the host says it is a headless/direct server.

The in-game **Add a private server...** option also remains available. These bookmarks are local and are not submitted to the community dashboard. Add friends with a friend code, friendship card, or the social tablet; requests must be accepted. Only accepted friends appear in your friends list.

Both games must be online for invitations to arrive. Networking starts automatically and uses the public Tox network for discovery and connection assistance. Server passwords and access rules still apply.

## Source and antivirus reports

The setup ZIP includes **TavernHubSource.zip**: readable C#, Python, PowerShell, build scripts, and pinned native dependency sources. [Build instructions](setup-v1.8.4/SOURCE-AND-BUILD.md) explain the compatible game references you must provide. There is no source encryption or obfuscation.

The release is unsigned. A reported “crypto” warning has not yet been identified as a file detection or a blocked network address. Friends use encrypted Tox networking. If a warning appears, capture the detection name and affected file or URL/IP; see the [security guide](setup-v1.8.4/SECURITY.md). Component hashes are included for audit.

## Help

- **No Play Game button?** Restart the selected launcher and enable **Tavern In-Game Hub - Play Game** in Addons.
- **Tablet actions unavailable?** Ask the host to install the current server support.
- **Friends stay offline?** Keep both games open, allow time for discovery, and check the friends board's network status.
- **Friend card fails?** Both players need this mod and the host needs current server support. Hand the card directly to the other player. Friend codes work without server support; players without the mod cannot join your peer friends list.
- **Something failed?** Save the setup log or include relevant MelonLoader log lines in a bug report. Do not share account or identity files.

[Friends guide](setup-v1.8.4/MESH-FRIENDS.md) · [Tablet guide](setup-v1.8.4/SOCIAL-TABLET.md) · [Technical details and build instructions](setup-v1.8.4/ADVANCED.md)

**Testing status:** compiled and checked with automated tests; a two-player VR session still needs verification. This is a Windows PC mod, not a standalone Quest app. Launcher add-ons that require server-specific work before launch, such as custom-model syncing, are not integrated with this flow.
