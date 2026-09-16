# Native social relay and game server companion

The relay supplies actual private friend lists and live invitations. It does not create a public server listing. Everyone in a friend group uses the same relay URL. Native friend cards additionally need the companion installed on the game server where players exchange cards.

`TavernNativeSocialHost.exe` is a small Windows app with Start/Stop, live logs, **Test connection**, and an **Install / update server companion** button. Ordinary players use the client setup EXE instead and enter the relay URL with **Connect friends service...** on the game's friends board. They do not host a relay themselves.

## Host it on the same computer as your game server

1. Extract the social host package, keeping this guide beside `TavernNativeSocialHost.exe`.
2. Close your game server. Double-click `TavernNativeSocialHost.exe`.
3. Enter your **Public relay URL**. For testing entirely on this computer, use `http://127.0.0.1:1764`. Other computers cannot reach that address on your behalf: they need a reachable HTTPS domain.
4. Click **Install / update server companion**, then select the patched **game server folder**. This is the folder containing `MelonLoader`, `Mods`, `Plugins/TavernLib.dll`, and `A Township Tale_Data`; it is not the Tavern Launcher program folder. Setup copies the companion DLL and creates a separate trusted-server credential. Updates preserve that credential. Do not share the configuration or its token with players.
5. Click **Start relay**, then **Test connection**. Once the local check succeeds, start your game server with Tavern Launcher. Leave the host app open while people use social features.
6. Give friends the public relay URL. Each player connects it in the native menu's Friends settings before joining. On a server with the companion installed, pass the normal physical friend card to the other player. Both participants must have connected the same relay. A successful server-confirmed handoff saves the reciprocal friendship.
7. Select a friend in the menu, choose your saved private server, and send the invitation. Only that recipient receives the server address. Accepting uses the ordinary Tavern join and password checks; an invitation never grants a whitelist exemption or carries a saved password/token.

No game or server settings are published to a community dashboard by this social component. The relay operator can read stored names, friendships and invitations in their data folder, so use an operator you trust.

## Make the relay reachable over the internet

The app deliberately listens only on `127.0.0.1:<Local port>`. Public bearer credentials must be protected by HTTPS. Configure a reverse proxy on the same computer, with a valid certificate for your public domain, forwarding HTTPS requests to `http://127.0.0.1:1764`. Your router/firewall needs to route the proxy's HTTPS port to that computer. Do not forward port 1764 directly. There is no relay hosting, DNS configuration, certificate issuance or router access bundled into the client mod.

Use a proxy you already operate or a hosting provider's HTTPS proxy setup UI. A public URL such as `https://friends.example.org` goes in the host app and each player's Friends settings. The public URL must have no path, query string or embedded username/password. Test `/v1/health` through that URL before sharing it. A healthy response identifies `TavernNativeSocial` and version `1`.

For a group testing on one computer only, the loopback URL needs no proxy, administrator privileges, firewall changes or commands. Plain HTTP to another computer is intentionally rejected.

## A separate computer for the game server

The GUI installer configures the companion's private `relay_url` as the host app's loopback URL, suitable when both programs run on the same computer. For a separate game-server computer, install the companion into that server's patched game folder, then change **only** `relay_url` in `UserData/TavernNativeSocialServer.json` to the reachable HTTPS origin. `public_relay_url` is the same HTTPS origin announced to players. Keep the generated `server_id` and `server_token` intact and private. The social host's trusted-server database must remain on the relay computer.

The game server still requires its normal game/auth ports and Tavern setup. This relay is only the friends-and-invitations service.

## Presence, updates and backups

- A player is online only while their client sends heartbeats; they become offline after 60 seconds without a heartbeat. A running game server does not keep an absent player online. A relay outage shows social features as unavailable; it does not erase friends.
- The host saves data under `%LOCALAPPDATA%\TavernNativeSocialRelay\social-data.json`. The GUI has **Open data folder**. Stop the host before copying this folder to back it up or move it. Keep its `.bak` file as recovery data. User bearer credentials are hashed in the relay database; clients must preserve their own credentials to keep their identity.
- **Install / update server companion** replaces only `Mods/TavernNativeSocialServer.dll` and its own `UserData/TavernNativeSocialServer.json`, preserves an existing valid trusted-server credential, verifies written files, and restores prior contents if installation fails. Previous versions are retained beside those two files as `.previous` backups. It does not modify the launcher, world, players or other mods.
- To remove the companion, close the game server and remove its DLL from `Mods`. Keep the configuration as a backup if you plan to restore it. To stop the relay, click Stop or close the app. Player joins and private server bookmarks continue to use Tavern's normal behavior.
- Logs contain request routes and outcomes; they do not contain bearer tokens, tickets, passwords, invitation endpoints or complete HTTP payloads.

## If the relay shows an error

The host automatically writes `%LOCALAPPDATA%\TavernNativeSocialRelay\host.log`, including startup failures. The previous log is retained as `host.log.previous` after rotation. Use **Open data folder** to find it, or **Save log** to export the currently displayed output. Local folder names may appear in diagnostic errors; review logs before posting publicly. Do not send the social database or server/client credential files as a bug report.

- **Port already in use:** another relay or program is listening on the chosen port. Use the existing host window or choose a different Local port. The loopback test URL follows that change automatically. Update the HTTPS proxy's forwarding port and reinstall/update the server companion so it uses the new port.
- **Local connection test fails:** start the relay first and check its live log. The test expects a Tavern social health response from the selected local port.
- **Local succeeds but public fails:** check the public domain, HTTPS certificate and reverse-proxy forwarding. Redirects are rejected; enter the final HTTPS origin directly. A passing test on the host computer does not prove that another network can reach it.
- **Database cannot be read:** stop the host and restore `social-data.json` from your backup. Do not replace it with an empty file; that would lose identities and friendships. Only one host instance should use the data folder.
- **Card exchange is denied:** both players need valid social sessions on the same relay. Exchange cards again after connectivity returns. The companion requires both authenticated sides of the handoff; repeating one player's message does not add the other as a friend.

## Compatibility and verification limits

The companion targets the supplied Tavern game build and MelonLoader `net472`. TavernLib must be present on both server and clients because it enables the game's extended message numbers. Social networking uses message `34` only if no other mod has registered it on that connection; a collision is logged and social binding is skipped instead of replacing the other mod's handler.

The service and companion compile against the supplied managed assemblies. Automated tests cover authentication separation, one-use server-bound tickets, persistent reciprocal friendships, private recipient-only invitations, removing friends and loopback HTTP handling. Native VR card exchange and remote friend invitation flows still need testing with two real clients and an accessible relay.

## Build from source

Developers with the game assemblies and .NET Framework 4.x can run `Build-Social.ps1 -GamePath <patched-game-folder>`. The normal distributed host EXE already includes its compiled companion; end users do not compile anything.
