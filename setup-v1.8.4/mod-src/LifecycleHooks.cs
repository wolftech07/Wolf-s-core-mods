using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MelonLoader;

namespace TavernNativeMenu
{
    // Keep Unity's own scene, controller spawning, mode cleanup and loading screen.
    // TavernLib's two menu-bypass prefixes are the only foreign patches removed.
    internal static class LifecycleHooks
    {
        private static bool installed;
        private static Task loadingMenu;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            if (installed || CommandLineArguments.Contains("/start_server") || ApplicationManager.IsHeadless)
                return;

            MethodInfo startup = AccessTools.Method(typeof(ApplicationStartupManager), "RunStartupActions");
            Type returnAction = AccessTools.TypeByName("Alta.QuickAccessActions.ReturnToMainMenuAction");
            MethodInfo returnOrb = returnAction == null ? null : AccessTools.Method(returnAction, "LetGoValid");
            MethodInfo stopMode = AccessTools.Method(typeof(GameModeManager), "StopCurrentModeAsync");
            if (startup == null || returnOrb == null || stopMode == null)
                throw new MissingMethodException("This game build does not expose the expected native menu lifecycle methods.");

            RemoveTavernPrefix(harmony, startup, "LocalJoinArg");
            RemoveTavernPrefix(harmony, returnOrb, "QuitOnMenuReturn");

            harmony.Patch(startup, new HarmonyMethod(typeof(LifecycleHooks), "BeforeStartup"));
            harmony.Patch(stopMode,
                new HarmonyMethod(typeof(LifecycleHooks), "BeforeStopMode"),
                new HarmonyMethod(typeof(LifecycleHooks), "AfterStopMode"));
            installed = true;
            MelonLogger.Msg("[Tavern In-Game Hub] Native menu startup and return hooks installed.");
        }

        private static void RemoveTavernPrefix(HarmonyLib.Harmony harmony, MethodBase target, string methodName)
        {
            Patches patches = HarmonyLib.Harmony.GetPatchInfo(target);
            if (patches == null)
                return;
            foreach (Patch patch in patches.Prefixes)
            {
                MethodInfo method = patch.PatchMethod;
                Type owner = method.DeclaringType;
                if (method.Name == methodName && owner != null &&
                    owner.FullName == "TavernLib.Patches.TeenyPatches" &&
                    owner.Assembly.GetName().Name == "TavernLib")
                {
                    harmony.Unpatch(target, method);
                    MelonLogger.Msg("[Tavern In-Game Hub] Restored menu behavior from TavernLib." + methodName + ".");
                }
            }
        }

        private static bool BeforeStartup()
        {
            if (CommandLineArguments.Contains("/start_server") || ApplicationManager.IsHeadless)
                return true;
            if (GameModeManager.CurrentMode != null)
                return false;
            if (loadingMenu == null || loadingMenu.IsCompleted)
                loadingMenu = LoadNativeMenu();
            return false;
        }

        private static async Task LoadNativeMenu()
        {
            try
            {
                await WaitForSceneIdle();
                if (ApplicationManager.IsQuitting)
                    return;
                if (CommandLineArguments.Contains("/tavern_native_menu"))
                {
                    if (MenuMod.Catalog == null || MenuMod.Catalog.Profile == null)
                        throw new InvalidOperationException("Tavern Launcher settings have not been loaded.");
                    TavernIdentity.InitializeMenu(MenuMod.Catalog.Profile.Username);
                }
                ApplicationStartupManager.IsSkippingMenu = false;
                MelonLogger.Msg("[Tavern In-Game Hub] Opening the original server selection scene.");
                await AltaSceneManager.LoadMainMenuSceneAsync();
            }
            catch (Exception error)
            {
                MelonLogger.Error("[Tavern In-Game Hub] Could not open the native menu: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static void BeforeStopMode(ref bool isReturningToMenu, out bool __state)
        {
            __state = isReturningToMenu && !ApplicationManager.IsHeadless &&
                !CommandLineArguments.Contains("/start_server") &&
                !ApplicationManager.IsQuitting && GameModeManager.CurrentMode != null;
            if (__state)
            {
                // The normal stop path refreshes an Alta account before loading the menu.
                // Tavern sessions use server-local credentials, so retain normal cleanup
                // and perform the native scene return after that cleanup has completed.
                isReturningToMenu = false;
            }
        }

        private static void AfterStopMode(bool __state, ref Task __result)
        {
            if (__state)
                __result = ReturnAfterStop(__result);
        }

        private static async Task ReturnAfterStop(Task stopping)
        {
            try
            {
                await stopping;
                if (ApplicationManager.IsQuitting)
                    return;
                await WaitForSceneIdle();
                if (ApplicationManager.IsQuitting)
                    return;
                ApplicationManager.StartReload();
                try
                {
                    await AltaSceneManager.ReturnToMainMenuAsync();
                    MelonLogger.Msg("[Tavern In-Game Hub] Returned to the native server selection scene.");
                }
                finally
                {
                    ApplicationManager.FinishReload();
                }
            }
            catch (Exception error)
            {
                MelonLogger.Error("[Tavern In-Game Hub] Could not return to the native menu: " + error.GetType().Name + ": " + error.Message);
            }
        }

        private static async Task WaitForSceneIdle()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (AltaSceneManager.IsLoading && !ApplicationManager.IsQuitting)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Another scene operation did not finish within 30 seconds.");
                await Task.Delay(100);
            }
        }
    }
}
