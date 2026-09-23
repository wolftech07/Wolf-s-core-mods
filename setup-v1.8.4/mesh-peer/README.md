# Automatic Tavern peer helper

This standalone Windows x64 program owns the peer identity and Tox networking.
The menu mod starts it with redirected standard input/output and closes its input
when the game exits. There is no window, listening control port, or user setup.

The helper and its source files are licensed under GPL-3.0-or-later. This license
applies to this helper, not to unrelated files in the repository. The helper
depends on c-toxcore (GPL-3.0-or-later) and Newtonsoft.Json (MIT); their license
and source notices accompany the distribution. No Unity or game assemblies are
linked into the helper. The mod communicates with it over a JSON process boundary.

Build with Windows .NET Framework 4.8:

```powershell
.\Build-Peer.ps1 -GameFolder '<game-folder>'
```

`<game-folder>` is the patched A Township Tale folder. Only its redistributable
Newtonsoft.Json library and netstandard reference are used during compilation.
Alternatively, use the matching upstream Newtonsoft.Json package and a framework
netstandard reference. No proprietary game code is included in this source archive.

Install the helper next to `libtoxcore.dll`, `Newtonsoft.Json.dll` and
`bootstrap-nodes.json` under `<game-folder>/TavernNativeMenu/native/`. The single
mod installer performs these steps automatically. Do not launch it manually.

The protocol starts with `--stdio-v2 <game-folder> <display-name>` using Windows argv quoting. Each line is one JSON object. Inputs are
bounded at 16 KiB; snapshots at 256 KiB on the game side. `state`, `paired` and
`reply` output messages contain public codes, accepted contacts, requests and
invitations. Private key savedata never crosses that boundary. Closing standard
input exits the process and saves the identity.

Private identity, contacts and queued invitations are saved atomically in
`UserData/TavernMesh.json`, with a `.bak` previous generation. Only one writer can
hold the profile lock. Keep these files private. `TavernFriendCode.txt` contains
only the public address and may be shared with a prospective friend.
