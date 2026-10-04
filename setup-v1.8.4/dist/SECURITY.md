# Security and antivirus reports

Tavern In-Game Hub has no cryptocurrency wallet, mining feature, paid token, or investment feature. The Tox/libsodium dependency uses cryptography to protect friends networking. The readable sources and dependency archives are supplied in **TavernHubSource.zip**; see [SOURCE-AND-BUILD.md](SOURCE-AND-BUILD.md).

## What runs

- **TavernHubSetup.exe** extracts its bundled files into its own temporary folder and invokes the readable PowerShell installation worker. Version 2.3.0 uses **RemoteSigned**, replacing the previous blanket `Bypass` policy. Windows policy can still prevent installation; report that error rather than disabling policy.
- **TavernNativeMenu.dll** is loaded through MelonLoader inside the game.
- **TavernMeshPeer.exe** is the friends helper. It starts with the game, exchanges JSON over local standard input/output, and stops when the game closes. It is not installed as an automatically starting Windows service or scheduled task.
- **libtoxcore.dll** provides encrypted peer networking. Public bootstrap nodes, DHT peers and TCP helpers assist discovery and delivery. A connection can involve public IPs beyond your friends' computers.
- The game also contacts the Tavern community directory and the game server you choose. It uses the server's normal authentication and access rules.

Setup does not add antivirus exclusions or change antivirus settings. Installation changes and original-file backups are recorded for update, rollback and Undo. Private peer keys stay on your computer; account tokens are used for normal server authentication. Neither is included in the source release.

## If Malwarebytes reports something

Capture the **detection name**, **file path or blocked URL/IP**, **product/database version**, and whether it occurred during installation, a file scan, or an outbound connection. These details distinguish a file detection from a blocked public network endpoint. Include the affected file's SHA256 from `SHA256SUMS.txt` or `COMPONENTS.json` when available.

The current files are unsigned. Self-extracting installers, executable helpers and a new publisher's reputation can contribute to warnings, but the exact cause of a Malwarebytes report has not been confirmed. A public endpoint can also be blocked independently of the downloaded program. A clean scan from a different product does not establish that Malwarebytes has cleared the release.

Export the report using [Malwarebytes' report instructions](https://help.malwarebytes.com/hc/en-us/articles/31589573227035-View-and-download-scan-reports-in-Malwarebytes-for-Windows-and-Mac). If review confirms a false positive, use [Malwarebytes' official review process](https://help.malwarebytes.com/hc/en-us/articles/31589211404571-Report-a-false-positive-to-Malwarebytes-Support). No sample or report is uploaded automatically by this project. Review personal information before sharing a report.

Trusted publisher signing can help establish release identity and reputation; it does not guarantee that a new build has no warnings. See [Microsoft's signing and reputation guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation). This release does not claim to be signed or certified by an antivirus vendor.

## Optional friends-network opt-out

Close the game. In `<GameFolder>\UserData\TavernNativeMenu.json`, set the existing `EnableFriendsNetworking` setting to `false` (or add `"EnableFriendsNetworking": false` as a JSON property). Keep the rest of the file and its server bookmarks. Start the game again.

Server selection, PC bookmarks, joining and server-supported tablet moderation remain available. Online friend requests, presence, friendship cards and invitations are unavailable while the peer network is disabled. Existing friends and private identity files are preserved. Set the property back to `true` to reconnect. This controls this mod's network use; it does not disable protection or suppress warnings.

## Release audit

`COMPONENTS.json` lists the setup and each embedded payload file's SHA256 and source location. `SHA256SUMS.txt` identifies the downloadable artifacts. Plain C#, Python and PowerShell source is included; the project does not use an executable packer, source obfuscator or encrypted source archive.

Build and behavior checks are documented in [ADVANCED.md](ADVANCED.md). Public peer-network behavior and encryption are described by the [Tox project](https://tox.chat/faq.html). Automated checks and code review have practical limits; report the precise detection rather than assuming every antivirus warning is a false positive.
