using System;
using System.Collections.Generic;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Converters;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Utility;
using Alta.Customization;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.QuickAccessActions;
using Alta.Serialization;
using HarmonyLib;

namespace TavernLib.Patches;

[HarmonyPatch]
public class TeenyPatches
{
	private static readonly string ServerSecretPath = Path.Combine(TavernDirectories.ModdingTavern, "server_secret.key");

	private static byte[] _serverSecret;

	private static byte[] ServerSecret => _serverSecret ?? (_serverSecret = LoadOrCreateServerSecret());

	[HarmonyPatch(typeof(UserApiClient), "GetAllCosmeticsPresets")]
	[HarmonyPrefix]
	public static bool LocalGetAllCosmeticPresets(ref Task<IEnumerable<UserPresetDataInfo>> __result)
	{
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		List<UserPresetDataInfo> list = new List<UserPresetDataInfo>();
		try
		{
			string path = Path.Combine(TavernDirectories.ATTSave, "presets");
			if (Directory.Exists(path))
			{
				string[] files = Directory.GetFiles(path, "*.preset");
				foreach (string path2 in files)
				{
					try
					{
						int presetId = int.Parse(Path.GetFileNameWithoutExtension(path2));
						byte[] array = File.ReadAllBytes(path2);
						uint[] array2 = new uint[(array.Length + 3) / 4];
						Buffer.BlockCopy(array, 0, array2, 0, array.Length);
						list.Add(new UserPresetDataInfo
						{
							PresetId = presetId,
							ByteSize = array.Length,
							Data = array2
						});
					}
					catch (Exception)
					{
					}
				}
			}
		}
		catch (Exception)
		{
		}
		__result = Task.FromResult((IEnumerable<UserPresetDataInfo>)list);
		return false;
	}

