using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alta.Global;
using Alta.Networking.Internal;
using Alta.Networking.Scripts.Player;
using Alta.NetworkingTransport;
using Alta.Serialization;
using NLog;
using UnityEngine;

namespace Alta.Networking;

public class Connection : IConnection
{
	private static readonly NLog.Logger logger = LogManager.GetCurrentClassLogger();

	private static readonly object sendLock = new object();

	private readonly int MaxServerQueueSize;

	private readonly int MaxConnectionQueueSize;

	private readonly string IpAddress;

	private bool IsQueuingSends;

	private readonly Dictionary<ConnectionChannel, OutgoingPacketManager> packetManagerMap = new Dictionary<ConnectionChannel, OutgoingPacketManager>();

	private readonly List<OutgoingPacketManager> outgoingPacketManagers = new List<OutgoingPacketManager>();

	private readonly Dictionary<MessageType, SerializeConnectionMethod> messageHandlers = new Dictionary<MessageType, SerializeConnectionMethod>();

	private IPlayer player;

	private bool isDisposed;

	private bool isFlooded;

	private ConnectionChannel[] channels;

	private readonly Dictionary<int, ConnectionReceiver> receivers = new Dictionary<int, ConnectionReceiver>();

	private bool isQueueLimitWarned;

	private int outgoingQueueSize;

	private CancellationTokenSource clientPauseDisconnectCancel;

	public string PlayerName { get; private set; } = "Unknown Player";

	public IPlayer Player
	{
		get
		{
			return player;
		}
		set
		{
			PlayerName = value?.UserInfo.Username;
			this.PlayerSet?.Invoke(value);
			player = value;
		}
	}

	public ConnectionChannel DefaultChannel { get; set; }

	public ISocket Socket { get; private set; }

	public bool IsApproved { get; private set; }

	public int Identifier { get; }

	private ITransportConnection TransportConnection { get; }

	public UserInfoAndRole UserInfo { get; set; }

	public bool IsDisposed => isDisposed;

	public event ConnectionEventHandler Approved;

	public event ConnectionEventHandler Disconnected;

	public event Action<IPlayer> PlayerSet;

	public event PacketSentHandler PacketSent;

	public Connection(ISocket socket, ITransportConnection connection, string ipAddress, ConnectionChannel[] channels)
	{
		Socket = socket;
		TransportConnection = connection;
		Identifier = connection.Id;
		IpAddress = ipAddress;
		this.channels = channels;
		foreach (ConnectionChannel connectionChannel in channels)
		{
			OutgoingPacketManager outgoingPacketManager = new OutgoingPacketManager(this, connectionChannel);
			packetManagerMap.Add(connectionChannel, outgoingPacketManager);
			outgoingPacketManagers.Add(outgoingPacketManager);
			receivers.Add(connectionChannel.Identifier, connectionChannel.CreateConnectionReceiver(this));
		}
		MaxServerQueueSize = GlobalSettings<NetworkingSettings>.Instance.MaxServerQueueSize;
		MaxConnectionQueueSize = GlobalSettings<NetworkingSettings>.Instance.MaxConnectionQueueSize;
	}

	public IEnumerator Ping(Action<float> callback, float timeOut = 5f)
	{
		return new Ping(IpAddress).GetPing(callback, timeOut);
	}

	public void FlushPacketManagers()
	{
		if (!IsApproved)
		{
			return;
		}
		foreach (OutgoingPacketManager outgoingPacketManager in outgoingPacketManagers)
		{
			outgoingPacketManager.SendAll();
		}
	}

	public void UpdatePacketManagers()
	{
		if (!IsApproved)
		{
			return;
		}
		outgoingQueueSize = TransportConnection.OutgoingQueueSize;
		foreach (OutgoingPacketManager outgoingPacketManager in outgoingPacketManagers)
		{
			outgoingPacketManager.Update();
		}
	}

	public void Approve()
	{
		if (!IsApproved)
		{
			IsApproved = true;
			OnApproved();
		}
	}

