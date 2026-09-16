using System;

namespace TavernLib.ModdingApi;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class TavernModAttribute : Attribute
{
	public ModSide Side { get; }

	public TavernModAttribute(ModSide side)
	{
		Side = side;
	}
}
