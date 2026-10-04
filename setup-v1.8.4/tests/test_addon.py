"""Isolated tests: no real game, launcher, settings, network, or GUI are opened."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import Mock, patch


class Var:
    def __init__(self, value):
        self.value = value

    def get(self):
        return self.value

    def set(self, value):
        self.value = value


class FakeWindow:
    def __init__(self):
        self.v_exe = Var("")
        self.v_username = Var("Menu User")
        self.v_platform = Var("SteamVR")
        self.v_debug_helper = Var(False)
        self.v_show_melonloader = Var(True)
        self.messages = []
        self._build_ui()

    def _build_ui(self):
        self._action_btn = Mock()

    def _on_join_clicked(self):
        raise AssertionError("Original prelaunch join must not run")

    def _print(self, message, tag=""):
        self.messages.append((message, tag))

    def _save(self):
        Path(fake_launcher.CONFIG_FILE).write_text(json.dumps({"username": self.v_username.get(), "game_exe": self.v_exe.get()}), encoding="utf-8")

    def _popen_console_kwargs(self):
        return {} if self.v_show_melonloader.get() else {"stdout": -3, "stderr": -3}


class FakeTree:
    def __init__(self):
        self.rows = {}
        self.selected = []
        self.serial = 0

    def get_children(self):
        return list(self.rows)

    def delete(self, item):
        del self.rows[item]

    def insert(self, parent, position, values):
        self.serial += 1
        item = str(self.serial)
        self.rows[item] = values
        return item

    def selection(self):
        return self.selected


class FakePanel:
    def __init__(self):
        self._on_select = Mock()
        self.destroy = Mock()


fake_launcher = types.ModuleType("client.core.launcher_window")
fake_launcher.ClientLauncher = FakeWindow
fake_launcher.ServerListPanel = FakePanel
fake_launcher.PLATFORM_DISPLAY_TO_BACKEND = {"SteamVR": "openvr", "Quest": "oculus", "Fly": "fly"}
fake_launcher.USERNAME_MAX_LEN = 16
fake_launcher._is_valid_username = lambda value: all(c in " -_" or c.isascii() and c.isalnum() for c in value)
fake_launcher.build_tokens = Mock(return_value=("ACCESS_SECRET", "REFRESH_SECRET", "IDENTITY_SECRET"))
fake_core = types.ModuleType("client.core")
fake_core.launcher_window = fake_launcher
fake_ui = types.ModuleType("client.core.server_list_panel")
fake_ui.tk = types.SimpleNamespace(Frame=Mock(), Toplevel=Mock(), Label=Mock(), Entry=Mock(), StringVar=Var)
fake_ui.ttk = types.SimpleNamespace(Combobox=Mock())
fake_ui._mk_tree = lambda *args, **kwargs: FakeTree()
fake_ui._start_hidden = Mock()
fake_ui._finish_dark_window = Mock()
for color in ("BG", "SURF", "PARCH", "MUTED"):
    setattr(fake_ui, color, "#ffffff")
fake_core.server_list_panel = fake_ui
fake_client = types.ModuleType("client")
fake_client.core = fake_core
fake_tk = types.ModuleType("tkinter")
fake_tk.messagebox = Mock()
sys.modules.update({"client": fake_client, "client.core": fake_core, "client.core.launcher_window": fake_launcher,
                    "client.core.server_list_panel": fake_ui, "tkinter": fake_tk})
source = Path(__file__).resolve().parents[1] / "payload" / "addons" / "tavern_native_menu" / "client.py"
spec = importlib.util.spec_from_file_location("tavern_menu_addon_test", source)
addon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(addon)


class AddonTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="menu addon ' & [test] ")
        self.folder = Path(self.temporary.name) / "caf\u00e9"
        self.folder.mkdir()
        fake_launcher.CONFIG_FILE = str(self.folder / "profile.json")
        fake_tk.messagebox.reset_mock()
        fake_launcher.build_tokens.reset_mock()
        self.buttons = {}

        def button(parent, label, callback, *args, **kwargs):
            self.buttons[label] = callback
            return Mock()

        fake_ui._btn = button
        self.window = FakeWindow()
        self.exe = self.folder / "A Township Tale.exe"
        self.exe.write_bytes(b"fixture")
        self.window.v_exe.set(str(self.exe))
        for relative in ("version.dll", "MelonLoader/net472/MelonLoader.dll", "Plugins/TavernLib.dll", "Mods/TavernNativeMenu.dll", "A Township Tale_Data/Managed/Root.Township.dll"):
            path = self.folder / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture")
        self.digest = patch.object(addon, "_digest", side_effect=lambda path: next(iter(addon.SUPPORTED_TAVERN_LIBS if str(path).endswith("TavernLib.dll") else addon.SUPPORTED_ROOTS)))
        self.digest.start()
        self.launch = patch.object(addon.subprocess, "Popen")
        self.popen = self.launch.start()
        self.popen.return_value.pid = 123
        self.popen.return_value.poll.return_value = None

    def tearDown(self):
        self.launch.stop()
        self.digest.stop()
        self.temporary.cleanup()

    def test_primary_button_and_idempotent_import(self):
        callback = FakeWindow._on_join_clicked
        initialize = FakeWindow.__init__
        addon._install()
        self.assertIs(FakeWindow._on_join_clicked, callback)
        self.assertIs(FakeWindow.__init__, initialize)
        self.window._action_btn.config.assert_called_with(text="Play Game", command=self.window._on_join_clicked)
        self.assertIn("Tavern In-Game Hub enabled", self.window.messages[0][0])

    def test_launch_neutral_menu_with_profile_and_safe_arguments(self):
        self.window._on_join_clicked()
        fake_tk.messagebox.showerror.assert_not_called()
        fake_launcher.build_tokens.assert_called_once_with(0, "Menu User", "")
        args = self.popen.call_args.args[0]
        kwargs = self.popen.call_args.kwargs
        self.assertEqual(args[0], str(self.exe))
        self.assertIn("/tavern_native_menu", args)
        self.assertIn("/force_offline", args)
        self.assertEqual(args[args.index("/vrmode") + 1], "openvr")
        for forbidden in ("/join_local_server", "/dev_server_ip", "/dev_server_port", "/questScene"):
            self.assertNotIn(forbidden, args)
        self.assertEqual(kwargs["env"]["TAVERN_NATIVE_MENU_LAUNCHER_CONFIG"], fake_launcher.CONFIG_FILE)
        self.assertNotIn("shell", kwargs)
        self.assertEqual(kwargs["cwd"], str(self.folder))
        self.assertTrue(Path(fake_launcher.CONFIG_FILE).is_file())
        for text, tag in self.window.messages:
            self.assertNotIn("SECRET", text)
        self.window._action_btn.config.assert_called_with(text="Play Game", state="normal", command=self.window._on_join_clicked)

    def test_fly_and_quest_and_legacy_platforms(self):
        for display, backend in (("Fly", "fly"), ("none", "fly"), ("Quest", "oculus"), ("Oculus", "oculus"), ("OpenVR", "openvr")):
            with self.subTest(platform=display):
                self.window._native_menu_process = None
                self.window.v_platform.set(display)
                self.window._on_join_clicked()
                args = self.popen.call_args.args[0]
                if backend == "fly":
                    self.assertIn("/fly", args)
                    self.assertNotIn("/vrmode", args)
                else:
                    self.assertEqual(args[args.index("/vrmode") + 1], backend)

    def test_debug_and_hidden_console_preferences(self):
        self.window.v_debug_helper.set(True)
        self.window.v_show_melonloader.set(False)
        self.window._on_join_clicked()
        self.assertIn("/debug_helper", self.popen.call_args.args[0])
        self.assertIn("--melonloader.hideconsole", self.popen.call_args.args[0])
        self.assertEqual(self.popen.call_args.kwargs["stdout"], -3)

    def test_duplicate_running_game_and_busy_clicks(self):
        self.window._on_join_clicked()
        self.window._on_join_clicked()
        self.popen.assert_called_once()
        self.window._native_menu_process = None
        self.window._native_menu_launch_busy = True
        self.window._on_join_clicked()
        self.popen.assert_called_once()

    def test_invalid_username_and_platform(self):
        for name in ("", "x" * 17, "emoji\U0001f600", "bad/name"):
            self.window.v_username.set(name)
            self.window._on_join_clicked()
        self.window.v_username.set("Valid")
        self.window.v_platform.set("invalid")
        self.window._on_join_clicked()
        self.popen.assert_not_called()
        self.assertEqual(fake_tk.messagebox.showerror.call_count, 5)

    def test_missing_dependency_and_wrong_build(self):
        mod = self.folder / "Mods" / "TavernNativeMenu.dll"
        mod.unlink()
        self.window._on_join_clicked()
        self.popen.assert_not_called()
        mod.write_bytes(b"fixture")
        with patch.object(addon, "_digest", return_value="unknown"):
            self.window._on_join_clicked()
        self.popen.assert_not_called()
        self.assertEqual(fake_tk.messagebox.showerror.call_count, 2)

    def test_silent_save_failure_is_blocked(self):
        Path(fake_launcher.CONFIG_FILE).write_text('{"username":"PreviousUser"}', encoding="utf-8")
        with patch.object(self.window, "_save"):
            self.window._on_join_clicked()
        self.popen.assert_not_called()
        self.assertIn("could not save", fake_tk.messagebox.showerror.call_args.args[1])

    def test_failed_process_restores_button(self):
        self.popen.side_effect = OSError("Fixture launch failure")
        self.window._on_join_clicked()
        self.assertFalse(self.window._native_menu_launch_busy)
        self.window._action_btn.config.assert_called_with(text="Play Game", state="normal", command=self.window._on_join_clicked)
        self.assertIn("Fixture launch failure", fake_tk.messagebox.showerror.call_args.args[1])

    def test_manual_server_schema_is_private_and_accepts_custom_ports(self):
        entry = addon._server_entry("  My Tavern  ", "EXAMPLE.com.", "1777", "1888")
        self.assertEqual(entry, {"name": "My Tavern", "ip": "example.com", "port": 1777,
                                 "auth_port": 1888, "kind": "official", "private": True})
        direct = addon._server_entry("", "127.0.0.1", "", "", "headless")
        self.assertEqual(direct["name"], "127.0.0.1")
        self.assertEqual(direct["kind"], "headless")
        self.assertEqual(direct["port"], 1757)
        self.assertEqual(direct["auth_port"], 1762)
        self.assertEqual(addon._server_host("t\u00e4vern.example"), "xn--tvern-gra.example")

    def test_rejects_invalid_address_ports_and_names(self):
        for host in ("", "http://example.com", "example.com:1762", "999.1.1.1", "a b.com", "a\\b", "-bad.com", "a..b", "::1"):
            with self.subTest(host=host), self.assertRaises(ValueError):
                addon._server_entry("Server", host)
        for port in ("0", "65536", "-1", "1757.0", "bad", "\uff11\uff17\uff15\uff17"):
            with self.subTest(port=port), self.assertRaises(ValueError):
                addon._server_entry("Server", "example.com", port)
        for name in ("x" * 101, "bad\nname"):
            with self.assertRaises(ValueError):
                addon._server_entry(name, "example.com")

    def test_save_preserves_settings_tokens_and_unicode(self):
        profile = {"username": "Menu User", "custom_option": {"opaque": "caf\u00e9"},
                   "saved_servers": [{"name": "Old", "ip": "example.com", "port": "1757", "unknown": "keep"}],
                   "recent_servers": [{"name": "Recent", "ip": "other.example"}]}
        path = Path(fake_launcher.CONFIG_FILE)
        path.write_text(json.dumps(profile), encoding="utf-8")
        tokens = self.folder / "tokens" / ".token_example__menu.json"
        tokens.parent.mkdir()
        tokens.write_bytes(b"DO_NOT_REWRITE_TOKEN")
        self.assertTrue(addon._save_server(addon._server_entry("New \u00e9", "EXAMPLE.com")))
        saved = json.loads(path.read_text(encoding="ascii"))
        self.assertEqual(saved["custom_option"], profile["custom_option"])
        self.assertEqual(saved["recent_servers"], profile["recent_servers"])
        self.assertEqual(saved["username"], profile["username"])
        self.assertEqual(saved["saved_servers"][0]["unknown"], "keep")
        self.assertEqual(saved["saved_servers"][0]["name"], "New \u00e9")
        self.assertEqual(tokens.read_bytes(), b"DO_NOT_REWRITE_TOKEN")

    def test_same_host_different_game_or_auth_ports_remain_independent(self):
        entries = [addon._server_entry("One", "example.com", 1757, 1762),
                   addon._server_entry("Two", "example.com", 1758, 1762),
                   addon._server_entry("Three", "example.com", 1757, 1763)]
        for entry in entries:
            self.assertFalse(addon._save_server(entry))
        addon._remove_server(entries[1])
        saved = addon._read_profile()["saved_servers"]
        self.assertEqual([entry["name"] for entry in saved], ["One", "Three"])

    def test_legacy_invalid_port_can_be_removed_without_losing_other_settings(self):
        addon._write_profile({"saved_servers": [{"name": "Old invalid", "ip": "example.com", "port": "bad"}],
                              "username": "KeepUser"})
        panel = FakePanel()
        panel._build_fav_tab(Mock())
        self.assertEqual(list(panel.fav_tree.rows.values()), [("Old invalid", "example.com:1757")])
        panel.fav_tree.selected = list(panel.fav_tree.rows)
        self.buttons["Remove"]()
        self.assertEqual(addon._read_profile(), {"saved_servers": [], "username": "KeepUser"})

    def test_corrupt_profile_is_not_replaced(self):
        path = Path(fake_launcher.CONFIG_FILE)
        for content in ('{"username":', '[1,2]', '{"saved_servers":{}}'):
            path.write_text(content, encoding="utf-8")
            with self.assertRaises(ValueError):
                addon._save_server(addon._server_entry("New", "example.com"))
            self.assertEqual(path.read_text(encoding="utf-8"), content)

    def test_atomic_save_failure_keeps_original_profile_and_cleans_temporary(self):
        path = Path(fake_launcher.CONFIG_FILE)
        original = '{"username":"Original","saved_servers":[]}'
        path.write_text(original, encoding="utf-8")
        with patch.object(addon.os, "replace", side_effect=PermissionError("Settings file is locked")):
            with self.assertRaises(PermissionError):
                addon._save_server(addon._server_entry("New", "example.com"))
        self.assertEqual(path.read_text(encoding="utf-8"), original)
        self.assertEqual(list(self.folder.glob(".tavern-servers-*.tmp")), [])

    def test_existing_panel_add_remove_and_select_exact_server(self):
        entries = [addon._server_entry("One", "example.com", 1757),
                   addon._server_entry("Two", "example.com", 1777)]
        for entry in entries:
            addon._save_server(entry)
        panel = FakePanel()
        panel._build_fav_tab(Mock())
        self.assertEqual(list(panel.fav_tree.rows.values()), [("One", "example.com:1757"), ("Two", "example.com:1777")])
        with patch.object(addon, "_add_server_dialog") as dialog:
            self.buttons["+ Add Server"]()
            dialog.assert_called_once_with(panel)
        panel.fav_tree.selected = [list(panel.fav_tree.rows)[1]]
        self.buttons["Select"]()
        panel._on_select.assert_called_once_with("example.com", "Two", 1777)
        panel.destroy.assert_called_once()
        self.buttons["Remove"]()
        self.assertEqual([entry["name"] for entry in addon._read_profile()["saved_servers"]], ["One"])

    def test_recent_favorite_preserves_connection_metadata(self):
        entry = addon._server_entry("Direct", "example.com", 1777, 1888, "headless")
        addon._write_profile({"recent_servers": [entry]})
        panel = FakePanel()
        panel._build_fav_tab(Mock())
        panel._build_recent_tab(Mock())
        panel.rec_tree.selected = list(panel.rec_tree.rows)
        self.buttons["Save as Favourite"]()
        self.assertEqual(addon._read_profile()["saved_servers"], [entry])
        self.assertEqual(len(panel.fav_tree.rows), 1)
        self.assertIn("refresh shortly", fake_tk.messagebox.showinfo.call_args.args[1])

    def test_failed_panel_remove_shows_error_without_deleting_row(self):
        addon._save_server(addon._server_entry("One", "example.com"))
        panel = FakePanel()
        panel._build_fav_tab(Mock())
        panel.fav_tree.selected = list(panel.fav_tree.rows)
        with patch.object(addon.os, "replace", side_effect=PermissionError("Locked")):
            self.buttons["Remove"]()
        self.assertEqual(len(panel.fav_tree.rows), 1)
        self.assertEqual(len(addon._read_profile()["saved_servers"]), 1)
        self.assertEqual(fake_tk.messagebox.showerror.call_args.args, ("Could not remove server", "Locked"))

    def test_add_dialog_saves_once_and_reports_success(self):
        panel = FakePanel()
        panel.fav_tree = FakeTree()
        variables = []

        def variable(value):
            result = Var(value)
            variables.append(result)
            return result

        with patch.object(fake_ui.tk, "StringVar", side_effect=variable):
            addon._add_server_dialog(panel)
        for variable, value in zip(variables, ("Friends Tavern", "example.com", "1777", "1888", "Tavern authentication")):
            variable.set(value)
        self.buttons["Add"]()
        self.assertEqual(addon._read_profile()["saved_servers"], [addon._server_entry("Friends Tavern", "example.com", 1777, 1888)])
        fake_ui.tk.Toplevel.return_value.destroy.assert_called()
        self.assertEqual(len(panel.fav_tree.rows), 1)
        self.assertEqual(fake_tk.messagebox.showinfo.call_args.args[0], "Server saved")

    def test_add_dialog_save_failure_stays_open_for_retry(self):
        panel = FakePanel()
        panel.fav_tree = FakeTree()
        variables = []

        def variable(value):
            result = Var(value)
            variables.append(result)
            return result

        fake_ui.tk.Toplevel.return_value.reset_mock()
        with patch.object(fake_ui.tk, "StringVar", side_effect=variable):
            addon._add_server_dialog(panel)
        variables[1].set("example.com")
        with patch.object(addon.os, "replace", side_effect=PermissionError("Settings locked")):
            self.buttons["Add"]()
        fake_ui.tk.Toplevel.return_value.destroy.assert_not_called()
        fake_tk.messagebox.showinfo.assert_not_called()
        self.assertEqual(fake_tk.messagebox.showerror.call_args.args, ("Could not save server", "Settings locked"))
        self.assertFalse(Path(fake_launcher.CONFIG_FILE).exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
