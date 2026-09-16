using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ATT.PlayerStates;
using Alta;
using Alta.Alchemy;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Character;
using Alta.Console;
using Alta.Console.Commands;
using Alta.Global;
using Alta.Impact;
using Alta.Networking.Scripts.Player;
using Alta.Networking.Servers;
using Alta.StatSystem;
using Alta.Utilities;
using NLog;
using TavernLib.Backend.Api;
using TavernLib.Backend.Server.Configs;
using TavernLib.Services;
using UnityEngine;

namespace TavernLib.ModulePorts;

[PlayOnly]
[Module("player", "Commands to interact with a player.")]
public static class PlayerCommandModule
{
	[ServerOnly]
	[Module("inventory", "inventory related commands")]
	public static class InventoryModule
	{
		[ServerOnly]
		[Command("save", "Saves a players inventory")]
		private static async Task<string> Save(UserInfoAccess user)
		{
			if (!(await user.IsValid()))
			{
				CommandService.ThrowError("Invalid player", Array.Empty<object>());
			}
			int id = await user.GetIdentifier();
			Player player = Player.GetPlayer(id);
			if ((Object)(object)player != (Object)null)
			{
				player.WriteSave(false);
				logger.Trace("Saving inventory for online player " + id);
				return GetData(player.Save);
			}
			IAltaFile altaFile = await ServerHandler.Current.SaveUtility.PlayerSaveUtility.Load(id);
			if (((altaFile != null) ? altaFile.Content : null) == null)
			{
				CommandService.ThrowError("No save file found", Array.Empty<object>());
			}
			logger.Trace("Saving inventory for offline player " + id);
			string result = GetData((PlayerSave)altaFile.Content);
			if ((Object)(object)Player.GetPlayer(id) == (Object)null)
			{
				await altaFile.QueueUnloadAsync();
			}
			return result;
			static string GetData(PlayerSave save)
			{
				byte[] array = new byte[save.PrefabData.Length * 4];
				Buffer.BlockCopy(save.PrefabData, 0, array, 0, array.Length);
				return Encoding.UTF8.GetString(array);
			}
		}

		[ServerOnly]
		[Command("load", "Loads a players inventory")]
		private static async Task Load(UserInfoAccess user, string save)
		{
			if (!(await user.IsValid()))
			{
				CommandService.ThrowError("Invalid player", Array.Empty<object>());
			}
			int id = await user.GetIdentifier();
			if ((Object)(object)Player.GetPlayer(id) != (Object)null)
			{
				CommandService.ThrowError("Can't load inventory for online players", Array.Empty<object>());
			}
			IAltaFile obj = await ServerHandler.Current.SaveUtility.PlayerSaveUtility.Load(id);
			if ((Object)(object)Player.GetPlayer(id) != (Object)null)
			{
				CommandService.ThrowError("Can't load inventory for online players", Array.Empty<object>());
			}
			if (((obj != null) ? obj.Content : null) == null)
			{
				CommandService.ThrowError("Can't load inventory for players without a save", Array.Empty<object>());
			}
			logger.Trace("Loading inventory for " + id);
			byte[] bytes = Encoding.UTF8.GetBytes(save);
			uint[] array = new uint[bytes.Length / 4];
			Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
			IAltaFileFormat content = obj.Content;
			((PlayerSave)((content is PlayerSave) ? content : null)).PrefabData = array;
			await obj.QueueUnloadAsync();
		}

		[ServerOnly]
		[Command(null, "Views an online players inventory")]
		private static IEnumerable<Inventory> Inventory(PlayerList players)
		{
			logger.Trace("Getting inventories.");
			foreach (Player player2 in players)
			{
				Player player = player2;
				if ((Object)(object)((player != null) ? player.PlayerController : null) == (Object)null)
				{
					yield return null;
				}
				yield return new Inventory((IPlayer)(object)player);
			}
		}
	}

	public enum StatApplicationType
	{
		Base,
		Min,
		Max
	}

	public class StatValue
	{
		public HashedItemInfo Definition { get; set; }

		public float Value { get; set; }

		public float Base { get; set; }

		public static implicit operator StatValue(Stat stat)
		{
			return new StatValue
			{
				Definition = HashedItemInfo.op_Implicit((HashedGeneralValue)(object)stat.Definition),
				Value = stat.Value,
				Base = stat.Base
			};
		}
	}

