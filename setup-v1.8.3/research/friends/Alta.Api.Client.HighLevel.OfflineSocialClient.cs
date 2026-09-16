using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Exceptions;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;

namespace Alta.Api.Client.HighLevel;

public class OfflineSocialClient : ISocialClient
{
	public Task<FriendRequestedType> AddFriend(int friendIdentifier)
	{
		throw new OfflineException();
	}

	public Task<IEnumerable<FriendshipInfo>> GetFriendRequests()
	{
		return Task.FromResult(Enumerable.Empty<FriendshipInfo>());
	}

	public Task<IEnumerable<FriendshipInfo>> GetFriends()
	{
		return Task.FromResult(Enumerable.Empty<FriendshipInfo>());
	}

	public Task<IEnumerable<FriendshipInfo>> GetFriendsForUser(int userIdentifier)
	{
		return Task.FromResult(Enumerable.Empty<FriendshipInfo>());
	}

	public async Task<FriendshipInfo> GetFriendship(int userIdentifier, int friendIdentifier)
	{
		return null;
	}

	public Task RemoveFriend(int friendIdentifier)
	{
		throw new OfflineException();
	}

	public Task<FriendRequestResult> CreateFriendshipBetweenUsers(int userIdentifier, int friendIdentifier)
	{
		throw new OfflineException();
	}

	public Task<IEnumerable<FriendshipInfo>> GetSentFriendRequests()
	{
		return Task.FromResult(Enumerable.Empty<FriendshipInfo>());
	}

	public Task<IEnumerable<OculusFriendInfo>> GetOculusFriends(IEnumerable<ulong> oculusIds)
	{
		return Task.FromResult(Enumerable.Empty<OculusFriendInfo>());
	}
}
