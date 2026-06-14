using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Zenject;
using _scripts._by_scene._common._balancing;
using _scripts._by_scene._common._balancing._configuration;
using _scripts._by_scene._game._upgrades;
using _scripts._by_scene._game._upgrades._specific_upgrades;
using _scripts._by_scene._game._automation._upgrades;
using _scripts._by_scene._game._baggage_spawner;
using _scripts._by_scene._game._work_button;
using _scripts._by_scene._game._job;
using _scripts._by_scene._game._building;
using _scripts._by_scene._game._baggage;
using _scripts._text_style;
using Produktivkeller.SimpleLocalization.Unity.Core;
using TMPro;
using _scripts._by_scene._game._day_time._day_transition;

namespace SpawningUpgradeMod
{
    [BepInPlugin("com.morg.spawning_upgrade_mod", "Spawning and Machine Upgrades Mod", "1.0.0")]
    public class SpawningUpgradePlugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource LoggerInstance;
        internal static List<LinearConveyor> DispenserLinearConveyors = new List<LinearConveyor>();
        internal static Dictionary<LinearConveyor, float> DispenserLinearBaseSpeeds = new Dictionary<LinearConveyor, float>();
        internal static List<RadialConveyor> DispenserRadialConveyors = new List<RadialConveyor>();
        internal static Dictionary<RadialConveyor, float> DispenserRadialBaseSpeeds = new Dictionary<RadialConveyor, float>();

        // Roller type resolved at runtime via reflection (lives in ModularConveyorTools.dll)
        private static Type _rollerType = null;
        private static FieldInfo _rollerSpeedField = null;
        private static bool _rollerTypeResolved = false;

        // Cache of only the spawner's roller Rigidbodies — so the FixedUpdate postfix
        // bails out immediately for the hundreds of non-spawner rollers every physics frame.
        internal static HashSet<Rigidbody> _spawnerRollerRigidbodies = new HashSet<Rigidbody>();

        // Pre-computed multiplier for the roller postfix — updated in BoostSpawnerRollers()
        // and UpdateDispenserSpeed(). NEVER call FindAnyObjectByType from a FixedUpdate path.
        internal static float _rollerAngularMultiplier = 1.5f;

        // Tracks which Roller instances have already had their tangentSpeed boosted,
        // so we don't double-multiply if BoostSpawnerRollers and RollerStart_Postfix both fire.
        internal static HashSet<Component> _boostedRollers = new HashSet<Component>();

        // Cached BaggageSpawner reference — set once at Start, used by RollerStart_Postfix
        // to avoid calling FindAnyObjectByType for every single roller during load.
        internal static BaggageSpawner _cachedSpawner = null;



