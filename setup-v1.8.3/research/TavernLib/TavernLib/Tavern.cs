using System;
using System.Reflection;
using Alta.Console;
using Alta.Console.Commands;
using MelonLoader;
using MonoMod.RuntimeDetour;
using TavernLib.Backend;
using TavernLib.Backend.Api;
using TavernLib.Debugging;
using TavernLib.Patches;
using TavernLib.Services;

namespace TavernLib;

public class Tavern : MelonPlugin
{
	public const string Version = "1.5.0";

	internal static Instance Logger { get; private set; }

	public override void OnEarlyInitializeMelon()
	{
		Logger = ((MelonBase)this).LoggerInstance;
		SetupServices();
	}

	public override void OnInitializeMelon()
	{
		CommandService.CommandCollection.Collect(Assembly.GetExecutingAssembly());
	}

	public override void OnLateInitializeMelon()
	{
		SetupSelectionFixPatch();
	}

	private void SetupSelectionFixPatch()
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		MethodInfo method = typeof(SelectionCommandModule).GetMethod("FindObjects", BindingFlags.Static | BindingFlags.NonPublic);
		MethodInfo method2 = typeof(SelectFixPatch).GetMethod("SelectionFix", BindingFlags.Static | BindingFlags.Public);
		new Hook((MethodBase)method, method2);
	}

	private void SetupServices()
	{
		try
		{
			if (CommandLineArguments.Contains("/debug_helper"))
			{
				TavernServices.AddService(new DebugHelper());
			}
			if (CommandLineArguments.Contains("/start_server"))
			{
				TavernLogger.Msg("Booting TavernLib in server mode", "SetupServices");
				if (!CommandLineArguments.Contains("/launcherauth"))
				{
					TeenyPatches.EnsureConsoleToken();
				}
				TavernServices.AddService(new TavernManager());
			}
			TavernServices.AddService(new EntranceMessageHandler());
		}
		catch (Exception arg)
		{
			Logger.BigError($"Error when setting up base TavernLib services!!!!! {arg}");
			throw;
		}
	}
}
