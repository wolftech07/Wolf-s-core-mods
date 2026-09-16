using Alta.Networking;
using Alta.Serialization;
using Newtonsoft.Json;
using TavernLib.Backend.Server.Configs;
using TavernLib.Services;
using TavernLib.Utils;

namespace TavernLib.Backend;

public class EntranceMessageHandler : IService
{
	public UserConfig.User LocalUser { get; private set; } = new UserConfig.User();

	public EntranceMessageHandler()
	{
		TavernEvents.SocketCreated.Subscribe(OnSocketCreated);
	}

	private void OnSocketCreated(ISocket socket)
	{
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Expected O, but got Unknown
		socket.ConnectionCreated += new ConnectionEventHandler(SetMessageHandlerForConnection);
	}

	private void SetMessageHandlerForConnection(Connection connection)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected O, but got Unknown
		TavernLogger.Msg("Socket created, setting up receive role handler", "SetMessageHandlerForConnection");
		if (!NetworkSceneManager.IsServer)
		{
			connection.SetHandler((MessageType)32, new SerializeConnectionMethod(OnRolesReceived));
		}
	}

	private void OnRolesReceived(Connection connection, Stream stream)
	{
		TavernLogger.Msg("Role message received", "OnRolesReceived");
		int num = 0;
		stream.SerializeInteger(ref num);
		LocalUser.Roles.Clear();
		TavernLogger.Msg($"Role count is {num}", "OnRolesReceived");
		for (int i = 0; i < num; i++)
		{
			string text = "";
			stream.SerializeString(ref text, (StringEncoding)0);
			TavernLogger.Msg("Deserialized role " + text, "OnRolesReceived");
			LocalUser.Roles.Add(text);
		}
		TavernLogger.Warn($"ReceiveRoles result: IsMod: {LocalUser.IsModerator}, Roles: {JsonConvert.SerializeObject((object)LocalUser.Roles)}", "OnRolesReceived");
	}
}
