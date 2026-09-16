using System;
using Newtonsoft.Json;

namespace Alta.Api.DataTransferModels.Models.Responses;

public class FriendshipInfo : UserInfo
{
	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("icon")]
	public int IconIndex { get; set; }

	[JsonProperty("type")]
	public FriendshipType Type { get; set; }
}
