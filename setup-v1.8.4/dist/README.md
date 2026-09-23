# Tavern In-Game Hub

**Version 2.2.0 · Tavern Launcher 1.8.3 / 1.8.4 · Windows PC VR**

Choose servers and use friends, invitations, and the social tablet inside A Township Tale.

## Before you start

- Install Tavern Launcher Client **1.8.3 or 1.8.4**.
- Use its **Patch** and **Mods** controls to prepare your compatible Windows game and install Mono MelonLoader.
- Save your username, game path, and platform in Tavern Launcher.
- Setup requires **.NET Framework 4.8** and **Windows PowerShell 5.1**.

This package does not include the game, launcher, MelonLoader, or TavernLib.

## Install

1. Extract this ZIP and close the game and launcher.
2. Open **TavernHubSetup.exe**.
3. Select **A Township Tale.exe** from your game folder.
4. Select **TavernLauncher - Client.exe** from your launcher folder.
5. Click **Install / Update**. Wait for the live log to confirm completion.
6. Open Tavern Launcher and click **Play Game**. Select a server inside the game.

No commands or compiling required. Use **Check setup** to check compatibility or **Save log** to save an error report.

## Update an existing installation

Run the new setup EXE and choose **Install / Update**, using the same game and launcher folders. You do not need to uninstall first. Your settings, friends, saved servers, and original Undo backups are preserved.

If upgrading Tavern Launcher to 1.8.4, update it in its existing folder, keep its `addons` folder, and apply its updated client patch first. Close it before running this setup.

Previously called **Tavern Native Menu**, this release keeps the existing mod filenames and recovery folders so updates and Undo continue to work. Do not rename those installed files or folders.

## Server hosts

1. Stop the game server.
2. Open the same setup EXE and click **Install server support...**.
3. Select the patched game-server folder containing **A Township Tale.exe**.
4. Wait for completion, then start the server.

This enables friendship cards and full tablet support. Both players need the current client mod for friendship actions. Moderation permissions come from the server's Tavern **moderator** and **owner** roles.

## Using it

- **Servers:** browse community servers, or choose **Add a private server...** to save your own address. Password and whitelist rules still apply.
- **Friends:** share a friend code, exchange cards on a supported server, or select a player on the social tablet. The recipient must accept.
- **Invitations:** select a friend and invite them to a saved server. Both games must be online for delivery.
- **Tablet:** view players, mute or block them, and use moderation controls if your role allows it.

Networking starts automatically. There is no separate friends relay app to run; discovery and connection assistance use public Tox infrastructure. Only accepted friends appear in the friends list. If upgrading from the old relay version, add those friends once again.

## Remove or troubleshoot

Close the game and launcher, then choose **Undo setup**. Server hosts can use **Undo server support...**. Keep the original backup folders until you no longer need Undo.

- **Missing Play Game button:** restart the selected launcher and enable **Tavern In-Game Hub - Play Game** in Addons.
- **Missing tablet actions:** update the server companion using **Install server support...**.
- **Friends offline:** keep both games open, allow discovery time, and check network status on the friends board.
- **Setup error:** save the log and include the exact error in your bug report. Review personal paths before sharing logs; keep identity and credential files private.

## More information

[Friends and invitations](MESH-FRIENDS.md) · [Social tablet](SOCIAL-TABLET.md) · [Recovery, compatibility, and build details](ADVANCED.md)

**Testing status:** automated checks pass, but end-to-end two-player VR testing remains outstanding. This is not a standalone Quest port. Add-ons that need server-specific work before launch, such as custom-model syncing, are not integrated with the in-game picker.
