using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Chunks;
using Alta.Console.Commands;
using Alta.Networking;
using UnityEngine;

namespace TavernLib.Patches;

public static class SelectFixPatch
{
	public delegate IEnumerable<NetworkEntityInfo> FindObjects(Player player, float distance = 10f);

	public static IEnumerable<NetworkEntityInfo> SelectionFix(FindObjects orig, Player player, float distance = 10f)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if ((Object)(object)((player != null) ? player.PlayerController : null) == (Object)null)
			{
				TavernLogger.Warn("PlayerController was null when using 'select find' command", "SelectionFix");
				return Array.Empty<NetworkEntityInfo>().AsEnumerable();
			}
			HashSet<NetworkEntity> hashSet = new HashSet<NetworkEntity>();
			Vector3 position = ((Component)player.PlayerController).transform.position;
			foreach (NetworkEntity item in Chunk.ChunksByIndex.Values.SelectMany((Chunk chunk) => chunk.Entities.Entities))
			{
				if ((Object)(object)item == (Object)null)
				{
					TavernLogger.Warn("NetworkEntity " + item.SafeName + " was null and isn't?? " + item.Chunk.ChunkIdentifier, "SelectionFix");
				}
				else if (Vector3.Distance(((Component)item).transform.position, position) <= distance)
				{
					hashSet.Add(item.PrefabRoot);
				}
			}
			return hashSet.Select((NetworkEntity entity) => NetworkEntityInfo.op_Implicit(entity));
		}
		catch (Exception arg)
		{
			TavernLogger.Error($"Error when using select find! {arg}", "SelectionFix");
			throw;
		}
	}
}
