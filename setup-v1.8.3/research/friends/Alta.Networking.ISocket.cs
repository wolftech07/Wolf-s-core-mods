using System;
using System.Collections.Generic;
using Alta.Networking.Scripts.Player;
using Alta.NetworkingTransport;

namespace Alta.Networking;

public interface ISocket : IDisposable
{
	ITransportSocket TransportSocket { get; }

	int ConnectionCount { get; }

	IEnumerable<Connection> Connections { get; }

	ConnectionChannel DefaultChannel { get; set; }

	bool IsServer { get; }

	int OutgoingQueueSize { get; set; }

	event ConnectionEventHandler ConnectionCreated;

	event ConnectionEventHandler ConnectionDestroyed;

	event Action<ISocket> SocketDestroyed;

	Connection CreateConnection(ITransportConnection transportConnection, string ipAddress);

	Connection CreateConnection(string ipAddress, int port);

	void DestroyConnection(Connection connection, bool isFlushing = false);

	Connection GetConnection(int identifier);

	bool SendToAll(ConnectionChannel channel, MessageType type, SerializeConnectionMethod handler);

	bool SendToPlayers(ConnectionChannel channel, MessageType type, SerializeConnectionMethod handler, IEnumerable<IPlayer> players);

	void UpdateReceive();

	void UpdateSend();
}
