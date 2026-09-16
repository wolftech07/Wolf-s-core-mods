using System;
using System.IO;
using Newtonsoft.Json;

namespace TavernLib.Backend.Server.Configs;

public abstract class ServerConfigFile<T>(string filePath) where T : class, new()
{
	private string FilePath { get; set; } = filePath;

	public T LastRead { get; private set; } = new T();

	public virtual void ReadFromFile()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				LastRead = new T();
				using StreamWriter streamWriter = File.CreateText(FilePath);
				streamWriter.WriteAsync(JsonConvert.SerializeObject((object)LastRead, (Formatting)1));
				return;
			}
			string text = File.ReadAllText(FilePath);
			T lastRead = JsonConvert.DeserializeObject<T>(text);
			LastRead = lastRead;
		}
		catch (Exception arg)
		{
			TavernLogger.Error(string.Format("Error when managing file responsible for type {0}! {1}", "T", arg), "ReadFromFile");
			throw;
		}
	}

	public virtual void WriteToFile()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				if (LastRead == null)
				{
					T val = (LastRead = new T());
				}
				using StreamWriter streamWriter = File.CreateText(FilePath);
				streamWriter.WriteAsync(JsonConvert.SerializeObject((object)LastRead, (Formatting)1));
				return;
			}
			File.WriteAllText(FilePath, JsonConvert.SerializeObject((object)LastRead, (Formatting)1));
		}
		catch (Exception arg)
		{
			TavernLogger.Error(string.Format("Error when managing file responsible for type {0}! {1}", "T", arg), "WriteToFile");
			throw;
		}
	}
}