	[HarmonyPatch(typeof(UserApiClient), "CreateCosmeticsPreset")]
	[HarmonyPrefix]
	public static bool LocalCreateCosmeticPreset(int presetId, uint[] data, int byteSize, ref Task<UserPresetDataInfo> __result)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Expected O, but got Unknown
		try
		{
			string text = Path.Combine(TavernDirectories.ATTSave, "presets");
			Directory.CreateDirectory(text);
			byte[] array = new byte[byteSize];
			Buffer.BlockCopy(data, 0, array, 0, byteSize);
			File.WriteAllBytes(Path.Combine(text, presetId + ".preset"), array);
		}
		catch (Exception)
		{
		}
		__result = Task.FromResult<UserPresetDataInfo>(new UserPresetDataInfo
		{
			PresetId = presetId,
			ByteSize = byteSize,
			Data = data
		});
		return false;
	}

	[HarmonyPatch(typeof(UserApiClient), "DeleteCosmeticPreset")]
	[HarmonyPrefix]
	public static bool LocalDeleteCosmeticPreset(int presetId, ref Task __result)
	{
		try
		{
			string path = Path.Combine(TavernDirectories.ATTSave, "presets", presetId + ".preset");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception)
		{
		}
		__result = Task.CompletedTask;
		return false;
	}

	[HarmonyPatch(typeof(PurchasedList), "UpdateCurrentRotation")]
	public static bool LocalDeleteCosmeticPreset(ref Task __result)
	{
		PurchasedList.CurrentOnStore.Clear();
		PurchasedList.CurrentRotationToSets.Clear();
		__result = Task.CompletedTask;
		return false;
	}

	[HarmonyPatch(typeof(PurchasedList), "ValidatePlayersOwnedItems")]
	[HarmonyPrefix]
	public static bool AlwaysAllowAnyItem(ref Task<bool> __result)
	{
		__result = Task.FromResult(result: true);
		return false;
	}

	[HarmonyPatch(typeof(PurchasedList), "Refresh")]
	[HarmonyPrefix]
	public static bool DisallowPaidCosmetics(ref Task __result, PurchasedList __instance)
	{
		PurchasedList.Initialize();
		PurchasedList.owned.Clear();
		PurchasedList.owned = PurchasedList.owned.Concat(PurchasedList.OwnedByAll).ToHashSet();
		PurchasedList.available.Clear();
		PurchasedList.available = PurchasedList.available.Concat(PurchasedList.OwnedByAll).ToHashSet();
		__result = Task.CompletedTask;
		return false;
	}

	[HarmonyPatch(typeof(ReturnToMainMenuAction), "LetGoValid")]
	[HarmonyPrefix]
	public static bool QuitOnMenuReturn(ReturnToMainMenuAction __instance)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		__instance.ClearOrb();
		GameModeManager.StopCurrentModeAsync("return to menu", false);
		ApplicationManager.ExternalOnApplicationQuit(new ShutdownReason("Player exited", true));
		return false;
	}

	[HarmonyPatch(typeof(ApplicationStartupManager), "RunStartupActions")]
	[HarmonyPrefix]
	public static bool LocalJoinArg()
	{
		if (!CommandLineArguments.Contains("/join_local_server"))
		{
			return true;
		}
		string[] array = default(string[]);
		string text = (CommandLineArguments.TryGetNextArguments("/dev_server_ip", 1, ref array, false) ? array[0] : IPAddress.Loopback.ToString());
		string[] array2 = default(string[]);
		int result;
		int num = ((CommandLineArguments.TryGetNextArguments("/dev_server_port", 1, ref array2, false) && int.TryParse(array2[0], out result)) ? result : 1757);
		GameModeManager.JoinServer((GameServerInfo)(object)DevGameServerInfo.GetDevServer(text, num, 0));
		return false;
	}

	private static byte[] HexToBytes(string hex)
	{
		byte[] array = new byte[hex.Length / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		}
		return array;
	}

	private static string BytesToHex(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
		foreach (byte b in bytes)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	private static byte[] LoadOrCreateServerSecret()
	{
		try
		{
			if (File.Exists(ServerSecretPath))
			{
				byte[] array = HexToBytes(File.ReadAllText(ServerSecretPath).Trim());
				if (array.Length == 32)
				{
					return array;
				}
			}
		}
		catch
		{
		}
		byte[] array2 = new byte[32];
		using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
		{
			randomNumberGenerator.GetBytes(array2);
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(ServerSecretPath));
			File.WriteAllText(ServerSecretPath, BytesToHex(array2));
		}
		catch
		{
		}
		return array2;
	}

	private static string B64Url(byte[] bytes)
	{
		return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_')
			.TrimEnd('=');
	}

	private static string BuildConsoleToken(byte[] secret)
	{
		string text = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
		string text2 = B64Url(Encoding.UTF8.GetBytes("{\"UserId\":\"0\",\"Username\":\"Server\",\"role\":\"Access\",\"is_verified\":\"True\",\"is_member\":\"True\",\"server_id\":\"-1\",\"Policy\":[\"offline\",\"play_offline\",\"server_access_pre_alpha\",\"game_access_public\",\"server_owner\",\"debug_features\",\"database_admin\",\"reuse_refresh_tokens\"],\"exp\":9999999999,\"iss\":\"AltaWebAPI\",\"aud\":\"AltaClient\"}"));
		string s = text + "." + text2;
		byte[] bytes;
		using (HMACSHA256 hMACSHA = new HMACSHA256(secret))
		{
			bytes = hMACSHA.ComputeHash(Encoding.UTF8.GetBytes(s));
		}
		return text + "." + text2 + "." + B64Url(bytes);
	}

	internal static void EnsureConsoleToken()
	{
		try
		{
			string path = Path.Combine(TavernDirectories.ModdingTavern, "console_token.txt");
			if (!File.Exists(path) || !File.Exists(ServerSecretPath))
			{
				byte[] serverSecret = ServerSecret;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, BuildConsoleToken(serverSecret));
			}
		}
		catch
		{
		}
	}

	[HarmonyPatch(typeof(ServerConsoleManager), "ValidateConsoleToken")]
	[HarmonyPrefix]
	public static bool ValidateConsoleToken(JwtSecurityToken token, ref Task<bool> __result)
	{
		if (!token.Claims.Any((Claim c) => c.Type == "Policy" && c.Value == "server_owner"))
		{
			__result = Task.FromResult(result: false);
			return false;
		}
		byte[] serverSecret = ServerSecret;
		if (serverSecret == null)
		{
			__result = Task.FromResult(result: false);
			return false;
		}
		try
		{
			string text = token.Claims.FirstOrDefault((Claim c) => c.Type == "raw")?.Value;
			if (text == null)
			{
				__result = Task.FromResult(result: false);
				return false;
			}
			int num = text.LastIndexOf('.');
			string s = text.Substring(0, num);
			string text2 = text.Substring(num + 1);
			byte[] bytes;
			using (HMACSHA256 hMACSHA = new HMACSHA256(serverSecret))
			{
				bytes = hMACSHA.ComputeHash(Encoding.UTF8.GetBytes(s));
			}
			__result = Task.FromResult(string.Equals(text2.Trim(), B64Url(bytes), StringComparison.Ordinal));
		}
		catch
		{
			__result = Task.FromResult(result: false);
		}
		return false;
	}

	[HarmonyPatch(typeof(ServerPlayerConnectionHandlerOld), "CheckIfPlayerIsAllowedCustom")]
	[HarmonyPrefix]
	public static bool ValidateConsoleToken(string tokenString, int playerId, ref Task<PlayerJoinResult> __result)
	{
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Expected O, but got Unknown
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			JwtSecurityToken val = JWTUtility.CreateFromString(tokenString, false);
			string value = val.Claims.First((Claim c) => c.Type == "Username").Value;
			if (int.Parse(val.Claims.First((Claim c) => c.Type == "UserId").Value) != playerId)
			{
				__result = Task.FromResult<PlayerJoinResult>(PlayerJoinResult.CreateDeniedResult("Token was for a different user"));
			}
			UserInfoAndRole val2 = new UserInfoAndRole(new UserInfo(playerId, value), UserRolesUtility.GetRolesFromIdentityToken(tokenString));
			__result = Task.FromResult<PlayerJoinResult>(PlayerJoinResult.CreateSuccessResult(val2));
		}
		catch (Exception)
		{
			__result = Task.FromResult<PlayerJoinResult>(PlayerJoinResult.CreateDeniedResult("Error reading token"));
		}
		return false;
	}

	[HarmonyPatch(typeof(Player), "SyncCosmetics")]
	[HarmonyPrefix]
	public static bool LocalDeleteCosmeticPreset(IPlayer player, Stream stream, Player __instance)
	{
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		TavernLogger.Msg("SyncCosmetics patch!", "LocalDeleteCosmeticPreset");
		try
		{
			if (StreamEntityExtensions.IsReadingOnServerNonLocalTest(stream) && (object)__instance != player)
			{
				Player.logger.Error<IPlayer, Player>("[Player] Received message to alter cosmetics from {0} to {1}", player, __instance);
				StreamAuthorityHelper.LogUnauthorizedMessage(player);
				return false;
			}
			if (NetworkSceneManager.IsServer && stream.IsWriting)
			{
				if (__instance.saveData != null && __instance.saveData.CosmeticBytes > 4)
				{
					StreamWriter val = (StreamWriter)(object)((stream is StreamWriter) ? stream : null);
					if (val != null)
					{
						StreamReader val2 = new StreamReader(__instance.saveData.CosmeticData, __instance.saveData.CosmeticBytes);
						int num = __instance.saveData.CosmeticBytes * 8;
						for (int i = 0; i < num; i += 32)
						{
							int num2 = Math.Min(32, num - i);
							uint num3 = 0u;
							((Stream)val2).SerializeBits(ref num3, num2);
							((Stream)val).SerializeBits(ref num3, num2);
						}
						return false;
					}
				}
				CustomizationWrapperSerializer.SerializeStreamAsync((ICustomizationWrapper)(object)__instance.Customization.Cosmetics, stream, false, CancellationToken.None);
				return false;
			}
			CustomizationWrapperSerializer.SerializeStreamAsync((ICustomizationWrapper)(object)__instance.Customization.Cosmetics, stream, !NetworkSceneManager.IsServer, CancellationToken.None);
		}
		catch (Exception ex)
		{
			Player.logger.Error(ex, "[Player] Error syncing customization");
		}
		TavernLogger.Msg("SyncCosmetics patch done!", "LocalDeleteCosmeticPreset");
		return false;
	}

	[HarmonyPatch(typeof(MenuSettings), "LocalGameServerInfo")]
	[HarmonyPostfix]
	public static void LocalGameServerInfo_Postfix(int sceneIndex, ref GameServerInfo __result)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		GameServerInfo obj = __result;
		DevGameServerInfo val = (DevGameServerInfo)(object)((obj is DevGameServerInfo) ? obj : null);
		if (val != null)
		{
			if (CommandLineArguments.Contains("/questScene"))
			{
				__result.SceneIndex = 4;
			}
			string[] array = default(string[]);
			int result;
			int gamePort = ((CommandLineArguments.TryGetNextArguments("/dev_server_port", 1, ref array, false) && int.TryParse(array[0], out result)) ? result : 1757);
			val.ConnectionInfo = new ConnectionInfo
			{
				Address = IPAddress.Loopback,
				GamePort = gamePort
			};
			__result = (GameServerInfo)(object)val;
		}
	}

	[HarmonyPatch(typeof(ApiAccess), "IsConnectedToInternetInternal")]
	[HarmonyPostfix]
	public static void IsConnectedToInternetInternal_Postfix(ref bool __result)
	{
		__result = true;
	}
}
