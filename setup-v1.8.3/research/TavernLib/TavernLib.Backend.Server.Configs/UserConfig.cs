using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace TavernLib.Backend.Server.Configs;

public class UserConfig
{
	public class User
	{
		[JsonProperty("user_id")]
		public ulong UserId { get; set; }

		[JsonProperty("token")]
		public string Token { get; set; }

		[JsonProperty("registered_from")]
		public string RegisteredFrom { get; set; }

		[JsonProperty("roles")]
		public List<string> Roles { get; set; } = new List<string>();

		public bool IsModerator => HasRole("moderator") || HasRole("owner");

		public bool CanEnterFlyMode => IsModerator || HasRole("fly");

		public bool HasRole(string role)
		{
			return Roles != null && Roles.Any((string r) => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
		}
	}

	public class ListConfig
	{
		[JsonProperty("usernames")]
		public List<string> Usernames { get; set; } = new List<string>();

		[JsonProperty("ips")]
		public List<string> Ips { get; set; } = new List<string>();

		[JsonProperty("user_ids")]
		public List<ulong> UserIds { get; set; } = new List<ulong>();
	}

	[JsonProperty("users")]
	public Dictionary<string, User> Users { get; set; } = new Dictionary<string, User>();

	[JsonProperty("whitelist")]
	public ListConfig Whitelist { get; set; } = new ListConfig();

	[JsonProperty("blacklist")]
	public ListConfig Blacklist { get; set; } = new ListConfig();
}
