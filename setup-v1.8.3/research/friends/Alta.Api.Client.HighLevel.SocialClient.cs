using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Requests;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;

namespace Alta.Api.Client.HighLevel;

public class SocialClient : HighLevelApiClientBase, ISocialClient
{
	internal SocialClient(IHighLevelApiClientManager clientManager)
		: base(clientManager)
	{
	}

	public async Task<FriendRequestedType> AddFriend(int friendIdentifier)
	{
		return (await lowLevel.ApiClient.AddFriend(friendIdentifier)).Result;
	}

	public Task<IEnumerable<FriendshipInfo>> GetFriendRequests()
	{
		return lowLevel.ApiClient.GetFriendRequests().GetAllAsync();
	}

	public Task<IEnumerable<FriendshipInfo>> GetFriends()
	{
		return lowLevel.ApiClient.GetFriends().GetAllAsync();
	}

	public Task<IEnumerable<FriendshipInfo>> GetFriendsForUser(int userIdentifier)
	{
		return lowLevel.ApiClient.GetFriendsForUser(userIdentifier).GetAllAsync();
	}

	public Task<FriendshipInfo> GetFriendship(int userIdentifier, int friendIdentifier)
	{
		return lowLevel.ApiClient.GetFriendship(userIdentifier, friendIdentifier);
	}

	public Task RemoveFriend(int friendIdentifier)
	{
		return lowLevel.ApiClient.RemoveFriend(friendIdentifier);
	}

	public Task<FriendRequestResult> CreateFriendshipBetweenUsers(int userIdentifier, int friendIdentifier)
	{
		return lowLevel.ApiClient.CreateFriendship(userIdentifier, friendIdentifier);
	}

	public Task<IEnumerable<FriendshipInfo>> GetSentFriendRequests()
	{
		return lowLevel.ApiClient.GetSentFriendRequests().GetAllAsync();
	}

	public Task<IEnumerable<OculusFriendInfo>> GetOculusFriends(IEnumerable<ulong> oculusIds)
	{
		return lowLevel.ApiClient.GetOculusFriendships(new OculusFriendsList
		{
			OculusFriends = oculusIds
		});
	}
}
