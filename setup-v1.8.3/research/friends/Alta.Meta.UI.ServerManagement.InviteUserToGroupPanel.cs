using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Api.Client.HighLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using UnityEngine;

namespace Alta.Meta.UI.ServerManagement;

public class InviteUserToGroupPanel : PanelComponent
{
	[SerializeField]
	private UIList uiList;

	[SerializeField]
	private ListElementWithButton elementTemplate;

	private UserInfo user;

	public void SetupForUser(UserInfo user)
	{
		this.user = user;
		base.Panel.Window.gameObject.SetActive(value: true);
	}

	public void InviteToGroup(JoinedGroupManager group)
	{
		base.Panel.Window.ShowConfirmPopup("Invite " + user.Username + " to " + group.Info.Info.Group.Name + "?", "Yes", "No", delegate
		{
			FinishInvite(group);
		});
	}

	private async void FinishInvite(JoinedGroupManager group)
	{
		await base.Panel.Window.ShowLoadingForTask(group.Invites.Add(user));
		base.Panel.Window.gameObject.SetActive(value: false);
	}

	public override async void RefreshData()
	{
		await panel.Window.ShowLoadingForTask(SetupGroupsList());
	}

	private async Task SetupGroupsList()
	{
		IEnumerable<JoinedGroupManager> obj = await ApiAccess.ApiClient.Groups.Joined.GetAll();
		List<ListElementWithButton> list = new List<ListElementWithButton>();
		foreach (JoinedGroupManager joinedServer in obj)
		{
			if (joinedServer.Info.Roles.HaveIGotPermissions(GroupPermissions.Invite))
			{
				ListElementWithButton listElementWithButton = Object.Instantiate(elementTemplate, base.transform);
				list.Add(listElementWithButton);
				listElementWithButton.Text.Text = joinedServer.Info.Info.Group.Name;
				listElementWithButton.Selected += delegate
				{
					InviteToGroup(joinedServer);
				};
			}
		}
		uiList.CompleteReset(list, isDestroyingElements: true);
	}
}
