namespace TavernNativeMenu
{
    internal static class JoinVisibility
    {
        internal static void Configure(ServerSelectionOrb selection)
        {
            if (selection == null) return;
            var action = MenuMod.Get<ActionOrb>(selection, "actionOrb");
            if (action != null) MenuMod.Set(action, "hasScreenFade", false);
            // ActionOrb still handles distance, haptics and valid release. Only
            // its preview fade is disabled; VrMainMenu owns the real loading fade.
        }

        internal static void RestoreMenu()
        {
            if (GameModeManager.CurrentMode != null) return;
            var controller = PlayerController.Current;
            if (controller != null && controller.ScreenFader != null)
                controller.ScreenFader.FadeInIfNotLocked(0);
        }
    }
}