        private void Awake()
        {
            LoggerInstance = Logger;
            Logger.LogInfo("Spawning and Machine Upgrades Mod is loading...");

            // Extend automatic machines DurationPerLevel to support level 6 (1.0s processing duration)
            try
            {
                var field = typeof(Upgrade_Automat_Speed).GetField("DurationPerLevel", BindingFlags.Static | BindingFlags.NonPublic);
                if (field != null)
                {
                    var newArray = new float[] { 20f, 12f, 8f, 6f, 4f, 3f, 1f };
                    field.SetValue(null, newArray);
                    Logger.LogInfo("Successfully extended Upgrade_Automat_Speed.DurationPerLevel to support Level 6.");
                }
                else
                {
                    Logger.LogError("Could not find private static field 'DurationPerLevel' in 'Upgrade_Automat_Speed'.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to overwrite DurationPerLevel: {ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                var harmony = new Harmony("com.morg.spawning_upgrade_mod");
                harmony.PatchAll();

                // Patch the compiler-generated MoveNext method in BaggageSpawner's coroutine
                var nestedType = typeof(BaggageSpawner).GetNestedType("<SpawnBaggageWhileShiftIsActive>d__22", BindingFlags.NonPublic | BindingFlags.Instance);
                if (nestedType != null)
                {
                    var moveNextMethod = nestedType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (moveNextMethod != null)
                    {
                        harmony.Patch(moveNextMethod, postfix: new HarmonyMethod(typeof(SpawningUpgradePlugin).GetMethod(nameof(MoveNext_Postfix))));
                        Logger.LogInfo("Successfully patched SpawnBaggageWhileShiftIsActive MoveNext.");
                    }
                    else
                    {
                        Logger.LogError("Could not find MoveNext method in <SpawnBaggageWhileShiftIsActive>d__22.");
                    }
                }
                else
                {
                    Logger.LogError("Could not find nested type <SpawnBaggageWhileShiftIsActive>d__22 in BaggageSpawner.");
                }

                // Dynamically patch Roller.FixedUpdate so the angular velocity boost persists every physics frame.
                // We do this here (not via [HarmonyPatch] attribute) because Roller type isn't a compile-time reference.
                try
                {
                    Type rollerType = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("Roller");
                        if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t)) { rollerType = t; break; }
                    }
                    if (rollerType != null)
                    {
                        var fixedUpdate = rollerType.GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fixedUpdate != null)
                        {
                            harmony.Patch(fixedUpdate, postfix: new HarmonyMethod(typeof(SpawningUpgradePlugin).GetMethod(nameof(RollerFixedUpdate_Postfix))));
                            Logger.LogInfo($"Successfully patched Roller.FixedUpdate for speed boost.");
                        }
                        else
                        {
                            Logger.LogWarning("Roller type found but FixedUpdate method not found (may be compiled differently).");
                        }

                        // Also patch Roller.Start so each roller registers itself after its own init
                        var startMethod = rollerType.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        if (startMethod != null)
                        {
                            harmony.Patch(startMethod, postfix: new HarmonyMethod(typeof(SpawningUpgradePlugin).GetMethod(nameof(RollerStart_Postfix))));
                            Logger.LogInfo("Successfully patched Roller.Start for spawner registration.");
                        }
                        else
                        {
                            Logger.LogWarning("Roller type found but Start method not found.");
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Roller type not found in any loaded assembly at Awake time — will retry at spawner Start.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to patch Roller.FixedUpdate: {ex.Message}\n{ex.StackTrace}");
                }

                Logger.LogInfo("Spawning and Machine Upgrades Mod patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static void RegisterDispenserConveyors(List<LinearConveyor> linears, List<RadialConveyor> radials)
        {
            foreach (var conveyor in linears)
            {
                if (conveyor == null) continue;
                if (!DispenserLinearConveyors.Contains(conveyor))
                {
                    DispenserLinearConveyors.Add(conveyor);
                }
                if (!DispenserLinearBaseSpeeds.ContainsKey(conveyor))
                {
                    DispenserLinearBaseSpeeds[conveyor] = conveyor.speed;
                    LoggerInstance.LogInfo($"Dispenser linear conveyor '{conveyor.gameObject.name}' base speed recorded as: {conveyor.speed}");
                }
            }

            foreach (var conveyor in radials)
            {
                if (conveyor == null) continue;
                if (!DispenserRadialConveyors.Contains(conveyor))
                {
                    DispenserRadialConveyors.Add(conveyor);
                }
                if (!DispenserRadialBaseSpeeds.ContainsKey(conveyor))
                {
                    DispenserRadialBaseSpeeds[conveyor] = conveyor.speed;
                    LoggerInstance.LogInfo($"Dispenser radial conveyor '{conveyor.gameObject.name}' base speed recorded as: {conveyor.speed}");
                }
            }

            UpdateDispenserSpeed();
        }

        public static Vector3 SnapToCardinal(Vector3 vector)
        {
            float x = Mathf.Abs(vector.x);
            float y = Mathf.Abs(vector.y);
            float z = Mathf.Abs(vector.z);

            if (x > y && x > z)
            {
                return new Vector3(Mathf.Sign(vector.x), 0f, 0f);
            }
            else if (y > x && y > z)
            {
                return new Vector3(0f, Mathf.Sign(vector.y), 0f);
            }
            else
            {
                return new Vector3(0f, 0f, Mathf.Sign(vector.z));
            }
        }

        public static bool IsConveyorInSpawnerPath(Vector3 pos)
        {
            var spawner = UnityEngine.Object.FindAnyObjectByType<BaggageSpawner>();
            if (spawner == null) return false;

            Transform spawnPosTransform = spawner.transform;
            var spawnPositionField = typeof(BaggageSpawner).GetField("spawnPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            if (spawnPositionField != null)
            {
                if (spawnPositionField.GetValue(spawner) is Transform t)
                {
                    spawnPosTransform = t;
                }
            }

            Vector3 spawnPos = spawnPosTransform.position;
            Vector3 relPos = pos - spawnPos;

            // Snap spawner vectors to cardinal axes to ignore slight rotational misalignments in the editor
            Vector3 snappedForward = SnapToCardinal(spawnPosTransform.forward);
            Vector3 snappedRight = SnapToCardinal(spawnPosTransform.right);
            Vector3 snappedUp = SnapToCardinal(spawnPosTransform.up);

            float forwardDist = Vector3.Dot(relPos, snappedForward);
            float rightDist = Vector3.Dot(relPos, snappedRight);
            float upDist = Vector3.Dot(relPos, snappedUp);

            return Mathf.Abs(rightDist) < 1.8f && forwardDist > -1.5f && forwardDist < 25.0f && Mathf.Abs(upDist) < 2.5f;
        }


        public static float CalculateDispenserLinearSpeed(LinearConveyor conveyor)
        {
            float speedBoost = 1.5f;
            float levelMultiplier = 1f + 0.65f * GetDispenserSpeedUpgradeLevel();
            
            float activeModifierMultiplier = 1f;
            try
            {
                var activeModifiers = UnityEngine.Object.FindAnyObjectByType<ActiveModifiers>();
                if (activeModifiers != null)
                {
                    activeModifierMultiplier = activeModifiers.GetConveyorBeltSpeedMultiplier();
                }
            }
            catch (Exception) {}

            float baseSpeed = 0.5f;
            if (DispenserLinearBaseSpeeds.TryGetValue(conveyor, out float origBase))
            {
                baseSpeed = Mathf.Sign(origBase) * 0.5f;
            }
            else
            {
                baseSpeed = Mathf.Sign(conveyor.speed) * 0.5f;
            }
            return baseSpeed * speedBoost * activeModifierMultiplier * levelMultiplier;
        }

        public static float CalculateDispenserRadialSpeed(RadialConveyor conveyor)
        {
            float speedBoost = 1.5f;
            float levelMultiplier = 1f + 0.65f * GetDispenserSpeedUpgradeLevel();
            
            float activeModifierMultiplier = 1f;
            try
            {
                var activeModifiers = UnityEngine.Object.FindAnyObjectByType<ActiveModifiers>();
                if (activeModifiers != null)
                {
                    activeModifierMultiplier = activeModifiers.GetConveyorBeltSpeedMultiplier();
                }
            }
            catch (Exception) {}

            float baseSpeed = 0.5f;
            if (DispenserRadialBaseSpeeds.TryGetValue(conveyor, out float origBase))
            {
                baseSpeed = Mathf.Sign(origBase) * 0.5f;
            }
            else
            {
                baseSpeed = Mathf.Sign(conveyor.speed) * 0.5f;
            }
            return baseSpeed * speedBoost * activeModifierMultiplier * levelMultiplier;
        }

        public static void RegisterSingleLinearConveyor(LinearConveyor conveyor)
        {
            if (conveyor == null) return;
            // Never boost player-placed belts — only scene-placed spawner conveyors
            if (conveyor.GetComponentInParent<Building>() != null) return;
            if (!DispenserLinearConveyors.Contains(conveyor))
            {
                DispenserLinearConveyors.Add(conveyor);
            }
            if (!DispenserLinearBaseSpeeds.ContainsKey(conveyor))
            {
                DispenserLinearBaseSpeeds[conveyor] = conveyor.speed;
                LoggerInstance.LogInfo($"Registered linear conveyor '{conveyor.gameObject.name}' at {conveyor.transform.position} with base speed {conveyor.speed}");
            }
            float targetSpeed = CalculateDispenserLinearSpeed(conveyor);
            conveyor.ChangeSpeed(targetSpeed);
        }


        public static void RegisterSingleRadialConveyor(RadialConveyor conveyor)
        {
            if (conveyor == null) return;
            // Never boost player-placed belts — only scene-placed spawner conveyors
            if (conveyor.GetComponentInParent<Building>() != null) return;
            if (!DispenserRadialConveyors.Contains(conveyor))
            {
                DispenserRadialConveyors.Add(conveyor);
            }
            if (!DispenserRadialBaseSpeeds.ContainsKey(conveyor))
            {
                DispenserRadialBaseSpeeds[conveyor] = conveyor.speed;
                LoggerInstance.LogInfo($"Registered radial conveyor '{conveyor.gameObject.name}' at {conveyor.transform.position} with base speed {conveyor.speed}");
            }
            float targetSpeed = CalculateDispenserRadialSpeed(conveyor);
            conveyor.ChangeSpeed(targetSpeed);
        }


        public static void UpdateDispenserSpeed()
        {
            DispenserLinearConveyors.RemoveAll(c => c == null);
            DispenserRadialConveyors.RemoveAll(c => c == null);

            foreach (var conveyor in DispenserLinearConveyors)
            {
                if (conveyor == null) continue;
                float newSpeed = CalculateDispenserLinearSpeed(conveyor);
                conveyor.ChangeSpeed(newSpeed);
                LoggerInstance.LogInfo($"Updated linear conveyor '{conveyor.gameObject.name}' speed to: {newSpeed} (boost: 1.5x, level: {GetDispenserSpeedUpgradeLevel()})");
            }

            foreach (var conveyor in DispenserRadialConveyors)
            {
                if (conveyor == null) continue;
                float newSpeed = CalculateDispenserRadialSpeed(conveyor);
                conveyor.ChangeSpeed(newSpeed);
                LoggerInstance.LogInfo($"Updated radial conveyor '{conveyor.gameObject.name}' speed to: {newSpeed} (boost: 1.5x, level: {GetDispenserSpeedUpgradeLevel()})");
            }

            // Also re-boost the Roller-based spawner chute section
            BoostSpawnerRollers();
        }

        public static int GetSpawnRateUpgradeLevel()
        {
            var workButton = UnityEngine.Object.FindAnyObjectByType<WorkButton>();
            if (workButton != null)
            {
                var upgrade = workButton.GetComponent<Upgrade_BaggageSpawner_Rate>();
                if (upgrade != null)
                {
                    return upgrade.GetCurrentLevel();
                }
            }
            return 0;
        }

        public static int GetDispenserSpeedUpgradeLevel()
        {
            var workButton = UnityEngine.Object.FindAnyObjectByType<WorkButton>();
            if (workButton != null)
            {
                var upgrade = workButton.GetComponent<Upgrade_Dispenser_Speed>();
                if (upgrade != null)
                {
                    return upgrade.GetCurrentLevel();
                }
            }
            return 0;
        }

        /// <summary>
        /// Resolves the Roller MonoBehaviour type from ModularConveyorTools.dll at runtime.
        /// Roller uses a Rigidbody with angular velocity to push bags via friction.
        /// </summary>
        private static bool TryResolveRollerType()
        {
            if (_rollerTypeResolved) return _rollerType != null;
            _rollerTypeResolved = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Roller");
                if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t))
                {
                    _rollerType = t;
                    // Log ALL fields so we can find the speed-controlling one
                    var allFields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    LoggerInstance.LogInfo($"Roller type found in '{asm.GetName().Name}'. Fields: {string.Join(", ", allFields.Select(f => f.FieldType.Name + " " + f.Name))}");
                    // Try common speed field names, both public and private
                    string[] candidates = { "tangentSpeed", "speed", "_speed", "rollerSpeed", "spinSpeed", "angularSpeed", "rotationSpeed", "velocity" };

                    foreach (var name in candidates)
                    {
                        var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null && f.FieldType == typeof(float))
                        {
                            _rollerSpeedField = f;
                            LoggerInstance.LogInfo($"Roller speed field found: '{name}' (public={f.IsPublic})");
                            break;
                        }
                    }
                    if (_rollerSpeedField == null)
                        LoggerInstance.LogWarning("No speed field found on Roller. Will rely on FixedUpdate postfix only.");
                    return true;
                }
            }
            LoggerInstance.LogWarning("Could not resolve Roller type from any loaded assembly.");
            return false;
        }

