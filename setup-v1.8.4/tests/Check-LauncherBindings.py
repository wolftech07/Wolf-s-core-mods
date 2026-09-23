"""Read-only contract checks against a downloaded official launcher source tag.

Usage: python Check-LauncherBindings.py <launcher source directory>
"""
import ast
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
window = ast.parse((root / "client/core/launcher_window.py").read_text(encoding="utf-8-sig"))
cls = next(n for n in window.body if isinstance(n, ast.ClassDef) and n.name == "ClientLauncher")
methods = {n.name: n for n in cls.body if isinstance(n, ast.FunctionDef)}
checks = 0


def check(condition, label):
    global checks
    assert condition, label
    checks += 1
    print("PASS", label)


for name, count in (("__init__", 1), ("_build_ui", 1), ("_on_join_clicked", 1), ("_save", 1), ("_popen_console_kwargs", 1)):
    check(name in methods and len(methods[name].args.args) >= count, "launcher method " + name)
fields = {n.attr for n in ast.walk(cls) if isinstance(n, ast.Attribute) and isinstance(n.value, ast.Name) and n.value.id == "self"}
for name in ("v_exe", "v_username", "v_platform", "v_debug_helper", "v_show_melonloader", "_action_btn"):
    check(name in fields, "launcher control " + name)
module_names = {n.name for n in ast.walk(window) if isinstance(n, ast.alias)} | {n.id for n in ast.walk(window) if isinstance(n, ast.Name)}
for name in ("build_tokens", "USERNAME_MAX_LEN", "_is_valid_username", "CONFIG_FILE", "PLATFORM_DISPLAY_TO_BACKEND"):
    check(name in module_names, "launcher module export " + name)
auth = ast.parse((root / "client/core/auth.py").read_text(encoding="utf-8-sig"))
tokens = next(n for n in auth.body if isinstance(n, ast.FunctionDef) and n.name == "build_tokens")
check(len(tokens.args.args) == 3 and len(tokens.args.defaults) >= 1, "menu token call keeps its optional third token argument")
loader = (root / "client/core/addon_loader.py").read_text(encoding="utf-8-sig")
check('client_enabled_addons.json' in loader and 'importlib.import_module(f"addons.{name}.{side}")' in loader, "external add-on loading and enablement file remain compatible")
check('"game_exe"' in ast.get_source_segment((root / "client/core/launcher_window.py").read_text(encoding="utf-8-sig"), methods["_save"]), "launcher saves the game path used by the mod")
print(f"PASS {checks} actual launcher source contract checks.")
