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
using _scripts._by_scene._common._balancing;
using _scripts._by_scene._common._balancing._configuration;
using _scripts._by_scene._game._tablet._application._airport_application;
using _scripts._by_scene._game._quest._tutorial_quests;
using _scripts._by_scene._game._baggage._save_data;
using _scripts._by_scene._game._baggage_receiver;
using _scripts._by_scene._game._baggage_spawner;
using _scripts._by_scene._game._baggage_gate;
using _scripts._by_scene._game._quest;
using Produktivkeller.SimpleLocalization.Unity.Core;

namespace AirportNamesMod
{
    [BepInPlugin("com.morg.airport_names_mod", "Airport Names Mod", "1.0.0")]
    public class AirportNamesPlugin : BaseUnityPlugin
    {
        private static AirportNamesPlugin _instance;
        private static readonly Dictionary<string, string> DefaultNames = new Dictionary<string, string>
        {
            { "FRA", "Frankfurt" },
            { "ATL", "Atlanta" },
            { "DEL", "Delhi" },
            { "AMS", "Amsterdam" },
            { "JFK", "New York" },
            { "BCN", "Barcelona" },
            { "JED", "Jeddah" },
            { "MEX", "Mexico City" },
            { "CMI", "Champaign" },
            { "RCE", "Roche" },
            { "SFO", "San Francisco" },
            { "SEA", "Seattle" },
            { "BOM", "Mumbai" },
            { "BKK", "Bangkok" },
            { "MIA", "Miami" },
            { "SZX", "Shenzhen" },
            { "MCO", "Orlando" },
            { "SIN", "Singapore" },
            { "CAN", "Guangzhou" }
        };

        private static readonly Dictionary<string, Replacement> Replacements = new Dictionary<string, Replacement>();
        private static readonly List<Replacement> ActiveReplacements = new List<Replacement>();

