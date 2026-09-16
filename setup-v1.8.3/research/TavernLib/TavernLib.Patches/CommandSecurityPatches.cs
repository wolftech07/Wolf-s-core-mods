using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ATT.Character.QuickAccessMenu;
using Alta.Api.Client.LowLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Console;
using HarmonyLib;

namespace TavernLib.Patches;

[HarmonyPatch]
public class CommandSecurityPatches
{
	[HarmonyPatch(typeof(CommandSync), "RouteCommand")]
	[HarmonyPrefix]
	public static bool CancelRouteCommand(string command)
	{
		TavernLogger.Msg("Cancelling RouteCommand attempt: " + command, "CancelRouteCommand");
		return false;
	}

	[HarmonyPatch(typeof(ServerConsoleManager), "StartRemoteConsole")]
	[HarmonyPostfix]
	public static void InstantCloseRemoteConsole(ServerConsoleManager __instance)
	{
		TavernLogger.Msg("Closing ServerRemoteConsole instantly (unnecessary handler)", "InstantCloseRemoteConsole");
		__instance.CloseConsole(__instance.remoteConsole);
	}

	[HarmonyPatch(typeof(WebSocketCommandHandler), "GetCurrentServerPermissionsForLoggedInUser")]
	[HarmonyPrefix]
	public static bool SortPermissions(ref Task<IEnumerable<GroupPermissions>> __result)
	{
		TavernLogger.Msg("Bypassing GetCurrentServerPermissionsForLoggedInUser (temp?)", "SortPermissions");
		__result = Task.FromResult(new List<GroupPermissions> { (GroupPermissions)8 }.AsEnumerable());
		return false;
	}

	[HarmonyPatch(typeof(LowLevelApiClient), "ValidateIdentityToken")]
	[HarmonyPrefix]
	public static bool SkipValidateIdentityToken(ref Task<BooleanResponse> __result)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected O, but got Unknown
		TavernLogger.Msg("Bypassing ValidateIdentityToken (we have no central validator!)", "SkipValidateIdentityToken");
		__result = Task.FromResult<BooleanResponse>(new BooleanResponse(true));
		return false;
	}
}
