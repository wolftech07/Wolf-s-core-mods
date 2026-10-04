# Read, audit, and build Tavern In-Game Hub

Download **TavernHubSource.zip** for the complete readable source of this mod. Its C#, Python, and PowerShell files are ordinary text. They are not encrypted or obfuscated; no decompiler is needed.

## What is included

| Folder | Contents |
| --- | --- |
| `mod-src/` | Client menu, joining, keyboard, private servers, friends, and social tablet code. |
| `payload/addons/tavern_native_menu/` | Tavern Launcher desktop add-on, including Play Game and server-bookmark integration. |
| `src/` | Windows setup interface and live log. |
| `payload/*.ps1` | Installation, update, backup, recovery, and server-support workers. |
| `mesh-peer/src/` | Separate friends-network helper, with its source and GPL license. |
| `mesh-server/src/` | Optional game-server companion for cards, tablet roster, and moderation. |
| `mesh-native/` | Native build scripts, pinned dependency archives, port patches, licenses, and public bootstrap snapshot. |
| `mod-assets/` | Badge PNG inputs and their provenance. |
| `tests/` and `mesh-server/tests/` | Automated checks and test harnesses. |
| `social-server/` | Retired relay code retained for inspection; it is not built or installed by this release. |

`SOURCE-SHA256SUMS.txt` lists every file in the source archive. The source release excludes compiled EXEs/DLLs, downloaded game and launcher files, decompiled third-party research, build caches, logs, saved identities, passwords, login tokens, and personal configuration. Compressed upstream dependency archives contain their original public source files.

The game and launcher are separate projects. Their implementation is not this mod's source; use your own compatible installation as compiler references. This source archive does not supply or decrypt the game.

## Build prerequisites

- **Windows x64**, Windows PowerShell **5.1**, and **.NET Framework 4.8**. The scripts use the framework's C# compiler with language version 5.
- **Visual Studio 2022 or 2026 / Build Tools** with **Desktop development with C++**, the x64 MSVC toolset, Windows SDK, CMake, and the bundled vcpkg component. These are required for the default native rebuild.
- Your compatible managed Windows **A Township Tale** installation, patched through **Tavern Launcher 1.8.4**, with Mono MelonLoader and its `MelonLoader\net472` assemblies. The project also supports the inspected 1.8.3 launcher files. Arbitrary newer game/launcher versions have not been verified.
- **Internet access** for vcpkg's pinned registry metadata and build tools on a fresh native build. The principal library source archives and patches are already included.
- Optional **64-bit Python 3** for Python tests and the native smoke test. Python is not needed to compile the setup or the C# components.

`<GameFolder>` below is the directory containing `A Township Tale.exe`, `A Township Tale_Data`, and `MelonLoader`. `<LauncherFolder>` contains the supported `TavernLauncher - Client.exe` and its `Patch` folder. Replace these placeholders with your own folders; do not create them literally.

`Build-Mod.ps1` and `mesh-server\Build-MeshServer.ps1` list the required reference assemblies. `payload\Setup-Worker.ps1` contains the inspected game, launcher, and TavernLib SHA256 allowlists. The build does not download those external projects.

## Build everything

Extract the source ZIP into a short folder such as `C:\TavernHubSource`. Short paths avoid upstream MSVC path-length limits; the native build reports a clear error if its folder path exceeds 90 characters. Open PowerShell in its `setup-v1.8.4` folder and run:

```powershell
.\Build-All.ps1 -GameFolder '<GameFolder>' -TavernLib '<LauncherFolder>\Patch\TavernLib.dll'
```

This rebuilds the native library, helper, server companion, client mod, and setup. It creates `dist\TavernHubSetup.exe`, `dist\TavernHubSetup.zip`, and `dist\TavernHubSource.zip`. It only creates build outputs in the extracted project; it does not install them into your game. Run the generated setup EXE separately to install.

The default uses the bundled public bootstrap snapshot. Add `-RefreshBootstrapSeeds` to fetch and validate a current snapshot. Add `-VisualStudioPath '<VisualStudioFolder>'` if automatic Visual Studio detection selects the wrong installation.

