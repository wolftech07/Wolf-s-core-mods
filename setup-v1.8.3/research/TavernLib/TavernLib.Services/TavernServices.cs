using System;
using System.Collections.Generic;

namespace TavernLib.Services;

public static class TavernServices
{
	private static readonly Dictionary<Type, IService> ServiceEntries = new Dictionary<Type, IService>();

	public static void AddService<T>(T instance) where T : IService
	{
		if (ServiceEntries.ContainsKey(typeof(T)))
		{
			TavernLogger.Warn("Cannot add multiple services of the same type!", "AddService");
		}
		else
		{
			ServiceEntries[typeof(T)] = instance;
		}
	}

	public static T GetService<T>() where T : class, IService
	{
		if (ServiceEntries.TryGetValue(typeof(T), out var value))
		{
			return value as T;
		}
		TavernLogger.Error("Service of type " + typeof(T).Name + " was not found!", "GetService");
		return null;
	}
}
