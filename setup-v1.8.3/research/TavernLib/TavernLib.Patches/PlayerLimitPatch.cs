using HarmonyLib;
using TavernLib.Backend.Api;
using TavernLib.Services;

namespace TavernLib.Patches;

[HarmonyPatch]
public class PlayerLimitPatch
{
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPostfix]
	public static void SetPlayerLimit(ref int __result)
	{
		__result = TavernServices.GetService<TavernManager>().ServerConfig.LastRead?.MaxPlayers ?? 0;
	}
}
