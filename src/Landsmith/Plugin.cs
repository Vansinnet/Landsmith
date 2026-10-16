using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Landsmith
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static Plugin instance;
        private ConfigEntry<bool> modEnabled;
        private Harmony harmony;
        private bool failed;

        internal static Controller Controller { get; private set; }

        private void Awake()
        {
            modEnabled = Config.Bind("General", "Enabled", true, "Enable Landsmith. Restart after changing the configuration file.");
            if (!modEnabled.Value)
            {
                Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} is disabled in its configuration file.");
                return;
            }

            string access = GameAccess.Resolve();
            if (!GameAccess.Ready)
            {
                Logger.LogError($"{ModInfo.Name} {ModInfo.Version} is not compatible with this Valheim version (missing: {access}). The tools are disabled.");
                return;
            }
            if (access != null) Logger.LogWarning($"Tool swing animation unavailable ({access}).");

            instance = this;
            ToolRegistry.Initialize(Logger);
            var settings = new Settings(Config);
            if (!UnityEngine.Application.isBatchMode) Controller = new Controller(settings, Logger);
            harmony = new Harmony(ModInfo.Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            // The item database may already exist when the plugin loads late.
            if (ObjectDB.instance) RegisterTools(ObjectDB.instance);
            Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded. Client-side Hoe tools; edits are ordinary terrain data.");
        }

        internal static void RegisterTools(ObjectDB db)
        {
            if (!instance || instance.failed) return;
            try
            {
                ToolRegistry.Register(db);
            }
            catch (Exception error)
            {
                instance.failed = true;
                instance.Logger.LogError($"Could not add the Landsmith tools: {error}");
            }
        }

        private void Update()
        {
            if (Controller == null || failed) return;
            try
            {
                Controller.Update();
            }
            catch (Exception error)
            {
                failed = true;
                Controller.Halt();
                Logger.LogError($"Landsmith stopped for this session after an unexpected error: {error}");
            }
        }

        private void OnGUI()
        {
            if (Controller == null || failed) return;
            Controller.OnGUI();
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Controller?.Destroy();
            Controller = null;
            ToolRegistry.Unregister();
            if (instance == this) instance = null;
        }
    }
}
