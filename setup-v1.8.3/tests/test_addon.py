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


fake_launcher = types.ModuleType("client.core.launcher_window")
fake_launcher.ClientLauncher = FakeWindow
fake_launcher.PLATFORM_DISPLAY_TO_BACKEND = {"SteamVR": "openvr", "Quest": "oculus", "Fly": "fly"}
fake_launcher.USERNAME_MAX_LEN = 16
fake_launcher._is_valid_username = lambda value: all(c in " -_" or c.isascii() and c.isalnum() for c in value)
fake_launcher.build_tokens = Mock(return_value=("ACCESS_SECRET", "REFRESH_SECRET", "IDENTITY_SECRET"))
fake_core = types.ModuleType("client.core")
fake_core.launcher_window = fake_launcher
fake_client = types.ModuleType("client")
fake_client.core = fake_core
fake_tk = types.ModuleType("tkinter")
fake_tk.messagebox = Mock()
sys.modules.update({"client": fake_client, "client.core": fake_core, "client.core.launcher_window": fake_launcher, "tkinter": fake_tk})
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
        self.assertIn("Tavern Native Menu enabled", self.window.messages[0][0])

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


if __name__ == "__main__":
    unittest.main(verbosity=2)
