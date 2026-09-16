using System.Diagnostics;
using Alta.Customization;
using HarmonyLib;

namespace TavernLib.Patches;

[HarmonyPatch]
public class CosmeticsPatches
{
	[HarmonyPatch(typeof(ServerHostingGameMode), "OnStartSucceeded")]
	[HarmonyPostfix]
	public static void LoadCosmeticsIntoRamIfAppropriate()
	{
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		TavernLogger.Msg("Loading cosmetics into RAM for server...", "LoadCosmeticsIntoRamIfAppropriate");
		Purchasable.LoadAllIntoRAM();
		stopwatch.Stop();
		TavernLogger.Msg($"Time taken to load cosmetics into RAM: {stopwatch.Elapsed.TotalMilliseconds}ms", "LoadCosmeticsIntoRamIfAppropriate");
	}
}