        private void Awake()
        {
            _instance = this;
            Logger.LogInfo("Airport Names Mod is loading...");

            // Load Configuration
            foreach (var kvp in DefaultNames)
            {
                var oldCode = kvp.Key;
                var oldName = kvp.Value;

                var newCode = Config.Bind(oldCode, "Code", "", $"New 3-letter code to replace {oldCode} (leave empty to keep default)").Value.Trim();
                var newName = Config.Bind(oldCode, "Name", "", $"New name to replace {oldName} (leave empty to keep default)").Value.Trim();

                if (!string.IsNullOrEmpty(newCode) || !string.IsNullOrEmpty(newName))
                {
                    var rep = new Replacement
                    {
                        OldCode = oldCode,
                        OldName = oldName,
                        NewCode = newCode,
                        NewName = newName
                    };
                    Replacements[oldCode] = rep;
                    ActiveReplacements.Add(rep);
                    Logger.LogInfo($"Configured replacement: {oldCode} ({oldName}) -> {(string.IsNullOrEmpty(newCode) ? "[Default]" : newCode)} ({(string.IsNullOrEmpty(newName) ? "[Default]" : newName)})");
                }
            }

            try
            {
                // Patch the static lists on class initialization of Airports
                ApplyAirportsStaticReplacements();

                var harmony = new Harmony("com.morg.airport_names_mod");
                harmony.PatchAll();
                Logger.LogInfo("Airport Names Mod patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to apply patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void ApplyAirportsStaticReplacements()
        {
            // Access Airports.ValidAirports and Airports.InvalidAirports to trigger static initializer and modify them
            var valid = typeof(Airports).GetField("ValidAirports", BindingFlags.NonPublic | BindingFlags.Static);
            var invalid = typeof(Airports).GetField("InvalidAirports", BindingFlags.NonPublic | BindingFlags.Static);

            if (valid != null)
            {
                var list = (List<string>)valid.GetValue(null);
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var oldCode = list[i];
                        if (Replacements.TryGetValue(oldCode, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                        {
                            list[i] = rep.NewCode;
                        }
                    }
                    _instance.Logger.LogInfo("Successfully replaced codes in Airports.ValidAirports");
                }
            }

            if (invalid != null)
            {
                var list = (List<string>)invalid.GetValue(null);
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var oldCode = list[i];
                        if (Replacements.TryGetValue(oldCode, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                        {
                            list[i] = rep.NewCode;
                        }
                    }
                    _instance.Logger.LogInfo("Successfully replaced codes in Airports.InvalidAirports");
                }
            }
        }

        private static void ReplaceCodesInBalancing(BalancingConfiguration config)
        {
            if (config == null) return;
            _instance.Logger.LogInfo("BalancingConfiguration constructor postfix: replacing airport codes recursively...");
            var visited = new HashSet<object>();
            ReplaceCodesInObject(config, visited);
            _instance.Logger.LogInfo("Successfully replaced airport codes inside BalancingConfiguration object graph.");
        }

        private static void ReplaceCodesInObject(object obj, HashSet<object> visited)
        {
            if (obj == null || !visited.Add(obj)) return;

            var type = obj.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return;

            // Handle collections
            if (obj is System.Collections.IEnumerable enumerable)
            {
                if (obj is System.Collections.IDictionary dict)
                {
                    var keys = new List<object>();
                    foreach (var key in dict.Keys) keys.Add(key);

                    foreach (var key in keys)
                    {
                        var val = dict[key];
                        if (val is string strVal)
                        {
                            if (Replacements.TryGetValue(strVal, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                            {
                                dict[key] = rep.NewCode;
                            }
                        }
                        else
                        {
                            ReplaceCodesInObject(val, visited);
                        }

                        if (key is string strKey)
                        {
                            if (Replacements.TryGetValue(strKey, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                            {
                                dict.Remove(strKey);
                                dict[rep.NewCode] = val;
                            }
                        }
                    }
                }
                else if (obj is System.Collections.IList list)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var val = list[i];
                        if (val is string strVal)
                        {
                            if (Replacements.TryGetValue(strVal, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                            {
                                list[i] = rep.NewCode;
                            }
                        }
                        else
                        {
                            ReplaceCodesInObject(val, visited);
                        }
                    }
                }
                else
                {
                    foreach (var val in enumerable)
                    {
                        ReplaceCodesInObject(val, visited);
                    }
                }
                return;
            }

            // Handle fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(string))
                {
                    var val = (string)field.GetValue(obj);
                    if (val != null && Replacements.TryGetValue(val, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                    {
                        field.SetValue(obj, rep.NewCode);
                    }
                }
                else if (field.FieldType.IsClass || field.FieldType.IsInterface)
                {
                    var val = field.GetValue(obj);
                    ReplaceCodesInObject(val, visited);
                }
            }
        }

        // Harmony patches
        [HarmonyPatch(typeof(Balancing), MethodType.Constructor)]
        public static class BalancingConstructorPatch
        {
            [HarmonyPostfix]
            public static void Postfix(BalancingConfiguration ____balancingConfiguration)
            {
                ReplaceCodesInBalancing(____balancingConfiguration);
            }
        }

        [HarmonyPatch(typeof(LocalizationService), "ResolveLocalizationKey", new Type[] { typeof(string) })]
        public static class LocalizationServicePatch
        {
            [HarmonyPostfix]
            public static void Postfix(string localizationKey, ref string __result)
            {
                if (string.IsNullOrEmpty(__result)) return;

                foreach (var replacement in ActiveReplacements)
                {
                    if (!string.IsNullOrEmpty(replacement.NewCode))
                    {
                        __result = __result.Replace(replacement.OldCode, replacement.NewCode);
                    }
                    if (!string.IsNullOrEmpty(replacement.NewName))
                    {
                        __result = __result.Replace(replacement.OldName, replacement.NewName);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Quest_23_IntroducePreScannedBaggage))]
        public static class Quest_23_Patches
        {
            [HarmonyPatch("GetFlightAirport")]
            [HarmonyPostfix]
            public static void GetFlightAirport_Postfix(ref string __result)
            {
                if (__result == "FRA" && Replacements.TryGetValue("FRA", out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    __result = rep.NewCode;
                }
            }

            [HarmonyPatch("GetBaggageSpawnConfigs")]
            [HarmonyPostfix]
            public static void GetBaggageSpawnConfigs_Postfix(ref List<BaggageSpawnConfig> __result)
            {
                if (__result == null) return;
                for (int i = 0; i < __result.Count; i++)
                {
                    var config = __result[i];
                    if (config.targetAirport == "FRA" && Replacements.TryGetValue("FRA", out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                    {
                        config.targetAirport = rep.NewCode;
                        __result[i] = config;
                    }
                }
            }

            [HarmonyPatch("GetDeliveryTypeBitIndex")]
            [HarmonyPrefix]
            public static bool GetDeliveryTypeBitIndex_Prefix(Quest_23_IntroducePreScannedBaggage __instance, ReceivedBaggage baggage, BaggageReceiver receiver, ref int __result)
            {
                if (Replacements.TryGetValue("FRA", out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    if (receiver != null)
                    {
                        switch (receiver.GetBaggageReceiverMode())
                        {
                            case BaggageReceiverMode.Terminal2:
                                __result = 0;
                                return false;
                            case BaggageReceiverMode.LostAndFound:
                                __result = 1;
                                return false;
                            default:
                                break;
                        }
                    }
                    if (baggage.targetAirport == rep.NewCode)
                    {
                        __result = 2;
                        return false;
                    }
                    __result = -1;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(FlightScheduler))]
        public static class FlightSchedulerPatches
        {
            [HarmonyPatch("ShouldFavorDelAirportDuringXRayPromotionQuest")]
            [HarmonyPrefix]
            public static bool ShouldFavorDelAirportDuringXRayPromotionQuest_Prefix(FlightScheduler __instance, List<string> ____targetAirports, QuestPanel ____questPanel, ref bool __result)
            {
                var delCode = GetReplacementCode("DEL");
                var amsCode = GetReplacementCode("AMS");

                if (____questPanel.GetCurrentQuestId() == "checklist-for-promotion-x-ray" && ____targetAirports.Contains(delCode))
                {
                    __result = ____targetAirports.Contains(amsCode);
                    return false;
                }
                __result = false;
                return false;
            }

            [HarmonyPatch("SelectAirport")]
            [HarmonyPrefix]
            public static bool SelectAirport_Prefix(FlightScheduler __instance, List<string> ____targetAirports, ref string __result)
            {
                var shouldFavor = (bool)typeof(FlightScheduler)
                    .GetMethod("ShouldFavorDelAirportDuringXRayPromotionQuest", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(__instance, null);

                if (shouldFavor)
                {
                    var delCode = GetReplacementCode("DEL");
                    var amsCode = GetReplacementCode("AMS");
                    if (UnityEngine.Random.value < 0.8f)
                    {
                        __result = delCode;
                    }
                    else
                    {
                        __result = amsCode;
                    }
                    return false;
                }
                __result = ____targetAirports[UnityEngine.Random.Range(0, ____targetAirports.Count)];
                return false;
            }

            private static string GetReplacementCode(string originalCode)
            {
                if (Replacements.TryGetValue(originalCode, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    return rep.NewCode;
                }
                return originalCode;
            }
        }

        [HarmonyPatch(typeof(Airports), "Start")]
        public static class AirportsStartPatch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                ApplyAirportsStaticReplacements();
            }
        }

        [HarmonyPatch(typeof(Baggage))]
        public static class BaggagePatches
        {
            [HarmonyPatch("Apply")]
            [HarmonyPrefix]
            public static void Apply_Prefix(ref string targetAirportText)
            {
                if (targetAirportText != null && Replacements.TryGetValue(targetAirportText, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    targetAirportText = rep.NewCode;
                }
            }
        }

        [HarmonyPatch(typeof(AutomatDirectionProviderForTargetAirport))]
        public static class AutomatDirectionProviderPatches
        {
            [HarmonyPatch("SetTargetAirport")]
            [HarmonyPrefix]
            public static void SetTargetAirport_Prefix(ref string targetAirport)
            {
                if (targetAirport != null && Replacements.TryGetValue(targetAirport, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    targetAirport = rep.NewCode;
                }
            }

            [HarmonyPatch("SetTargetAirport2")]
            [HarmonyPrefix]
            public static void SetTargetAirport2_Prefix(ref string targetAirport)
            {
                if (targetAirport != null && Replacements.TryGetValue(targetAirport, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    targetAirport = rep.NewCode;
                }
            }
        }

        [HarmonyPatch(typeof(BaggageGate))]
        public static class BaggageGatePatches
        {
            [HarmonyPatch("Start")]
            [HarmonyPrefix]
            public static void Start_Prefix(BaggageGate __instance)
            {
                var field = typeof(BaggageGate).GetField("targetAirports", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var list = (List<string>)field.GetValue(__instance);
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var oldCode = list[i];
                            if (Replacements.TryGetValue(oldCode, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                            {
                                list[i] = rep.NewCode;
                            }
                        }
                    }
                }
            }

            [HarmonyPatch("GetTargetAirport")]
            [HarmonyPostfix]
            public static void GetTargetAirport_Postfix(ref string __result)
            {
                if (__result != null && Replacements.TryGetValue(__result, out var rep) && !string.IsNullOrEmpty(rep.NewCode))
                {
                    __result = rep.NewCode;
                }
            }
        }
    }

    public class Replacement
    {
        public string OldCode { get; set; }
        public string OldName { get; set; }
        public string NewCode { get; set; }
        public string NewName { get; set; }
    }
}
