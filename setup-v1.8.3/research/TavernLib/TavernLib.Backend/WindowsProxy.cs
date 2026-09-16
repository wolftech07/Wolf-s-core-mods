using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TavernLib.Backend;

public static class WindowsProxy
{
	private const string InternetSettingsKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";

	public static IWebProxy CreateSystemProxy()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings");
			if (registryKey == null)
			{
				return null;
			}
			if ((registryKey.GetValue("ProxyEnable") as int?).GetValueOrDefault() == 0)
			{
				return null;
			}
			string text = registryKey.GetValue("ProxyServer") as string;
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			Uri uri = ParseProxyServer(text, "http");
			if (uri == null)
			{
				return null;
			}
			WebProxy webProxy = new WebProxy(uri, BypassOnLocal: false);
			string text2 = registryKey.GetValue("ProxyOverride") as string;
			if (!string.IsNullOrWhiteSpace(text2))
			{
				IEnumerable<string> source = from e in text2.Split(';')
					select e.Trim() into e
					where e.Length > 0
					select e;
				webProxy.BypassList = source.Where((string e) => !e.Equals("<local>", StringComparison.OrdinalIgnoreCase)).Select(RegexEscapeForBypass).ToArray();
				webProxy.BypassProxyOnLocal = source.Any((string e) => e.Equals("<local>", StringComparison.OrdinalIgnoreCase));
			}
			TavernLogger.Msg($"Using Windows system proxy for API calls: {uri}", "CreateSystemProxy");
			return webProxy;
		}
		catch (Exception arg)
		{
			TavernLogger.Warn($"Failed to read Windows system proxy settings, continuing without a proxy: {arg}", "CreateSystemProxy");
			return null;
		}
	}

	private static Uri ParseProxyServer(string proxyServer, string protocol)
	{
		string text = ((!proxyServer.Contains("=")) ? proxyServer.Trim() : (from entry in proxyServer.Split(';')
			select entry.Split(new char[1] { '=' }, 2) into parts
			where parts.Length == 2 && parts[0].Trim().Equals(protocol, StringComparison.OrdinalIgnoreCase)
			select parts[1].Trim()).FirstOrDefault());
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string uriString = (text.Contains("://") ? text : ("http://" + text));
		Uri result;
		return Uri.TryCreate(uriString, UriKind.Absolute, out result) ? result : null;
	}

	private static string RegexEscapeForBypass(string wildcardEntry)
	{
		string text = Regex.Escape(wildcardEntry).Replace("\\*", ".*");
		return "^" + text + "$";
	}
}
