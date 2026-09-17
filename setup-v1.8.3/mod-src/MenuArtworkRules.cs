using System;

namespace TavernNativeMenu
{
    // Exact scene/asset names, not broad matches for "logo" or vendor text.
    internal static class MenuArtworkRules
    {
        internal const float BadgeSurfaceGapMeters = 0.035f;
        internal const float CancelRightShift = 0.008f;
        internal const float WallSignsLeftShiftMeters = 0.06f;

        internal static bool RemoveWholeBoard(string name, string parentName, string grandparentName)
        {
            return parentName == "Credits" && grandparentName == "Static Models" &&
                (name == "LogoBoard" || name == "ScreenLogos");
        }

        private static string AssetName(string name)
        {
            string result = (name ?? "").Trim();
            foreach (string suffix in new[] { " (Instance)", " (Clone)" })
                if (result.EndsWith(suffix, StringComparison.Ordinal)) result = result.Substring(0, result.Length - suffix.Length).TrimEnd();
            return result;
        }

        internal static bool Remove(string name)
        {
            switch (AssetName(name).ToLowerInvariant())
            {
                case "screenlogos": case "screenqld": case "screenqldlogo": case "screennsw":
                case "vivox logo": case "vivoxlogo": case "vivox": return true;
                default: return false;
            }
        }

        internal static bool ReplaceBadge(string name)
        {
            switch (AssetName(name))
            {
                case "Alta_Logo_Black": case "Alta Logo Ridged": case "Alta Logo Unlit": return true;
                default: return false;
            }
        }
    }
}
