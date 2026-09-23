"""Play Game integration for Tavern Launcher Client 1.8.3 and 1.8.4.

The launcher loads this module through its external add-on loader. Only the
primary launch action changes. Server authentication happens in the game after
selection, through TavernNativeMenu.dll and the normal Tavern auth service.
"""
import hashlib
import json
import os
import subprocess

from tkinter import messagebox
from client.core import launcher_window as launcher


SUPPORTED_ROOTS = frozenset((
    "06e1fc38f1b1a592d30dcce34fe26b608d5c56761e7a23b8e165c32c8063d735",
    "a4bd5661ef8176c644c3f0f66e32ec90c05f66c0c5a9575668a86196577db643",
))
SUPPORTED_TAVERN_LIBS = frozenset((
    "c2229c0b54d883e9502e2cacabe2ff539c8a0826ef47107d5f5381d200d4cc9e",
    "770b2ee118709b6472ca99869fed668d1d850db55916fb634fb9eef0a5da0498",
    "3c02046c9647821ea549f35a113d4f8f48a0130252b7efc8d83dae7a98bd6074",
    "b1260921ccae6f48ce688850402de19bcf6952ca75c1c073476ae03533586790",
))


def _digest(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _validate_game(exe):
    if not os.path.isfile(exe) or os.path.basename(exe).lower() != "a township tale.exe":
        raise ValueError("Select A Township Tale.exe in Tavern Launcher first.")
    folder = os.path.dirname(exe)
    required = (
        "version.dll",
        os.path.join("MelonLoader", "net472", "MelonLoader.dll"),
        os.path.join("Plugins", "TavernLib.dll"),
        os.path.join("Mods", "TavernNativeMenu.dll"),
        os.path.join("A Township Tale_Data", "Managed", "Root.Township.dll"),
    )
    for relative in required:
        if not os.path.isfile(os.path.join(folder, relative)):
            raise ValueError("Missing " + relative + ". Install the Tavern client patch and run Tavern In-Game Hub Setup for this game folder.")
    if _digest(os.path.join(folder, required[-1])) not in SUPPORTED_ROOTS:
        raise ValueError("This game build has not been checked for Tavern In-Game Hub. Use the compatible game patch or an updated mod.")
    if _digest(os.path.join(folder, "Plugins", "TavernLib.dll")) not in SUPPORTED_TAVERN_LIBS:
        raise ValueError("This TavernLib build has not been checked for Tavern In-Game Hub. Use the 1.8.4 client patch or an updated mod.")


def _platform(display):
    value = str(launcher.PLATFORM_DISPLAY_TO_BACKEND.get(display, display)).strip().lower()
    values = {"steamvr": "openvr", "openvr": "openvr", "quest": "oculus", "oculus": "oculus", "none": "fly", "fly": "fly"}
    if value not in values:
        raise ValueError("Choose SteamVR, Quest, or Fly in Tavern Launcher.")
    return values[value]


def _play_game(self):
    previous = getattr(self, "_native_menu_process", None)
    if previous is not None and previous.poll() is None:
        self._print("A Township Tale is already running from Play Game.", "warn")
        return
    if getattr(self, "_native_menu_launch_busy", False):
        return
    self._native_menu_launch_busy = True
    self._action_btn.config(state="disabled")
    try:
        raw_exe = self.v_exe.get().strip().strip('"')
        if not raw_exe:
            raise ValueError("Select A Township Tale.exe in Tavern Launcher first.")
        exe = os.path.abspath(os.path.expandvars(os.path.expanduser(raw_exe)))
        username = self.v_username.get().strip()
        if not username or len(username) > launcher.USERNAME_MAX_LEN or not launcher._is_valid_username(username):
            raise ValueError("Enter a username with 1-16 letters, numbers, spaces, hyphens, or underscores.")
        platform = _platform(self.v_platform.get())
        _validate_game(exe)
        # Save through the launcher so the in-game menu reads the same profile,
        # saved servers, and tokens. Detect a silent profile-write failure.
        self.v_exe.set(exe)
        self._save()
        with open(launcher.CONFIG_FILE, "r", encoding="utf-8") as stream:
            saved = json.load(stream)
        if str(saved.get("username", "")).strip() != username:
            raise ValueError("Tavern Launcher could not save this username. Check access to its settings folder before playing.")
        # This temporary menu identity does not authorize access to any server.
        access, refresh, identity = launcher.build_tokens(0, username, "")
        args = [exe, "/force_offline", "/access_token", access, "/refresh_token", refresh,
                "/identity_token", identity, "/tavern_native_menu"]
        if platform == "fly":
            args.append("/fly")
        else:
            args.extend(("/vrmode", platform))
        if self.v_debug_helper.get():
            args.append("/debug_helper")
        if not self.v_show_melonloader.get():
            args.append("--melonloader.hideconsole")
        environment = os.environ.copy()
        environment["TAVERN_NATIVE_MENU_LAUNCHER_CONFIG"] = os.path.abspath(launcher.CONFIG_FILE)
        self._print("Starting A Township Tale. Choose your server inside the game.", "warn")
        # List arguments and shell=False preserve spaces and special characters.
        # Never log tokens or the argument list.
        self._native_menu_process = subprocess.Popen(
            args, cwd=os.path.dirname(exe), env=environment,
            **self._popen_console_kwargs()
        )
        self._print("Game started (PID " + str(self._native_menu_process.pid) + "). The native menu will load Tavern's community server list.", "ok")
    except Exception as error:
        # No command or raw profile data is included in these messages.
        detail = str(error)
        self._print("Play Game could not start: " + detail, "err")
        messagebox.showerror("Tavern In-Game Hub", detail, parent=self)
    finally:
        self._native_menu_launch_busy = False
        self._action_btn.config(text="Play Game", state="normal", command=self._on_join_clicked)


def _install():
    cls = launcher.ClientLauncher
    if getattr(cls, "_tavern_native_menu_installed", False):
        return
    original_init = cls.__init__
    original_build_ui = cls._build_ui

    def build_ui(self, *args, **kwargs):
        result = original_build_ui(self, *args, **kwargs)
        self._action_btn.config(text="Play Game", command=self._on_join_clicked)
        return result

    def initialize(self, *args, **kwargs):
        original_init(self, *args, **kwargs)
        self._print("Tavern In-Game Hub enabled. Press Play Game, then select a server in the game.", "ok")

    cls._on_join_clicked = _play_game
    cls._build_ui = build_ui
    cls.__init__ = initialize
    cls._tavern_native_menu_installed = True


_install()
