using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ATT.Analytics.Social;
using Alta.Analytics;
using Alta.Api.DataTransferModels.Models.Responses;
using UnityEngine;
using VContainer;

namespace Alta.Meta.UI.ServerManagement;

public class FriendsListPanel : PanelComponent
{
	[SerializeField]
	private UIList uiList;

	[SerializeField]
	private ListElementWithButton listElement;

	[SerializeField]
	private InputField friendSearch;

	[SerializeField]
	private PanelComponent friendInvitesPanel;

	[SerializeField]
	private TextRenderer emptyListText;

	[SerializeField]
	private InviteUserToGroupPanel inviteWindow;

	private IAnalyticsService analyticsService;

	[Inject]
	public void Inject(IAnalyticsService analyticsService)
	{
		this.analyticsService = analyticsService;
	}

	protected override void HandleStateChange(Panel panel)
	{
		base.HandleStateChange(panel);
		friendSearch.Submitted -= AddFriend;
		if (panel.IsActive)
		{
			friendSearch.Submitted += AddFriend;
		}
	}

	private async void AddFriend(InputField obj)
	{
		if (!string.IsNullOrEmpty(friendSearch.InputText))
		{
			await panel.Window.ShowLoadingForTask(AddFriend(friendSearch.InputText));
		}
	}

	private async Task AddFriend(string username)
	{
		UserInfo userInfo;
		try
		{
			userInfo = await ApiAccess.ApiClient.UserClient.GetUserInfoAsync(username);
		}
		catch (Exception exception)
		{
			PanelComponent.logger.Warn(exception, "Error during get user for add as friend");
			panel.Window.ShowConfirmPopup("Couldnt find user: " + username, "Ok", null);
			return;
		}
		if (userInfo == null)
		{
			panel.Window.ShowConfirmPopup("Couldnt find user: " + username, "Ok", null);
			return;
		}
		await SocialService.RequestFriend(userInfo.Identifier);
		analyticsService.Send(new SocialAnalyticsEvent("Friend Request", SocialAnalyticsEventOrigin.MainMenu));
		friendSearch.SetText(string.Empty);
		friendInvitesPanel.ForceRefreshOnNextLoad();
		panel.Window.ShowConfirmPopup("Sent " + username + " a friend request", "Ok", null);
	}

	public override async void RefreshData()
	{
		await panel.Window.ShowLoadingForTask(SetupFriendsList());
	}

	private async Task SetupFriendsList()
	{
		IEnumerable<FriendshipInfo> enumerable = await ApiAccess.ApiClient.SocialClient.GetFriends();
		List<ListElementWithButton> list = new List<ListElementWithButton>();
		FriendshipInfo[] obj = (enumerable as FriendshipInfo[]) ?? enumerable.ToArray();
		bool flag = obj.Count() != 0;
		emptyListText.gameObject.SetActive(!flag);
		FriendshipInfo[] array = obj;
		foreach (FriendshipInfo friendship in array)
		{
			ListElementWithButton listElementWithButton = UnityEngine.Object.Instantiate(listElement, base.transform);
			list.Add(listElementWithButton);
			listElementWithButton.Text.Text = friendship.Username;
			listElementWithButton.Button.Pressed += delegate
			{
				ShowOptionsForFriend(friendship);
			};
		}
		uiList.CompleteReset(list, isDestroyingElements: true);
	}

	private void ShowOptionsForFriend(FriendshipInfo friendship)
	{
		panel.Window.ShowOptions(new OptionsConfig[2]
		{
			new OptionsConfig("Invite to Server", delegate
			{
				ShowInviteWindow(friendship);
			}),
			new OptionsConfig("Unfriend", delegate
			{
				RemoveFriend(friendship);
			}, UIButtonType.Bad)
		});
	}

	private void ShowInviteWindow(FriendshipInfo friendship)
	{
		inviteWindow.SetupForUser(friendship);
	}

	private async void RemoveFriend(FriendshipInfo friendship)
	{
		await panel.Window.ShowLoadingForTask(SocialService.RemoveFriend(friendship.Identifier));
		analyticsService.Send(new SocialAnalyticsEvent("Remove Friendship", SocialAnalyticsEventOrigin.MainMenu));
		RefreshData();
	}
}
