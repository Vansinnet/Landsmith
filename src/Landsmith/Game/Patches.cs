using HarmonyLib;
using UnityEngine;

namespace Landsmith
{
    // Adds the tools whenever the item database is (re)initialized: main menu, world load and reconnect.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDbAwakePatch
    {
        private static void Postfix(ObjectDB __instance) => Plugin.RegisterTools(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB), new[] { typeof(ObjectDB) })]
    internal static class ObjectDbCopyPatch
    {
        private static void Postfix(ObjectDB __instance) => Plugin.RegisterTools(__instance);
    }

    // A Landsmith piece only selects a tool. For a valid placement the edit replaces the vanilla TryPlacePiece:
    // no piece is instantiated and no build statistics are recorded. Vanilla UpdatePlacement then charges
    // resources, stamina and durability only when the edit succeeded (the result is true).
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece), new[] { typeof(Piece) })]
    internal static class TryPlacePiecePatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!ToolRegistry.TryGet(piece, out ToolDefinition definition)) return true;
            if (__instance != Player.m_localPlayer || Plugin.Controller == null)
            {
                __result = false;
                return false;
            }
            return Plugin.Controller.TryPlace(__instance, definition, piece, ref __result);
        }
    }

    // Safety net: nothing else should place a Landsmith piece, but if something does it must not spawn the clone.
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece), new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool) })]
    internal static class PlacePiecePatch
    {
        private static bool Prefix(Piece piece) => !ToolRegistry.Contains(piece);
    }

    // GameCamera zooms with the mouse wheel unless the selected piece can rotate. While a Landsmith modifier
    // key is held the wheel adjusts the brush instead, so zooming is suppressed for that frame.
    [HarmonyPatch(typeof(Player), nameof(Player.CanRotatePiece))]
    internal static class CanRotatePiecePatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (!__result && __instance == Player.m_localPlayer && Plugin.Controller != null && Plugin.Controller.ConsumesScroll)
                __result = true;
        }
    }
}
