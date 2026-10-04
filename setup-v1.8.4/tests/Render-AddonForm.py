"""Render the add-on with official launcher widgets and temporary settings.

Usage: python Render-AddonForm.py <launcher-source-folder> [--observe]
No real launcher profile, identity token, game, or network is accessed.
"""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import types
from unittest.mock import Mock


def main():
    upstream = Path(sys.argv[1]).resolve()
    observe = "--observe" in sys.argv[2:]
    sys.path.insert(0, str(upstream))
    with tempfile.TemporaryDirectory(prefix="tavern-form-test-") as folder:
        profile = Path(folder) / "profile.json"
        profile.write_text('{"saved_servers":[]}', encoding="utf-8")
        # Keep the official panel and theme while replacing only persistence.
        config = types.ModuleType("client.core.config")
        config.load_cfg = lambda: json.loads(profile.read_text(encoding="utf-8"))
        config.save_cfg = lambda data: profile.write_text(json.dumps(data), encoding="utf-8")
        sys.modules["client.core.config"] = config
        from client.core import server_list_panel
        from client import core

        class Window:
            def __init__(self):
                pass

            def _build_ui(self):
                pass

        launcher = types.ModuleType("client.core.launcher_window")
        launcher.ClientLauncher = Window
        launcher.ServerListPanel = server_list_panel.ServerListPanel
        launcher.CONFIG_FILE = str(profile)
        sys.modules["client.core.launcher_window"] = launcher
        core.launcher_window = launcher
        source = Path(__file__).resolve().parents[1] / "payload" / "addons" / "tavern_native_menu" / "client.py"
        spec = importlib.util.spec_from_file_location("render_tavern_addon", source)
        addon = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(addon)
        addon.messagebox.showinfo = Mock()
        addon.messagebox.showerror = Mock()
        tk = server_list_panel.tk
        root = tk.Tk()
        root.withdraw()
        root.tk.call("tk", "scaling", 96 / 72)
        server_list_panel.ttk.Style().theme_use("clam")
        try:
            panel = server_list_panel.ServerListPanel(root, Mock())
            addon._add_server_dialog(panel)
            form = next(widget for widget in panel.winfo_children() if isinstance(widget, tk.Toplevel))
            root.update()

            def descend(widget):
                for child in widget.winfo_children():
                    yield child
                    yield from descend(child)

            widgets = list(descend(form))
            entries = [widget for widget in widgets if type(widget) is tk.Entry]
            choices = [widget for widget in widgets if isinstance(widget, server_list_panel.ttk.Combobox)]
            labels = [widget.cget("text") for widget in widgets if isinstance(widget, tk.Label)]
            buttons = {widget.cget("text"): widget for widget in widgets if isinstance(widget, tk.Button)}
            assert len(entries) == 4 and len(choices) == 1
            assert "IP address or hostname" in labels and "Authentication port" in labels
            assert {"Add", "Cancel"}.issubset(buttons)
            assert all(widget.winfo_width() > 100 for widget in entries + choices)
            for entry, value in zip(entries, ("Friends Tavern", "example.com", "1777", "1888")):
                entry.delete(0, "end")
                entry.insert(0, value)
            print(json.dumps({"title": form.title(), "size": [form.winfo_width(), form.winfo_height()],
                              "fields": labels, "buttons": list(buttons)}), flush=True)
            if observe:
                form.bind("<Destroy>", lambda event: root.after_idle(root.destroy) if event.widget is form else None)
                root.after(180000, root.destroy)
                root.mainloop()
            else:
                buttons["Add"].invoke()
                addon.messagebox.showerror.assert_not_called()
                expected = addon._server_entry("Friends Tavern", "example.com", 1777, 1888)
                assert config.load_cfg()["saved_servers"] == [expected]
                assert list(panel._native_saved_rows.values()) == [expected]
                print("PASS: official Tk form rendered; Add saved and refreshed its row.")
        finally:
            try:
                root.destroy()
            except tk.TclError:
                pass


if __name__ == "__main__":
    main()
