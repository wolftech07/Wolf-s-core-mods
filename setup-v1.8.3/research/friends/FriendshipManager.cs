using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Networking.Scripts.Player;
using NLog;

public class FriendshipManager
{
	private static Logger logger = LogManager.GetCurrentClassLogger();

	private int player;

	public bool IsFriend { get; private set; }

	public bool IsUpdating { get; private set; }

	public List<UserInfo> Friends { get; } = new List<UserInfo>();

	private HashSet<int> friendsIDs { get; } = new HashSet<int>();

	private HashSet<int> outgoingRequestsIDs { get; } = new HashSet<int>();

	private HashSet<int> incomingRequestsIDs { get; } = new HashSet<int>();

	public event Action FriendshipStatusUpdated;

	public static event Action<int, bool> AnyFriendshipStatusUpdated;

	public static event Action LocalFriendshipStatusUpdated;

	public void Setup(int player)
	{
		this.player = player;
	}

	public void UpdateStatus(bool isFriend)
	{
		if (IsFriend != isFriend)
		{
			IsFriend = isFriend;
			this.FriendshipStatusUpdated?.Invoke();
			FriendshipManager.AnyFriendshipStatusUpdated?.Invoke(player, isFriend);
		}
	}

	public async void UpdateFromNetwork(IPlayer player)
	{
		FriendshipInfo friendshipInfo = await ApiAccess.ApiClient.SocialClient.GetFriendship(player.UserInfo.Identifier, this.player);
		UpdateStatus(friendshipInfo != null && friendshipInfo.Type == FriendshipType.Accepted);
		player.FriendshipManager.UpdateStatusWithPlayer(IsFriend, this.player);
	}

	public void UpdateStatusWithPlayer(bool isFriend, int playerId)
	{
		if (isFriend)
		{
			friendsIDs.Add(playerId);
			if (Player.SafeGetPlayer(playerId, out var result))
			{
				Friends.Add(result.UserInfo.UserInfo);
			}
			return;
		}
		friendsIDs.Remove(playerId);
		for (int i = 0; i < Friends.Count; i++)
		{
			if (Friends[i].Identifier == playerId)
			{
				Friends.RemoveAt(i);
				break;
			}
		}
	}

	public void LocalStatusUpdate(int playerId, FriendshipType newStatus)
	{
		switch (GetFriendshipType(playerId))
		{
		case FriendshipType.Accepted:
			friendsIDs.Remove(playerId);
			Friends.RemoveAll((UserInfo o) => o.Identifier == playerId);
			break;
		case FriendshipType.Requested:
			incomingRequestsIDs.Remove(playerId);
			break;
		case FriendshipType.WaitingForReply:
			outgoingRequestsIDs.Remove(playerId);
			break;
		}
		switch (newStatus)
		{
		case FriendshipType.Accepted:
			friendsIDs.Add(playerId);
			Friends.Add(new UserInfo(playerId, "[temp]"));
			break;
		case FriendshipType.Requested:
			incomingRequestsIDs.Add(playerId);
			break;
		case FriendshipType.WaitingForReply:
			outgoingRequestsIDs.Add(playerId);
			break;
		}
	}

	public async Task GetFriends(bool isLocalPlayer)
	{
		if ((!NetworkSceneManager.IsServer && !isLocalPlayer) || IsUpdating)
		{
			return;
		}
		logger.Info("[Friendship Manager] Updating Friends");
		IsUpdating = true;
		try
		{
			IEnumerable<FriendshipInfo> friendships;
			if (isLocalPlayer)
			{
				friendships = await ApiAccess.ApiClient.SocialClient.GetFriends();
				IEnumerable<FriendshipInfo> obj = await ApiAccess.ApiClient.SocialClient.GetSentFriendRequests();
				outgoingRequestsIDs.Clear();
				foreach (FriendshipInfo item in obj)
				{
					outgoingRequestsIDs.Add(item.Identifier);
				}
				IEnumerable<FriendshipInfo> obj2 = await ApiAccess.ApiClient.SocialClient.GetFriendRequests();
				incomingRequestsIDs.Clear();
				foreach (FriendshipInfo item2 in obj2)
				{
					incomingRequestsIDs.Add(item2.Identifier);
				}
			}
			else
			{
				friendships = await ApiAccess.ApiClient.SocialClient.GetFriendsForUser(player);
			}
			friendsIDs.Clear();
			Friends.Clear();
			foreach (FriendshipInfo item3 in friendships)
			{
				friendsIDs.Add(item3.Identifier);
				Friends.Add(item3);
			}
			this.FriendshipStatusUpdated?.Invoke();
			if (isLocalPlayer)
			{
				logger.Info("Local Updating Friends complete");
				FriendshipManager.LocalFriendshipStatusUpdated?.Invoke();
			}
		}
		catch (Exception ex)
		{
			logger.Error(ex, ex.Message);
			throw;
		}
		finally
		{
			IsUpdating = false;
		}
	}

	public bool IsFriendsWith(int player)
	{
		return friendsIDs.Contains(player);
	}

	public FriendshipType GetFriendshipType(int player)
	{
		if (IsFriendsWith(player))
		{
			return FriendshipType.Accepted;
		}
		if (FriendRequestSent(player))
		{
			return FriendshipType.WaitingForReply;
		}
		if (FriendRequestReceived(player))
		{
			return FriendshipType.Requested;
		}
		return FriendshipType.None;
	}

	public bool FriendRequestSent(int player)
	{
		return outgoingRequestsIDs.Contains(player);
	}

	public bool FriendRequestReceived(int player)
	{
		return incomingRequestsIDs.Contains(player);
	}

	public void AddFriend(FriendshipInfo newFriendShip)
	{
		friendsIDs.Add(newFriendShip.Identifier);
		Friends.Add(newFriendShip);
		if (!NetworkSceneManager.IsServer)
		{
			this.FriendshipStatusUpdated?.Invoke();
			FriendshipManager.LocalFriendshipStatusUpdated?.Invoke();
		}
	}
}