	private bool HasHandler(MessageType message)
	{
		return messageHandlers.ContainsKey(message);
	}

	public void ClearHandler(MessageType message)
	{
		messageHandlers.Remove(message);
	}

	public void ClearAllHandlers()
	{
		logger.Debug("Cleared all handlers on connection");
		messageHandlers.Clear();
	}

	public void SetHandler(MessageType message, SerializeConnectionMethod handler)
	{
		if (HasHandler(message))
		{
			logger.Warn("Overriding existing message handler. Call ClearHandler(message) to ignore.");
		}
		messageHandlers[message] = handler;
	}

	public void Disconnect(string reason)
	{
		logger.Info($"{UserInfo} was disconnected for: {reason}");
		if (IsApproved)
		{
			IsApproved = false;
		}
		Socket.DestroyConnection(this);
	}

	public bool Send(ConnectionChannel channel, MessageType type, SerializeConnectionMethod handler)
	{
		if (isDisposed)
		{
			return false;
		}
		if (channel == null)
		{
			channel = DefaultChannel;
		}
		OutgoingPacketManager outgoingPacketManager = packetManagerMap.SafeGetValue(channel, null);
		if (outgoingPacketManager == null)
		{
			logger.Error("Unknown channel {0}", channel);
			return false;
		}
		if (IsQueuingSends)
		{
			return outgoingPacketManager.SendWithQueued(type, handler);
		}
		return outgoingPacketManager.Send(type, handler);
	}

	public bool Send(ConnectionChannel channel, StreamWriter writer, int byteCount)
	{
		if (isDisposed)
		{
			return false;
		}
		if (channel == null)
		{
			channel = DefaultChannel;
		}
		OutgoingPacketManager outgoingPacketManager = packetManagerMap.SafeGetValue(channel, null);
		if (outgoingPacketManager == null)
		{
			logger.Error("Unknown channel {0}", channel);
			return false;
		}
		if (IsQueuingSends)
		{
			return outgoingPacketManager.SendWithQueued(writer, byteCount);
		}
		return outgoingPacketManager.Send(writer, byteCount);
	}

	public void StartQueuing()
	{
		IsQueuingSends = true;
		logger.Info("Starting to Queue message on connection: {0}", this);
	}

	public void StopQueuing()
	{
		IsQueuingSends = false;
		foreach (OutgoingPacketManager outgoingPacketManager in outgoingPacketManagers)
		{
			outgoingPacketManager.SendAllQueued();
		}
	}

	internal bool InternalSend(byte[] data, int byteCount, ConnectionChannel channel, out INetworkTransportResult result, out bool isErrorSilent)
	{
		if ((object)channel == null)
		{
			channel = DefaultChannel;
		}
		isErrorSilent = false;
		if (!NetworkManager.IsTransportStarted)
		{
			result = NetworkTransportResult.Success;
			return false;
		}
		if (QueueLimitReached())
		{
			result = NetworkTransportResult.NoResources;
			isErrorSilent = true;
			return false;
		}
		lock (sendLock)
		{
			result = TransportConnection.Send(data, byteCount, channel.Identifier);
		}
		if (result.IsSuccess)
		{
			NetworkManager.SentPackets++;
			Socket.OutgoingQueueSize++;
		}
		else
		{
			NetworkManager.FailedPackets++;
		}
		this.PacketSent?.Invoke(this, data, byteCount, channel, result);
		MessageProcessor.MessageLogger.Trace("send packet, socket: {0}, connection: {1}, channel: {2}, byteCount: {3}, success: {4}, error: {5}", Socket.TransportSocket, Identifier, channel.Identifier, byteCount, result.IsSuccess, result.Error);
		return result.IsSuccess;
	}

