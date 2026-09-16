using System.Collections.Generic;
using Alta.Networking;
using Alta.Serialization;
using HarmonyLib;
using TavernLib.Backend.Api;
using TavernLib.Services;

namespace TavernLib.Patches;

[HarmonyPatch]
public class ServerSettingsRolePatch
{
	[HarmonyPatch(typeof(Player), "InitializePlayerOnServer")]
	[HarmonyPostfix]
	public static void SendRolesToUser(Player __instance)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Expected O, but got Unknown
		List<string> roles = TavernServices.GetService<TavernManager>().UserConfig.GetUser(__instance.UserInfo.Username).Roles;
		__instance.ConnectionToRemotePlayer.Send((ConnectionChannel)null, (MessageType)32, (SerializeConnectionMethod)delegate(Connection _, Stream stream)
		{
			int count = roles.Count;
			stream.SerializeInteger(ref count);
			if (count >= 1)
			{
				for (int i = 0; i < count; i++)
				{
					string text = roles[i];
					stream.SerializeString(ref text, (StringEncoding)0);
				}
			}
		});
	}
}
