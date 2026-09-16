using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using MelonLoader.Logging;

namespace TavernLib;

internal static class TavernLogger
{
	private static void Log(ColorARGB color, string callerName, object msg, string prefix = "")
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		StackTrace stackTrace = new StackTrace();
		Type type = stackTrace.GetFrame(2)?.GetMethod()?.DeclaringType;
		if (IsCompilerGeneratedAsyncStateMachine(type))
		{
			type = type?.DeclaringType;
		}
		string arg = "UNKNOWN";
		if (type != null)
		{
			arg = $"{type}.{callerName}";
		}
		Tavern.Logger.Msg(color, $"{prefix} [{arg}]: {msg}");
	}

	private static bool IsCompilerGeneratedAsyncStateMachine(Type type)
	{
		return typeof(IAsyncStateMachine).IsAssignableFrom(type) && type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
	}

	public static void Msg(object msg, [CallerMemberName] string callerName = "")
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		Log(ColorARGB.MediumSpringGreen, callerName, msg);
	}

	public static void Warn(object msg, [CallerMemberName] string callerName = "")
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		Log(ColorARGB.SandyBrown, callerName, msg, "WARNING");
	}

	public static void Error(object msg, [CallerMemberName] string callerName = "")
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		Log(ColorARGB.Tomato, callerName, msg, "ERROR");
	}
}
