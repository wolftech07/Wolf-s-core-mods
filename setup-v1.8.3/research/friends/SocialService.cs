using System;
using System.Linq;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;
using NLog;

public static class SocialService
{
	private static readonly Logger logger = LogManager.GetCurrentClassLogger();

	private static readonly GroupPermissions[] serverOwnerPermissions = new GroupPermissions[1] { GroupPermissions.ControlServer };

	public static async Task<FriendRequestedType> RequestFriend(int identifier)
	{
		try
		{
			FriendRequestedType result = await ApiAccess.ApiClient.SocialClient.AddFriend(identifier);
			logger.Trace($"Sent Friendship request to player Id: {identifier}");
			return result;
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
			throw;
		}
	}

	public static async Task<FriendRequestResult> RequestFriend(int tokenOwnerId, int newFriendId)
	{
		try
		{
			FriendRequestResult result = await ApiAccess.ApiClient.SocialClient.CreateFriendshipBetweenUsers(tokenOwnerId, newFriendId);
			logger.Trace($"Created Friendship with Token between player Id: {tokenOwnerId} and player Id {newFriendId}");
			return result;
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
			throw;
		}
	}

	public static async Task<FriendRequestedType> AcceptFriendRequest(int identifier)
	{
		try
		{
			FriendRequestedType result = await ApiAccess.ApiClient.SocialClient.AddFriend(identifier);
			logger.Trace($"Accepted Friend Request from player with Id: {identifier}");
			return result;
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
			throw;
		}
	}

	public static async Task RemoveFriend(int identifier)
	{
		try
		{
			await ApiAccess.ApiClient.SocialClient.RemoveFriend(identifier);
			logger.Trace($"Remove friend player with Id: {identifier}");
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
			throw;
		}
	}

	public static async Task<BanRole?> BanPlayer(int playerToBanId, int serverId)
	{
		_ = 5;
		try
		{
			UserInfo adminUser = ((Player.Current != null) ? Player.Current.UserInfo.UserInfo : ApiAccess.ApiClient.UserClient.LoggedInUserInfo);
			if (adminUser == null)
			{
				logger.Error($"Attempt to ban player with id {playerToBanId} on server {serverId} from null player. Aborting.");
				return null;
			}
			GameServerInfo server = await ApiAccess.ApiClient.ServerClient.GetServerAsync(serverId);
			UserInfoWithPermissions bannedUser = await ApiAccess.ApiClient.UserClient.GetUserInfoWithPermissionsAsync(playerToBanId);
			if (!(await CanBanFromServer(bannedUser, server)))
			{
				logger.Info(adminUser.Username + " attempted to ban " + bannedUser.Username + " but they are a server owner or developer. Aborting.");
				return null;
			}
			bool isServerOwner = await ApiAccess.ApiClient.ServerClient.HasPermissionsInServer(serverId, adminUser.Identifier, serverOwnerPermissions);
			GroupBan groupBan = await (await ApiAccess.ApiClient.Groups.Joined.GetByIdentifier(server.GroupIdentifier.Value)).Bans.Add(bannedUser);
			logger.Trace($"{adminUser.Username} has banned player [{groupBan.MemberInfo.Username}:{groupBan.MemberInfo.UserInfo}] on server Id: {serverId}");
			return (!isServerOwner) ? BanRole.Moderator : BanRole.Owner;
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
			return null;
		}
	}

	private static async Task<bool> CanBanFromServer(UserInfoWithPermissions user, GameServerInfo server)
	{
		if (user.Policies.Contains("server_owner"))
		{
			return false;
		}
		return !(await ApiAccess.ApiClient.ServerClient.HasPermissionsInServer(server.Identifier, user.Identifier, serverOwnerPermissions));
	}

	public static async Task UnbanPlayer(int revokedPlayerId, int serverId)
	{
		try
		{
			GameServerInfo gameServerInfo = await ApiAccess.ApiClient.ServerClient.GetServerAsync(serverId);
			GroupBan ban = (await (await ApiAccess.ApiClient.Groups.Joined.GetByIdentifier(gameServerInfo.GroupIdentifier.Value)).Bans.GetAll()).FirstOrDefault((GroupBan x) => x.MemberInfo.UserIdentifier == revokedPlayerId);
			await ban.Revoke();
			logger.Trace($"UnBanning player [{ban.MemberInfo.Username}:{ban.MemberInfo.UserInfo}] from server Id: {serverId}");
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
		}
	}

	public static async Task ReportPlayer(int playerToReportId, ReportType reportType, int serverId)
	{
		try
		{
			await ApiAccess.ApiClient.PlayerReports.ReportPlayer(playerToReportId, reportType, serverId);
			logger.Trace("Reporting playerId: {0} for: {1}", playerToReportId, reportType);
		}
		catch (Exception value)
		{
			Console.WriteLine(value);
			throw;
		}
	}
}
