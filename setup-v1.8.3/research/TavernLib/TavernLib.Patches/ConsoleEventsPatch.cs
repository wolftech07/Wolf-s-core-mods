using System;
using Alta.Console;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using HarmonyLib;

namespace TavernLib.Patches;

[HarmonyPatch]
public class ConsoleEventsPatch
{
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPostfix]
	public static void PlayerLeavePatch(ServerPlayerConnectionHandlerOld __instance)
	{
		__instance.UserLeft += delegate(Connection connection)
		{
			if (connection.player != null)
			{
				ConsoleEvents.PlayerLeft.Invoke((Func<PlayerJoinLeaveData>)(() => new PlayerJoinLeaveData(connection.Player)));
			}
		};
	}

	[HarmonyPatch(typeof(Player), "InitializePlayerOnServer")]
	[HarmonyPostfix]
	public static void PlayerJoinPatch(Player __instance)
	{
		ConsoleEvents.PlayerJoined.Invoke((Func<PlayerJoinLeaveData>)(() => new PlayerJoinLeaveData((IPlayer)(object)__instance)));
	}
}
