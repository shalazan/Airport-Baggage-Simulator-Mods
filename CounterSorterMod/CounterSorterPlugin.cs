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
using _scripts._by_scene._game._tablet._application._shop_application;
using _scripts._by_scene._game._building;
using _scripts._by_scene._common._balancing;
using _scripts._by_scene._common._balancing._configuration;
using _scripts._by_scene._game._upgrades;
using _scripts._by_scene._game._upgrades._specific_upgrades;
using _scripts._by_scene._game._automation;
using _scripts._by_scene._game._automation._automat_direction_provider;
using _scripts._by_scene._game._automation._upgrades;
using _scripts._by_scene._game._license;
using Produktivkeller.SimpleLocalization.Unity.Core;
using Produktivkeller.SimpleCat.Interaction;

namespace CounterSorterMod
{
    [BepInPlugin("com.morg.counter_sorter_mod", "Counter Sorter Mod", "1.0.0")]
    public class CounterSorterPlugin : BaseUnityPlugin
    {
        private static CounterSorterPlugin _instance;

        private void Awake()
        {
            _instance = this;
            Logger.LogInfo("Counter Sorter Mod is loading...");

            try
            {
                var harmony = new Harmony("com.morg.counter_sorter_mod");
                harmony.PatchAll();
                Logger.LogInfo("Counter Sorter Mod patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        internal static void InjectShopItem(List<ShopCategory> shopCategories)
        {
            if (shopCategories == null) return;

            if (shopCategories.Any(c => c.shopItems != null && c.shopItems.Any(i => i != null && i.id == "automat-counter-sorter")))
            {
                return;
            }

            var automationCategory = shopCategories.FirstOrDefault(c => c.shopCategoryId == ShopCategoryId.Automation);
            if (automationCategory == null)
            {
                _instance.Logger.LogError("Could not find Automation category in ShopCategories!");
                return;
            }

            var sourceItem = automationCategory.shopItems.FirstOrDefault(i => i != null && i.id == "automat-for-target-airport");

            var newItem = ScriptableObject.CreateInstance<ShopItem>();
            newItem.id = "automat-counter-sorter";
            newItem.shopCategoryId = ShopCategoryId.Automation;
            if (sourceItem != null)
            {
                newItem.sprite = sourceItem.sprite;
            }

            automationCategory.shopItems.Add(newItem);
            _instance.Logger.LogInfo("Successfully injected shop item 'automat-counter-sorter' into Automation category.");
        }

        internal static void InjectBuildingPrefab(Buildings buildings, DiContainer diContainer)
        {
            if (buildings == null || buildings.buildings == null) return;

            if (buildings.buildings.Any(b => b != null && b.GetId() == "automat-counter-sorter"))
            {
                return;
            }

            var sourceBuilding = buildings.buildings.FirstOrDefault(b => b != null && b.GetId() == "automat-for-target-airport");
            if (sourceBuilding == null)
            {
                _instance.Logger.LogError("Could not find 'automat-for-target-airport' building prefab in buildings list!");
                return;
            }

            _instance.Logger.LogInfo("Cloning 'automat-for-target-airport' prefab...");
            
            // Create an inactive parent container to hold our prefab template.
            // This keeps the prefab template inactive in the scene hierarchy, but leaves activeSelf as true
            // so that any cloned instances instantiated under active parents start active, visible, and collidable.
            GameObject container = new GameObject("CounterSorterPrefabContainer");
            container.SetActive(false);
            GameObject.DontDestroyOnLoad(container);

            GameObject newGo = GameObject.Instantiate(sourceBuilding.gameObject);
            newGo.transform.SetParent(container.transform);
            newGo.name = "Counter Sorter Prefab";
            newGo.SetActive(true);

            _instance.Logger.LogInfo("--- Prefab Hierarchy Dump ---");
            PrintHierarchy(newGo.transform, "");
            _instance.Logger.LogInfo("--- End Prefab Hierarchy Dump ---");

            var oldProvider = newGo.GetComponentInChildren<AutomatDirectionProviderForTargetAirport>(true);
            var oldData = newGo.GetComponentInChildren<AdditionalDataForAutomatForTargetAirport>(true);
            var oldUpgrade = newGo.GetComponentInChildren<Upgrade_Automat_TargetAirport>(true);

            if (oldProvider == null) _instance.Logger.LogError("oldProvider (AutomatDirectionProviderForTargetAirport) is NULL!");
            if (oldData == null) _instance.Logger.LogError("oldData (AdditionalDataForAutomatForTargetAirport) is NULL!");
            if (oldUpgrade == null) _instance.Logger.LogError("oldUpgrade (Upgrade_Automat_TargetAirport) is NULL!");

            if (oldProvider == null || oldData == null || oldUpgrade == null)
            {
                _instance.Logger.LogError("Cloned prefab did not contain expected components!");
                GameObject.Destroy(newGo);
                return;
            }

            // 1. Create and configure CounterDirectionProvider
            var newProvider = oldProvider.gameObject.AddComponent<CounterDirectionProvider>();
            _instance.Logger.LogInfo("Created CounterDirectionProvider component.");
            
            var airportField = typeof(AutomatDirectionProviderForTargetAirport).GetField("targetAirportTexts", BindingFlags.NonPublic | BindingFlags.Instance);
            var airport2Field = typeof(AutomatDirectionProviderForTargetAirport).GetField("targetAirport2Texts", BindingFlags.NonPublic | BindingFlags.Instance);
            var combinedField = typeof(AutomatDirectionProviderForTargetAirport).GetField("combinedTargetAirportTexts", BindingFlags.NonPublic | BindingFlags.Instance);

            if (airportField != null && airportField.GetValue(oldProvider) is List<TMPro.TMP_Text> list1) newProvider.displayTexts.AddRange(list1);
            if (airport2Field != null && airport2Field.GetValue(oldProvider) is List<TMPro.TMP_Text> list2) newProvider.displayTexts.AddRange(list2);
            if (combinedField != null && combinedField.GetValue(oldProvider) is List<TMPro.TMP_Text> list3) newProvider.displayTexts.AddRange(list3);
            _instance.Logger.LogInfo($"Collected {newProvider.displayTexts.Count} display texts.");

            // Identify and rename/label the buttons on the prefab template before oldProvider is destroyed
            var interactables = newGo.GetComponentsInChildren<Interactable>(true);
            _instance.Logger.LogInfo($"Found {interactables.Length} Interactables in cloned prefab template.");
            foreach (var interactable in interactables)
            {
                var onInteractField = typeof(Interactable).GetField("onInteract", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onInteractField == null) continue;

                var onInteract = onInteractField.GetValue(interactable) as UnityEngine.Events.UnityEvent;
                if (onInteract == null) continue;

                int count = onInteract.GetPersistentEventCount();
                bool isButton1 = false;
                bool isButton2 = false;

                for (int i = 0; i < count; i++)
                {
                    string methodName = onInteract.GetPersistentMethodName(i);
                    if (methodName == "SwitchTargetAirport")
                    {
                        isButton1 = true;
                    }
                    else if (methodName == "SwitchTargetAirport2")
                    {
                        isButton2 = true;
                    }
                }

                if (isButton1)
                {
                    interactable.gameObject.name = "Button_IncrementLimit";
                    interactable.gameObject.SetActive(true);
                    var locKeyField = typeof(Interactable).GetField("localizationKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (locKeyField != null)
                    {
                        locKeyField.SetValue(interactable, "controls.change-count");
                    }
                    _instance.Logger.LogInfo("Prepared Button 1 prefab (renamed and set localization key).");
                }
                else if (isButton2)
                {
                    interactable.gameObject.name = "Button_Reset";
                    interactable.gameObject.SetActive(true);
                    var locKeyField = typeof(Interactable).GetField("localizationKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (locKeyField != null)
                    {
                        locKeyField.SetValue(interactable, "controls.reset");
                    }
                    _instance.Logger.LogInfo("Prepared Button 2 prefab (renamed and set localization key).");
                }
            }

            // Find and modify LocalizedText components
            var localizedTexts = newGo.GetComponentsInChildren<Produktivkeller.SimpleLocalization.Unity.Components.LocalizedText>(true);
            _instance.Logger.LogInfo($"Found {localizedTexts.Length} LocalizedText components in cloned prefab.");
            foreach (var locText in localizedTexts)
            {
                var keyField = typeof(Produktivkeller.SimpleLocalization.Unity.Components.LocalizedText).GetField("translationKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (keyField != null)
                {
                    string currentKey = keyField.GetValue(locText) as string;
                    _instance.Logger.LogInfo($"LocalizedText GameObject: {locText.gameObject.name}, Key: {currentKey}");
                    if (currentKey == "automat.other-airport")
                    {
                        keyField.SetValue(locText, "automat.over-count");
                        _instance.Logger.LogInfo("Changed LocalizedText key to automat.over-count");
                    }
                }
            }

            // 2. Create and configure AdditionalDataForCounter
            var newData = oldData.gameObject.AddComponent<AdditionalDataForCounter>();
            var automat = newGo.GetComponent<Automat>();
            if (automat == null)
            {
                // Try searching in children if not on root
                automat = newGo.GetComponentInChildren<Automat>(true);
            }
            if (automat == null) _instance.Logger.LogError("Automat component is NULL!");

            newData.automat = automat;
            newData.provider = newProvider;
            _instance.Logger.LogInfo("Configured AdditionalDataForCounter component.");

            // 3. Create and configure Upgrade_Counter_AutoReset
            var newUpgradeComponent = oldUpgrade.gameObject.AddComponent<Upgrade_Counter_AutoReset>();
            newUpgradeComponent.provider = newProvider;
            CopyUpgradeFields(oldUpgrade, newUpgradeComponent);
            _instance.Logger.LogInfo("Configured Upgrade_Counter_AutoReset component.");

            // 4. Update the Automat's direction provider reference
            if (automat != null)
            {
                var directionProviderField = typeof(Automat).GetField("_automatDirectionProvider", BindingFlags.NonPublic | BindingFlags.Instance);
                if (directionProviderField != null)
                {
                    directionProviderField.SetValue(automat, newProvider);
                    _instance.Logger.LogInfo("Successfully set _automatDirectionProvider field on Automat.");
                }
                else
                {
                    _instance.Logger.LogError("Could not find '_automatDirectionProvider' field on Automat class!");
                }
            }

            // 5. Remove old components
            GameObject.DestroyImmediate(oldProvider);
            GameObject.DestroyImmediate(oldData);
            GameObject.DestroyImmediate(oldUpgrade);
            _instance.Logger.LogInfo("Destroyed old components.");

            // 6. Set Building ID
            var newBuilding = newGo.GetComponent<Building>();
            if (newBuilding == null)
            {
                newBuilding = newGo.GetComponentInChildren<Building>(true);
            }
            if (newBuilding == null) _instance.Logger.LogError("Building component is NULL!");

            if (newBuilding != null)
            {
                var idField = typeof(Building).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null)
                {
                    idField.SetValue(newBuilding, "automat-counter-sorter");
                    _instance.Logger.LogInfo("Successfully set Building id field.");
                }
                else
                {
                    _instance.Logger.LogError("Could not find 'id' field on Building class!");
                }
            }

            if (diContainer != null)
            {
                diContainer.InjectGameObject(newGo);
                _instance.Logger.LogInfo("Injected dependencies into prefab GameObject.");
            }

            buildings.buildings.Add(newBuilding);
            _instance.Logger.LogInfo("Successfully injected custom prefab 'automat-counter-sorter' into BuildingProvider.");
        }

        private static void CopyUpgradeFields(Upgrade source, Upgrade target)
        {
            if (source == null)
            {
                _instance.Logger.LogError("CopyUpgradeFields: source is NULL!");
                return;
            }
            if (target == null)
            {
                _instance.Logger.LogError("CopyUpgradeFields: target is NULL!");
                return;
            }

            var fields = new string[] { "soundEventIds", "particleSystems", "satisfactionTextPosition" };
            foreach (var fieldName in fields)
            {
                var field = typeof(Upgrade).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var val = field.GetValue(source);
                    field.SetValue(target, val);
                    _instance.Logger.LogInfo($"Copied field '{fieldName}' from source to target.");
                }
                else
                {
                    _instance.Logger.LogError($"Could not find field '{fieldName}' on Upgrade class!");
                }
            }
        }

        private static void ClearPersistentListeners(UnityEngine.Events.UnityEventBase unityEvent)
        {
            if (unityEvent == null) return;
            try
            {
                var persistentCallsField = typeof(UnityEngine.Events.UnityEventBase).GetField("m_PersistentCalls", BindingFlags.NonPublic | BindingFlags.Instance);
                if (persistentCallsField != null)
                {
                    var persistentCalls = persistentCallsField.GetValue(unityEvent);
                    if (persistentCalls != null)
                    {
                        var callsField = persistentCalls.GetType().GetField("m_Calls", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (callsField != null)
                        {
                            var list = callsField.GetValue(persistentCalls) as System.Collections.IList;
                            if (list != null)
                            {
                                list.Clear();
                                _instance.Logger.LogInfo("Successfully cleared persistent listeners.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _instance.Logger.LogError($"Failed to clear persistent listeners: {ex.Message}");
            }
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
            _instance.Logger.LogInfo($"{indent}- {t.gameObject.name} (active: {t.gameObject.activeSelf}) {compStr}");
            for (int i = 0; i < t.childCount; i++)
            {
                PrintHierarchy(t.GetChild(i), indent + "  ");
            }
        }

        [HarmonyPatch(typeof(ShopItemProvider))]
        public static class ShopItemProviderPatches
        {
            [HarmonyPatch("InitializeIfNecessary")]
            [HarmonyPrefix]
            public static void InitializeIfNecessary_Prefix(ShopItemProvider __instance, List<ShopCategory> ___shopCategories)
            {
                InjectShopItem(___shopCategories);
            }

            [HarmonyPatch("GetByCategory")]
            [HarmonyPrefix]
            public static void GetByCategory_Prefix(ShopItemProvider __instance, List<ShopCategory> ___shopCategories)
            {
                InjectShopItem(___shopCategories);
            }
        }

        [HarmonyPatch(typeof(BuildingProvider))]
        public static class BuildingProviderPatches
        {
            [HarmonyPatch("InitializeIfNecessary")]
            [HarmonyPrefix]
            public static void InitializeIfNecessary_Prefix(BuildingProvider __instance, Buildings ___buildings, DiContainer ____diContainer)
            {
                InjectBuildingPrefab(___buildings, ____diContainer);
            }

            [HarmonyPatch("GetBuildingForShopItem")]
            [HarmonyPrefix]
            public static void GetBuildingForShopItem_Prefix(BuildingProvider __instance, Buildings ___buildings, DiContainer ____diContainer)
            {
                InjectBuildingPrefab(___buildings, ____diContainer);
            }

            [HarmonyPatch("GetBuildingById")]
            [HarmonyPrefix]
            public static void GetBuildingById_Prefix(BuildingProvider __instance, Buildings ___buildings, DiContainer ____diContainer)
            {
                InjectBuildingPrefab(___buildings, ____diContainer);
            }
        }

        [HarmonyPatch(typeof(Balancing), MethodType.Constructor)]
        public static class BalancingPatches
        {
            [HarmonyPostfix]
            public static void Postfix(Balancing __instance, BalancingConfiguration ____balancingConfiguration)
            {
                if (____balancingConfiguration == null) return;

                var itemBal = ____balancingConfiguration.shopItemBalancing;
                if (itemBal != null)
                {
                    itemBal.cost["automat-counter-sorter"] = 1500;
                    itemBal.requiredTerminalLevel["automat-counter-sorter"] = 3;
                    itemBal.requiredPromotion["automat-counter-sorter"] = _scripts._by_scene._game._license.PromotionId.None;
                }

                var upBal = ____balancingConfiguration.upgradeBalancing;
                if (upBal != null)
                {
                    var costs = new UpgradeCosts();
                    costs.costs.Add(800);
                    costs.requiredUpgradePoints.Add(15);
                    upBal.upgradeCosts["counter-auto-reset"] = costs;
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
                    case "shop-item.name.automat-counter-sorter":
                        __result = "Counter";
                        return false;
                    case "shop-item.description.automat-counter-sorter":
                        __result = "Routes based on amount of luggage passed through it";
                        return false;
                    case "upgrade.counter-auto-reset.name":
                        __result = "Counter Auto-Reset";
                        return false;
                    case "upgrade.counter-auto-reset.description":
                        __result = "Automatically resets the counter to 0 at the start of each day.";
                        return false;
                    case "automat.over-count":
                        __result = "Over Count";
                        return false;
                    case "controls.change-count":
                        __result = "Change Count";
                        return false;
                    case "controls.reset":
                        __result = "Reset";
                        return false;
                    default:
                        return true;
                }
            }
        }
    }
}
