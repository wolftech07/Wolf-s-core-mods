namespace TavernLib.Backend.Server.Configs;

public class ServerSettingsConfig : ServerConfigFile<ServerSettings>
{
	public ServerSettingsConfig(string filePath)
		: base(filePath)
	{
	}

	public override void ReadFromFile()
	{
		base.ReadFromFile();
		if (string.IsNullOrWhiteSpace(base.LastRead.CommunityListingToken))
		{
			base.LastRead.CommunityListingToken = BackendUtils.TokenUrlSafe(24);
			WriteToFile();
		}
	}
}
