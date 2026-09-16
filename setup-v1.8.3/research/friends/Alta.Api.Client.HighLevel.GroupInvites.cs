using System.Threading.Tasks;
using Alta.Api.Client.LowLevel;
using Alta.Api.DataTransferModels.Models.Responses;

namespace Alta.Api.Client.HighLevel;

public class GroupInvites : PaginatedListWithAdd<OutgoingGroupInvite>
{
	public GroupInfo GroupInfo { get; }

	internal GroupInvites(GroupInfo group, LowLevelApiClient api)
		: base((IPagedResult<OutgoingGroupInvite>)new ConvertedPagedResult<GroupMemberInfo, OutgoingGroupInvite>(api.GetInvitesInGroup(group.Identifier), (GroupMemberInfo item) => new OutgoingGroupInvite(api, item)), api)
	{
		GroupInfo = group;
	}

	public override async Task<OutgoingGroupInvite> Add(UserInfo user)
	{
		GroupMemberInfo memberInfo = await api.InviteUserToGroup(GroupInfo.Identifier, user.Identifier);
		return new OutgoingGroupInvite(api, memberInfo);
	}
}
