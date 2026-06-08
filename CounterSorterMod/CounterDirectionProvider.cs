// -----------------------------------------------------------------------------
// This file is part of an AI-assisted/generated mod for Airport Baggage Simulator.
// Developed with the assistance of Antigravity, an agentic AI coding assistant.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using _scripts._by_scene._game._automation;
using _scripts._by_scene._game._automation._automat_direction_provider;
using _scripts._by_scene._game._baggage;
using _scripts._by_scene._game._building._flip;
using Produktivkeller.SimpleAudioSolution.Access;
using TMPro;
using UnityEngine;
using Zenject;
using _scripts._by_scene._game._day_time;

using Produktivkeller.SimpleCat.Interaction;

namespace CounterSorterMod
{
    public class CounterDirectionProvider : MonoBehaviour, IAutomatDirectionProvider
    {
        [SerializeField]
        public List<TMP_Text> displayTexts = new List<TMP_Text>();

        [Inject]
        private SignalBus _signalBus;

        private int _currentCount = 0;
        private int _targetCountLimit = 5;
        private bool _autoResetUnlocked = false;
        private bool _subscribed = false;

        private void Start()
        {
            if (_signalBus != null)
            {
                _signalBus.Subscribe<NewDayStartedSignal>(OnNewDayStarted);
                _subscribed = true;
            }
            UpdateDisplay();
            BindButtons();
        }

        private void BindButtons()
        {
            var building = GetComponentInParent<_scripts._by_scene._game._building.Building>();
            if (building == null)
            {
                UnityEngine.Debug.LogError("[Counter Mod] BindButtons: Building component not found in parent hierarchy!");
                return;
            }

            var interactables = building.GetComponentsInChildren<Interactable>(true);
            UnityEngine.Debug.Log($"[Counter Mod] BindButtons found {interactables.Length} interactables on building {building.name}.");
            foreach (var interactable in interactables)
            {
                UnityEngine.Debug.Log($"[Counter Mod] Checking interactable on GameObject: {interactable.gameObject.name}");
                var onInteractField = typeof(Interactable).GetField("onInteract", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (onInteractField == null) continue;

                var onInteract = onInteractField.GetValue(interactable) as UnityEngine.Events.UnityEvent;
                if (onInteract == null) continue;

                if (interactable.gameObject.name == "Button_IncrementLimit")
                {
                    onInteract.RemoveAllListeners();
                    ClearPersistentListeners(onInteract);
                    onInteract.AddListener(IncrementLimit);
                    
                    var locKeyField = typeof(Interactable).GetField("localizationKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (locKeyField != null)
                    {
                        locKeyField.SetValue(interactable, "controls.change-count");
                    }
                    UnityEngine.Debug.Log("[Counter Mod] Successfully bound IncrementLimit to Button_IncrementLimit");
                }
                else if (interactable.gameObject.name == "Button_Reset")
                {
                    onInteract.RemoveAllListeners();
                    ClearPersistentListeners(onInteract);
                    onInteract.AddListener(ResetCounter);
                    
                    var locKeyField = typeof(Interactable).GetField("localizationKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (locKeyField != null)
                    {
                        locKeyField.SetValue(interactable, "controls.reset");
                    }
                    UnityEngine.Debug.Log("[Counter Mod] Successfully bound ResetCounter to Button_Reset");
                }
            }
        }

        private void ClearPersistentListeners(UnityEngine.Events.UnityEventBase unityEvent)
        {
            if (unityEvent == null) return;
            try
            {
                var persistentCallsField = typeof(UnityEngine.Events.UnityEventBase).GetField("m_PersistentCalls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (persistentCallsField != null)
                {
                    var persistentCalls = persistentCallsField.GetValue(unityEvent);
                    if (persistentCalls != null)
                    {
                        var callsField = persistentCalls.GetType().GetField("m_Calls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
            catch (System.Exception)
            {
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && _signalBus != null)
            {
                _signalBus.TryUnsubscribe<NewDayStartedSignal>(OnNewDayStarted);
            }
        }

        private void OnNewDayStarted(NewDayStartedSignal signal)
        {
            if (_autoResetUnlocked)
            {
                ResetCounter();
            }
        }

        public AutomatDirection DetermineOutDirection(Flip flip, ICanBeProcessedByAutomat canBeProcessedByAutomat)
        {
            if (!(canBeProcessedByAutomat is Baggage))
            {
                return AutomatDirection.Back;
            }

            _currentCount++;
            UpdateDisplay();

            // If count is less than or equal to the limit, route to the side (Left/Right depending on flip)
            if (_currentCount <= _targetCountLimit)
            {
                return (flip.GetIndex() != 0) ? AutomatDirection.Left : AutomatDirection.Right;
            }

            // Otherwise, route straight ahead (backup exit)
            return AutomatDirection.Back;
        }

        // Triggered by Button 1 (Increase Limit)
        public void IncrementLimit()
        {
            SoundAccess.GetInstance().PlayOneShot("/SFX/Automat for Target Airport/Interact", base.transform.position);
            _targetCountLimit = (_targetCountLimit % 15) + 1; // Cycle 1 to 15
            UpdateDisplay();
        }

        // Triggered by Button 2 (Reset Count)
        public void ResetCounter()
        {
            SoundAccess.GetInstance().PlayOneShot("/SFX/Automat for Target Airport/Interact", base.transform.position);
            _currentCount = 0;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (displayTexts == null) return;
            foreach (var text in displayTexts)
            {
                if (text != null)
                {
                    text.text = $"{_currentCount}/{_targetCountLimit}";
                }
            }
        }

        public void SetAutoReset(bool unlocked)
        {
            _autoResetUnlocked = unlocked;
        }

        public int GetCurrentCount() => _currentCount;
        public int GetTargetLimit() => _targetCountLimit;
        public bool GetAutoReset() => _autoResetUnlocked;

        public void SetCountAndLimit(int count, int limit)
        {
            _currentCount = count;
            _targetCountLimit = limit;
            UpdateDisplay();
        }
    }
}
