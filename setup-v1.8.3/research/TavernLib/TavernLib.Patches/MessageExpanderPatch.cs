using System;
using Alta.Networking;
using Alta.Networking.Internal;
using Alta.Serialization;
using HarmonyLib;

namespace TavernLib.Patches;

[HarmonyPatch]
public class MessageExpanderPatch
{
	[HarmonyPatch(typeof(MessageProcessor), "ProcessSingleMessageFromData")]
	[HarmonyPrefix]
	public static bool LogMessageHandler(Connection connection, ArraySegment<byte> data, ConnectionChannel channel, out MessageType messageType, ref int __result)
	{
		Buffer.BlockCopy(data.Array, data.Offset, MessageProcessor.size, 0, 2);
		ushort num = MessageProcessor.size[0];
		int srcOffset = data.Offset + 2;
		Buffer.BlockCopy(data.Array, srcOffset, MessageProcessor.receiveBuffer, 0, num);
		MessageProcessor.messageReader.Initialize(MessageProcessor.receiveBuffer, (int)num);
		uint num2 = 0u;
		((Stream)MessageProcessor.messageReader).SerializeBits(ref num2, 8);
		messageType = (MessageType)num2;
		MessageProcessor.HandleMessage(connection, (Stream)(object)MessageProcessor.messageReader, messageType, channel);
		__result = num + 2;
		return false;
	}

	[HarmonyPatch(typeof(OutgoingPacketManager), "StartSerialize")]
	[HarmonyPrefix]
	public static bool StartSerializeCustom(Connection connection, MessageType type, ref StreamWriter __result)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Expected I4, but got Unknown
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		int key = ((connection != null) ? connection.Identifier : 0);
		if (!OutgoingPacketManager.writerMap.TryGetValue(key, out var value))
		{
			value = new StreamWriter(new uint[(OutgoingPacketManager.maximumPacketSize + 3) / 4]);
			OutgoingPacketManager.writerMap[key] = value;
		}
		uint messageType = (uint)(int)type;
		value.Clear();
		((Stream)value).SerializeBits(ref messageType, 8);
		value.MessageType = messageType;
		__result = value;
		return false;
	}
}
