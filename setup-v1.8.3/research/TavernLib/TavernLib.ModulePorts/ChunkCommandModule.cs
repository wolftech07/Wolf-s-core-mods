using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alta;
using Alta.Api.DataTransferModels.Extensions;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Caves;
using Alta.Chunks;
using Alta.Console;
using Alta.Console.Commands;
using Alta.Global;
using Alta.Networking;
using Alta.Networking.Internal;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.Utilities;
using NLog;
using TableParser;
using UnityEngine;

namespace TavernLib.ModulePorts;

[PlayOnly]
[Module("chunks", "Various chunk console commands")]
public static class ChunkCommandModule
{
	private static Logger logger = LogManager.GetCurrentClassLogger();

	private static StringBuilder writer = new StringBuilder();

	[Command("set-timed", "set settings for timed receiver")]
	private static void SetReceiveAmount(float processingPercent = 0.1f, float maxAllowance = 5f, float minNeeded = 2f)
	{
		GlobalSettings<ConnectionReceiveSettings>.Instance.ProcessingPercent = processingPercent;
		GlobalSettings<ConnectionReceiveSettings>.Instance.MaxProcessingAllowance = maxAllowance;
		GlobalSettings<ConnectionReceiveSettings>.Instance.MinAllowanceToProcess = minNeeded;
	}

	[Command("set-receive-count", "set min packets to receive per frame")]
	private static void SetReceiveAmount(int receiveAmount)
	{
		GlobalSettings<ConnectionReceiveSettings>.Instance.MinReliableMessagePerFrame = receiveAmount;
	}

	[Command("set-receive-ms", "set max ms to spend receiving packets per frame")]
	private static void SetReceiveMs(float receiveDuration)
	{
		GlobalSettings<ConnectionReceiveSettings>.Instance.MaxReliableMessageProcessingDurationMs = receiveDuration;
	}

	[ServerOnly]
	[Command("set-sync-amount", "set prefabs to sync each frame")]
	private static void SetSyncNumber(int syncAmount)
	{
		ChunkSync.SendCount = syncAmount;
	}

	[ServerOnly]
	[Command("set-sync-interval", "set interval to wait in between entity spawns to client")]
	private static void SetSyncInterval(float interval)
	{
		ChunkSync.SendInterval = interval;
	}

	[WithPermissionOnly(new string[] { "debug_features" })]
	[Command("set-static-load", "set static pointers to load load or not")]
	private static void SetStaticLoad(bool isLoading)
	{
		StaticChunkContentManager.IsLoadingPointers = isLoading;
	}

	[WithPermissionOnly(new string[] { "debug_features" })]
	[Command("set-static-sequence", "set static pointers to load sequentially")]
	private static void SetStaticSequential(bool loadSequentially)
	{
		StaticChunkContentManager.IsLoadingSequentially = loadSequentially;
	}

	[WithPermissionOnly(new string[] { "debug_features" })]
	[Command("set-static-per-frame", "set amount of static pointers to load per frame")]
	private static void SetPointersToLoadPerFrame(int numberToLoadPerFrame)
	{
		StaticChunkContentManager.IsLoadingSequentially = false;
		StaticChunkContentManager.PointersToLoadPerFrame = numberToLoadPerFrame;
	}

	[WithPermissionOnly(new string[] { "debug_features" })]
	[Command("set-load", "set load and unload distances")]
	private static void SetLoadDistances(float loadDistance, float unloadDistance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		CaveSettings<StreamingSettings>.Instance.LoadBounds = Vector3.one * loadDistance;
		CaveSettings<StreamingSettings>.Instance.UnloadBounds = Vector3.one * unloadDistance;
		logger.Trace<Vector3, Vector3>("Set load distance to: {0} and unload to: {1}", CaveSettings<StreamingSettings>.Instance.LoadBounds, CaveSettings<StreamingSettings>.Instance.UnloadBounds);
	}

