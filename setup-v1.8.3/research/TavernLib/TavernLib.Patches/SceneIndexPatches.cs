using Alta.Api.DataTransferModels.Models.Responses;
using HarmonyLib;

namespace TavernLib.Patches;

[HarmonyPatch]
public class SceneIndexPatches
{
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPostfix]
	public static void OverrideSceneIndex(GameServerInfo server)
	{
		if (CommandLineArguments.Contains("/questScene"))
		{
			server.SceneIndex = 4;
		}
	}
}
