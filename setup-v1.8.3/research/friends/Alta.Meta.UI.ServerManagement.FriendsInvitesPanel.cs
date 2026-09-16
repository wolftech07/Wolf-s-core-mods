using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ATT.Analytics.Social;
using Alta.Analytics;
using Alta.Api.DataTransferModels.Models.Responses;
using UnityEngine;
using VContainer;

namespace Alta.Meta.UI.ServerManagement;

public class FriendsInvitesPanel : PanelComponent
{
	[SerializeField]
	private UIList uiList;

	[SerializeField]
	private ListElementWithButton listElement;

	[SerializeField]
	private TextRenderer emptyListText;

	private IAnalyticsService analyticsService;

	[Inject]
	public void Inject(IAnalyticsService analyticsService)
	{
		this.analyticsService = analyticsService;
	}

	public override async void RefreshData()
	{
		await panel.Window.ShowLoadingForTask(SetupFriendsList());
	}

	private async Task SetupFriendsList()
	{
		IEnumerable<FriendshipInfo> obj = await ApiAccess.ApiClient.SocialClient.GetSentFriendRequests();
		List<ListElementWithButton> list = new List<ListElementWithButton>();
		bool flag = obj.Count() != 0;
		emptyListText.gameObject.SetActive(!flag);
		foreach (FriendshipInfo friendship in obj)
		{
			ListElementWithButton listElementWithButton = Object.Instantiate(listElement, base.transform);
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
		panel.Window.ShowOptions(new OptionsConfig[1]
		{
			new OptionsConfig("Revoke", delegate
			{
				Revoke(friendship);
			}, UIButtonType.Bad)
		});
	}

	private async void Revoke(FriendshipInfo friendship)
	{
		await panel.Window.ShowLoadingForTask(SocialService.RemoveFriend(friendship.Identifier));
		analyticsService.Send(new SocialAnalyticsEvent("Remove Friendship", SocialAnalyticsEventOrigin.MainMenu));
		RefreshData();
	}
}
