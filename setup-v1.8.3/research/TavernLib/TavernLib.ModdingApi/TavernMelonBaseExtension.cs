using System.Reflection;
using MelonLoader;

namespace TavernLib.ModdingApi;

public static class TavernMelonBaseExtension
{
	public static ModSide GetModSide(this MelonBase melonMod)
	{
		return ((object)melonMod).GetType().GetCustomAttribute<TavernModAttribute>()?.Side ?? ModSide.None;
	}
}
