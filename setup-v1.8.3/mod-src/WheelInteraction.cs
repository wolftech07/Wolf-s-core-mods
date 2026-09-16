using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TavernNativeMenu
{
    internal static class WheelInteraction
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ServerSelectionMenu), "SwitchToBoard", new[] { typeof(ServerBoardType) });
            if (target == null) throw new MissingMethodException("The native server-board switch was not found.");
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(WheelInteraction), "GuardOptionalLever"));
        }

        private static IEnumerable<CodeInstruction> GuardOptionalLever(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo levers = AccessTools.Field(typeof(ServerSelectionMenu), "boardLevers");
            MethodInfo setIndex = AccessTools.Method(typeof(SelectionLever), "SetLeverToIndex", new[] { typeof(int) });
            int replaced = 0;
            for (int i = 0; i + 4 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldfld || !Equals(codes[i].operand, levers) ||
                    codes[i + 2].opcode != OpCodes.Ldelem_Ref || !Equals(codes[i + 4].operand, setIndex)) continue;
                // Some boards have no physical lever. This visual sync must not
                // abort Setup before it subscribes the native wheel and ropes.
                codes[i + 2].opcode = OpCodes.Call;
                codes[i + 2].operand = AccessTools.Method(typeof(WheelInteraction), "GetOptionalLever");
                codes[i + 4].opcode = OpCodes.Call;
                codes[i + 4].operand = AccessTools.Method(typeof(WheelInteraction), "SetOptionalLever");
                replaced++;
            }
            if (replaced != 1) throw new InvalidOperationException("The native board-lever layout changed; wheel repair was not applied.");
            return codes;
        }

        private static SelectionLever GetOptionalLever(SelectionLever[] levers, int index)
        {
            return levers != null && index >= 0 && index < levers.Length ? levers[index] : null;
        }

        private static void SetOptionalLever(SelectionLever lever, int index)
        {
            if (lever != null) lever.SetLeverToIndex(index);
        }
    }
}