	public class StatDefinitionInfo
	{
		public string Name { get; set; }

		public float Min { get; set; }

		public float Max { get; set; }

		public float Value { get; set; }

		public static implicit operator StatDefinitionInfo(StatDefinitionApplication stat)
		{
			return new StatDefinitionInfo
			{
				Name = stat.Name,
				Max = stat.DefaultMaximum,
				Min = stat.DefaultMinimum
			};
		}

		public static implicit operator StatDefinitionInfo(Stat stat)
		{
			return new StatDefinitionInfo
			{
				Name = stat.Definition.Name,
				Max = stat.Maximum,
				Min = stat.Minimum,
				Value = stat.Value
			};
		}
	}

	[Module("progression", "Progression related commands")]
	public static class ProgressionCommandModule
	{
		private static Logger logger = LogManager.GetCurrentClassLogger();

		private static Dictionary<int, ProgressionSlot> available = new Dictionary<int, ProgressionSlot>();

		private static Player lastPlayer;

		[Command("list", "List the available paths")]
		[ServerOnly]
		private static void List()
		{
			int length = typeof(ProgressionPath).GetEnumValues().Length;
			logger.Debug("Available Paths:");
			for (int i = 0; i < length; i++)
			{
				logger.Debug<ProgressionPath>("  - {0}", (ProgressionPath)i);
			}
		}

		[Command("pathxp", "Gain Experience on a Profession Path")]
		[ServerOnly]
		private static void GainPathXp(PlayerList players, ProgressionPath path, int xp)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Expected O, but got Unknown
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			foreach (Player player in players)
			{
				Player val = player;
				uint num = 0u;
				if (val.ProgressionManager.PathExperiences.TryGetValue(path, out var value))
				{
					num = value.CurrentLevel;
				}
				PlayerProgressionManager.GainExperience((IPlayer)(object)val, path, xp);
				uint currentLevel = val.ProgressionManager.PathExperiences[path].CurrentLevel;
				logger.Debug("Player {0} got {1} xp on {2}, going from level {3} to level {4}", new object[5]
				{
					val.UserInfo.Username,
					xp,
					path,
					num,
					currentLevel
				});
			}
		}

