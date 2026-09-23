# Native peer dependency

The automatic Tavern peer helper uses c-toxcore **0.2.23**, built for Windows x64
with static libsodium **1.0.22**, pthreads4w **3.0.0** and the static MSVC runtime.
Audio/video, bootstrap daemons, standalone server programs and tests are disabled.
The client does not open a public TCP relay listener or enable LAN discovery.

## Rebuild

Install Visual Studio Desktop development with C++, CMake and vcpkg. Run
`Build-Native.ps1` in PowerShell. It verifies the pinned toxcore source SHA256
and uses `vcpkg.json`'s pinned baseline for its dependencies. `-SkipDependencies`
uses a completed local vcpkg install. All paths derive from this folder.

The corresponding-source archive includes original source archives and the
vcpkg port patches used for libsodium and pthreads. To rebuild without fetching
these source archives, copy them into `downloads` before running the build.
vcpkg may still fetch its build tools and the pinned registry metadata.
`SOURCE-SHA256SUMS.txt` records archive checksums. No library source was changed
outside those supplied port patches; build options are in `Build-Native.ps1`.

Run `Native-Smoke.py` with 64-bit Python to exercise two local native peers,
encrypted message callbacks, and identity save/restore. It does not contact the
public network. `Refresh-Seeds.ps1` fetches and validates the official bootstrap
snapshot. The packaged snapshot provenance is in `seed-provenance.json`.

`Package-Native.ps1` gathers license texts, source archives and port patches into
`dist`. The installer embeds and installs them next to the peer helper. Keep
these notices and corresponding source with redistributed binaries.

Sources: https://github.com/TokTok/c-toxcore/releases/tag/v0.2.23,
https://github.com/jedisct1/libsodium, https://sourceforge.net/projects/pthreads4w/,
https://github.com/microsoft/vcpkg, https://nodes.tox.chat/json.

The GPL applies to the native library and standalone helper. This project does
not change the license of unrelated mod, game or launcher code. No game binaries
or personal identities are included in the source archive.