        /// <summary>
        /// Finds all Roller Rigidbodies in the BaggageSpawner hierarchy and boosts their angular velocity
        /// to match the dispenser speed upgrade level. The Roller spins its capsule collider;
        /// bags sitting on top get dragged by friction at a rate proportional to angularVelocity.
        /// Base angular velocity was observed at ~0.21 m/s bag speed => we target 0.75+ m/s.
        /// </summary>
        public static void BoostSpawnerRollers()
        {
            try
            {
                if (!TryResolveRollerType()) return;

                var spawner = UnityEngine.Object.FindAnyObjectByType<BaggageSpawner>();
                if (spawner == null) return;

                // Search ONLY within the BaggageSpawner's own subtree, NOT the full root.
                Transform searchRoot = spawner.transform;

                float speedMultiplier = 1.5f * (1f + 0.65f * GetDispenserSpeedUpgradeLevel());

                var rollers = searchRoot.GetComponentsInChildren(_rollerType, true);

                // Rebuild the cache
                _spawnerRollerMap.Clear();
                _spawnerRollerRigidbodies.Clear();
                // Note: do NOT set tangentSpeed here — RollerStart_Postfix handles that
                // per-roller with correct timing (after each Roller's own Start() runs).
                foreach (Component roller in rollers)
                {
                    var rb = roller.GetComponent<Rigidbody>();
                    if (rb == null || !rb.isKinematic) continue;
                    _spawnerRollerMap[roller] = rb;
                    _spawnerRollerRigidbodies.Add(rb);
                }

                // Pre-compute the multiplier and cache it
                _rollerAngularMultiplier = speedMultiplier;
                LoggerInstance.LogInfo($"BoostSpawnerRollers: cached {_spawnerRollerMap.Count}/{rollers.Length} Rollers. Multiplier={speedMultiplier:F2}x Level={GetDispenserSpeedUpgradeLevel()}");

            }
            catch (Exception ex)
            {
                LoggerInstance.LogError($"BoostSpawnerRollers failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static void MoveNext_Postfix(object __instance, ref bool __result)
        {
            if (!__result) return;

            var currentField = __instance.GetType().GetField("<>2__current", BindingFlags.NonPublic | BindingFlags.Instance);
            if (currentField != null)
            {
                var currentVal = currentField.GetValue(__instance);
                if (currentVal is WaitForSeconds)
                {
                    // Default wait time is 5.0 seconds. Each upgrade level subtracts 1.0 second, down to 1.0 second max.
                    int level = GetSpawnRateUpgradeLevel();
                    float interval = 5f - level;
                    if (interval < 1f) interval = 1f;

                    currentField.SetValue(__instance, new WaitForSeconds(interval));
                }
            }
        }

        public static void CopyUpgradeFields(Upgrade source, Upgrade target)
        {
            if (source == null || target == null) return;
            var fields = new string[] { "soundEventIds", "particleSystems", "satisfactionTextPosition" };
            foreach (var fieldName in fields)
            {
                var field = typeof(Upgrade).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(target, field.GetValue(source));
                }
            }
        }

        [HarmonyPatch(typeof(LinearConveyor), nameof(LinearConveyor.ChangeSpeed))]
        public static class LinearConveyorChangeSpeedPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(LinearConveyor __instance, ref float _speed)
            {
                if (DispenserLinearConveyors.Contains(__instance))
                {
                    float targetSpeed = CalculateDispenserLinearSpeed(__instance);
                    if (Mathf.Abs(_speed - targetSpeed) > 0.001f)
                    {
                        _speed = targetSpeed;
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(RadialConveyor), nameof(RadialConveyor.ChangeSpeed))]
        public static class RadialConveyorChangeSpeedPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(RadialConveyor __instance, ref float _speed)
            {
                if (DispenserRadialConveyors.Contains(__instance))
                {
                    float targetSpeed = CalculateDispenserRadialSpeed(__instance);
                    if (Mathf.Abs(_speed - targetSpeed) > 0.001f)
                    {
                        _speed = targetSpeed;
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(LinearConveyor), "Start")]
        public static class LinearConveyorStartPatch
        {
            [HarmonyPostfix]
            public static void Postfix(LinearConveyor __instance)
            {
                if (IsConveyorInSpawnerPath(__instance.transform.position))
                {
                    RegisterSingleLinearConveyor(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(RadialConveyor), "Start")]
        public static class RadialConveyorStartPatch
        {
            [HarmonyPostfix]
            public static void Postfix(RadialConveyor __instance)
            {
                if (IsConveyorInSpawnerPath(__instance.transform.position))
                {
                    RegisterSingleRadialConveyor(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(BaggageSpawner), "Start")]
        public static class BaggageSpawnerStartPatch
        {
            [HarmonyPostfix]
            public static void Postfix(BaggageSpawner __instance, DiContainer ____diContainer)
            {
                _cachedSpawner = __instance;

                DispenserLinearConveyors.Clear();
                DispenserLinearBaseSpeeds.Clear();
                DispenserRadialConveyors.Clear();
                DispenserRadialBaseSpeeds.Clear();

                var foundLinears = new List<LinearConveyor>();
                var foundRadials = new List<RadialConveyor>();

                foreach (var conveyor in UnityEngine.Object.FindObjectsByType<LinearConveyor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (IsConveyorInSpawnerPath(conveyor.transform.position))
                    {
                        foundLinears.Add(conveyor);
                    }
                }

                foreach (var conveyor in UnityEngine.Object.FindObjectsByType<RadialConveyor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (IsConveyorInSpawnerPath(conveyor.transform.position))
                    {
                        foundRadials.Add(conveyor);
                    }
                }

                if (foundLinears.Count > 0 || foundRadials.Count > 0)
                {
                    RegisterDispenserConveyors(foundLinears, foundRadials);
                }

                var workButton = UnityEngine.Object.FindAnyObjectByType<WorkButton>();
                if (workButton == null) return;

                int defaultLayer = LayerMask.NameToLayer("Default");
                workButton.gameObject.layer = defaultLayer;
                foreach (var col in workButton.GetComponentsInChildren<Collider>(true))
                {
                    col.gameObject.layer = defaultLayer;
                }

                if (workButton.GetComponent<UpgradeableInEnvironment>() != null) return;

                var upgradeable = workButton.gameObject.AddComponent<UpgradeableInEnvironment>();
                upgradeable.SetId("work-button-upgrades");

                var rateUpgrade = workButton.gameObject.AddComponent<Upgrade_BaggageSpawner_Rate>();
                var speedUpgrade = workButton.gameObject.AddComponent<Upgrade_Dispenser_Speed>();
                var resetUpgrade = workButton.gameObject.AddComponent<Upgrade_BaggageSpawner_Reset>();

                var sourceUpgrade = UnityEngine.Object.FindObjectsByType<Upgrade>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(u => u != null && u != rateUpgrade && u != speedUpgrade && u != resetUpgrade);
                if (sourceUpgrade != null)
                {
                    CopyUpgradeFields(sourceUpgrade, rateUpgrade);
                    CopyUpgradeFields(sourceUpgrade, speedUpgrade);
                    CopyUpgradeFields(sourceUpgrade, resetUpgrade);
                }

                ____diContainer.Inject(upgradeable);
                ____diContainer.Inject(rateUpgrade);
                ____diContainer.Inject(speedUpgrade);
                ____diContainer.Inject(resetUpgrade);

                upgradeable.ApplyUpgradesFromPersistence();
                BoostSpawnerRollers();
            }
        }

        [HarmonyPatch(typeof(Upgrade_Automat_Speed), nameof(Upgrade_Automat_Speed.GetMaxLevel))]
        public static class Upgrade_Automat_Speed_GetMaxLevel_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(ref int __result)
            {
                __result = 6;
                return false;
            }
        }

        internal static Dictionary<Component, Rigidbody> _spawnerRollerMap = new Dictionary<Component, Rigidbody>();

        public static void RollerStart_Postfix(MonoBehaviour __instance)
        {
            try
            {
                var rb = __instance.GetComponent<Rigidbody>();
                if (rb == null || !rb.isKinematic) return;

                if (_cachedSpawner == null) return;

                Transform t = __instance.transform;
                bool inSpawner = false;
                while (t != null)
                {
                    if (t == _cachedSpawner.transform) { inSpawner = true; break; }
                    t = t.parent;
                }
                if (!inSpawner) return;

                _spawnerRollerMap[__instance] = rb;
                _spawnerRollerRigidbodies.Add(rb);

                if (_boostedRollers.Contains(__instance)) return;
                _boostedRollers.Add(__instance);

                if (_rollerSpeedField != null)
                {
                    float current = (float)_rollerSpeedField.GetValue(__instance);
                    if (current != 0f)
                    {
                        _rollerSpeedField.SetValue(__instance, current * _rollerAngularMultiplier);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.LogError($"RollerStart_Postfix error: {ex.Message}");
            }
        }


        public static void RollerFixedUpdate_Postfix(MonoBehaviour __instance)
        {
            if (!_spawnerRollerMap.TryGetValue(__instance, out var rb)) return;
            rb.angularVelocity *= _rollerAngularMultiplier;
        }

        [HarmonyPatch(typeof(Balancing), MethodType.Constructor)]
        public static class BalancingConstructorPatch
        {
            [HarmonyPostfix]
            public static void Postfix(Balancing __instance, BalancingConfiguration ____balancingConfiguration)
            {
                if (____balancingConfiguration == null) return;
                var upBal = ____balancingConfiguration.upgradeBalancing;
                if (upBal == null) return;

                if (!upBal.upgradeCosts.ContainsKey("work-button-spawn-rate"))
                {
                    var costs = new UpgradeCosts();
                    costs.costs.AddRange(new int[] { 1000, 2000, 3500, 5000 });
                    costs.requiredUpgradePoints.AddRange(new int[] { 10, 15, 20, 30 });
                    upBal.upgradeCosts["work-button-spawn-rate"] = costs;
                    LoggerInstance.LogInfo("Injected balancing costs for 'work-button-spawn-rate'.");
                }

                // 2. Inject cost levels for the new dispenser conveyor speed upgrade ($800, $1500, $2500)
                if (!upBal.upgradeCosts.ContainsKey("work-button-dispenser-speed"))
                {
                    var costs = new UpgradeCosts();
                    costs.costs.AddRange(new int[] { 800, 1500, 2500 });
                    costs.requiredUpgradePoints.AddRange(new int[] { 5, 12, 20 });
                    upBal.upgradeCosts["work-button-dispenser-speed"] = costs;
                    LoggerInstance.LogInfo("Injected balancing costs for 'work-button-dispenser-speed'.");
                }

                if (!upBal.upgradeCosts.ContainsKey("work-button-spawn-reset"))
                {
                    var costs = new UpgradeCosts();
                    costs.costs.Add(0);
                    costs.requiredUpgradePoints.Add(0);
                    upBal.upgradeCosts["work-button-spawn-reset"] = costs;
                    LoggerInstance.LogInfo("Injected balancing costs for 'work-button-spawn-reset'.");
                }

                // 3. Inject level 6 costs to all automatic sorting machines
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
                    if (upBal.upgradeCosts.TryGetValue(key, out var costs))
                    {
                        if (costs.costs.Count == 5)
                        {
                            costs.costs.Add(4000);
                            costs.requiredUpgradePoints.Add(25);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LocalizationService), "ResolveLocalizationKey", new Type[] { typeof(string) })]
        public static class LocalizationServiceResolvePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(string localizationKey, ref string __result)
            {
                if (string.IsNullOrEmpty(localizationKey)) return true;

                switch (localizationKey)
                {
                    case "upgrade.work-button-spawn-rate.name":
                        __result = "Bag Spawn Rate";
                        return false;
                    case "upgrade.work-button-spawn-rate.description":
                        __result = "Reduces the wait time between bag deliveries. Current: {0}. Next: {1}.\n<color=red>WARNING: 1 bag/sec may cause severe lag and physics bugs!</color>";
                        return false;
                    case "upgrade.work-button-dispenser-speed.name":
                        __result = "Wall Conveyor Speed";
                        return false;
                    case "upgrade.work-button-dispenser-speed.description":
                        __result = "Increases the speed of the dispenser conveyor in the wall. Current: {0}. Next: {1}.";
                        return false;
                    case "upgrade.work-button-spawn-reset.name":
                        __result = "Reset Spawner Upgrades";
                        return false;
                    case "upgrade.work-button-spawn-reset.description":
                        __result = "Resets all spawner upgrades back to default level 0. Free of charge!";
                        return false;
                    default:
                        return true;
                }
            }
        }

        [HarmonyPatch(typeof(DailyStatsDisplay), "UpdateTexts")]
        public static class DailyStatsDisplayUpdateTextsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(DailyStatsDisplay __instance)
            {
                try
                {
                    var titleField = typeof(DailyStatsDisplay).GetField("titleText", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (titleField != null && titleField.GetValue(__instance) is TMP_Text titleText)
                    {
                        if (!titleText.text.Contains("MODDED"))
                        {
                            titleText.text += " <size=60%><color=yellow>(MODDED)</color></size>";
                        }
                    }

                    var dayField = typeof(DailyStatsDisplay).GetField("dayText", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (dayField != null && dayField.GetValue(__instance) is TMP_Text dayText)
                    {
                        if (!dayText.text.Contains("Spawning Mod"))
                        {
                            dayText.text += "\n<size=50%><color=#ffaa00>Spawning & Upgrades Mod Active</color></size>";
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerInstance.LogError($"Error in DailyStatsDisplayUpdateTextsPatch: {ex.Message}");
                }
            }
        }
    }

    public class Upgrade_BaggageSpawner_Rate : Upgrade
    {
        public override string GetKey() => "work-button-spawn-rate";
        public override int GetMaxLevel() => 4;

        public override string ProvideLocalizedDescription()
        {
            float current = 5f - GetCurrentLevel();
            string arg = current + "s";
            string arg2 = "-";
            if (GetCurrentLevel() < GetMaxLevel())
            {
                arg2 = ColorText.Positive((current - 1f) + "s");
            }
            return string.Format(LocalizationService.ResolveLocalizationKey("upgrade.work-button-spawn-rate.description"), arg, arg2);
        }

        public override void ApplyEffect()
        {
            // Spawning wait time evaluates this upgrade dynamically
        }
    }

    public class Upgrade_Dispenser_Speed : Upgrade
    {
        public override string GetKey() => "work-button-dispenser-speed";
        public override int GetMaxLevel() => 3;

        public override string ProvideLocalizedDescription()
        {
            string arg = "+" + (GetCurrentLevel() * 65) + "%";
            string arg2 = "-";
            if (GetCurrentLevel() < GetMaxLevel())
            {
                arg2 = ColorText.Positive("+" + ((GetCurrentLevel() + 1) * 65) + "%");
            }
            return string.Format(LocalizationService.ResolveLocalizationKey("upgrade.work-button-dispenser-speed.description"), arg, arg2);
        }

        public override void ApplyEffect()
        {
            SpawningUpgradePlugin.UpdateDispenserSpeed();
        }
    }

    public class Upgrade_BaggageSpawner_Reset : Upgrade
    {
        public override string GetKey() => "work-button-spawn-reset";
        public override int GetMaxLevel() => 1;

        public override string ProvideLocalizedDescription()
        {
            return base.ProvideLocalizedDescription();
        }

        public override void ApplyEffect()
        {
            if (GetCurrentLevel() > 0)
            {
                var levelField = typeof(Upgrade).GetField("level", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (levelField != null)
                {
                    // Reset others
                    var rateUpg = GetComponent<Upgrade_BaggageSpawner_Rate>();
                    if (rateUpg != null) { levelField.SetValue(rateUpg, 0); rateUpg.ApplyEffect(); }
                    
                    var speedUpg = GetComponent<Upgrade_Dispenser_Speed>();
                    if (speedUpg != null) { levelField.SetValue(speedUpg, 0); speedUpg.ApplyEffect(); }
                    
                    // Reset self so it can be bought again
                    levelField.SetValue(this, 0);
                }
            }
        }
    }


    // SpawningUpgradeDiagnostics removed — was causing significant lag due to per-frame FindObjectsByType calls.
    // One-shot scan is now performed in BaggageSpawnerStartPatch.Postfix instead.
    /*
    public class SpawningUpgradeDiagnostics : MonoBehaviour
    {
        private float _lastLogTime = 0f;

        private void Update()
        {
            if (Time.time - _lastLogTime < 1.5f) return;
            _lastLogTime = Time.time;

            try
            {
                SpawningUpgradePlugin.LoggerInstance.LogInfo("=== RUNTIME CONVEYOR DIAGNOSTICS ===");
                
                // Get spawner info
                var spawner = GetComponent<BaggageSpawner>();
                if (spawner == null)
                {
                    SpawningUpgradePlugin.LoggerInstance.LogError("Diagnostics: BaggageSpawner component missing!");
                    return;
                }

                Transform spawnPosTransform = spawner.transform;
                var spawnPositionField = typeof(BaggageSpawner).GetField("spawnPosition", BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawnPositionField != null)
                {
                    if (spawnPositionField.GetValue(spawner) is Transform t)
                    {
                        spawnPosTransform = t;
                    }
                }
                Vector3 spawnPos = spawnPosTransform.position;
                SpawningUpgradePlugin.LoggerInstance.LogInfo($"Spawner SpawnPos: {spawnPos}, rotation: {spawnPosTransform.rotation.eulerAngles}");

                // Find all conveyors in scene
                var linears = UnityEngine.Object.FindObjectsByType<LinearConveyor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                var radials = UnityEngine.Object.FindObjectsByType<RadialConveyor>(FindObjectsInactive.Include, FindObjectsSortMode.None);

                SpawningUpgradePlugin.LoggerInstance.LogInfo($"Total scene conveyors - Linears: {linears.Length}, Radials: {radials.Length}");

                foreach (var conveyor in linears)
                {
                    if (conveyor == null) continue;
                    Vector3 pos = conveyor.transform.position;
                    // Check if within spawner path
                    if (SpawningUpgradePlugin.IsConveyorInSpawnerPath(pos))
                    {
                        string path = conveyor.name;
                        Transform curr = conveyor.transform;
                        while (curr.parent != null)
                        {
                            curr = curr.parent;
                            path = curr.name + "/" + path;
                        }

                        // Get material speed
                        float matSpeed = 0f;
                        try
                        {
                            var mr = conveyor.GetComponent<MeshRenderer>();
                            if (mr == null) mr = conveyor.GetComponentInChildren<MeshRenderer>();
                            if (mr != null && mr.material != null)
                            {
                                matSpeed = mr.material.GetFloat("_Speed");
                            }
                        }
                        catch (Exception) {}

                        // Get Rigidbody info
                        var rb = conveyor.GetComponent<Rigidbody>();
                        string rbStr = rb == null ? "None" : $"Kinematic: {rb.isKinematic}, gravity: {rb.useGravity}, vel: {rb.linearVelocity}, angVel: {rb.angularVelocity}, detectCol: {rb.detectCollisions}";

                        // Get Collider info
                        var col = conveyor.GetComponent<Collider>();
                        string colStr = col == null ? "None" : $"Enabled: {col.enabled}, trigger: {col.isTrigger}, offset: {col.contactOffset}";

                        bool isRegistered = SpawningUpgradePlugin.DispenserLinearConveyors.Contains(conveyor);

                        SpawningUpgradePlugin.LoggerInstance.LogInfo($"Dispenser Linear '{conveyor.name}' at {pos}:\n" +
                            $"   Path: {path}\n" +
                            $"   Rotation: {conveyor.transform.rotation.eulerAngles}\n" +
                            $"   Conveyor speed: {conveyor.speed}, distance: {conveyor.distance}, instancedMat: {conveyor.instancedMaterial}, shaderSpeed: {matSpeed}\n" +
                            $"   Rigidbody: {rbStr}\n" +
                            $"   Collider: {colStr}\n" +
                            $"   Registered: {isRegistered}");
                    }
                }

                foreach (var conveyor in radials)
                {
                    if (conveyor == null) continue;
                    Vector3 pos = conveyor.transform.position;
                    if (SpawningUpgradePlugin.IsConveyorInSpawnerPath(pos))
                    {
                        string path = conveyor.name;
                        Transform curr = conveyor.transform;
                        while (curr.parent != null)
                        {
                            curr = curr.parent;
                            path = curr.name + "/" + path;
                        }

                        float matSpeed = 0f;
                        try
                        {
                            var mr = conveyor.GetComponent<MeshRenderer>();
                            if (mr == null) mr = conveyor.GetComponentInChildren<MeshRenderer>();
                            if (mr != null && mr.material != null)
                            {
                                matSpeed = mr.material.GetFloat("_Speed");
                            }
                        }
                        catch (Exception) {}

                        var rb = conveyor.GetComponent<Rigidbody>();
                        string rbStr = rb == null ? "None" : $"Kinematic: {rb.isKinematic}, gravity: {rb.useGravity}, vel: {rb.linearVelocity}, angVel: {rb.angularVelocity}, detectCol: {rb.detectCollisions}";

                        var col = conveyor.GetComponent<Collider>();
                        string colStr = col == null ? "None" : $"Enabled: {col.enabled}, trigger: {col.isTrigger}, offset: {col.contactOffset}";

                        float radialDistance = 0f;
                        float radialAngle = 0f;
                        try
                        {
                            var distField = typeof(RadialConveyor).GetField("distance", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (distField != null) radialDistance = (float)distField.GetValue(conveyor);
                            var angleField = typeof(RadialConveyor).GetField("angle", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (angleField != null) radialAngle = (float)angleField.GetValue(conveyor);
                        }
                        catch (Exception) {}

                        bool isRegistered = SpawningUpgradePlugin.DispenserRadialConveyors.Contains(conveyor);

                        SpawningUpgradePlugin.LoggerInstance.LogInfo($"Dispenser Radial '{conveyor.name}' at {pos}:\n" +
                            $"   Path: {path}\n" +
                            $"   Rotation: {conveyor.transform.rotation.eulerAngles}\n" +
                            $"   Conveyor speed: {conveyor.speed}, radius: {conveyor.radius}, angle: {radialAngle}, distance: {radialDistance}, instancedMat: {conveyor.instancedMaterial}, shaderSpeed: {matSpeed}\n" +
                            $"   Rigidbody: {rbStr}\n" +
                            $"   Collider: {colStr}\n" +
                            $"   Registered: {isRegistered}");
                    }
                }

                // Log any baggage in spawner path
                var baggages = UnityEngine.Object.FindObjectsByType<Baggage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var bag in baggages)
                {
                    if (bag == null) continue;
                    Vector3 pos = bag.transform.position;
                    if (Vector3.Distance(pos, spawnPos) < 25.0f)
                    {
                        var rb = bag.GetComponent<Rigidbody>();
                        string rbStr = rb == null ? "None" : $"vel: {rb.linearVelocity}, mass: {rb.mass}";
                        SpawningUpgradePlugin.LoggerInstance.LogInfo($"Baggage '{bag.name}' at {pos}, dist from spawner: {Vector3.Distance(pos, spawnPos):F2}m, Rigidbody: {rbStr}");
                    }
                }

                SpawningUpgradePlugin.LoggerInstance.LogInfo("=== END RUNTIME CONVEYOR DIAGNOSTICS ===");
            }
            catch (Exception ex)
            {
                SpawningUpgradePlugin.LoggerInstance.LogError($"Diagnostics error: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
    */
}
