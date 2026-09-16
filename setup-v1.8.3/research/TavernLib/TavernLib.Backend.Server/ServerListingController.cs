using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Extensions;
using TavernLib.Backend.Api;
using UnityEngine;

namespace TavernLib.Backend.Server;

public class ServerListingController
{
	private readonly HttpClient _apiClient;

	private TavernManager _manager;

	public ServerListingController(TavernManager manager)
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Expected O, but got Unknown
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected O, but got Unknown
		_manager = manager;
		HttpClientHandler val = new HttpClientHandler();
		IWebProxy webProxy = WindowsProxy.CreateSystemProxy();
		if (webProxy != null)
		{
			val.Proxy = webProxy;
			val.UseProxy = true;
		}
		_apiClient = new HttpClient((HttpMessageHandler)(object)val)
		{
			BaseAddress = new Uri(BackendUtils.TavernApi),
			Timeout = TimeSpan.FromSeconds(6.0)
		};
		HeartbeatAsync();
		Application.wantsToQuit += StartClosingListing;
	}

	private async Task HeartbeatAsync()
	{
		TavernLogger.Msg("Started server listing lifecycle", "HeartbeatAsync");
		try
		{
			await OpenListing();
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error when publishing data to api! {e}", "HeartbeatAsync");
			throw;
		}
		while (true)
		{
			try
			{
				await Ping();
				await Task.Delay(TimeSpan.FromSeconds(3.0));
			}
			catch (Exception ex)
			{
				Exception e2 = ex;
				TavernLogger.Error($"Error when pinging server! {e2}", "HeartbeatAsync");
			}
		}
	}

	public async Task Ping()
	{
		TavernLogger.Msg("Server listing heartbeat", "Ping");
		try
		{
			ServerListingPayload payload = ServerListingPayload.FromConfig(_manager.ServerConfig, _manager.TavernConfig);
			await _apiClient.PostAsync(BackendUtils.ServerUri, (HttpContent)new JsonContent((object)payload));
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error when pinging servers endpoint! {e}", "Ping");
			throw;
		}
	}

	public async Task OpenListing()
	{
		await Ping();
	}

	public bool StartClosingListing()
	{
		CloseListing();
		return false;
	}

	public void CloseListing()
	{
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected O, but got Unknown
		TavernLogger.Msg("Server listing closing", "CloseListing");
		var anon = new
		{
			listing_token = _manager.ServerConfig.LastRead.CommunityListingToken
		};
		HttpClientExtensions.DeleteAsync(_apiClient, BackendUtils.ServerUri, (HttpContent)new JsonContent((object)anon));
	}
}
