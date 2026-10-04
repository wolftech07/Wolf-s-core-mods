"""Play Game integration for Tavern Launcher Client 1.8.3 and 1.8.4.

The launcher loads this module through its external add-on loader. Play Game
opens the VR picker; Saved & Recent Servers can add private destinations to it.
Server authentication happens in the game after selection, through
TavernNativeMenu.dll and the normal Tavern auth service.
"""
import hashlib
import ipaddress
import json
import os
import re
import subprocess
import tempfile

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


def _server_port(value, fallback, description):
    text = str(value if value is not None else "").strip()
    if not text:
        return fallback
    if not re.fullmatch(r"[0-9]{1,5}", text) or not 1 <= int(text) <= 65535:
        raise ValueError(description + " must be a number from 1 to 65535.")
    return int(text)


def _server_host(value):
    host = str(value or "").strip().rstrip(".")
    if not host or any(c.isspace() for c in host) or any(c in host for c in "/\\:@?#"):
        raise ValueError("Enter an IPv4 address or hostname, without a URL or port.")
    try:
        return str(ipaddress.IPv4Address(host))
    except ValueError:
        # Do not accept a mistyped numeric IPv4 address as a DNS name.
        if re.fullmatch(r"[0-9.]+", host):
            raise ValueError("Enter a valid IPv4 address.")
    try:
        host = host.encode("idna").decode("ascii").lower()
    except UnicodeError:
        raise ValueError("Enter a valid hostname.")
    labels = host.split(".")
    if len(host) > 253 or any(not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?", label) for label in labels):
        raise ValueError("Enter a valid hostname.")
    return host


def _server_entry(name, host, game_port="1757", auth_port="1762", kind="official"):
    host = _server_host(host)
    name = str(name or "").strip() or host
    if len(name) > 100 or any(ord(c) < 32 for c in name):
        raise ValueError("Use a server name of 100 characters or fewer, without control characters.")
    if kind not in ("official", "headless"):
        raise ValueError("Choose Tavern authentication or a direct game server.")
    return {"name": name, "ip": host,
            "port": _server_port(game_port, 1757, "Game port"),
            "auth_port": _server_port(auth_port, 1762, "Authentication port"),
            "kind": kind, "private": True}


def _endpoint(entry):
    # Older Add IP dialogs accepted arbitrary port text. Use the game's import
    # defaults for those records so people can still view, replace, or remove
    # a bad old entry. Newly submitted entries use strict _server_entry checks.
    ports = []
    for key, fallback in (("port", 1757), ("auth_port", 1762)):
        try:
            ports.append(_server_port(entry.get(key), fallback, "Port"))
        except ValueError:
            ports.append(fallback)
    return (str(entry.get("ip", "")).strip().rstrip(".").lower(),
            ports[0], ports[1])


def _read_profile():
    try:
        with open(launcher.CONFIG_FILE, "r", encoding="utf-8-sig") as stream:
            data = json.load(stream)
    except FileNotFoundError:
        return {}
    except (ValueError, UnicodeError):
        raise ValueError("The launcher settings file cannot be read. Repair or restore it before changing saved servers.")
    if not isinstance(data, dict):
        raise ValueError("The launcher settings file must contain an object. No settings were changed.")
    return data


def _write_profile(data):
    # Replace in the same folder so the game reads either the complete old
    # profile or the complete new one. Never rewrite separate identity tokens.
    folder = os.path.dirname(os.path.abspath(launcher.CONFIG_FILE))
    os.makedirs(folder, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=folder,
                                         prefix=".tavern-servers-", suffix=".tmp", delete=False) as stream:
            temporary = stream.name
            # ASCII escapes also support the launcher's default-encoding reader.
            json.dump(data, stream, indent=2, ensure_ascii=True)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, launcher.CONFIG_FILE)
        temporary = None
    finally:
        if temporary is not None:
            try:
                os.remove(temporary)
            except OSError:
                pass


