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
using TMPro;
using _scripts._by_scene._game._tablet._application._shop_application;
using _scripts._by_scene._game._building;
using _scripts._by_scene._common._balancing;
using _scripts._by_scene._common._balancing._configuration;
using Produktivkeller.SimpleLocalization.Unity.Core;
using Produktivkeller.SimpleCat.Interaction;

namespace ScreenMod
{
    [BepInPlugin("com.morg.screen_mod", "Placeable Screen Mod", "1.0.0")]
    public class ScreenPlugin : BaseUnityPlugin
    {
        private static ScreenPlugin _instance;

        private void Awake()
        {
            _instance = this;
            Logger.LogInfo("Placeable Screen Mod is loading...");

            try
            {
                var harmony = new Harmony("com.morg.screen_mod");
                harmony.PatchAll();
                Logger.LogInfo("Placeable Screen Mod patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        internal static void InjectShopItem(List<ShopCategory> shopCategories)
        {
            if (shopCategories == null) return;

            if (shopCategories.Any(c => c.shopItems != null && c.shopItems.Any(i => i != null && i.id == "placeable-screen-large")))
            {
                return;
            }

            var toolsCategory = shopCategories.FirstOrDefault(c => c.shopCategoryId == ShopCategoryId.Tools);
            if (toolsCategory == null)
            {
                _instance.Logger.LogError("Could not find Tools category in ShopCategories!");
                return;
            }

            var sourceItem = toolsCategory.shopItems.FirstOrDefault();

            string[] ids = new string[] { "placeable-screen-large", "placeable-screen-tall", "placeable-screen-medium" };
            foreach (var id in ids)
            {
                var newItem = ScriptableObject.CreateInstance<ShopItem>();
                newItem.id = id;
                newItem.shopCategoryId = ShopCategoryId.Tools;
                if (sourceItem != null)
                {
                    newItem.sprite = sourceItem.sprite;
                }
                toolsCategory.shopItems.Add(newItem);
                _instance.Logger.LogInfo($"Successfully injected shop item '{id}' into Tools category.");
            }
        }

        internal static void InjectBuildingPrefab(Buildings buildings, DiContainer diContainer)
        {
            if (buildings == null || buildings.buildings == null) return;

            foreach (var b in buildings.buildings)
            {
                if (b != null)
                {
                    _instance.Logger.LogInfo($"[InjectBuildingPrefab] Found building: '{b.gameObject.name}' with id: '{b.GetId()}'");
                }
            }

            if (buildings.buildings.Any(b => b != null && b.GetId() == "placeable-screen-large"))
            {
                return;
            }

            // Find source building prefab: Building - airline-table (used for all screen types)
            var sourceBuilding = buildings.buildings.FirstOrDefault(b => b != null && b.gameObject.name.Contains("airline-table"));
            if (sourceBuilding == null)
            {
                _instance.Logger.LogError("Could not find 'Building - airline-table' prefab in buildings list!");
                return;
            }

            // All screen types use the same source - visual setup happens at runtime in EditableScreen.cs
            var mediumSourceBuilding = sourceBuilding;

            _instance.Logger.LogInfo("Cloning prefabs for placeable screens...");
            
            // Create inactive parent container
            GameObject container = new GameObject("ScreenPrefabContainer");
            container.SetActive(false);
            GameObject.DontDestroyOnLoad(container);

            string[] ids = new string[] { "placeable-screen-large", "placeable-screen-tall", "placeable-screen-medium" };
            foreach (var id in ids)
            {
                var srcBuilding = (id == "placeable-screen-medium") ? mediumSourceBuilding : sourceBuilding;
                GameObject newGo = GameObject.Instantiate(srcBuilding.gameObject);
                newGo.transform.SetParent(container.transform);
                newGo.name = $"Placeable Screen Prefab ({id})";
                newGo.SetActive(true);

                // Find D_TV_wall child
                Transform tvWall = newGo.transform.Find("D_TV_wall");
                if (tvWall == null)
                {
                    tvWall = newGo.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "D_TV_wall");
                }

                if (tvWall == null)
                {
                    _instance.Logger.LogError("Could not find 'D_TV_wall' child in cloned prefab!");
                    GameObject.Destroy(newGo);
                    continue;
                }

                Vector3 originalLocalRot = tvWall.localRotation.eulerAngles;

                // Remove all other children on the prefab
                List<GameObject> childrenToDelete = new List<GameObject>();
                for (int i = 0; i < newGo.transform.childCount; i++)
                {
                    var child = newGo.transform.GetChild(i).gameObject;
                    if (child != tvWall.gameObject)
                    {
                        childrenToDelete.Add(child);
                    }
                }
                foreach (var child in childrenToDelete)
                {
                    GameObject.DestroyImmediate(child);
                }

                // Create stand pole and base plate (temporary fallbacks used during preview/placement before Start copies real visuals)
                GameObject stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stand.name = "Stand";
                stand.transform.SetParent(newGo.transform, false);
                stand.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                stand.transform.localScale = new Vector3(0.04f, 0.7f, 0.04f);

                GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                basePlate.name = "BasePlate";
                basePlate.transform.SetParent(newGo.transform, false);
                basePlate.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                basePlate.transform.localScale = new Vector3(0.4f, 0.04f, 0.3f);

                var tvRenderer = tvWall.GetComponent<MeshRenderer>();
                if (tvRenderer != null && tvRenderer.sharedMaterial != null)
                {
                    stand.GetComponent<MeshRenderer>().sharedMaterial = tvRenderer.sharedMaterial;
                    basePlate.GetComponent<MeshRenderer>().sharedMaterial = tvRenderer.sharedMaterial;
                }

                tvWall.localPosition = new Vector3(0f, 0.7f, 0f);
                tvWall.localRotation = Quaternion.Euler(originalLocalRot);
                tvWall.localScale = tvWall.localScale;

                GameObject.DestroyImmediate(stand.GetComponent<Collider>());
                GameObject.DestroyImmediate(basePlate.GetComponent<Collider>());

                var existingBoxColliders = newGo.GetComponents<BoxCollider>();
                foreach (var c in existingBoxColliders)
                {
                    GameObject.DestroyImmediate(c);
                }
                var rootCollider = newGo.AddComponent<BoxCollider>();
                rootCollider.center = new Vector3(0f, 0.5f, 0f);
                rootCollider.size = new Vector3(0.6f, 1.0f, 0.3f);

                // Setup TextMeshPro child on TV screen
                GameObject textGo = new GameObject("TextDisplay");
                textGo.transform.SetParent(tvWall, false);
                textGo.transform.localPosition = new Vector3(0f, 0f, 0.055f);
                textGo.transform.localRotation = Quaternion.identity;

                TextMeshPro tmpText = textGo.AddComponent<TextMeshPro>();
                tmpText.alignment = TextAlignmentOptions.Center;
                tmpText.fontSize = 2f;
                tmpText.color = Color.white;
                tmpText.enableAutoSizing = true;
                tmpText.fontSizeMin = 0.5f;
                tmpText.fontSizeMax = 5.0f;
                tmpText.enableWordWrapping = true;

                var rect = textGo.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(1.3f, 0.85f);
                }

                var editableScreen = newGo.AddComponent<EditableScreen>();
                editableScreen.textComponent = tmpText;

                var interactable = newGo.GetComponent<Interactable>();
                if (interactable == null)
                {
                    interactable = newGo.AddComponent<Interactable>();
                }

                InitializeUnityEvent(interactable, "onInteract");
                InitializeUnityEvent(interactable, "onInteractBegin");
                InitializeUnityEvent(interactable, "onInteractAbort");
                
                var onInteractField = typeof(Interactable).GetField("onInteract", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onInteractField != null)
                {
                    var onInteractEvent = (UnityEngine.Events.UnityEvent)onInteractField.GetValue(interactable);
                    if (onInteractEvent != null)
                    {
                        ClearPersistentListeners(onInteractEvent);
                        onInteractEvent.AddListener(() =>
                        {
                            editableScreen.OnInteract();
                        });
                    }
                }

                var locKeyField = typeof(Interactable).GetField("localizationKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (locKeyField != null)
                {
                    locKeyField.SetValue(interactable, "controls.edit-text");
                }

                var overwriteActionField = typeof(Interactable).GetField("overwriteAction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (overwriteActionField != null)
                {
                    overwriteActionField.SetValue(interactable, "Upgrade");
                }

                var newBuilding = newGo.GetComponent<Building>();
                if (newBuilding == null)
                {
                    newBuilding = newGo.AddComponent<Building>();
                }

                var idField = typeof(Building).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null)
                {
                    idField.SetValue(newBuilding, id);
                }

                if (diContainer != null)
                {
                    diContainer.InjectGameObject(newGo);
                }

                buildings.buildings.Add(newBuilding);
                _instance.Logger.LogInfo($"Successfully injected custom prefab '{id}' into BuildingProvider.");
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

        private static void InitializeUnityEvent(Interactable interactable, string fieldName)
        {
            try
            {
                var field = typeof(Interactable).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var evt = field.GetValue(interactable) as UnityEngine.Events.UnityEvent;
                    if (evt == null)
                    {
                        evt = new UnityEngine.Events.UnityEvent();
                        field.SetValue(interactable, evt);
                    }
                }
            }
            catch (Exception ex)
            {
                _instance.Logger.LogError($"Failed to initialize UnityEvent {fieldName}: {ex.Message}");
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
                    string[] ids = new string[] { "placeable-screen-large", "placeable-screen-tall", "placeable-screen-medium" };
                    foreach (var id in ids)
                    {
                        itemBal.cost[id] = 0;
                        itemBal.requiredTerminalLevel[id] = 1;
                        itemBal.requiredPromotion[id] = _scripts._by_scene._game._license.PromotionId.None;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(_scripts._by_scene._game._job.JobCover), "Start")]
        public static class JobCoverStartPatch
        {
            [HarmonyPostfix]
            public static void Postfix(_scripts._by_scene._game._job.JobCover __instance)
            {
                try
                {
                    _instance.Logger.LogInfo($"[JobCover Diagnostic] Start called on JobCover {__instance.name}");
                    LogHierarchy(__instance.gameObject, 0);
                }
                catch (Exception ex)
                {
                    _instance.Logger.LogError($"Error in JobCover diagnostic: {ex.Message}");
                }
            }

            private static void LogHierarchy(GameObject go, int indent)
            {
                if (go == null) return;
                string indentStr = new string(' ', indent * 2);
                _instance.Logger.LogInfo($"[JobCover Diagnostic] {indentStr}- '{go.name}' (Active: {go.activeSelf}, localPos: {go.transform.localPosition}, localRot: {go.transform.localRotation.eulerAngles}, localScale: {go.transform.localScale})");
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    LogHierarchy(go.transform.GetChild(i).gameObject, indent + 1);
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
                    case "shop-item.name.placeable-screen-large":
                        __result = "Editable Large Screen";
                        return false;
                    case "shop-item.description.placeable-screen-large":
                        __result = "A large display monitor on twin poles that can show custom text and icons. Interact with it to edit.";
                        return false;
                    case "shop-item.name.placeable-screen-tall":
                        __result = "Editable Tall Screen";
                        return false;
                    case "shop-item.description.placeable-screen-tall":
                        __result = "A tall display sign modeled after the Gate signs. Interact with it to edit.";
                        return false;
                    case "shop-item.name.placeable-screen-medium":
                        __result = "Editable Medium Screen";
                        return false;
                    case "shop-item.description.placeable-screen-medium":
                        __result = "A medium table-style sign on a support pole. Interact with it to edit.";
                        return false;
                    case "controls.edit-text":
                        __result = "Edit Text";
                        return false;
                    default:
                        return true;
                }
            }
        }
    }
}
