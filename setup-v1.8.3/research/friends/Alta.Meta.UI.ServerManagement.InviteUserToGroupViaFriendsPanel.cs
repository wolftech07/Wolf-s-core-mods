using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Meta.Friends;
using Alta.Meta.Friends.Platform;
using Alta.PlatformInformation;
using Features.Meta.Resolvers;
using UnityEngine;

namespace Alta.Meta.UI.ServerManagement;

public class InviteUserToGroupViaFriendsPanel : PanelComponent
{
	[SerializeField]
	private UIList uiList;

	[SerializeField]
	private ListElementWithButton elementTemplate;

	[SerializeField]
	private ListElementWithButton oculusUserTemplate;

	private JoinedGroupManager group;

	public void SetupForGroup(JoinedGroupManager group)
	{
		this.group = group;
		base.Panel.Window.gameObject.SetActive(value: true);
	}

	public void InviteToGroup(UserInfo user)
	{
		base.Panel.Window.ShowConfirmPopup("Invite " + user.Username + " to " + group.Info.Info.Group.Name + "?", "Yes", "No", delegate
		{
			FinishInvite(user);
		});
	}

	private async void FinishInvite(UserInfo user)
	{
		await base.Panel.Window.ShowLoadingForTask(group.Invites.Add(user));
		base.Panel.Window.gameObject.SetActive(value: false);
	}

	public override async void RefreshData()
	{
		await panel.Window.ShowLoadingForTask(SetupFriendsList());
	}

	private async Task SetupFriendsList()
	{
		IPlatformFriendService friendService = InterfaceResolver.IPlatformFriendService;
		List<ListElementWithButton> newElements = new List<ListElementWithButton>();
		HashSet<UserInfo> altaUsers = new HashSet<UserInfo>(await ApiAccess.ApiClient.SocialClient.GetFriends());
		SpawnAltaFriendButtons(altaUsers, newElements);
		if (PlatformProvider.IsQuest())
		{
			SpawnOculusFriendButtons(await friendService.GetUnlinkedFriends(), newElements);
		}
		uiList.CompleteReset(newElements, isDestroyingElements: true);
	}

	private void SpawnAltaFriendButtons(HashSet<UserInfo> altaUsers, ICollection<ListElementWithButton> newElements)
	{
		foreach (UserInfo friendship in altaUsers)
		{
			ListElementWithButton listElementWithButton = Object.Instantiate(elementTemplate, base.transform);
			newElements.Add(listElementWithButton);
			listElementWithButton.Text.Text = friendship.Username;
			listElementWithButton.Selected += delegate
			{
				InviteToGroup(friendship);
			};
		}
	}

	private void SpawnOculusFriendButtons(IEnumerable<Friend> nonAltaOculusUsers, ICollection<ListElementWithButton> newElements)
	{
		foreach (Friend friend in nonAltaOculusUsers)
		{
			ListElementWithButton listElementWithButton = Object.Instantiate(oculusUserTemplate, base.transform);
			newElements.Add(listElementWithButton);
			listElementWithButton.Text.Text = friend.DisplayName;
			listElementWithButton.Selected += delegate
			{
				InviteOculusFriendToGroup(friend);
			};
		}
	}

	public void InviteOculusFriendToGroup(Friend friend)
	{
		base.Panel.Window.ShowConfirmPopup("Invite " + friend.DisplayName + " to " + group.Info.Info.Group.Name + "?", "Yes", "No", delegate
		{
			FinishOculusInvite(friend);
		});
	}

	private async void FinishOculusInvite(Friend friend)
	{
		Task task = AddOculusUser(friend);
		await base.Panel.Window.ShowLoadingForTask(task);
		base.Panel.Window.gameObject.SetActive(value: false);
	}

	private Task AddOculusUser(Friend friend)
	{
		return ApiAccess.ApiClient.FutureUserActions.InviteOculusFriendToGroup(friend.ID, group.Info.Info.Group.Identifier);
	}
}
