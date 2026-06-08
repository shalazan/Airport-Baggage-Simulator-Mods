// -----------------------------------------------------------------------------
// This file is part of an AI-assisted/generated mod for Airport Baggage Simulator.
// Developed with the assistance of Antigravity, an agentic AI coding assistant.
// -----------------------------------------------------------------------------

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
using _scripts._by_scene._game._upgrades;
using _scripts._by_scene._game._upgrades._specific_upgrades;
using _scripts._by_scene._game._automation;
using _scripts._by_scene._game._automation._automat_direction_provider;
using _scripts._by_scene._game._automation._upgrades;
using _scripts._by_scene._game._scanner;
using _scripts._by_scene._game._baggage;
using _scripts._by_scene._game._building;
using _scripts._by_scene._game._building._flip;
using Produktivkeller.SimpleLocalization.Unity.Core;
using Produktivkeller.SimpleAudioSolution.Access;

namespace BaggageTagAnyMod
{
    [BepInPlugin("com.morg.baggage_tag_any_mod", "Baggage Tag Any Mod", "1.0.0")]
    public class BaggageTagAnyPlugin : BaseUnityPlugin
    {
        private static BaggageTagAnyPlugin _instance;

        private void Awake()
        {
            _instance = this;
            Logger.LogInfo("Baggage Tag Any Mod is loading...");

            try
            {
                var harmony = new Harmony("com.morg.baggage_tag_any_mod");
                harmony.PatchAll();
                Logger.LogInfo("Baggage Tag Any Mod patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        internal static void InjectUpgrade(Buildings buildings, DiContainer diContainer)
        {
            if (buildings == null || buildings.buildings == null) return;

            var building = buildings.buildings.FirstOrDefault(b => b != null && b.GetId() == "automat-for-baggage-tag");
            if (building == null)
            {
                _instance.Logger.LogError("Could not find 'automat-for-baggage-tag' building prefab!");
                return;
            }

            if (building.gameObject.GetComponent<Upgrade_BaggageTag_Any>() != null)
            {
                return; // Already injected
            }

            _instance.Logger.LogInfo("Found 'automat-for-baggage-tag' building prefab. Injecting Upgrade_BaggageTag_Any...");

            _instance.Logger.LogInfo("--- Baggage Tag Sorter Prefab Hierarchy Dump ---");
            PrintHierarchy(building.transform, "");
            _instance.Logger.LogInfo("--- End Prefab Hierarchy Dump ---");

            // Create and configure Upgrade_BaggageTag_Any
            var upgradeComponent = building.gameObject.AddComponent<Upgrade_BaggageTag_Any>();
            
            var speedUpgrade = building.gameObject.GetComponent<Upgrade_Automat_Speed_BaggageTag>();
            if (speedUpgrade != null)
            {
                CopyUpgradeFields(speedUpgrade, upgradeComponent);
            }
            else
            {
                _instance.Logger.LogWarning("Could not find Upgrade_Automat_Speed_BaggageTag to copy fields from.");
            }

            if (diContainer != null)
            {
                diContainer.Inject(upgradeComponent);
            }

            _instance.Logger.LogInfo("Successfully injected Upgrade_BaggageTag_Any into 'automat-for-baggage-tag' prefab.");
        }

        private static void PrintHierarchy(Transform t, string indent)
        {
            if (t == null) return;
            var comps = t.GetComponents<Component>();
            string compStr = "";
            foreach (var c in comps)
            {
                if (c == null) continue;
                compStr += "[" + c.GetType().Name + "] ";
            }
            _instance.Logger.LogInfo($"{indent}- {t.gameObject.name} (active: {t.gameObject.activeSelf}, pos: {t.localPosition}) {compStr}");
            for (int i = 0; i < t.childCount; i++)
            {
                PrintHierarchy(t.GetChild(i), indent + "  ");
            }
        }

        private static void CopyUpgradeFields(Upgrade source, Upgrade target)
        {
            if (source == null || target == null) return;

            var fields = new string[] { "soundEventIds", "particleSystems", "satisfactionTextPosition" };
            foreach (var fieldName in fields)
            {
                var field = typeof(Upgrade).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var val = field.GetValue(source);
                    field.SetValue(target, val);
                    _instance.Logger.LogInfo($"Copied field '{fieldName}' from speed upgrade.");
                }
            }
        }

        [HarmonyPatch(typeof(BuildingProvider))]
        public static class BuildingProviderPatches
        {
            [HarmonyPatch("InitializeIfNecessary")]
            [HarmonyPrefix]
            public static void InitializeIfNecessary_Prefix(BuildingProvider __instance, Buildings ___buildings, DiContainer ____diContainer)
            {
                InjectUpgrade(___buildings, ____diContainer);
            }

            [HarmonyPatch("GetBuildingForShopItem")]
            [HarmonyPrefix]
            public static void GetBuildingForShopItem_Prefix(BuildingProvider __instance, Buildings ___buildings, DiContainer ____diContainer)
            {
                InjectUpgrade(___buildings, ____diContainer);
            }

            [HarmonyPatch("GetBuildingById")]
            [HarmonyPrefix]
            public static void GetBuildingById_Prefix(BuildingProvider __instance, Buildings ___buildings, DiContainer ____diContainer)
            {
                InjectUpgrade(___buildings, ____diContainer);
            }
        }

        [HarmonyPatch(typeof(Balancing), MethodType.Constructor)]
        public static class BalancingPatches
        {
            [HarmonyPostfix]
            public static void Postfix(Balancing __instance, BalancingConfiguration ____balancingConfiguration)
            {
                if (____balancingConfiguration == null) return;

                var upBal = ____balancingConfiguration.upgradeBalancing;
                if (upBal != null)
                {
                    if (!upBal.upgradeCosts.ContainsKey("baggage-tag-any"))
                    {
                        var costs = new UpgradeCosts();
                        costs.costs.Add(1500);
                        costs.requiredUpgradePoints.Add(25);
                        upBal.upgradeCosts["baggage-tag-any"] = costs;
                        _instance.Logger.LogInfo("Registered upgrade costs for 'baggage-tag-any' ($1500, 25 wrenches).");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(LocalizationService), "ResolveLocalizationKey", new Type[] { typeof(string) })]
        public static class LocalizationServicePatches
        {
            [HarmonyPrefix]
            public static bool Prefix(string localizationKey, ref string __result)
            {
                switch (localizationKey)
                {
                    case "upgrade.baggage-tag-any.name":
                        __result = "Wildcard";
                        return false;
                    case "upgrade.baggage-tag-any.description":
                        __result = "Adds a fourth setting to divert any tagged baggage, whether it is red, yellow, or green.";
                        return false;
                    default:
                        return true;
                }
            }
        }

        [HarmonyPatch(typeof(AutomatDirectionProviderForBaggageTag), "DetermineOutDirection")]
        public static class DetermineOutDirectionPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(AutomatDirectionProviderForBaggageTag __instance, Flip flip, ICanBeProcessedByAutomat canBeProcessedByAutomat, ref AutomatDirection __result)
            {
                var scannerType = __instance.GetScannerType();
                if (scannerType == (ScannerType)5)
                {
                    if (!(canBeProcessedByAutomat is Baggage baggage))
                    {
                        __result = AutomatDirection.Back;
                        return false;
                    }
                    bool hasTag = baggage.BaggageTagShowsIsValid() || baggage.BaggageTagShowsIsInvalid() || baggage.BaggageTagShowsIsTerminal2();
                    if (!hasTag)
                    {
                        __result = AutomatDirection.Back;
                        return false;
                    }
                    if (flip.GetIndex() != 0)
                    {
                        __result = AutomatDirection.Left;
                    }
                    else
                    {
                        __result = AutomatDirection.Right;
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(AutomatDirectionProviderForBaggageTag), "SwitchTargetAirport")]
        public static class SwitchTargetAirportPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(AutomatDirectionProviderForBaggageTag __instance)
            {
                var upgrade = __instance.GetComponentInParent<Upgrade_BaggageTag_Any>();
                bool hasAnyUpgrade = upgrade != null && upgrade.GetCurrentLevel() > 0;

                if (hasAnyUpgrade)
                {
                    SoundAccess.GetInstance().PlayOneShot("/SFX/Automat for Target Airport/Interact", __instance.transform.position);
                    var current = __instance.GetScannerType();
                    ScannerType next;
                    switch (current)
                    {
                        case ScannerType.Valid:
                            next = ScannerType.Invalid;
                            break;
                        case ScannerType.Invalid:
                            next = ScannerType.Terminal2;
                            break;
                        case ScannerType.Terminal2:
                            next = (ScannerType)5; // Any mode
                            break;
                        default:
                            next = ScannerType.Valid;
                            break;
                    }
                    __instance.SetScannerType(next);
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(AutomatDirectionProviderForBaggageTag), "UpdateSymbols")]
        public static class UpdateSymbolsPatch
        {
            [HarmonyPostfix]
            public static void Postfix(AutomatDirectionProviderForBaggageTag __instance, List<GameObject> ___validSymbols, List<GameObject> ___invalidSymbols, List<GameObject> ___terminal2Symbols)
            {
                var scannerType = __instance.GetScannerType();
                if (scannerType == (ScannerType)5)
                {
                    if (___validSymbols != null)
                    {
                        foreach (var v in ___validSymbols)
                        {
                            if (v != null)
                            {
                                v.SetActive(true);
                                v.transform.localPosition = new Vector3(-0.10f, 0f, 0.01f);
                            }
                        }
                    }
                    if (___invalidSymbols != null)
                    {
                        foreach (var v in ___invalidSymbols)
                        {
                            if (v != null)
                            {
                                v.SetActive(true);
                                v.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                            }
                        }
                    }
                    if (___terminal2Symbols != null)
                    {
                        foreach (var v in ___terminal2Symbols)
                        {
                            if (v != null)
                            {
                                v.SetActive(true);
                                v.transform.localPosition = new Vector3(0.10f, 0f, 0.01f);
                            }
                        }
                    }
                }
                else
                {
                    // Restore original centered positions
                    if (___validSymbols != null)
                    {
                        foreach (var v in ___validSymbols)
                        {
                            if (v != null) v.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                        }
                    }
                    if (___invalidSymbols != null)
                    {
                        foreach (var v in ___invalidSymbols)
                        {
                            if (v != null) v.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                        }
                    }
                    if (___terminal2Symbols != null)
                    {
                        foreach (var v in ___terminal2Symbols)
                        {
                            if (v != null) v.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                        }
                    }
                }
            }
        }
    }

    public class Upgrade_BaggageTag_Any : Upgrade
    {
        public override string GetKey()
        {
            return "baggage-tag-any";
        }

        public override int GetMaxLevel()
        {
            return 1;
        }

        public override void ApplyEffect()
        {
            // Effect is evaluated passively during DetermineOutDirection and SwitchTargetAirport
        }
    }
}
