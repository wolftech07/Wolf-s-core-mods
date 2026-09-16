using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using TavernLib.Services;
using UnityEngine;

namespace TavernLib.Debugging;

public class DebugHelper : IService
{
	private readonly NLogCatcher _logCatcher = new NLogCatcher();

	internal DebugHelper()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		((MelonEventBase<LemonAction>)(object)MelonEvents.OnGUI).Subscribe(new LemonAction(OnGui), 0, false);
	}

	private void OnGui()
	{
		for (int i = 0; i < _logCatcher.Target.LoggingLevels.Count; i++)
		{
			KeyValuePair<string, bool> keyValuePair = _logCatcher.Target.LoggingLevels.ElementAt(i);
			_logCatcher.Target.LoggingLevels[keyValuePair.Key] = GUILayout.Toggle(keyValuePair.Value, keyValuePair.Key, Array.Empty<GUILayoutOption>());
		}
	}
}
