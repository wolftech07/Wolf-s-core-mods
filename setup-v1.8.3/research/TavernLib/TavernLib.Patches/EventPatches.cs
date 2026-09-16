using Alta.Networking;
using HarmonyLib;
using TavernLib.Utils;

namespace TavernLib.Patches;

[HarmonyPatch]
public class EventPatches
{
	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPostfix]
	public static void SocketCreatedEvent()
	{
		TavernEvents.SocketCreated.Invoke(Socket.Current);
	}
}
