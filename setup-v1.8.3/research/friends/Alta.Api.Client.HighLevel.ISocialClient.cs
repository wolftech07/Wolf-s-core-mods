using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;

namespace Alta.Api.Client.HighLevel;

public interface ISocialClient
{
	Task<FriendRequestedType> AddFriend(int friendIdentifier);

	Task RemoveFriend(int friendIdentifier);

	Task<IEnumerable<FriendshipInfo>> GetFriends();

	Task<IEnumerable<FriendshipInfo>> GetFriendsForUser(int userIdentifier);

	Task<FriendshipInfo> GetFriendship(int userIdentifier, int friendIdentifier);

	Task<FriendRequestResult> CreateFriendshipBetweenUsers(int userIdentifier, int friendIdentifier);

	Task<IEnumerable<OculusFriendInfo>> GetOculusFriends(IEnumerable<ulong> oculusIds);

	Task<IEnumerable<FriendshipInfo>> GetFriendRequests();

	Task<IEnumerable<FriendshipInfo>> GetSentFriendRequests();
}