For a quick C#/Python-only development rebuild, `-NativeBundle '<NativeDistFolder>'` explicitly uses an existing native bundle. That folder must contain `libtoxcore.dll`, its public seed data, licenses, and `native-source.zip`; an official release installs these in `<GameFolder>\TavernNativeMenu\native`. **This optional mode does not rebuild or verify the native binary from source.** The source release itself contains no native binary; use the default build to rebuild it.

## Individual build steps

The same build is available as separate readable scripts:

```powershell
.\mesh-native\Build-Native.ps1
.\mesh-native\Package-Native.ps1
.\mesh-peer\Build-Peer.ps1 -GameFolder '<GameFolder>'
.\mesh-server\Build-MeshServer.ps1 -GamePath '<GameFolder>'
.\Build-Mod.ps1 -GameFolder '<GameFolder>' -TavernLib '<LauncherFolder>\Patch\TavernLib.dll'
.\Build-Setup.ps1
.\Package-Source.ps1
.\Package-Setup.ps1
```

When using individual steps from a fresh source archive, copy `mesh-native\bootstrap-nodes.json` to `mesh-native\dist\bootstrap-nodes.json` before `Build-Setup.ps1`, or run `mesh-native\Refresh-Seeds.ps1`. `Build-All.ps1` handles this automatically.

## Dependencies and licenses

| Dependency | Pinned input | License / source |
| --- | --- | --- |
| c-toxcore | **0.2.23**, SHA256 `15cdd006ed7793dfc657e340ef9f218f6637d2fe5b130704d39b961389bb6cd6` | GPL-3.0-or-later; original archive and license included. |
| libsodium | **1.0.22**, SHA256 `5838bb0c3da6148c24ebe531d1ed1297de9a87aea77d426bcd99f289e681631c` | ISC; original archive, license, and port patches included. |
| pthreads4w | **3.0.0**, SHA256 `b81136effb7185c77601fe2e0e6ac19bd996912e4814cebdd3010b0fac9e259b` | Apache-2.0; original archive, license, and port patches included. |
| vcpkg registry | Baseline `e03dc9b29710050cd1018bc5674688108658d327` | Registry metadata and build tools may be fetched during the build. |
| Newtonsoft.Json | Compatible game-supplied assembly | MIT; license included. The compiler copies your reference assembly for the helper. |
| Unity, Alta/Township, TavernLib, MelonLoader, Harmony | Compatible installation's reference assemblies | External dependencies; their binaries and decompiled sources are not redistributed here. |

The standalone helper is GPL-3.0-or-later. See `mesh-peer\LICENSE.txt` and `mesh-native\THIRD-PARTY-NOTICES.txt`; preserve license notices and corresponding-source archives when redistributing their binaries. Branding has separate ownership documented in `mod-assets\README.md`. Supplying readable source does not change the license of the game, launcher, or other unrelated files.

There is no miner, cryptocurrency wallet, or payment code in the friends helper. Its native cryptography dependency protects peer messages. Public bootstrap nodes are networking dependencies, not a shared friends database. Audit `mesh-peer\src`, `mesh-native\Build-Native.ps1`, and the supplied upstream archives to review this behavior.

## Checks you can run

After building, from `setup-v1.8.4`:

```powershell
.\tests\Check-SourcePackage.ps1
.\tests\Run-WorkerTests.ps1
.\tests\Run-ServerCardWorkerTests.ps1
.\tests\Check-TabletMuteLifecycle.ps1
.\mesh-server\tests\Run-TabletPolicyTests.ps1 -GamePath '<GameFolder>'
.\tests\Run-MeshPeerProofTests.ps1 -GameFolder '<GameFolder>'
.\tests\Run-MeshIntegrationTests.ps1 -GameFolder '<GameFolder>'
python .\tests\test_addon.py
python .\mesh-native\Native-Smoke.py
```

These checks do not replace a two-player VR session. Build outputs may differ between compiler/SDK versions or changing public bootstrap snapshots; byte-for-byte reproducibility has not been established. Compare supplied SHA256 checksums to identify a particular release, and inspect the readable inputs before building.
