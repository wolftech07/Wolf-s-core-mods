using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TavernLib.Backend.Api;
using TavernLib.Backend.Server.Configs;
using UnityEngine;

namespace TavernLib.Backend.Auth;

internal class AuthManager
{
	private TavernManager _manager;

	private TcpListener _listener;

	public AuthManager(TavernManager manager)
	{
		_manager = manager;
		_listener = new TcpListener(IPAddress.Any, 1762);
		StartAuthCycle();
	}

	private async Task StartAuthCycle()
	{
		TavernLogger.Msg("Starting auth cycle", "StartAuthCycle");
		_listener.Start();
		CancellationTokenSource stopOnQuit = new CancellationTokenSource();
		Application.quitting += stopOnQuit.Cancel;
		TavernLogger.Msg("Starting auth listening cycle", "StartAuthCycle");
		while (!stopOnQuit.IsCancellationRequested)
		{
			InterpretTcpStream(await _listener.AcceptTcpClientAsync());
		}
	}

	private async Task<string> ReadFullPayload(Stream stream, CancellationToken token)
	{
		TavernLogger.Msg("Reading payload", "ReadFullPayload");
		byte[] buffer = new byte[4096];
		int totalRead = 0;
		while (totalRead < buffer.Length)
		{
			int bytesRead = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, token);
			if (bytesRead == 0)
			{
				break;
			}
			totalRead += bytesRead;
			string currentText = Encoding.UTF8.GetString(buffer, 0, totalRead).Trim();
			if (currentText.StartsWith("{") && currentText.EndsWith("}"))
			{
				try
				{
					JObject.Parse(currentText);
					return currentText;
				}
				catch (JsonReaderException)
				{
				}
			}
		}
		throw new Exception("Invalid or incomplete payload received.");
	}

	private async Task InterpretTcpStream(TcpClient client)
	{
		TavernLogger.Msg($"Connected to TcpClient ({(client.Client.RemoteEndPoint as IPEndPoint)?.Address}), interpreting stream", "InterpretTcpStream");
		using NetworkStream stream = client.GetStream();
		using CancellationTokenSource timeout = new CancellationTokenSource();
		timeout.CancelAfter(TimeSpan.FromSeconds(5.0));
		string payload = await ReadFullPayload(stream, timeout.Token);
		try
		{
			TavernLogger.Msg("Finding payload type", "InterpretTcpStream");
			JObject jsonPayload = JObject.Parse(payload);
			if (jsonPayload.ContainsKey("ping"))
			{
				await WritePongResponse(stream);
			}
			else if (jsonPayload.ContainsKey("register_whitelist_application"))
			{
				await ManageWhitelistApplicationRequest(stream, jsonPayload, client);
			}
			else if (jsonPayload.ContainsKey("username"))
			{
				await ManageAuthRequest(stream, jsonPayload, client);
			}
			else
			{
				TavernLogger.Warn($"Unknown payload sent to AuthManager {jsonPayload}", "InterpretTcpStream");
			}
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error decoding TCP stream {e}", "InterpretTcpStream");
		}
		finally
		{
			TavernLogger.Msg("Closing connection to joining user", "InterpretTcpStream");
			client.Close();
		}
	}

	private async Task WritePongResponse(Stream stream)
	{
		AuthPayloads.PingResponse response = new AuthPayloads.PingResponse(_manager.ServerConfig.LastRead.Name, !string.IsNullOrWhiteSpace(_manager.ServerConfig.LastRead.PasswordHash), _manager.UserConfig.LastRead.Whitelist.Usernames.Count > 0 || _manager.UserConfig.LastRead.Whitelist.Ips.Count > 0, 1757);
		await WriteResponse(stream, response);
	}

	private async Task ManageAuthRequest(Stream stream, JObject payload, TcpClient joiner)
	{
		try
		{
			_manager.UserConfig.ReadFromFile();
			AuthPayloads.AuthenticateRequest typedPayload = JsonConvert.DeserializeObject<AuthPayloads.AuthenticateRequest>(((object)payload).ToString());
			if (string.IsNullOrWhiteSpace(typedPayload.Token) || string.IsNullOrWhiteSpace(typedPayload.Username))
			{
				await WriteResponse(stream, new AuthPayloads.GenericFail("Malformed authentication data"));
				return;
			}
			string joinerIp = ((IPEndPoint)joiner.Client.RemoteEndPoint).Address.ToString();
			TavernLogger.Msg("User at " + joinerIp + " joining server", "ManageAuthRequest");
			TavernLogger.Msg($"With payload {payload}", "ManageAuthRequest");
			if (await CheckIfPermitted(joinerIp, typedPayload, stream))
			{
				await PostPermissionCheck(stream, typedPayload, joinerIp);
			}
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error when managing join request {e}", "ManageAuthRequest");
			throw;
		}
	}

	private async Task PostPermissionCheck(Stream stream, AuthPayloads.AuthenticateRequest payload, string ip)
	{
		if (_manager.UserConfig.LastRead.Users.TryGetValue(payload.Username.ToLower(), out var userData))
		{
			if (string.IsNullOrWhiteSpace(userData.Token))
			{
				userData.Token = payload.Token;
			}
			else if (payload.Token != userData.Token)
			{
				TavernLogger.Msg("Joining user at IP " + ip + " tried to take username", "PostPermissionCheck");
				await WriteResponse(stream, new AuthPayloads.GenericFail("Name taken by someone else, or you lost the token to your account!"));
				return;
			}
		}
		else
		{
			TavernLogger.Msg("Joining user at IP " + ip + " being allotted a slot in Users.json", "PostPermissionCheck");
			_manager.UserConfig.LastRead.Users[payload.Username.ToLower()] = new UserConfig.User
			{
				RegisteredFrom = ip,
				Token = payload.Token,
				UserId = 1000000000uL + (ulong)_manager.UserConfig.LastRead.Users.Count
			};
			userData = _manager.UserConfig.LastRead.Users[payload.Username.ToLower()];
		}
		TavernLogger.Msg("Writing any potential changes during join to file", "PostPermissionCheck");
		_manager.UserConfig.WriteToFile();
		await WriteResponse(stream, new AuthPayloads.AuthenticateOk(userData.UserId, _manager.ServerConfig.LastRead.QuestScene));
	}

	private async Task<bool> CheckIfPermitted(string joinerIp, AuthPayloads.AuthenticateRequest payload, Stream stream)
	{
		TavernLogger.Msg("Checking if joining user " + joinerIp + " can join", "CheckIfPermitted");
		if (_manager.UserConfig.LastRead.Whitelist.Ips.Count > 0 || _manager.UserConfig.LastRead.Whitelist.Usernames.Count > 0)
		{
			bool ipAllowed = _manager.UserConfig.LastRead.Whitelist.Ips.Contains(joinerIp);
			bool nameAllowed = _manager.UserConfig.LastRead.Whitelist.Usernames.Contains(payload.Username);
			if (!ipAllowed && !nameAllowed)
			{
				TavernLogger.Msg("Joining user at " + joinerIp + " was not on the whitelist", "CheckIfPermitted");
				await WriteResponse(stream, default(AuthPayloads.NotWhitelisted));
				return false;
			}
		}
		if (_manager.UserConfig.LastRead.Blacklist.Ips.Count > 0 || _manager.UserConfig.LastRead.Blacklist.Usernames.Count > 0)
		{
			bool ipBlocked = _manager.UserConfig.LastRead.Blacklist.Ips.Contains(joinerIp);
			bool nameBlocked = _manager.UserConfig.LastRead.Blacklist.Usernames.Contains(payload.Username);
			if (ipBlocked || nameBlocked)
			{
				TavernLogger.Msg("Joining user at " + joinerIp + " was on the blacklist", "CheckIfPermitted");
				await WriteResponse(stream, new AuthPayloads.GenericFail("Blacklisted"));
				return false;
			}
		}
		if (!string.IsNullOrWhiteSpace(_manager.ServerConfig.LastRead.PasswordHash))
		{
			if (string.IsNullOrWhiteSpace(payload.Password))
			{
				TavernLogger.Msg("Joining user at " + joinerIp + " gave no password to server", "CheckIfPermitted");
				await WriteResponse(stream, default(AuthPayloads.NeedsPassword));
				return false;
			}
			if (BackendUtils.HashDigest(payload.Password) != _manager.ServerConfig.LastRead.PasswordHash)
			{
				TavernLogger.Msg("Joining user at " + joinerIp + " gave the wrong password", "CheckIfPermitted");
				await WriteResponse(stream, default(AuthPayloads.WrongPassword));
				return false;
			}
		}
		if (_manager.ServerConfig.LastRead.EnforceIpLimit)
		{
			int matchingIpCount = _manager.UserConfig.LastRead.Users.Select((KeyValuePair<string, UserConfig.User> user) => user.Value.RegisteredFrom == joinerIp).Count();
			if (matchingIpCount > 4)
			{
				await WriteResponse(stream, new AuthPayloads.GenericFail("Too many accounts with same origin"));
				return false;
			}
		}
		TavernLogger.Msg("Joining user at " + joinerIp + " passed all authentication checks", "CheckIfPermitted");
		return true;
	}

	private async Task ManageWhitelistApplicationRequest(Stream stream, JObject payload, TcpClient joiner)
	{
		if (CommandLineArguments.Contains("/launcherauth"))
		{
			TavernLogger.Msg("Ignoring whitelist application -- TavernLauncher is managing auth for this instance.", "ManageWhitelistApplicationRequest");
			return;
		}
		try
		{
			string username = ((JToken)payload).Value<string>((object)"username");
			if (string.IsNullOrWhiteSpace(username))
			{
				await WriteResponse(stream, new AuthPayloads.GenericFail("Missing username"));
				return;
			}
			string joinerIp = ((IPEndPoint)joiner.Client.RemoteEndPoint).Address.ToString();
			TavernLogger.Msg("Whitelist application from " + username + " at " + joinerIp, "ManageWhitelistApplicationRequest");
			_manager.WhitelistRequests.ReadFromFile();
			List<WhitelistRequestList.WhitelistRequest> requests = _manager.WhitelistRequests.LastRead.Requests;
			bool alreadyPending = requests.Exists((WhitelistRequestList.WhitelistRequest r) => string.Equals(r.Username, username, StringComparison.OrdinalIgnoreCase) && r.Ip == joinerIp);
			if (!alreadyPending)
			{
				requests.Add(new WhitelistRequestList.WhitelistRequest
				{
					Username = username,
					Ip = joinerIp,
					AppliedAt = DateTime.UtcNow.ToString("o")
				});
				_manager.WhitelistRequests.WriteToFile();
			}
			await WriteResponse(stream, new AuthPayloads.WhitelistApplicationReceived(!alreadyPending));
		}
		catch (Exception arg)
		{
			TavernLogger.Error($"Error managing whitelist application {arg}", "ManageWhitelistApplicationRequest");
			await WriteResponse(stream, new AuthPayloads.GenericFail("Error processing application"));
		}
	}

	private async Task WriteResponse(Stream stream, object response)
	{
		try
		{
			string serializedResponse = JsonConvert.SerializeObject(response);
			byte[] encodedResponse = Encoding.UTF8.GetBytes(serializedResponse);
			TavernLogger.Msg("Writing response to joining client: " + serializedResponse, "WriteResponse");
			await stream.WriteAsync(encodedResponse, 0, encodedResponse.Length);
		}
		catch (Exception ex)
		{
			Exception e = ex;
			TavernLogger.Error($"Error when sending auth OK {e}", "WriteResponse");
			throw;
		}
	}
}
