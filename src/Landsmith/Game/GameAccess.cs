using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Landsmith
{
    // Private game members, resolved once at startup and verified against docs/CONTRACTS.md.
    // A missing required member disables the tools instead of failing during play.
    internal static class GameAccess
    {
        internal static AccessTools.FieldRef<TerrainComp, bool> Initialized;
        internal static AccessTools.FieldRef<TerrainComp, bool[]> ModifiedHeight;
        internal static AccessTools.FieldRef<TerrainComp, float[]> LevelDelta;
        internal static AccessTools.FieldRef<TerrainComp, float[]> SmoothDelta;
        internal static AccessTools.FieldRef<TerrainComp, bool[]> ModifiedPaint;
        internal static AccessTools.FieldRef<TerrainComp, Color[]> PaintMask;
        internal static AccessTools.FieldRef<TerrainComp, Heightmap> CompHeightmap;
        internal static AccessTools.FieldRef<TerrainComp, ZNetView> CompView;
        internal static AccessTools.FieldRef<TerrainComp, int> Operations;
        internal static AccessTools.FieldRef<TerrainComp, Vector3> LastOpPoint;
        internal static AccessTools.FieldRef<TerrainComp, float> LastOpRadius;
        internal static Action<TerrainComp, bool> Save;
        internal static Action<TerrainComp> CheckLoad;
        internal static AccessTools.FieldRef<Player, GameObject> PlacementGhost;
        internal static Action<Player, bool> UpdatePlacementGhost;

        // Optional: only the tool swing animation depends on these.
        internal static AccessTools.FieldRef<Character, ZSyncAnimation> Animator;
        internal static Func<Humanoid, ItemDrop.ItemData> RightItem;

        internal static bool Ready { get; private set; }

        // Returns null on success, otherwise the missing members.
        internal static string Resolve()
        {
            var missing = new List<string>();
            Initialized = Field<TerrainComp, bool>("m_initialized", missing);
            ModifiedHeight = Field<TerrainComp, bool[]>("m_modifiedHeight", missing);
            LevelDelta = Field<TerrainComp, float[]>("m_levelDelta", missing);
            SmoothDelta = Field<TerrainComp, float[]>("m_smoothDelta", missing);
            ModifiedPaint = Field<TerrainComp, bool[]>("m_modifiedPaint", missing);
            PaintMask = Field<TerrainComp, Color[]>("m_paintMask", missing);
            CompHeightmap = Field<TerrainComp, Heightmap>("m_hmap", missing);
            CompView = Field<TerrainComp, ZNetView>("m_nview", missing);
            Operations = Field<TerrainComp, int>("m_operations", missing);
            LastOpPoint = Field<TerrainComp, Vector3>("m_lastOpPoint", missing);
            LastOpRadius = Field<TerrainComp, float>("m_lastOpRadius", missing);
            Save = Method<Action<TerrainComp, bool>>(typeof(TerrainComp), "Save", new[] { typeof(bool) }, missing);
            CheckLoad = Method<Action<TerrainComp>>(typeof(TerrainComp), "CheckLoad", Type.EmptyTypes, missing);
            PlacementGhost = Field<Player, GameObject>("m_placementGhost", missing);
            UpdatePlacementGhost = Method<Action<Player, bool>>(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) }, missing);

            var optional = new List<string>();
            Animator = Field<Character, ZSyncAnimation>("m_zanim", optional);
            RightItem = Method<Func<Humanoid, ItemDrop.ItemData>>(typeof(Humanoid), "GetRightItem", Type.EmptyTypes, optional);

            Ready = missing.Count == 0;
            if (!Ready) return string.Join(", ", missing.ToArray());
            return optional.Count == 0 ? null : "optional:" + string.Join(", ", optional.ToArray());
        }

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name, List<string> missing) where T : class
        {
            FieldInfo field = AccessTools.Field(typeof(T), name);
            if (field == null || field.IsStatic || field.FieldType != typeof(F))
            {
                missing.Add(typeof(T).Name + "." + name);
                return null;
            }
            return AccessTools.FieldRefAccess<T, F>(field);
        }

        private static TDelegate Method<TDelegate>(Type type, string name, Type[] parameters, List<string> missing) where TDelegate : Delegate
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke");
            if (method == null || method.IsStatic || method.ReturnType != invoke.ReturnType)
            {
                missing.Add(type.Name + "." + name);
                return null;
            }
            try
            {
                return AccessTools.MethodDelegate<TDelegate>(method);
            }
            catch (Exception)
            {
                missing.Add(type.Name + "." + name);
                return null;
            }
        }
    }
}