def _profile_servers(data, key):
    entries = data.get(key, [])
    if not isinstance(entries, list) or any(not isinstance(entry, dict) for entry in entries):
        raise ValueError("The " + key.replace("_", " ") + " list is invalid. No settings were changed.")
    return entries


def _save_server(entry):
    data = _read_profile()
    saved = _profile_servers(data, "saved_servers")
    key = _endpoint(entry)
    updated = False
    for index, old in enumerate(saved):
        if _endpoint(old) == key:
            merged = dict(old)
            merged.update(entry)
            saved[index] = merged
            updated = True
            break
    if not updated:
        saved.append(dict(entry))
    data["saved_servers"] = saved
    _write_profile(data)
    return updated


def _remove_server(entry):
    data = _read_profile()
    key = _endpoint(entry)
    data["saved_servers"] = [old for old in _profile_servers(data, "saved_servers") if _endpoint(old) != key]
    _write_profile(data)


def _refresh_server_rows(panel, key):
    tree = panel.fav_tree if key == "saved_servers" else panel.rec_tree
    rows = {}
    # Read before clearing the rows: a failed read leaves the current view intact.
    entries = _profile_servers(_read_profile(), key)
    for item in tree.get_children():
        tree.delete(item)
    for entry in entries:
        host, game_port, _ = _endpoint(entry)
        if not host:
            continue
        item = tree.insert("", "end", values=(entry.get("name") or host, host + ":" + str(game_port)))
        rows[item] = dict(entry)
    if key == "saved_servers":
        panel._native_saved_rows = rows
    else:
        panel._native_recent_rows = rows


def _add_server_dialog(panel):
    from client.core import server_list_panel as ui
    window = ui.tk.Toplevel(panel)
    ui._start_hidden(window)
    window.title("Add Server to VR")
    window.configure(bg=ui.BG)
    window.resizable(False, False)
    window.transient(panel)
    ui.tk.Label(window, text="Save a server on this PC and use it in the VR picker.",
                bg=ui.BG, fg=ui.PARCH, wraplength=390, justify="left").grid(
                    row=0, column=0, columnspan=2, sticky="w", padx=16, pady=(16, 12))
    fields = {}
    for row, (key, label, default) in enumerate((
            ("name", "Server name", ""), ("host", "IP address or hostname", ""),
            ("port", "Game port", "1757"), ("auth_port", "Authentication port", "1762")), start=1):
        ui.tk.Label(window, text=label, bg=ui.BG, fg=ui.PARCH).grid(row=row, column=0, sticky="w", padx=16, pady=6)
        fields[key] = ui.tk.StringVar(value=default)
        widget = ui.tk.Entry(window, textvariable=fields[key], bg=ui.SURF, fg=ui.PARCH,
                             insertbackground=ui.PARCH, width=30)
        widget.grid(row=row, column=1, sticky="ew", padx=(0, 16), pady=6)
        if key == "host":
            address_widget = widget
    ui.tk.Label(window, text="Server type", bg=ui.BG, fg=ui.PARCH).grid(row=5, column=0, sticky="w", padx=16, pady=6)
    fields["kind"] = ui.tk.StringVar(value="Tavern authentication")
    ui.ttk.Combobox(window, textvariable=fields["kind"], state="readonly", width=28,
                    values=("Tavern authentication", "Direct game server")).grid(
                        row=5, column=1, sticky="ew", padx=(0, 16), pady=6)
    ui.tk.Label(window, text="Use the ports supplied by the server owner. This does not publish the server.",
                bg=ui.BG, fg=ui.MUTED, wraplength=390, justify="left").grid(
                    row=6, column=0, columnspan=2, sticky="w", padx=16, pady=(8, 10))
    buttons = ui.tk.Frame(window, bg=ui.BG)
    buttons.grid(row=7, column=0, columnspan=2, sticky="e", padx=16, pady=(0, 16))

    def add():
        try:
            entry = _server_entry(fields["name"].get(), fields["host"].get(),
                                  fields["port"].get(), fields["auth_port"].get(),
                                  "headless" if fields["kind"].get() == "Direct game server" else "official")
            _save_server(entry)
        except Exception as error:
            messagebox.showerror("Could not save server", str(error), parent=window)
            return
        window.destroy()
        _refresh_server_rows(panel, "saved_servers")
        messagebox.showinfo("Server saved", "Saved. Favorites / Saved refresh shortly while the VR menu is open.", parent=panel)

    ui._btn(buttons, "Cancel", window.destroy, style="normal").pack(side="left", padx=(0, 8))
    ui._btn(buttons, "Add", add, "primary").pack(side="left")
    window.bind("<Return>", lambda event: add())
    window.bind("<Escape>", lambda event: window.destroy())
    window.protocol("WM_DELETE_WINDOW", window.destroy)
    ui._finish_dark_window(window)
    address_widget.focus_set()
    window.grab_set()


