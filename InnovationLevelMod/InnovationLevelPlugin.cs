// -----------------------------------------------------------------------------
// This file is part of an AI-assisted/generated mod for Airport Baggage Simulator.
// Developed with the assistance of Antigravity, an agentic AI coding assistant.
// -----------------------------------------------------------------------------

using System;
using BepInEx;
using HarmonyLib;
using _scripts._by_scene._common._balancing;
using _scripts._by_scene._common._balancing._configuration;
using _scripts._by_scene._game._skill_dialog;

namespace InnovationLevelMod
{
    [BepInPlugin("com.morg.innovation_level_mod", "Innovation Level Mod", "1.0.1")]
    public class InnovationLevelPlugin : BaseUnityPlugin
    {
        private static InnovationLevelPlugin _instance;

        private void Awake()
        {
            _instance = this;
            Logger.LogInfo("Innovation Level Mod is loading...");

            try
            {
                var harmony = new Harmony("com.morg.innovation_level_mod");
                harmony.PatchAll();
                Logger.LogInfo("Innovation Level Mod patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        [HarmonyPatch(typeof(Balancing), MethodType.Constructor)]
        public static class BalancingPatches
        {
            [HarmonyPostfix]
            public static void Postfix(Balancing __instance, BalancingConfiguration ____balancingConfiguration)
            {
                if (____balancingConfiguration == null) return;

                var generalBalancing = ____balancingConfiguration.generalBalancing;
                if (generalBalancing == null) return;

                if (generalBalancing.skillMaxLevel != null)
                {
                    // Update UpgradeBonus (Innovation) skill max level to 8
                    generalBalancing.skillMaxLevel[SkillId.UpgradeBonus] = 8;
                    _instance.Logger.LogInfo("Successfully set skillMaxLevel[SkillId.UpgradeBonus] to 8!");
                }
                else
                {
                    _instance.Logger.LogError("generalBalancing.skillMaxLevel is NULL!");
                }
            }
        }
    }
}
