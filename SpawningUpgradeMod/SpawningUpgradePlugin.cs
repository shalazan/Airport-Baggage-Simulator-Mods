using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Zenject;
using _scripts._by_scene._common._balancing;
using _scripts._by_scene._common._balancing._configuration;
using _scripts._by_scene._game._upgrades._specific_upgrades;
using _scripts._by_scene._game._automation._upgrades;
using _scripts._by_scene._game._baggage_spawner;
using _scripts._by_scene._game._work_button;
using _scripts._by_scene._game._building;
using _scripts._text_style;
using Produktivkeller.SimpleLocalization.Unity.Core;
using TMPro;
using _scripts._by_scene._game._day_time._day_transition;

namespace SpawningUpgradeMod
{
    [BepInPlugin("com.morg.spawning_upgrade_mod", "Spawning and Machine Upgrades Mod", "1.3.0")]
    public class SpawningUpgradePlugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource LoggerInstance;

        // Extended TimeBetweenSpawns array: game has {5f, 4f, 3f} for levels 0-2.
        // We add levels 3 (2s) and 4 (1s). Index 0 is the base (un-upgraded) value.
        internal static readonly float[] ExtendedTimeBetweenSpawns = new float[] { 5f, 4f, 3f, 2f, 1f };

        internal const float AdditionalSpeedForRollersPerLevel = 0.3f;
        internal const float AdditionalSpeedForConveyorBeltsPerLevel = 0.15f;

        // Base scan durations for bridge (3.5s) and corner (1.0s)
        internal const float BridgeBaseDuration  = 3.5f;
        internal const float CornerBaseDuration  = 1.0f;

