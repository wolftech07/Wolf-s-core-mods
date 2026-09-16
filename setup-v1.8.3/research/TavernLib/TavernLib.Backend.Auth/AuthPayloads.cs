using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace TavernLib.Backend.Auth;

internal static class AuthPayloads
{
	public struct PingRequest
	{
		[JsonProperty(PropertyName = "ping")]
		private bool Ping { get; set; }
	}

	public struct PingResponse
	{
		[JsonProperty(PropertyName = "status")]
		private string Pong => "pong";

		[JsonProperty(PropertyName = "server_name")]
		private string ServerName { get; set; }

		[JsonProperty(PropertyName = "password_required")]
		private bool PasswordRequired { get; set; }

		[JsonProperty(PropertyName = "whitelist_enabled")]
		private bool WhitelistEnabled { get; set; }

		[JsonProperty(PropertyName = "game_port")]
		private int GamePort { get; set; }

		public PingResponse(string serverName, bool passwordRequired, bool whitelistEnabled, int gamePort)
		{
			ServerName = serverName;
			PasswordRequired = passwordRequired;
			WhitelistEnabled = whitelistEnabled;
			GamePort = gamePort;
		}
	}

	public struct AuthenticateRequest
	{
		[JsonProperty(PropertyName = "username")]
		public string Username { get; private set; }

		[JsonProperty(PropertyName = "token")]
		public string Token { get; private set; }

		[JsonProperty(PropertyName = "password")]
		public string Password { get; private set; }
	}

	public readonly struct AuthenticateOk
	{
		[JsonProperty(PropertyName = "status")]
		private string Status => "ok";

		[JsonProperty(PropertyName = "user_id")]
		private ulong UserId => _003CuserId_003EP;

		[JsonProperty(PropertyName = "quest_scene_required")]
		private bool QuestSceneRequired => _003CquestSceneRequired_003EP;

		public AuthenticateOk(ulong userId, bool questSceneRequired)
		{
			_003CuserId_003EP = userId;
			_003CquestSceneRequired_003EP = questSceneRequired;
		}
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public readonly struct NeedsPassword
	{
		[JsonProperty(PropertyName = "status")]
		private string Status => "needs_password";
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public readonly struct WrongPassword
	{
		[JsonProperty(PropertyName = "status")]
		private string Status => "wrong_password";

		[JsonProperty(PropertyName = "message")]
		private string Message => "Wrong Password";
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public readonly struct NotWhitelisted
	{
		[JsonProperty(PropertyName = "status")]
		private string Status => "not_whitelisted";

		[JsonProperty(PropertyName = "message")]
		private string Message => "Not Whitelisted";
	}

	public readonly struct WhitelistApplicationReceived
	{
		[JsonProperty(PropertyName = "status")]
		private string Status => "whitelist_application_received";

		[JsonProperty(PropertyName = "already_pending")]
		private bool AlreadyPending => !_003CwasNew_003EP;

		public WhitelistApplicationReceived(bool wasNew)
		{
			_003CwasNew_003EP = wasNew;
		}
	}

	public readonly struct GenericFail
	{
		[JsonProperty(PropertyName = "status")]
		private string Status => "error";

		[JsonProperty(PropertyName = "message")]
		private string Message => _003Cmessage_003EP;

		public GenericFail(string message)
		{
			_003Cmessage_003EP = message;
		}
	}
}
