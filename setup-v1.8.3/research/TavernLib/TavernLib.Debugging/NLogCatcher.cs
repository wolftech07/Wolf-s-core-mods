using System.Collections.Generic;
using MelonLoader;
using MelonLoader.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace TavernLib.Debugging;

internal class NLogCatcher
{
	public class MelonTarget : TargetWithLayout
	{
		public Dictionary<string, bool> LoggingLevels { get; } = new Dictionary<string, bool>
		{
			{ "Info", true },
			{ "Trace", true },
			{ "Debug", true },
			{ "Warn", true },
			{ "Error", true },
			{ "Fatal", true }
		};

		protected override void Write(LogEventInfo logEvent)
		{
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0118: Unknown result type (might be due to invalid IL or missing references)
			//IL_0154: Unknown result type (might be due to invalid IL or missing references)
			bool flag;
			switch (logEvent.LoggerName)
			{
			case "Connection":
			case "MessageDebug":
			case "SpawingLogger":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag || logEvent.Message.Contains("Initialize As Server:"))
			{
				return;
			}
			string text = $"[{logEvent.Level}] {((TargetWithLayout)this).Layout.Render(logEvent)}";
			if (LoggingLevels.TryGetValue(logEvent.Level.Name, out var value) && value)
			{
				if (logEvent.Level == LogLevel.Trace || logEvent.Level == LogLevel.Info)
				{
					Tavern.Logger.Msg(ColorARGB.CornflowerBlue, text);
				}
				if (logEvent.Level == LogLevel.Warn || logEvent.Level == LogLevel.Debug)
				{
					Tavern.Logger.Msg(ColorARGB.Yellow, text);
				}
				if (logEvent.Level == LogLevel.Error || logEvent.Level == LogLevel.Fatal)
				{
					Tavern.Logger.Msg(ColorARGB.Red, text);
				}
			}
		}
	}

	public MelonTarget Target { get; private set; }

	public NLogCatcher()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Expected O, but got Unknown
		((MelonEventBase<LemonAction>)(object)MelonEvents.OnApplicationLateStart).Subscribe((LemonAction)delegate
		{
			//IL_003c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Expected O, but got Unknown
			MelonTarget melonTarget = new MelonTarget();
			((Target)melonTarget).Name = "Melon";
			Target = melonTarget;
			LoggingRule melonRule = new LoggingRule("*", LogLevel.Trace, LogLevel.Fatal, (Target)(object)Target);
			LogManager.Configuration.AddTarget(((Target)Target).Name, (Target)(object)Target);
			LogManager.Configuration.LoggingRules.Insert(0, melonRule);
			LogManager.ReconfigExistingLoggers();
			LogManager.ConfigurationChanged += delegate(object sender, LoggingConfigurationChangedEventArgs args)
			{
				LoggingConfiguration activatedConfiguration = args.ActivatedConfiguration;
				if (activatedConfiguration != null && activatedConfiguration.FindTargetByName("Melon") == null)
				{
					activatedConfiguration.AddTarget(((Target)Target).Name, (Target)(object)Target);
					activatedConfiguration.LoggingRules.Insert(0, melonRule);
					LogManager.ReconfigExistingLoggers();
				}
			};
		}, 0, false);
	}
}