        private void Awake()
        {
            LoggerInstance = Logger;
            Logger.LogInfo("Spawning and Machine Upgrades Mod v1.3.0 loading...");

            // 1. Extend Upgrade_Automat_Speed DurationPerLevel to support level 6 (1.0s processing)
            try
            {
                var field = typeof(Upgrade_Automat_Speed).GetField("DurationPerLevel", BindingFlags.Static | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(null, new float[] { 20f, 12f, 8f, 6f, 4f, 3f, 1f });
                    Logger.LogInfo("Extended Upgrade_Automat_Speed.DurationPerLevel to 7 levels.");
                }
                else
                {
                    Logger.LogError("Could not find DurationPerLevel field on Upgrade_Automat_Speed.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to patch DurationPerLevel: {ex.Message}\n{ex.StackTrace}");
            }

            // 2. Extend Upgrade_BaggageSpawner_Speed.TimeBetweenSpawns to include 2s and 1s
            try
            {
                var tbsField = typeof(Upgrade_BaggageSpawner_Speed).GetField("TimeBetweenSpawns", BindingFlags.Static | BindingFlags.NonPublic);
                if (tbsField != null)
                {
                    tbsField.SetValue(null, ExtendedTimeBetweenSpawns);
                    Logger.LogInfo("Extended Upgrade_BaggageSpawner_Speed.TimeBetweenSpawns to 5 levels.");
                }
                else
                {
                    Logger.LogError("Could not find TimeBetweenSpawns field on Upgrade_BaggageSpawner_Speed.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to patch TimeBetweenSpawns: {ex.Message}\n{ex.StackTrace}");
            }

            // 3. Apply all Harmony patches
            try
            {
                var harmony = new Harmony("com.morg.spawning_upgrade_mod");
                harmony.PatchAll();

                // Dynamically patch AutomatDirectionProviderForBridge.Start and ForCorner.Start
                // We use reflection so we don't need a compile-time reference to the type.
                PatchAutomatProviderStart(harmony, "AutomatDirectionProviderForBridge",
                    typeof(SpawningUpgradePlugin).GetMethod(nameof(BridgeStart_Postfix)));
                PatchAutomatProviderStart(harmony, "AutomatDirectionProviderForCorner",
                    typeof(SpawningUpgradePlugin).GetMethod(nameof(CornerStart_Postfix)));

                Logger.LogInfo("Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply Harmony patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void PatchAutomatProviderStart(Harmony harmony, string typeName, MethodInfo postfixMethod)
        {
            try
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t == null)
                {
                    LoggerInstance.LogWarning($"PatchAutomatProviderStart: type '{typeName}' not found by AccessTools.");
                    return;
                }
                var start = t.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (start == null)
                {
                    LoggerInstance.LogWarning($"PatchAutomatProviderStart: Start() not found on '{typeName}'.");
                    return;
                }
                harmony.Patch(start, postfix: new HarmonyMethod(postfixMethod));
                LoggerInstance.LogInfo($"Patched {typeName}.Start successfully.");
            }
            catch (Exception ex)
            {
                LoggerInstance.LogError($"PatchAutomatProviderStart({typeName}) failed: {ex.Message}");
            }
        }

        public static void CopyUpgradeFields(Upgrade source, Upgrade target)
        {
            if (source == null || target == null) return;
            foreach (var fieldName in new[] { "soundEventIds", "particleSystems", "satisfactionTextPosition" })
            {
                var field = typeof(Upgrade).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    field.SetValue(target, field.GetValue(source));
            }
        }

        // ─── Patch: extend max level on the native Speed upgrade ───────────────────

        [HarmonyPatch(typeof(Upgrade_BaggageSpawner_Speed), nameof(Upgrade_BaggageSpawner_Speed.GetMaxLevel))]
        public static class SpawnerSpeedMaxLevelPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                __result = 4; // was 2 in base game
                return false;
            }
        }

        // ─── Patch: extend description to handle 5 levels ──────────────────────────

        [HarmonyPatch(typeof(Upgrade_BaggageSpawner_Speed), nameof(Upgrade_BaggageSpawner_Speed.ProvideLocalizedDescription))]
        public static class SpawnerSpeedDescriptionPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(Upgrade_BaggageSpawner_Speed __instance, ref string __result)
            {
                int level = __instance.GetCurrentLevel();
                int max = __instance.GetMaxLevel();

                string currentTime = ExtendedTimeBetweenSpawns[level] + "s";
                if (level > 0)
                    currentTime = ColorText.Positive(currentTime);

                string nextTime = "-";
                if (level < max)
                    nextTime = ColorText.Positive(ExtendedTimeBetweenSpawns[level + 1] + "s");

                string warning = (level == max - 1)
                    ? "\n<color=red>WARNING: 1 bag/sec may cause severe lag and physics issues!</color>"
                    : "";

                __result = string.Format(
                    "Reduces spawn interval and increases belt speed (+{0:F1} roller speed/level). Current: {1}. Next: {2}.{3}",
                    AdditionalSpeedForRollersPerLevel, currentTime, nextTime, warning);
                return false;
            }
        }

        // ─── Patch: extend Automat speed max level ─────────────────────────────────

