using System;
using _scripts._by_scene._game._automation;
using _scripts._by_scene._game._baggage;
using _scripts._by_scene._game._baggage_spawner;
using _scripts._by_scene._game._baggage._save_data;
using _scripts._by_scene._game._building;
using _scripts._by_scene._game._parcel;
using Newtonsoft.Json;
using Produktivkeller.SimpleSaveSystem.Core;
using UnityEngine;
using Zenject;

namespace CounterSorterMod
{
    public class AdditionalDataForCounter : MonoBehaviour, IAdditionalDataForBuilding
    {
        [SerializeField]
        public Automat automat;

        [SerializeField]
        public CounterDirectionProvider provider;

        [Inject]
        private BaggageSaveManager _baggageSaveManager;

        [Inject]
        private ParcelSaveManager _parcelSaveManager;

        [Inject]
        private BuildingSaveManager _buildingSaveManager;

        private int _lastSavedCount;
        private int _lastSavedLimit;

        private void Update()
        {
            if (provider == null) return;
            if (provider.GetCurrentCount() != _lastSavedCount || provider.GetTargetLimit() != _lastSavedLimit)
            {
                _lastSavedCount = provider.GetCurrentCount();
                _lastSavedLimit = provider.GetTargetLimit();
                if (_buildingSaveManager != null)
                {
                    _buildingSaveManager.ForceAdditionalUpdateOfPersistenceData();
                }
            }
        }

        public string ProvideForSaveGame()
        {
            if (provider == null || automat == null) return "{}";

            ICanBeProcessedByAutomat item = automat.GetItem();
            BaggageData baggageData = null;
            ParcelData parcelData = null;

            if (item is Baggage baggage && _baggageSaveManager != null)
            {
                baggageData = _baggageSaveManager.ToBaggageData(baggage);
            }
            else if (item is Parcel parcel && _parcelSaveManager != null)
            {
                parcelData = ParcelSaveManager.ToParcelData(parcel);
            }

            var saveData = new CounterSaveData
            {
                currentCount = provider.GetCurrentCount(),
                targetLimit = provider.GetTargetLimit(),
                baggageData = baggageData,
                parcelData = parcelData
            };

            return JsonConvert.SerializeObject(saveData, Formatting.None, SaveService.JsonSerializerSettings);
        }

        public void ApplyFromSaveGame(string additionalData)
        {
            if (string.IsNullOrWhiteSpace(additionalData) || provider == null)
            {
                return;
            }

            try
            {
                var saveData = JsonConvert.DeserializeObject<CounterSaveData>(additionalData, SaveService.JsonSerializerSettings);
                if (saveData != null)
                {
                    _lastSavedCount = saveData.currentCount;
                    _lastSavedLimit = saveData.targetLimit;
                    provider.SetCountAndLimit(saveData.currentCount, saveData.targetLimit);

                    if (saveData.baggageData != null && _baggageSaveManager != null && automat != null)
                    {
                        Baggage baggage = _baggageSaveManager.Spawn(saveData.baggageData);
                        if (baggage != null)
                        {
                            baggage.OnSpawningInAutomat();
                            baggage.gameObject.SetActive(false);
                            automat.ContinueProcessingAfterLoadingSaveGame(baggage);
                        }
                    }
                    else if (saveData.parcelData != null && _parcelSaveManager != null && automat != null)
                    {
                        Parcel parcel = _parcelSaveManager.Spawn(saveData.parcelData);
                        if (parcel != null)
                        {
                            parcel.OnSpawningInAutomat();
                            parcel.gameObject.SetActive(false);
                            automat.ContinueProcessingAfterLoadingSaveGame(parcel);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CounterSorterMod] Failed to load additional data: {ex.Message}");
            }
        }

        [Serializable]
        public class CounterSaveData
        {
            public int currentCount;
            public int targetLimit;
            public BaggageData baggageData;
            public ParcelData parcelData;
        }
    }
}
