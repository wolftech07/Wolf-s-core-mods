namespace TavernLib.Backend.Server.Configs;

public class WhitelistRequestsConfigFile : ServerConfigFile<WhitelistRequestList>
{
	public WhitelistRequestsConfigFile(string filePath)
		: base(filePath)
	{
	}
}