        [HarmonyPatch(typeof(Upgrade_Automat_Speed), nameof(Upgrade_Automat_Speed.GetMaxLevel))]
        public static class AutomatSpeedMaxLevelPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                __result = 6;
                return false;
            }
        }

        // ─── Patch: inject extra balancing costs ───────────────────────────────────

        [HarmonyPatch(typeof(Balancing), MethodType.Constructor)]
        public static class BalancingConstructorPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Balancing __instance, BalancingConfiguration ____balancingConfiguration)
            {
                if (____balancingConfiguration == null) return;
                var upBal = ____balancingConfiguration.upgradeBalancing;
                if (upBal == null) return;

                // Add 2 more levels to the native "baggage-spawner-speed" upgrade costs
                if (upBal.upgradeCosts.TryGetValue("baggage-spawner-speed", out var speedCosts))
                {
                    if (speedCosts.costs.Count == 2)
                    {
                        speedCosts.costs.Add(3500);
                        speedCosts.requiredUpgradePoints.Add(20);
                        speedCosts.costs.Add(6000);
                        speedCosts.requiredUpgradePoints.Add(30);
                        LoggerInstance.LogInfo("Injected 2 extra cost levels for 'baggage-spawner-speed'.");
                    }
                }
                else
                {
                    LoggerInstance.LogWarning("Could not find 'baggage-spawner-speed' in upgradeCosts — was the game updated again?");
                }

                // Reset upgrade costs (free, single-use)
                if (!upBal.upgradeCosts.ContainsKey("baggage-spawner-speed-reset"))
                {
                    var costs = new UpgradeCosts();
                    costs.costs.Add(0);
                    costs.requiredUpgradePoints.Add(0);
                    upBal.upgradeCosts["baggage-spawner-speed-reset"] = costs;
                    LoggerInstance.LogInfo("Injected balancing costs for 'baggage-spawner-speed-reset'.");
                }

                // Bridge speed upgrade: 1 level, doubles throughput ($2500, 15 wrenches)
                if (!upBal.upgradeCosts.ContainsKey("automat-speed-bridge"))
                {
                    var costs = new UpgradeCosts();
                    costs.costs.Add(2500);
                    costs.requiredUpgradePoints.Add(15);
                    upBal.upgradeCosts["automat-speed-bridge"] = costs;
                    LoggerInstance.LogInfo("Injected balancing costs for 'automat-speed-bridge'.");
                }

                // Corner/passageway speed upgrade: 1 level, doubles throughput ($1500, 10 wrenches)
                if (!upBal.upgradeCosts.ContainsKey("automat-speed-corner"))
                {
                    var costs = new UpgradeCosts();
                    costs.costs.Add(1500);
                    costs.requiredUpgradePoints.Add(10);
                    upBal.upgradeCosts["automat-speed-corner"] = costs;
                    LoggerInstance.LogInfo("Injected balancing costs for 'automat-speed-corner'.");
                }

                // Extend Automat speed costs with a level 6 entry
                string[] automatSpeedKeys = new string[]
                {
                    "automat-speed-airline-sticker",
                    "automat-speed-baggage-tag",
                    "automat-speed-baggage-type",
                    "automat-speed-illegal-substance",
                    "automat-speed-lost-and-found",
                    "automat-speed-target-airport",
                    "automat-speed-weight",
                    "automat-speed-x-ray"
                };

                foreach (var key in automatSpeedKeys)
                {
                    if (upBal.upgradeCosts.TryGetValue(key, out var costs) && costs.costs.Count == 5)
                    {
                        costs.costs.Add(4000);
                        costs.requiredUpgradePoints.Add(25);
                    }
                }
            }
        }

        // ─── Patch: add Reset upgrade to WorkButton ───────────────────────────────

        [HarmonyPatch(typeof(BaggageSpawner), "Start")]
        public static class BaggageSpawnerStartPatch
        {
            [HarmonyPostfix]
            public static void Postfix(BaggageSpawner __instance, DiContainer ____diContainer)
            {
                try
                {
                    var workButton = UnityEngine.Object.FindAnyObjectByType<WorkButton>();
                    if (workButton == null) return;

                    if (workButton.GetComponent<Upgrade_BaggageSpawner_Reset>() != null) return;

                    int defaultLayer = LayerMask.NameToLayer("Default");
                    workButton.gameObject.layer = defaultLayer;
                    foreach (var col in workButton.GetComponentsInChildren<Collider>(true))
                        col.gameObject.layer = defaultLayer;

                    var resetUpgrade = workButton.gameObject.AddComponent<Upgrade_BaggageSpawner_Reset>();

                    var sourceUpgrade = UnityEngine.Object.FindObjectsByType<Upgrade>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(u => u != null && u != resetUpgrade);
                    if (sourceUpgrade != null)
                        CopyUpgradeFields(sourceUpgrade, resetUpgrade);

                    ____diContainer.Inject(resetUpgrade);
                    LoggerInstance.LogInfo("Added Upgrade_BaggageSpawner_Reset to WorkButton.");
                }
                catch (Exception ex)
                {
                    LoggerInstance.LogError($"BaggageSpawnerStartPatch error: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        // ─── Reflection-based postfixes for Bridge Start ──────────────────────
        // Called by PatchAutomatProviderStart() via reflection.
        // __instance is the AutomatDirectionProviderForBridge MonoBehaviour.

        public static void BridgeStart_Postfix(MonoBehaviour __instance)
        {
            try
            {
                var building = __instance.GetComponentInParent<Building>();
                if (building != null && building.GetId() == "conveyor-belt-bridge-high")
                {
                    // This is the "Passage" machine
                    AddSpeedUpgradeToBuilding<Upgrade_Corner_Speed>(__instance);
                }
                else
                {
                    // This is the "Bridge" machine
                    AddSpeedUpgradeToBuilding<Upgrade_Bridge_Speed>(__instance);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.LogError($"BridgeStart_Postfix error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // CornerStart_Postfix intentionally does nothing — corner (turntable) ≠ passageway.
        public static void CornerStart_Postfix(MonoBehaviour __instance) { }

        /// <summary>
        /// Core helper: walks up to the Building root from a provider component,
        /// adds UpgradeableInEnvironment with a unique positional ID (if not already present),
        /// adds the TUpgrade component on the same root GO, injects both, and restores save data.
        /// This is what makes the upgrade panel actually appear when hovering the machine.
        /// </summary>
        private static void AddSpeedUpgradeToBuilding<TUpgrade>(MonoBehaviour providerInstance)
            where TUpgrade : Upgrade
        {
            // Find the Building root (all automat machines have one)
            var building = providerInstance.GetComponentInParent<Building>();
            if (building == null)
            {
                LoggerInstance.LogWarning($"AddSpeedUpgradeToBuilding<{typeof(TUpgrade).Name}>: no Building found above {providerInstance.name}.");
                return;
            }
            var root = building.gameObject;

            // Don’t add twice
            if (root.GetComponent<TUpgrade>() != null) return;

            // We use reflection because UpgradeableInEnvironment is not resolved at compile-time.
            Type upgradeableType = AccessTools.TypeByName("UpgradeableInEnvironment");

            Component upgradeable = null;
            bool isNew = false;
            if (upgradeableType != null)
            {
                upgradeable = root.GetComponent(upgradeableType);
                if (upgradeable == null)
                {
                    upgradeable = root.AddComponent(upgradeableType);
                    isNew = true;
                    var pos = building.transform.position;
                    var uid = $"{building.GetId()}-{Mathf.RoundToInt(pos.x)},{Mathf.RoundToInt(pos.z)}";
                    var setIdMethod = upgradeableType.GetMethod("SetId", BindingFlags.Public | BindingFlags.Instance);
                    if (setIdMethod != null)
                        setIdMethod.Invoke(upgradeable, new object[] { uid });
                    LoggerInstance.LogInfo($"Added UpgradeableInEnvironment id='{uid}' to {root.name}.");
                }
            }
            else
            {
                LoggerInstance.LogWarning("Could not find UpgradeableInEnvironment type via reflection.");
            }

            // Add the upgrade component on the Building root
            var upgrade = root.AddComponent<TUpgrade>();

            // Copy audio/particle visual fields from another existing Upgrade so the buy SFX works
            var src = UnityEngine.Object.FindObjectsByType<Upgrade>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(u => u != null && !(u is TUpgrade));
            if (src != null) CopyUpgradeFields(src, upgrade);

            // Inject via the nearest DiContainer
            var di = FindDiContainerInHierarchy(providerInstance);
            if (di != null)
            {
                if (isNew) di.Inject(upgradeable);
                di.Inject(upgrade);
            }
            else
            {
                LoggerInstance.LogWarning($"AddSpeedUpgradeToBuilding<{typeof(TUpgrade).Name}>: no DiContainer found.");
            }

            // Restore saved level and call ApplyEffect
            if (upgradeableType != null && upgradeable != null)
            {
                var applyMethod = upgradeableType.GetMethod("ApplyUpgradesFromPersistence", BindingFlags.Public | BindingFlags.Instance);
                if (applyMethod != null)
                    applyMethod.Invoke(upgradeable, null);
            }
            LoggerInstance.LogInfo($"Added {typeof(TUpgrade).Name} to Building '{root.name}' at {root.transform.position}.");
        }

        /// <summary>Walk the transform hierarchy looking for a _diContainer field on any MonoBehaviour.</summary>
        private static DiContainer FindDiContainerInHierarchy(MonoBehaviour source)
        {
            Transform t = source.transform;
            while (t != null)
            {
                foreach (var comp in t.GetComponents<MonoBehaviour>())
                {
                    if (comp == null) continue;
                    var f = comp.GetType().GetField("_diContainer",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f?.GetValue(comp) is DiContainer di) return di;
                }
                t = t.parent;
            }
            return null;
        }

        // Legacy shim kept for any remaining call sites
        private static void InjectViaDiContainerOnSameGameObject(MonoBehaviour source, MonoBehaviour target)
        {
            FindDiContainerInHierarchy(source)?.Inject(target);
        }


        // ─── Patch: localization strings ───────────────────────────────────────────

        [HarmonyPatch(typeof(LocalizationService), "ResolveLocalizationKey", new Type[] { typeof(string) })]
        public static class LocalizationServiceResolvePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(string localizationKey, ref string __result)
            {
                if (string.IsNullOrEmpty(localizationKey)) return true;

                switch (localizationKey)
                {
                    case "upgrade.baggage-spawner-speed-reset.name":
                        __result = "Reset Spawner Speed";
                        return false;
                    case "upgrade.baggage-spawner-speed-reset.description":
                        __result = "Resets the Spawner Speed upgrade back to level 0. Free of charge — use as a failsafe!";
                        return false;
                    case "upgrade.automat-speed-bridge.name":
                        __result = "Bridge Speed";
                        return false;
                    case "upgrade.automat-speed-bridge.description":
                        __result = "Doubles the throughput of this Bridge, halving its cycle time from 3.5s to 1.75s. Essential at high bag rates!";
                        return false;
                    case "upgrade.automat-speed-corner.name":
                        __result = "Passageway Speed";
                        return false;
                    case "upgrade.automat-speed-corner.description":
                        __result = "Doubles the throughput of this Passageway, halving its cycle time from 1.0s to 0.5s. Essential at high bag rates!";
                        return false;
                    default:
                        return true;
                }
            }
        }

        // ─── Patch: daily stats mod watermark ─────────────────────────────────────

        [HarmonyPatch(typeof(DailyStatsDisplay), "UpdateTexts")]
        public static class DailyStatsDisplayUpdateTextsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(DailyStatsDisplay __instance)
            {
                try
                {
                    var titleField = typeof(DailyStatsDisplay).GetField("titleText", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (titleField?.GetValue(__instance) is TMP_Text titleText && !titleText.text.Contains("MODDED"))
                        titleText.text += " <size=60%><color=yellow>(MODDED)</color></size>";

                    var dayField = typeof(DailyStatsDisplay).GetField("dayText", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (dayField?.GetValue(__instance) is TMP_Text dayText && !dayText.text.Contains("Spawning Mod"))
                        dayText.text += "\n<size=50%><color=#ffaa00>Spawning & Upgrades Mod Active</color></size>";
                }
                catch (Exception ex)
                {
                    LoggerInstance.LogError($"DailyStatsDisplayUpdateTextsPatch error: {ex.Message}");
                }
            }
        }

        // ─── Patch: Fix Bridge/Passage Animations ─────────────────────────────────

        [HarmonyPatch]
        public static class BridgeAnimationPatch
        {
            [HarmonyTargetMethod]
            static MethodBase TargetMethod()
            {
                var type = AccessTools.TypeByName("AutomatDirectionProviderForBridge");
                return type != null ? AccessTools.Method(type, "AnimateInBridge") : null;
            }

            [HarmonyPrefix]
            public static bool Prefix(MonoBehaviour __instance, object _, object canBeProcessedByAutomat, ref System.Collections.IEnumerator __result)
            {
                var automatUi = Traverse.Create(__instance).Field("automatUi").GetValue();
                float currentDuration = Traverse.Create(automatUi).Field("_scanDuration").GetValue<float>();

                if (Mathf.Abs(currentDuration - 3.5f) < 0.01f) return true;

                __result = CustomAnimate(__instance, canBeProcessedByAutomat, currentDuration);
                return false;
            }

            private static System.Collections.IEnumerator CustomAnimate(MonoBehaviour inst, object baggage, float duration)
            {
                var tr = Traverse.Create(inst);
                tr.Field("_canBeProcessedByAutomat").SetValue(baggage);
                tr.Method("Initialize").GetValue();
                tr.Method("TeleportBaggageOnStartPiston").GetValue();

                while (Time.timeScale <= 0f) yield return null;

                var pistonStart = tr.Field("pistonStart").GetValue<Transform>();
                var pistonEnd = tr.Field("pistonEnd").GetValue<Transform>();
                float height = tr.Field("height").GetValue<float>();
                Vector3 initialStart = tr.Field("_startPistonInitialPosition").GetValue<Vector3>();
                Vector3 initialEnd = tr.Field("_endPistonInitialPosition").GetValue<Vector3>();
                var automat = tr.Field("automat").GetValue<MonoBehaviour>();

                float t = 0;
                float p1 = duration * 0.25f;
                while (t < p1)
                {
                    t += Time.deltaTime;
                    float y = Mathf.Lerp(0, height, t / p1);
                    pistonStart.position = initialStart + Vector3.up * y;
                    if (baggage != null && (bool)Traverse.Create(baggage).Method("StillExists").GetValue()) 
                        Traverse.Create(baggage).Property("transform").GetValue<Transform>().position = pistonStart.position;
                    yield return null;
                }
                
                automat.StartCoroutine(MoveEndPistonUp(pistonEnd, initialEnd, height, duration * 0.25f));
                automat.StartCoroutine(DelayedStartPistonDown(inst, pistonStart, initialStart, height, duration * 0.15f, duration * 0.25f));

                float beltSpeed = 0.5f * (3.5f / duration);
                Traverse.Create(tr.Field("linearConveyorStart").GetValue()).Method("ChangeSpeed", beltSpeed).GetValue();
                Traverse.Create(tr.Field("linearConveyorMiddle").GetValue()).Method("ChangeSpeed", beltSpeed).GetValue();
                Traverse.Create(tr.Field("linearConveyorEnd").GetValue()).Method("ChangeSpeed", beltSpeed).GetValue();

                t = 0;
                float p2 = duration * 0.45f;
                while (t < p2)
                {
                    t += Time.deltaTime;
                    Vector3 pos = Vector3.Lerp(initialStart + Vector3.up * height, initialEnd + Vector3.up * height, t / p2);
                    if (baggage != null && (bool)Traverse.Create(baggage).Method("StillExists").GetValue()) 
                        Traverse.Create(baggage).Property("transform").GetValue<Transform>().position = pos;
                    yield return null;
                }

                Traverse.Create(tr.Field("linearConveyorStart").GetValue()).Method("ChangeSpeed", 0f).GetValue();
                Traverse.Create(tr.Field("linearConveyorMiddle").GetValue()).Method("ChangeSpeed", 0f).GetValue();
                Traverse.Create(tr.Field("linearConveyorEnd").GetValue()).Method("ChangeSpeed", 0f).GetValue();

                t = 0;
                float p3 = duration * 0.25f;
                while (t < p3)
                {
                    t += Time.deltaTime;
                    float y = Mathf.Lerp(height, 0, t / p3);
                    pistonEnd.position = initialEnd + Vector3.up * y;
                    if (baggage != null && (bool)Traverse.Create(baggage).Method("StillExists").GetValue()) 
                        Traverse.Create(baggage).Property("transform").GetValue<Transform>().position = pistonEnd.position;
                    yield return null;
                }
            }

            private static System.Collections.IEnumerator MoveEndPistonUp(Transform piston, Vector3 initial, float height, float duration)
            {
                float t = 0;
                while (t < duration)
                {
                    t += Time.deltaTime;
                    piston.position = initial + Vector3.up * Mathf.Lerp(0, height, t / duration);
                    yield return null;
                }
                piston.position = initial + Vector3.up * height;
            }

            private static System.Collections.IEnumerator DelayedStartPistonDown(MonoBehaviour inst, Transform piston, Vector3 initial, float height, float delay, float duration)
            {
                float d = 0;
                while (d < delay)
                {
                    d += Time.deltaTime;
                    yield return null;
                }

                Traverse.Create(Traverse.Create(inst).Field("linearConveyorStart").GetValue()).Method("ChangeSpeed", 0f).GetValue();
                
                float t = 0;
                while (t < duration)
                {
                    t += Time.deltaTime;
                    piston.position = initial + Vector3.up * Mathf.Lerp(height, 0, t / duration);
                    yield return null;
                }
                piston.position = initial;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Upgrade_BaggageSpawner_Reset
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Free, reusable upgrade that resets Upgrade_BaggageSpawner_Speed back to level 0.
    /// </summary>
    public class Upgrade_BaggageSpawner_Reset : Upgrade
    {
        public override string GetKey() => "baggage-spawner-speed-reset";
        public override int GetMaxLevel() => 1;

        public override void ApplyEffect()
        {
            if (GetCurrentLevel() <= 0) return;

            var levelField = typeof(Upgrade).GetField("level", BindingFlags.NonPublic | BindingFlags.Instance);
            if (levelField == null)
            {
                SpawningUpgradePlugin.LoggerInstance.LogError("Reset: could not find 'level' field on Upgrade.");
                return;
            }

            var speedUpgrade = GetComponent<Upgrade_BaggageSpawner_Speed>();
            if (speedUpgrade != null)
            {
                levelField.SetValue(speedUpgrade, 0);
                speedUpgrade.ApplyEffect();
                SpawningUpgradePlugin.LoggerInstance.LogInfo("Reset Upgrade_BaggageSpawner_Speed to level 0.");
            }
            else
            {
                SpawningUpgradePlugin.LoggerInstance.LogWarning("Reset: Upgrade_BaggageSpawner_Speed not found on WorkButton.");
            }

            // Reset self so it can be purchased again
            levelField.SetValue(this, 0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Upgrade_Bridge_Speed
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Halves the Bridge cycle time (3.5s → 1.75s) by updating the AutomatUi scan duration.
    /// </summary>
    public class Upgrade_Bridge_Speed : Upgrade
    {
        public override string GetKey() => "automat-speed-bridge";
        public override int GetMaxLevel() => 1;

        public override void ApplyEffect()
        {
            SetAutomatUiScanDuration(
                GetCurrentLevel() >= 1
                    ? SpawningUpgradePlugin.BridgeBaseDuration / 2f
                    : SpawningUpgradePlugin.BridgeBaseDuration);
        }

        private void SetAutomatUiScanDuration(float duration)
        {
            // Use reflection to avoid a compile-time dependency on AutomatUi
            foreach (var comp in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp == null) continue;
                var m = comp.GetType().GetMethod("SetScanDuration",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(float) }, null);
                if (m != null)
                {
                    m.Invoke(comp, new object[] { duration });
                    SpawningUpgradePlugin.LoggerInstance.LogInfo(
                        $"Upgrade_Bridge_Speed: set scan duration to {duration}s on {comp.name} (level {GetCurrentLevel()}).");
                    return;
                }
            }
            SpawningUpgradePlugin.LoggerInstance.LogWarning("Upgrade_Bridge_Speed: AutomatUi.SetScanDuration not found.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Upgrade_Corner_Speed
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Halves the Passageway (corner) cycle time (1.0s → 0.5s) by updating the AutomatUi scan duration.
    /// </summary>
    public class Upgrade_Corner_Speed : Upgrade
    {
        public override string GetKey() => "automat-speed-corner";
        public override int GetMaxLevel() => 1;

        public override void ApplyEffect()
        {
            SetAutomatUiScanDuration(
                GetCurrentLevel() >= 1
                    ? SpawningUpgradePlugin.CornerBaseDuration / 2f
                    : SpawningUpgradePlugin.CornerBaseDuration);
        }

        private void SetAutomatUiScanDuration(float duration)
        {
            foreach (var comp in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (comp == null) continue;
                var m = comp.GetType().GetMethod("SetScanDuration",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(float) }, null);
                if (m != null)
                {
                    m.Invoke(comp, new object[] { duration });
                    SpawningUpgradePlugin.LoggerInstance.LogInfo(
                        $"Upgrade_Corner_Speed: set scan duration to {duration}s on {comp.name} (level {GetCurrentLevel()}).");
                    return;
                }
            }
            SpawningUpgradePlugin.LoggerInstance.LogWarning("Upgrade_Corner_Speed: AutomatUi.SetScanDuration not found.");
        }
    }
}
