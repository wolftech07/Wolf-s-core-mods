using System;
using Alta.Api.DataTransferModels.Models.Responses;
using UnityEngine;

namespace Alta.Meta.Friends;

public class UserToken : MonoBehaviour
{
	[SerializeField]
	private TextRenderer username;

	[SerializeField]
	private GameObject acceptButton;

	[SerializeField]
	private GameObject rejectButton;

	private Action<UserToken> accept;

	private Action<UserToken> reject;

	public FriendsManager Manager { get; private set; }

	public UserInfo UserInfo { get; private set; }

	public void Setup(UserInfo userInfo, FriendsManager manager, Action<UserToken> accept, Action<UserToken> reject)
	{
		UserInfo = userInfo;
		Manager = manager;
		username.Text = userInfo.Username;
		this.accept = accept;
		this.reject = reject;
		acceptButton.SetActive(accept != null);
		rejectButton.SetActive(reject != null);
	}

	public void Run(bool hasAccepted)
	{
		if (hasAccepted)
		{
			accept?.Invoke(this);
		}
		else
		{
			reject?.Invoke(this);
		}
	}
}
