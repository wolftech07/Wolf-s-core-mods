using Alta.Networking.Servers;
using Newtonsoft.Json;
using TavernLib.Backend.Server.Configs;

namespace TavernLib.Backend.Server;

public struct ServerListingPayload
{
	[JsonProperty(PropertyName = "listing_token")]
	public string ListingToken { get; private set; }

	[JsonProperty(PropertyName = "name")]
	public string Name { get; private set; }

	[JsonProperty(PropertyName = "port")]
	public int Port { get; private set; }

	[JsonProperty(PropertyName = "player_limit")]
	public int PlayerLimit { get; private set; }

	[JsonProperty(PropertyName = "has_password")]
	public bool HasPassword { get; private set; }

	[JsonProperty(PropertyName = "player_count")]
	public int PlayerCount { get; private set; }

	[JsonProperty(PropertyName = "community_listed")]
	public bool CommunityListed { get; private set; }

	[JsonProperty(PropertyName = "hostname")]
	public string HostName { get; private set; }

	[JsonProperty(PropertyName = "version")]
	public string Version { get; private set; }

	[JsonProperty(PropertyName = "region")]
	public string Region { get; private set; }

	public static ServerListingPayload FromConfig(ServerSettingsConfig config, TavernServerConfig tavernConfig)
	{
		ServerListingPayload result = new ServerListingPayload
		{
			ListingToken = config.LastRead.CommunityListingToken,
			Name = config.LastRead.Name,
			Port = tavernConfig.LastRead.ServerPort
		};
		ServerHandler current = ServerHandler.Current;
		result.PlayerLimit = ((current != null) ? current.PlayerLimit : config.LastRead.MaxPlayers);
		result.HasPassword = !string.IsNullOrWhiteSpace(config.LastRead.PasswordHash);
		ServerHandler current2 = ServerHandler.Current;
		result.PlayerCount = ((current2 != null) ? current2.Connections : 0);
		result.CommunityListed = config.LastRead.CommunityListed;
		result.HostName = config.LastRead.PublicHostname;
		result.Version = "1.5.0";
		result.Region = config.LastRead.Region;
		return result;
	}
}
