using Newtonsoft.Json;

namespace TavernLib.Backend.Server.Configs;

public class TavernServer
{
	[JsonProperty(PropertyName = "server_port")]
	public int ServerPort { get; private set; }
}
