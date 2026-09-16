using System.Collections.Generic;
using System.Linq;

namespace TavernLib.Backend.Server.Configs;

public class UserConfigFile : ServerConfigFile<UserConfig>
{
	public UserConfigFile(string filePath)
		: base(filePath)
	{
	}

	public UserConfig.User GetUser(string username)
	{
		return base.LastRead.Users[username.ToLowerInvariant()];
	}

	public UserConfig.User GetUser(int identifier)
	{
		return base.LastRead.Users.First((KeyValuePair<string, UserConfig.User> user) => user.Value.UserId == (ulong)identifier).Value;
	}

	public bool TryGetUser(string username, out UserConfig.User user)
	{
		user = GetUser(username);
		return user != null;
	}

	public bool TryGetUser(int identifier, out UserConfig.User user)
	{
		user = GetUser(identifier);
		return user != null;
	}
}
