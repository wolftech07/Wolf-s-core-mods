# Tavern In-Game Hub

**v2.2.0 · A Township Tale · Tavern Launcher 1.8.3 / 1.8.4 · Windows PC VR**

Choose a server, invite friends, and manage players from inside the game.

**[Download setup](setup-v1.8.4/dist/TavernHubSetup.zip)** · [Installation guide](setup-v1.8.4/README.md) · [Report a bug](https://github.com/wolftech07/Wolf-s-core-mods/issues)

## What it does

- Adds **Play Game** to Tavern Launcher so you can choose your server in VR.
- Shows community servers and your saved or private servers in the original server picker.
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

Run the latest setup and choose **Install / Update**. Use the same game and launcher folders; your settings, friends, and original backups are kept. If upgrading Tavern Launcher too, update it in its existing folder and apply its new patch before updating this mod.

To remove the integration, close the game and launcher and choose **Undo setup**. Hosts can use **Undo server support...**.

## Friends and private servers

Use **Add a private server...** to save a server without submitting it to the community dashboard. Add friends with a friend code, friendship card, or the social tablet; requests must be accepted. Only accepted friends appear in your friends list.

Both games must be online for invitations to arrive. Networking starts automatically and uses the public Tox network for discovery and connection assistance. Server passwords and access rules still apply.

## Help

- **No Play Game button?** Restart the selected launcher and enable **Tavern In-Game Hub - Play Game** in Addons.
- **Tablet actions unavailable?** Ask the host to install the current server support.
- **Friends stay offline?** Keep both games open, allow time for discovery, and check the friends board's network status.
- **Something failed?** Save the setup log or include relevant MelonLoader log lines in a bug report. Do not share account or identity files.

[Friends guide](setup-v1.8.4/MESH-FRIENDS.md) · [Tablet guide](setup-v1.8.4/SOCIAL-TABLET.md) · [Technical details and build instructions](setup-v1.8.4/ADVANCED.md)

**Testing status:** compiled and checked with automated tests; a two-player VR session still needs verification. This is a Windows PC mod, not a standalone Quest app. Launcher add-ons that require server-specific work before launch, such as custom-model syncing, are not integrated with this flow.
