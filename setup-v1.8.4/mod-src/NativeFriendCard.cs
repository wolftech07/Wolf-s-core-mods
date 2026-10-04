using HarmonyLib;

namespace TavernNativeMenu
{
    // Catch unsupported handoffs before the original client optimistically marks
    // the other player as a friend. Physical pickup itself remains native.
    internal static class NativeFriendCard
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(FriendRequestToken), "Grabbed"),
                new HarmonyMethod(typeof(NativeFriendCard), "BeforeGrabbed"));
        }
        private static bool BeforeGrabbed(FriendRequestToken __instance, Interactor interactor)
        {
            if (interactor == null || Player.Current == null) return true;
            bool ownsCard = __instance.Owner == Player.Current.UserInfo.Identifier;
            // Only the two local handoff paths send friendship consent: taking
            // another player's card, or letting them take our released card.
            if (interactor.IsLocal == ownsCard) return true;
            string reason = MeshSocialTransport.CardUnavailableReason;
            if (reason == null) return true;
            MeshSocialClient.SetError(new System.InvalidOperationException(reason));
            int otherId = ownsCard ? interactor.Controller.PlayerController.NetworkPlayer.UserInfo.Identifier : __instance.Owner;
            MeshSocialTransport.NotifyCard(otherId, reason);
            return false;
        }
    }
}
