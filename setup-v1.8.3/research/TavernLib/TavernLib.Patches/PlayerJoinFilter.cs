using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Converters;
using Alta.Networking;
using Alta.Networking.Servers;
using Alta.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using TavernLib.Backend.Api;
using TavernLib.Backend.Server.Configs;
using TavernLib.Services;

namespace TavernLib.Patches;

[HarmonyPatch]
public static class PlayerJoinFilter
{
	[CompilerGenerated]
	private static class _003C_003EO
	{
		public static SerializeConnectionMethod _003C0_003E__FilterJoinRequest;
	}

	[HarmonyPatch(typeof(ServerPlayerConnectionHandlerOld), "InitializeConnection")]
	[HarmonyPrefix]
	public static bool FlyCamFilter(Connection connection)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		object obj = _003C_003EO._003C0_003E__FilterJoinRequest;
		if (obj == null)
		{
			SerializeConnectionMethod val = FilterJoinRequest;
			_003C_003EO._003C0_003E__FilterJoinRequest = val;
			obj = (object)val;
		}
		connection.SetHandler((MessageType)9, (SerializeConnectionMethod)obj);
		return false;
	}

	private static async void FilterJoinRequest(Connection connection, Stream stream)
	{
		TavernLogger.Msg("Filtering join request for user at IP " + connection.IpAddress, "FilterJoinRequest");
		try
		{
			TavernLogger.Msg("Filtering flycam joiners", "FilterJoinRequest");
			if (!(await FilterFlyCam(connection, stream)))
			{
				return;
			}
			TavernLogger.Msg("User passed flycam check, checking for valid account token", "FilterJoinRequest");
			if (!(await FilterInvalidTokens(connection, stream)))
			{
				return;
			}
			TavernLogger.Msg("User passed token check, moving onto vanilla check", "FilterJoinRequest");
			ServerHandler.Current.playerJoinHandler.CheckApproved(connection, stream);
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error when filtering join request! {e}", "FilterJoinRequest");
			await ServerPlayerConnectionHandlerOld.PlayerDenied(connection, "Error when checking authenticity");
			if (!(ex is Exception source))
			{
				throw ex;
			}
			ExceptionDispatchInfo.Capture(source).Throw();
		}
	}

	private static async Task<bool> FilterInvalidTokens(Connection connection, Stream stream)
	{
		try
		{
			object obj = stream.Clone();
			Stream readingStream = (Stream)((obj is Stream) ? obj : null);
			try
			{
				RequestJoinMessage requestJoinMessage = new RequestJoinMessage();
				requestJoinMessage.Serialize(connection, readingStream);
				TavernLogger.Msg("User trying to join with JWT " + requestJoinMessage.UserCredentials, "FilterInvalidTokens");
				JwtSecurityToken token = JWTUtility.CreateFromString(requestJoinMessage.UserCredentials, true);
				string tavernToken = token.Claims.FirstOrDefault((Claim claim) => claim.Type == "TavernToken")?.Value;
				ulong id = ulong.Parse(token.Claims.FirstOrDefault((Claim claim) => claim.Type == "UserId")?.Value ?? "0");
				string username = token.Claims.FirstOrDefault((Claim claim) => claim.Type == "Username")?.Value ?? "";
				Dictionary<string, UserConfig.User> users = TavernServices.GetService<TavernManager>().UserConfig.LastRead.Users;
				if (users.TryGetValue(username.ToLowerInvariant(), out var user) && user.UserId == id && user.Token == tavernToken)
				{
					return true;
				}
				await ServerPlayerConnectionHandlerOld.PlayerDenied(connection, "Data mismatch or account not found");
				return false;
			}
			finally
			{
				((IDisposable)readingStream)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error in FilterInvalidTokens! {e}", "FilterInvalidTokens");
			throw;
		}
	}

	private static async Task<bool> FilterFlyCam(Connection connection, Stream stream)
	{
		try
		{
			object obj = stream.Clone();
			Stream readingStream = (Stream)((obj is Stream) ? obj : null);
			try
			{
				RequestJoinMessage requestJoinMessage = new RequestJoinMessage();
				requestJoinMessage.Serialize(connection, readingStream);
				PlayerMode playerMode = requestJoinMessage.PlayerMode;
				if ((int)playerMode <= 2)
				{
					JwtSecurityToken token = JWTUtility.CreateFromString(requestJoinMessage.UserCredentials, true);
					string username = token.Claims.FirstOrDefault((Claim c) => c.Type == "Username")?.Value ?? "";
					UserConfigFile users = TavernServices.GetService<TavernManager>().UserConfig;
					if (users.TryGetUser(username, out var user) && user.CanEnterFlyMode)
					{
						TavernLogger.Msg("User " + username + " allowed unorthodox role via role", "FilterFlyCam");
						return true;
					}
					TavernLogger.Warn("User kicked for bizarre mode", "FilterFlyCam");
					await ServerPlayerConnectionHandlerOld.PlayerDenied(connection, "Bizarre mode detected.");
					return false;
				}
			}
			finally
			{
				((IDisposable)readingStream)?.Dispose();
			}
		}
		catch (Exception arg)
		{
			TavernLogger.Error($"Error in FilterFlyCam {arg}", "FilterFlyCam");
			await ServerPlayerConnectionHandlerOld.PlayerDenied(connection, "FlyCam filter error.");
			return false;
		}
		return true;
	}

	[HarmonyPatch(/*Could not decode attribute arguments.*/)]
	[HarmonyPostfix]
	public static void LogDisconnectAttempts(bool isAllowed, bool isDoingPrerequisites, ClientJoinResult joinResult, JoinedServerInfo serverInfo, string error, ref ConfirmJoinMessage __instance)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		TavernLogger.Warn("ConfirmJoinMessage created " + JsonConvert.SerializeObject((object)__instance, (Formatting)1), "LogDisconnectAttempts");
	}
}