	private bool QueueLimitReached()
	{
		if (TransportConnection.IsFlooded)
		{
			NetworkManager.FloodedPackets++;
			return true;
		}
		bool flag = Socket.OutgoingQueueSize >= MaxServerQueueSize;
		bool flag2 = outgoingQueueSize >= MaxConnectionQueueSize;
		if (flag || flag2)
		{
			if (!isQueueLimitWarned)
			{
				isQueueLimitWarned = true;
				string text = (flag ? "server" : $"connection - id: {TransportConnection.Id}");
				logger.Warn("Outgoing queue limit hit for this frame by " + text);
			}
			NetworkManager.QueueLimitPackets++;
			return true;
		}
		if (isQueueLimitWarned)
		{
			logger.Info($"Queue is fine for connection - id: {TransportConnection.Id}");
			isQueueLimitWarned = false;
		}
		return false;
	}

	public void ReceivePacket(int channelIdentifier, ArraySegment<byte> data)
	{
		receivers[channelIdentifier].ReceivePacket(Socket.TransportSocket, data);
	}

	public void ReceivePacketOnServer(ArraySegment<byte> data)
	{
		while (data.Count > 0)
		{
			MessageType messageType;
			int num = MessageProcessor.ProcessSingleMessageFromData(this, data, null, out messageType);
			int count = data.Count - num;
			int offset = data.Offset + num;
			data = new ArraySegment<byte>(data.Array, offset, count);
		}
	}

	internal void HandleConnectionPause(Connection connection, Stream stream)
	{
		bool value = false;
		stream.SerializeBool(ref value);
		logger.Info("Received connection pause, connection: {0}, isPausing: {1}", connection, value);
		if (value)
		{
			connection.StartQueuing();
			clientPauseDisconnectCancel?.Cancel();
			clientPauseDisconnectCancel = new CancellationTokenSource();
			DisconnectAfterTimeout(connection);
		}
		else
		{
			connection.StopQueuing();
			clientPauseDisconnectCancel?.Cancel();
		}
	}

	private async void DisconnectAfterTimeout(Connection connection)
	{
		TimeSpan delay = TimeSpan.FromMinutes(1.0);
		try
		{
			await Task.Delay(delay, clientPauseDisconnectCancel.Token);
		}
		catch (TaskCanceledException)
		{
			logger.Info("Cancelling running user disconnect due to client pause: {0}", connection);
			return;
		}
		logger.Warn("User has been paused for {0} seconds, Disconnecting the connection: {1}", delay.TotalSeconds, connection);
		connection.Disconnect("Pause Timeout");
	}

	public void ProcessExistingQueues()
	{
		ConnectionChannel[] array = channels;
		foreach (ConnectionChannel connectionChannel in array)
		{
			receivers[connectionChannel.Identifier].ProcessExistingQueues();
		}
	}

	public void Dispose()
	{
		if (isDisposed)
		{
			return;
		}
		if (NetworkManager.IsTransportStarted)
		{
			OnDisconnected();
			logger.Debug("Disconnect sending from {0}", Socket.IsServer ? "Server" : "Client");
			INetworkTransportResult networkTransportResult = TransportConnection.Disconnect();
			if (networkTransportResult.IsSuccess)
			{
				logger.Debug("Disconnect successful");
			}
			else
			{
				logger.Debug("Disconnected failed: " + networkTransportResult.Error);
			}
		}
		foreach (OutgoingPacketManager outgoingPacketManager in outgoingPacketManagers)
		{
			outgoingPacketManager.Dispose();
		}
		foreach (ConnectionReceiver value in receivers.Values)
		{
			value.Dispose();
		}
		messageHandlers.Clear();
		outgoingPacketManagers.Clear();
		packetManagerMap.Clear();
		receivers.Clear();
		isDisposed = true;
	}

	private void OnApproved()
	{
		this.Approved?.Invoke(this);
	}

	private void OnDisconnected()
	{
		try
		{
			this.Disconnected?.Invoke(this);
		}
		catch (Exception exception)
		{
			logger.Error(exception, "Encountered error on connection disconnect event");
		}
	}

	internal bool TryGetMessageHandler(MessageType type, out SerializeConnectionMethod handler)
	{
		return messageHandlers.TryGetValue(type, out handler);
	}

	public override string ToString()
	{
		return UserInfo?.ToString() ?? Identifier.ToString();
	}
}