def _build_server_tab(panel, parent, key):
    from client.core import server_list_panel as ui
    frame = ui.tk.Frame(parent, bg=ui.BG)
    frame.pack(fill="both", expand=True, padx=8, pady=(8, 4))
    tree = ui._mk_tree(frame, ("label", "address"), [230, 210], height=9)
    if key == "saved_servers":
        panel.fav_tree = tree
    else:
        panel.rec_tree = tree
    try:
        _refresh_server_rows(panel, key)
    except Exception as error:
        messagebox.showerror("Could not read servers", str(error), parent=panel)
    buttons = ui.tk.Frame(parent, bg=ui.BG)
    buttons.pack(fill="x", padx=8, pady=(0, 8))

    def selected():
        selection = tree.selection()
        rows = getattr(panel, "_native_saved_rows" if key == "saved_servers" else "_native_recent_rows", {})
        return rows.get(selection[0]) if selection else None

    def choose():
        entry = selected()
        if entry is None:
            return
        panel._on_select(entry["ip"], entry.get("name") or entry["ip"], _endpoint(entry)[1])
        panel.destroy()

    def remove():
        entry = selected()
        if entry is None:
            return
        try:
            _remove_server(entry)
            _refresh_server_rows(panel, key)
        except Exception as error:
            messagebox.showerror("Could not remove server", str(error), parent=panel)

    def favorite():
        entry = selected()
        if entry is None:
            return
        try:
            _save_server(entry)
            _refresh_server_rows(panel, "saved_servers")
            messagebox.showinfo("Server saved", "Saved. Favorites / Saved refresh shortly while the VR menu is open.", parent=panel)
        except Exception as error:
            messagebox.showerror("Could not save server", str(error), parent=panel)

    ui._btn(buttons, "Select", choose, "primary", font=("Georgia", 10, "bold"), pady=6, padx=14).pack(side="left")
    if key == "saved_servers":
        ui._btn(buttons, "+ Add Server", lambda: _add_server_dialog(panel), style="normal",
                font=("Segoe UI", 9), pady=6, padx=10).pack(side="left", padx=6)
        ui._btn(buttons, "Remove", remove, "danger", font=("Segoe UI", 9), pady=6, padx=10).pack(side="left")
    else:
        ui._btn(buttons, "Save as Favourite", favorite, style="normal",
                font=("Segoe UI", 9), pady=6, padx=10).pack(side="left", padx=6)


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
    # Enhance the launcher's existing server panel rather than maintaining a
    # second list. The original window and main Saved Servers button remain.
    panel = launcher.ServerListPanel
    panel._build_fav_tab = lambda self, parent: _build_server_tab(self, parent, "saved_servers")
    panel._build_recent_tab = lambda self, parent: _build_server_tab(self, parent, "recent_servers")
    cls._tavern_native_menu_installed = True


_install()