		[Command("allxp", "Gain Experience on ALL Profession Paths")]
		[ServerOnly]
		private static void GainXp(PlayerList players, int xp)
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Expected O, but got Unknown
			int length = typeof(ProgressionPath).GetEnumValues().Length;
			foreach (Player player in players)
			{
				Player val = player;
				for (int i = 0; i < length; i++)
				{
					uint num = 0u;
					if (val.ProgressionManager.PathExperiences.TryGetValue((ProgressionPath)i, out var value))
					{
						num = value.CurrentLevel;
					}
					if (PlayerProgressionManager.GainExperience((IPlayer)(object)val, (ProgressionPath)i, xp))
					{
						uint currentLevel = val.ProgressionManager.PathExperiences[(ProgressionPath)i].CurrentLevel;
						logger.Debug("Player {0} got {1} xp on {2}, going from level {3} to level {4}", new object[5]
						{
							val.UserInfo.Username,
							xp,
							(object)(ProgressionPath)i,
							num,
							currentLevel
						});
					}
				}
			}
		}

		[Command("pathlevelup", "Gain a level on a Profession Path")]
		[ServerOnly]
		private static void GainPathLevel(PlayerList players, ProgressionPath path)
		{
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Expected O, but got Unknown
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_006e: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			PathExperience value = null;
			foreach (Player player in players)
			{
				Player val = player;
				uint num = 0u;
				uint num2 = 0u;
				if (val.ProgressionManager.PathExperiences.TryGetValue(path, out value))
				{
					num = GlobalSettings<ProgressionSettings>.Instance.ExperiencePerLevel(value.CurrentLevel) - value.CurrentExperience;
					num2 = value.CurrentLevel;
				}
				else
				{
					num = GlobalSettings<ProgressionSettings>.Instance.ExperiencePerLevel(0u);
				}
				PlayerProgressionManager.GainExperience((IPlayer)(object)val, path, (float)num);
				uint num3 = num2 + 1;
				logger.Debug("Player {0} got {1} xp on {2}, going from level {3} to level {4}", new object[5]
				{
					val.UserInfo.Username,
					num,
					path,
					num2,
					num3
				});
			}
		}

		[Command("checkallxp", "List all paths and their xp")]
		[ServerOnly]
		private static void CheckAllXP(PlayerList players)
		{
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Expected O, but got Unknown
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			int length = typeof(ProgressionPath).GetEnumValues().Length;
			foreach (Player player in players)
			{
				Player val = player;
				for (int i = 0; i < length; i++)
				{
					ProgressionPath val2 = (ProgressionPath)i;
					uint num = 0u;
					uint num2 = 0u;
					if (val.ProgressionManager.PathExperiences.TryGetValue(val2, out var value))
					{
						num = value.CurrentExperience;
						num2 = value.CurrentLevel;
					}
					logger.Debug("Player {0} on {1} has {2} xp, next level at {3} xp", new object[4]
					{
						val.UserInfo.Username,
						val2,
						num,
						GlobalSettings<ProgressionSettings>.Instance.ExperiencePerLevel(num2)
					});
				}
			}
		}

		[Command("clearpath", "Clear all progress in a given path")]
		private static void ClearPath(Player player, ProgressionPath path)
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			if (player.ProgressionManager.PathExperiences.TryGetValue(path, out var value))
			{
				value.DebugClearSlots();
			}
			player.ProgressionManager.UpdateCounts();
			logger.Debug<string, ProgressionPath>("Player {0} has cleared progress on {1}", player.UserInfo.Username, path);
		}

		[Command("clearall", "Clear all progress")]
		private static void ClearAll(PlayerList players)
		{
			//IL_0026: Unknown result type (might be due to invalid IL or missing references)
			//IL_002c: Expected O, but got Unknown
			int length = typeof(ProgressionPath).GetEnumValues().Length;
			foreach (Player player in players)
			{
				Player val = player;
				for (int i = 0; i < length; i++)
				{
					if (val.ProgressionManager.PathExperiences.TryGetValue((ProgressionPath)i, out var value))
					{
						value.DebugClearSlots();
					}
				}
				val.ProgressionManager.UpdateCounts();
				logger.Debug("Player {0} has cleared progress on all paths", val.UserInfo.Username);
			}
		}

		[Command("showskills", "Show available skills for player")]
		private static void ShowAvailableSkills(Player player)
		{
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			//IL_004e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0060: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a7: Invalid comparison between Unknown and I4
			available.Clear();
			lastPlayer = player;
			int num = 0;
			logger.Trace("Available skills for {0}", player.UserInfo.Username);
			int length = typeof(ProgressionPath).GetEnumValues().Length;
			for (int i = 0; i < length; i++)
			{
				ProgressionPath val = (ProgressionPath)i;
				ProfessionSkillTree professionTree = ProfessionSkillTree.GetProfessionTree(val);
				logger.Trace<ProgressionPath>("{0} skills:", val);
				if (player.ProgressionManager.PathExperiences.TryGetValue(val, out var value))
				{
					foreach (ProgressionSlot slot in professionTree.Slots)
					{
						if ((int)value.IsSlotAvailable(slot) == 9)
						{
							available[num] = slot;
							logger.Trace<int, string>("{0} {1}", num, slot.Name);
							num++;
						}
					}
					continue;
				}
				foreach (ProgressionSlot slot2 in professionTree.Slots)
				{
					if (slot2.Cost == 0 && slot2.Dependencies.Count == 0)
					{
						available[num] = slot2;
						logger.Trace<int, string>("{0} {1}", num, slot2.Name);
						num++;
					}
				}
			}
		}

		[Command("buyskill", "Buy specified skill")]
		private static void BuySkill(int slotIndex, bool isConsumingExperience = true)
		{
			if ((Object)(object)lastPlayer != (Object)null)
			{
				if (available.TryGetValue(slotIndex, out var value))
				{
					lastPlayer.ProgressionManager.UnlockSlot(value, isConsumingExperience, false);
					logger.Trace<string, string>("{0} skill bought for {1}", value.Name, lastPlayer.UserInfo.Username);
				}
			}
			else
			{
				logger.Trace("Please user showskills first");
			}
		}

		[Command("offlinelevels", "Give levels to players even if they are offline")]
		[Alias(new string[] { "offlvl", "lvl" })]
		private static async Task AddOfflineLevels(UserInfoAccess userInfo, ProgressionPath path, int levels)
		{
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			if (await userInfo.IsValid())
			{
				int playerID = await userInfo.GetIdentifier();
				OfflinePlayerProgressionHandler.instance.ModifySkills(playerID, path, levels);
			}
		}

		[Command("printofflinelevels", "Give levels to players even if they are offline")]
		[Alias(new string[] { "printofflvl", "print-lvl" })]
		private static void PrintOfflineLevels()
		{
			OfflinePlayerProgressionHandler.instance.LogCurrent();
		}
	}

	private static Logger logger = LogManager.GetCurrentClassLogger();

	[ServerOnly]
	[Command("username", "Get the username from a user identifier")]
	private static UserInfo GetUsername(int userId)
	{
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected O, but got Unknown
		List<KeyValuePair<string, UserConfig.User>> list = TavernServices.GetService<TavernManager>().UserConfig.LastRead.Users.Where((KeyValuePair<string, UserConfig.User> kvp) => kvp.Value.UserId == (ulong)userId).ToList();
		if (!list.Any())
		{
			CommandService.ThrowError("Couldn't find user with id: {0}", new object[1] { userId });
		}
		KeyValuePair<string, UserConfig.User> keyValuePair = list[0];
		return new UserInfo((int)keyValuePair.Value.UserId, keyValuePair.Key);
	}

	[ServerOnly]
	[Command("id", "Get the id from a username")]
	private static UserInfo GetIdentifier(string username)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected O, but got Unknown
		if (TavernServices.GetService<TavernManager>().UserConfig.LastRead.Users.TryGetValue(username.ToLower(), out var value))
		{
			return new UserInfo((int)value.UserId, username);
		}
		CommandService.ThrowError("Couldn't find user with username: {0}", new object[1] { username });
		return null;
	}

	[ServerOnly]
	[Command("count", "Responds with the number of online players")]
	private static int Count()
	{
		return Player.AllPlayers.Count;
	}

	[ServerOnly]
	[Command("kick", "Disconnects the player from the server")]
	private static void KickPlayer(PlayerList players, string reason = "No reason provided")
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		foreach (Player player in players)
		{
			Player val = player;
			val.Kick(reason);
		}
	}

	[ServerOnly]
	[Command("list", "Lists all players")]
	private static IEnumerable<UserInfo> List()
	{
		return Player.AllPlayers.Select((IPlayer player) => player.UserInfo.UserInfo);
	}

	[ServerOnly]
	[Command("list-detailed", "Lists all players")]
	private static IEnumerable<UserInfoDetailed> ListDetailed()
	{
		return ((IEnumerable<IPlayer>)Player.AllPlayers).Select((Func<IPlayer, UserInfoDetailed>)((IPlayer player) => new UserInfoDetailed(player)));
	}

	[ServerOnly]
	[Command("detailed", "Gets user info")]
	private static UserInfoDetailed ListDetailed(IPlayer player)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Expected O, but got Unknown
		return new UserInfoDetailed(player);
	}

	[ServerOnly]
	[Command("message", "Sends a message to a player")]
	private static void Message(PlayerList players, string message, float duration = 10f)
	{
		logger.Trace("Sending message to " + players.CountOrName() + ".");
		foreach (IPlayer player in players)
		{
			PlayerCommunicationManager.Instance.SendMessageToPlayer(player, message, duration);
		}
	}

	[ServerOnly]
	[Command("kill", "Kills a player")]
	private static void KillPlayer(PlayerList players)
	{
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Expected O, but got Unknown
		logger.Trace("Killing " + players.CountOrName() + ".");
		foreach (IPlayer player in players)
		{
			PlayerController playerController = player.PlayerController;
			PlayerCharacter val = (PlayerCharacter)(object)((playerController is PlayerCharacter) ? playerController : null);
			bool value = PlayerHealth.ArePlayersAvoidingDamage.Value;
			bool isIgnoringDamage = (Object)(object)val != (Object)null && ((PlayerController)val).IsIgnoringDamage;
			PlayerHealth.ArePlayersAvoidingDamage.Value = false;
			if ((Object)(object)val != (Object)null)
			{
				((PlayerController)val).IsIgnoringDamage = false;
				float playerDamageTakenMultiplier = ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier;
				ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier = 1f;
				((HealthObject)val.Health).ReceiveDamage(float.MaxValue, 0f, new DamageData((DamageSource)2));
				ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier = playerDamageTakenMultiplier;
				PlayerHealth.ArePlayersAvoidingDamage.Value = value;
				((PlayerController)val).IsIgnoringDamage = isIgnoringDamage;
			}
		}
	}

	[ServerOnly]
	[Command("setdamagemulti", "Sets the PlayerDamageTakenMultiplier value in Server Settings")]
	private static void SetDamageMultiplier_Command(float multiplier)
	{
		ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier = multiplier;
		logger.Trace("PlayerDamageTakenMultiplier = " + ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier);
	}

	[ServerOnly]
	[Command("getdamagemulti", "Gets the PlayerDamageTakenMultiplier value in Server Settings")]
	private static void GetDamageMultiplier_Command()
	{
		logger.Trace("PlayerDamageTakenMultiplier = " + ServerHandler.Current.ServerConfig.Settings.PlayerDamageTakenMultiplier);
	}

	[ServerOnly]
	[Command("cripple", "Cripples a player")]
	private static void CripplePlayer(PlayerList players)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		logger.Trace("Crippling " + players.CountOrName() + ".");
		foreach (Player player in players)
		{
			Player val = player;
			PlayerController playerController = val.PlayerController;
			PlayerCharacter val2 = (PlayerCharacter)(object)((playerController is PlayerCharacter) ? playerController : null);
			if ((Object)(object)val == (Object)null)
			{
				break;
			}
			bool value = PlayerHealth.ArePlayersAvoidingDamage.Value;
			bool isIgnoringDamage = ((PlayerController)val2).IsIgnoringDamage;
			PlayerHealth.ArePlayersAvoidingDamage.Value = false;
			((PlayerController)val2).IsIgnoringDamage = false;
			Stat crippleStat = ((HealthObject)val2.Health).CrippleStat;
			crippleStat.Base -= 10000f;
			PlayerHealth.ArePlayersAvoidingDamage.Value = value;
			((PlayerController)val2).IsIgnoringDamage = isIgnoringDamage;
		}
	}

	[ServerOnly]
	[Command("teleport", "Teleports a player to another")]
	[Alias(new string[] { "tp" })]
	private static void Teleport(PlayerList players, Player targetPlayer)
	{
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Expected O, but got Unknown
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)targetPlayer.PlayerController == (Object)null)
		{
			logger.Trace("Target player has no controller");
			return;
		}
		logger.Trace("Teleporting " + players.CountOrName() + " to " + targetPlayer.UserInfo.Username + ".");
		Vector3 forward = targetPlayer.PlayerController.Head.forward;
		forward.y = 0f;
		((Vector3)(ref forward)).Normalize();
		Vector3 position = targetPlayer.PlayerController.Head.position;
		position.y = ((Component)targetPlayer.PlayerController).transform.position.y;
		position += forward * 2f;
		foreach (Player player in players)
		{
			Player val = player;
			PlayerController playerController = val.PlayerController;
			if (playerController != null)
			{
				playerController.ForceTeleportTo(position);
			}
		}
	}

	[Command("god-mode", "Makes a player invulnerable")]
	[Alias(new string[] { "godmodeon" })]
	private static void GodMode(PlayerList players, bool isOn)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		foreach (Player player in players)
		{
			Player val = player;
			if ((Object)(object)val.PlayerController != (Object)null)
			{
				val.PlayerController.IsIgnoringDamage = isOn;
			}
		}
	}

	[Command("teleport", "Teleports the player to a destination.")]
	[Alias(new string[] { "tp" })]
	public static void Teleport(PlayerList players, SpawnAreaIdentifier target = (SpawnAreaIdentifier)1)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Invalid comparison between Unknown and I4
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		foreach (Player player in players)
		{
			Player val = player;
			PlayerEffectController component = ((Component)val.PlayerController).GetComponent<PlayerEffectController>();
			Vector3 val2 = (((int)target != 60) ? SpawnArea.InstanceMap[target].GetRandomSpawnPosition() : val.GetSpawnPosition(true));
			if ((Object)(object)component != (Object)null)
			{
				((EffectHandler)component).Teleport(val2, 1f, (TeleportType)0);
			}
			else
			{
				val.PlayerController.LocomotionController.MoveTo(val2, (LocomotionFunction)2);
			}
			logger.Debug<string, SpawnAreaIdentifier>("Teleported {0} to {1}", val.UserInfo.Username, target);
		}
	}

	[Command("unlock-cancel", "Testing command while working on unlocks")]
	private static void UnlockCancel(Player player)
	{
		player.UnlockManager.CancelUnlock();
	}

	[Command("set-unlock", "Set Unlock")]
	[NonSimpleServer(new string[] { "debug_features" })]
	private static void CheckStat(PlayerList players, PlayerUnlock unlock, bool isUnlocked = true)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		foreach (Player player in players)
		{
			Player val = player;
			val.UnlockManager.SetPermanentUnlock(unlock, isUnlocked);
			if ((Object)(object)val.UnlockManager.CurrentUnlock == (Object)(object)unlock)
			{
				val.UnlockManager.CancelUnlock();
			}
		}
	}

	[Command("unlock-check", "Check Unlock")]
	private static bool CheckStat(Player player, PlayerUnlock unlock)
	{
		return player.UnlockManager.Check(unlock);
	}

	[Command("get-home", "Gets a players respawn point")]
	private static Vector3 GetHome(Player player)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		return player.Save.Data.home;
	}

	[Command("set-home", "Sets a players respawn point")]
	private static void SetHome(PlayerList players, Vector3 home = default(Vector3))
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		foreach (Player player in players)
		{
			Player val = player;
			val.Save.Data.home = home;
		}
	}

	[Command("set-stat", "Set stat value")]
	[Alias(new string[] { "setstat" })]
	private static void SetStat(PlayerList players, StatDefinition statDefinition, float value, StatApplicationType applicationType = StatApplicationType.Base)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Expected O, but got Unknown
		foreach (Player player in players)
		{
			Player val = player;
			Stat statOnPlayer = statDefinition.GetStatOnPlayer((IPlayer)(object)val);
			switch (applicationType)
			{
			case StatApplicationType.Base:
				statOnPlayer.Base = value;
				break;
			case StatApplicationType.Min:
				statOnPlayer.Minimum = value;
				break;
			case StatApplicationType.Max:
				statOnPlayer.Maximum = value;
				break;
			}
			logger.Trace(string.Concat(val, " ", statDefinition.Name, " ", applicationType.ToString(), " value is ", value));
		}
	}

	[Command("modify-stat", "Apply a timed modifier to a stat")]
	[Alias(new string[] { "modifystat" })]
	private static void ModifyStat(PlayerList players, StatDefinition statDefinition, float valueModifier, float duration, bool isMultiplier = false)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		foreach (Player player in players)
		{
			Player val = player;
			Stat statOnPlayer = statDefinition.GetStatOnPlayer((IPlayer)(object)val);
			StatModifier val2 = new StatModifier(statOnPlayer, valueModifier, isMultiplier, true);
			val.PlayerController.Health.StatManager.ApplyTimedModifier(val2, duration);
			logger.Trace(string.Concat(val, " ", statOnPlayer.Definition.Name, " is now modified, current value is ", statOnPlayer.Value));
		}
	}

	[Command("check-stat", "Check Stat Value")]
	[Alias(new string[] { "checkstat" })]
	private static StatValue CheckStat(Player player, Stat stat)
	{
		return stat;
	}

	[Command("list-stats", "List all available stats for players")]
	[Alias(new string[] { "list-stat" })]
	private static IEnumerable<StatDefinitionInfo> ListStats(Player player)
	{
		if ((Object)(object)((player != null) ? player.PlayerController : null) == (Object)null)
		{
			return ((IEnumerable<StatDefinitionApplication>)((Component)GlobalSettings<PlayerControllerPrefabs>.Instance.GetController((PlayerMode)4)).GetComponentInChildren<StatManager>().DefinitionApplications).Select((Func<StatDefinitionApplication, StatDefinitionInfo>)((StatDefinitionApplication stat) => stat));
		}
		return ((IEnumerable<KeyValuePair<StatDefinition, Stat>>)player.PlayerController.Health.StatManager.Stats).Select((Func<KeyValuePair<StatDefinition, Stat>, StatDefinitionInfo>)((KeyValuePair<StatDefinition, Stat> statPair) => statPair.Value));
	}
}