	[WithPermissionOnly(new string[] { "debug_features" })]
	[Command("set-load", "set load and unload distances")]
	private static void SetLoadDistances(Vector3 loadDistance, Vector3 unloadDistance)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		CaveSettings<StreamingSettings>.Instance.LoadBounds = loadDistance;
		CaveSettings<StreamingSettings>.Instance.UnloadBounds = unloadDistance;
		logger.Trace<Vector3, Vector3>("Set load distance to: {0} and unload to: {1}", loadDistance, unloadDistance);
	}

	[Command("entities", "List all the entities in a chunk")]
	private static IEnumerable<NetworkEntityInfo> ListEntities(Chunk chunk)
	{
		return chunk.Entities.Entities.Select((NetworkEntity item) => NetworkEntityInfo.op_Implicit(item));
	}

	[Command("wipe", "Reverts a chunks save to the default save (ie everything in it is wiped)")]
	private static async Task<string> ResetChunk(Chunk chunk)
	{
		Player[] players = null;
		if (chunk.IsLoaded)
		{
			players = chunk.Players.Items.ToArray();
			Player[] array = players;
			for (int i = 0; i < array.Length; i++)
			{
				PlayerCommandModule.Teleport(new PlayerList((IEnumerable<IPlayer>)EnumerableExtensions.AsEnumerable<Player>(array[i])), (SpawnAreaIdentifier)50);
			}
		}
		chunk.IsForceLoaded = false;
		await Task.Delay(1000);
		await chunk.Save();
		await chunk.ClearSave();
		if (players != null)
		{
			Player[] array2 = players;
			for (int j = 0; j < array2.Length; j++)
			{
				PlayerCommandModule.Teleport(new PlayerList((IEnumerable<IPlayer>)EnumerableExtensions.AsEnumerable<Player>(array2[j])), (SpawnAreaIdentifier)1);
			}
		}
		return "Wiped chunk: " + chunk.ChunkIdentifier;
	}

	[Command("print-all", "Prints the status of all chunks into a file")]
	private static FileDownload PrintAllChunkDetails()
	{
		string contents = LogChunkDetails(Chunk.ChunksByIndex.Values);
		string path = FileDownload.GetPath("chunk details.csv");
		logger.Trace("Wrote the details of all chunks to file: {0}", path);
		File.WriteAllText(path, contents);
		return FileDownload.op_Implicit("chunk details.csv");
	}

	[Command("print-loaded", "Prints the status of all chunks into a file")]
	private static FileDownload PrintLoadedChunkDetails()
	{
		string contents = LogChunkDetails(Chunk.ChunksByIndex.Values.Where((Chunk chunk) => chunk.IsLoaded));
		string path = FileDownload.GetPath("loaded chunk details.csv");
		logger.Trace("Wrote the details of loaded chunks to file: {0}", path);
		File.WriteAllText(path, contents);
		logger.Trace("Loaded Chunks:");
		logger.Trace(GetChunkInfoAsTable(Chunk.ChunksByIndex.Values));
		return FileDownload.op_Implicit("loaded chunk details.csv");
	}

	private static string GetChunkInfoAsTable(IEnumerable<Chunk> chunks)
	{
		return TableParserExtensions.ToStringTable<Chunk>(chunks.Where((Chunk chunk) => chunk.IsLoaded), new string[8] { "Chunk Name", "Identifier", "Index", "Entities", "Players", "Loaded", "ForceLoaded", "FullyLoaded" }, new Func<Chunk, object>[8]
		{
			(Chunk c) => ((Object)c).name,
			(Chunk c) => c.ChunkIdentifier,
			(Chunk c) => c.Index,
			(Chunk c) => c.Entities.Count,
			(Chunk c) => c.Players.Count,
			(Chunk c) => c.IsLoaded,
			(Chunk c) => c.IsForceLoaded,
			(Chunk c) => c.IsFullyLoaded
		});
	}

	private static string LogChunkDetails(IEnumerable<Chunk> chunks)
	{
		writer.Clear();
		writer.AppendLine("name,identifier,index,entities count,player count,loaded,forced,fully loaded");
		foreach (Chunk chunk in chunks)
		{
			writer.Append(((Object)chunk).name);
			writer.Append(",");
			writer.Append(chunk.ChunkIdentifier);
			writer.Append(",");
			StringBuilder stringBuilder = writer;
			ChunkEntityManager entities = chunk.Entities;
			stringBuilder.Append((entities != null) ? new int?(entities.Count) : ((int?)null));
			writer.Append(",");
			StringBuilder stringBuilder2 = writer;
			ChunkPlayerManager players = chunk.Players;
			stringBuilder2.Append((players != null) ? new int?(players.Count) : ((int?)null));
			writer.Append(",");
			writer.Append(chunk.IsLoaded);
			writer.Append(",");
			writer.Append(chunk.IsForceLoaded);
			writer.Append(",");
			writer.Append(chunk.IsFullyLoaded);
			writer.AppendLine();
		}
		return writer.ToString();
	}

	[Command("force-load", "Force load a chunk")]
	[ServerOnly]
	private static async Task SetChunkLoaded(Chunk chunk, bool isForceLoaded)
	{
		if (isForceLoaded)
		{
			await chunk.ForceLoad();
		}
		else
		{
			chunk.IsForceLoaded = false;
		}
		logger.Trace<string, bool>("Set chunk: {0} to force loaded: {1}", chunk.ChunkIdentifier, isForceLoaded);
	}

	[Command("info", "Display info about a chunk")]
	private static void PrintChunkInfo(Chunk chunk)
	{
		logger.Trace(GetChunkInfoAsTable(EnumerableExtensions.AsEnumerable<Chunk>(chunk)));
	}

	[Command("loadall", "Load All Chunks")]
	[ServerOnly]
	private static void LoadAll(bool loaded = true)
	{
		foreach (LocationChunk allLocationChunk in LocationChunk.AllLocationChunks)
		{
			if (allLocationChunk is OverworldChunk)
			{
				((Chunk)allLocationChunk).IsForceLoaded = loaded;
			}
		}
	}

	[Command("merge", "Merge the current save with the default one replacing a provided hash")]
	[ServerOnly]
	[WithPermissionOnly(new string[] { "debug_features" })]
	private static async Task Merge(NetworkPrefab targetprefab)
	{
		ChunkPhysicsResolver obj = Object.FindObjectOfType<ChunkPhysicsResolver>();
		if (obj != null)
		{
			obj.Stop();
		}
		foreach (Player allPlayer2 in Player.AllPlayers)
		{
			Player allPlayer = allPlayer2;
			allPlayer.Kick("Chunk Merge");
		}
		ServerJoinLock serverLock = new ServerJoinLock
		{
			message = "Server is Wiping",
			level = 10
		};
		ServerHandler.Current.ServerLocks.Add(serverLock);
		try
		{
			await ServerHandler.Current.ServerApiAccess.StopAsync(new ShutdownReason("merge operation", true));
			await ApiAccess.ApiClient.ServerClient.SetServerAsOfflineAsync(ServerHandler.Current.ServerInfo.Identifier, false);
		}
		catch (Exception ex)
		{
			logger.Error(ex, "Failed ending server session");
		}
		PrefabManager.PrepareSpawnSetups();
		await AltaFile.FinishAllAsync();
		List<Task> list = new List<Task>();
		foreach (LocationChunk allLocationChunk in LocationChunk.AllLocationChunks)
		{
			OverworldChunk overworldChunk = (OverworldChunk)(object)((allLocationChunk is OverworldChunk) ? allLocationChunk : null);
			if (overworldChunk != null)
			{
				ChunkContentManager contentManager = ((Chunk)overworldChunk).ContentManager;
				list.Add(((ServerChunkContentManager)((contentManager is ServerChunkContentManager) ? contentManager : null)).DynamicManager.MergeWithResources(targetprefab.Hash));
			}
		}
		await Task.WhenAll(list);
		await AltaFile.FinishAllAsync();
		await Task.Delay(5000);
		ApplicationManager.ExternalOnApplicationQuit(new ShutdownReason("merge operation", true));
	}

	[WithPermissionOnly(new string[] { "debug_features" })]
	[Command("merge", "Merge the current save with the default one replacing a provided hash")]
	[ServerOnly]
	private static async Task Merge(params uint[] prefabHashes)
	{
		logger.Warn("MERGING!");
		ChunkPhysicsResolver obj = Object.FindObjectOfType<ChunkPhysicsResolver>();
		if (obj != null)
		{
			obj.Stop();
		}
		foreach (Player allPlayer2 in Player.AllPlayers)
		{
			Player allPlayer = allPlayer2;
			allPlayer.Kick("Chunk Merge");
		}
		ServerJoinLock serverLock = new ServerJoinLock
		{
			message = "Server is Wiping",
			level = 10
		};
		ServerHandler.Current.ServerLocks.Add(serverLock);
		ServerHandler.Current.ServerConfig.Settings.IsAutoSaving = false;
		StreamerManager.Instance.StartAutoSave();
		try
		{
			await ServerHandler.Current.ServerApiAccess.StopAsync(new ShutdownReason("merge operation", true));
			await ApiAccess.ApiClient.ServerClient.SetServerAsOfflineAsync(ServerHandler.Current.ServerInfo.Identifier, false);
		}
		catch (Exception ex)
		{
			logger.Error(ex, "Failed ending server session");
		}
		PrefabManager.PrepareSpawnSetups();
		await AltaFile.FinishAllAsync();
		foreach (LocationChunk allLocationChunk in LocationChunk.AllLocationChunks)
		{
			OverworldChunk val;
			OverworldChunk overworldChunk2 = (val = (OverworldChunk)(object)((allLocationChunk is OverworldChunk) ? allLocationChunk : null));
			OverworldChunk overworldChunk3 = val;
			if (overworldChunk2 != null)
			{
				foreach (uint hash in prefabHashes)
				{
					ChunkContentManager contentManager = ((Chunk)overworldChunk3).ContentManager;
					await ((ServerChunkContentManager)((contentManager is ServerChunkContentManager) ? contentManager : null)).DynamicManager.MergeWithResources(hash);
				}
			}
		}
		logger.Warn("CHUNKS DONE, LETS BACK UP!");
		await AltaFile.FinishAllAsync();
		RemoteFiles.BackupAllChanged(true);
		logger.Warn("I'M DONE PEEPS!");
		await Task.Delay(5000);
		ApplicationManager.ExternalOnApplicationQuit(new ShutdownReason("merge operation", true));
	}

	[Command("check-wipe", "Check which chunks need to wipe")]
	[ServerOnly]
	private static IEnumerable<ChunkInfo> CheckWipe()
	{
		return from chunk in WipeManager.ChunksToPreWipe()
			select ChunkInfo.op_Implicit(chunk);
	}
}
