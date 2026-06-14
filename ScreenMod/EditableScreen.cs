// -----------------------------------------------------------------------------
// This file is part of an AI-assisted/generated mod for Airport Baggage Simulator.
// Developed with the assistance of Antigravity, an agentic AI coding assistant.
// -----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;
using Zenject;
using Newtonsoft.Json;
using Produktivkeller.SimpleSaveSystem.Core;
using Produktivkeller.SimpleCat.Interaction;
using Produktivkeller.SimpleInput;
using Produktivkeller.SimpleLocalization.Unity.Core;
using _scripts._by_scene._game._building;
using _scripts._by_scene._game._on_screen_controls;
using _scripts._by_scene._game._player_movement;
using _scripts._by_scene._game._tablet;
using _scripts._by_scene._game._target_dot;
using _scripts._input;

namespace ScreenMod
{
    public class EditableScreen : MonoBehaviour, IAdditionalDataForBuilding
    {
        [SerializeField]
        public TMP_Text textComponent;

        private class ClonedPill
        {
            public GameObject gameObject;
            public TMP_Text airportText;
            public TMP_Text cityText;
        }

        private System.Collections.Generic.List<ClonedPill> _clonedPills = new System.Collections.Generic.List<ClonedPill>();
        private GameObject _clonedCanvas;

        private float _sourceAirportFontSize = 36f;
        private float _sourceCityFontSize = 24f;
        private Sprite _sourcePillSprite;
        private Vector2 _sourcePillSize = new Vector2(250f, 140f);
        private Vector2 _sourceImageSize = new Vector2(250f, 140f);

        private Vector3 _textLocalPosition = new Vector3(0f, 1.47f, 0.105f);
        private Quaternion _textLocalRotation = Quaternion.Euler(0f, 180f, 0f);
        private Vector3 _canvasLocalPosition = new Vector3(0f, 1.45f, 0.11f);
        private Quaternion _canvasLocalRotation = Quaternion.Euler(0f, 180f, 0f);
        private Transform _visualParent;      // The TV mesh transform
        private Transform _textVisualParent;  // Where text/canvas should be parented (may differ from _visualParent)
        private Vector3 _textLocalScale = Vector3.one;
        private Vector3 _canvasOriginalLocalScale = new Vector3(0.0018f, 0.0018f, 0.0018f);
        private string _screenType = "large";

        private static readonly string[] ColorTags = new string[]
        {
            "<color=green>",
            "<color=red>",
            "<color=yellow>",
            "<color=blue>",
            "<color=orange>",
            "<color=white>"
        };

        [Inject]
        private TargetDot _targetDot;

        [Inject]
        private Interactor _interactor;

        [Inject]
        private StoneCarverFirstPersonController _firstPersonController;

        [Inject]
        private StoneCarverMouseLook _mouseLook;

        [Inject]
        private InteractionBlocker _interactionBlocker;

        [Inject]
        private Tablet _tablet;

        [Inject]
        private OnScreenControls _onScreenControls;

        [Inject]
        private BuildingSaveManager _buildingSaveManager;

        [Inject]
        private InputService _inputService;

        [Inject]
        private ILocalizationService _localizationService;

        private string _screenText = "EDITABLE SCREEN\n(Interact to edit)";
        private string _savedText = "EDITABLE SCREEN\n(Interact to edit)";
        private bool _isEditing;
        private float _blinkTimer;
        private bool _showCursor;
        private int _framesSinceStartEditing;
        private bool _waitingForKeysRelease;

        private void Start()
        {
            Debug.Log($"[EditableScreen] Start called. Parent Building name: {GetComponentInParent<Building>()?.gameObject.name}");
            _visualParent = transform;
            _textVisualParent = transform;
            _textLocalScale = Vector3.one;

            if (textComponent == null)
            {
                textComponent = GetComponentInChildren<TMP_Text>();
            }

            // Bind interactable event on clone instance
            var interactable = GetComponent<Interactable>();
            if (interactable != null)
            {
                InitializeUnityEvent(interactable, "onInteract");
                InitializeUnityEvent(interactable, "onInteractBegin");
                InitializeUnityEvent(interactable, "onInteractAbort");

                // Set overwriteAction to Upgrade on the instance
                var overwriteActionField = typeof(Interactable).GetField("overwriteAction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (overwriteActionField != null)
                {
                    overwriteActionField.SetValue(interactable, "Upgrade");
                }

                var onInteractField = typeof(Interactable).GetField("onInteract", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onInteractField != null)
                {
                    var onInteractEvent = (UnityEngine.Events.UnityEvent)onInteractField.GetValue(interactable);
                    if (onInteractEvent != null)
                    {
                        onInteractEvent.RemoveAllListeners();
                        onInteractEvent.AddListener(OnInteract);
                        Debug.Log("[EditableScreen] Successfully bound OnInteract listener on instance.");
                    }
                }
            }

            // Determine active screen type based on Building ID
            var building = GetComponentInParent<Building>();
            string id = building != null ? building.GetId() : "placeable-screen-large";
            _screenType = "large";
            if (id == "placeable-screen-tall") _screenType = "tall";
            else if (id == "placeable-screen-medium") _screenType = "medium";

            SetupScreenVisuals(_screenType);

            // If we are in building placement preview, don't allow interaction
            if (building != null && interactable != null)
            {
                // Simple check: if this is a preview building, disable the interactable
                if (building.gameObject.name.Contains("(Clone)") == false && !building.gameObject.activeInHierarchy)
                {
                    Debug.Log("[EditableScreen] Disabling interaction because this is a preview template building prefab.");
                    interactable.MarkAsNotInteractable();
                }
            }

            try
            {
                ScanGameIcons();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditableScreen] Error scanning game icons: {ex.Message}");
            }

            UpdateVisualText();
        }

        private void InitializeUnityEvent(Interactable interactable, string fieldName)
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
                Debug.LogError($"[EditableScreen] Failed to initialize UnityEvent {fieldName}: {ex.Message}");
            }
        }

        private void UpdateInteractableState()
        {
            var interactable = GetComponent<Interactable>();
            if (interactable == null) return;

            bool isHoldingEditKey = (UnityEngine.Input.GetKey(KeyCode.E) || 
                                     (_inputService != null && _inputService.GetButton(117)));

            bool isInBuildingMode = (_interactionBlocker != null && _interactionBlocker.IsInBuildingMode());

            if (isInBuildingMode || isHoldingEditKey)
            {
                interactable.MarkAsNotInteractable();
            }
            else
            {
                // Only allow interaction if we are not in preview template building prefab
                var building = GetComponentInParent<Building>();
                if (building != null && building.gameObject.name.Contains("(Clone)"))
                {
                    interactable.MarkAsInteractable();
                }
            }
        }

        private void SetupScreenVisuals(string type)
        {
            try
            {
                Debug.Log($"[EditableScreen] SetupScreenVisuals for type: '{type}'");

                // Default visual parent is the root transform
                _visualParent = transform;
                _textVisualParent = transform;
                _textLocalScale = Vector3.one;

                // 1. Destroy any previous custom visuals (not D_TV_wall for medium)
                if (type != "medium")
                {
                    Transform oldTv = transform.Find("D_TV_wall");
                    if (oldTv != null)
                    {
                        if (textComponent != null && textComponent.transform.IsChildOf(oldTv))
                            textComponent.transform.SetParent(transform, false);
                        GameObject.Destroy(oldTv.gameObject);
                    }
                }

                Transform oldStand = transform.Find("Stand");
                if (oldStand != null) GameObject.Destroy(oldStand.gameObject);

                Transform oldBase = transform.Find("BasePlate");
                if (oldBase != null) GameObject.Destroy(oldBase.gameObject);

                Transform oldVisuals = transform.Find("TV_Visuals");
                if (oldVisuals != null) GameObject.Destroy(oldVisuals.gameObject);

                Transform oldCanvas = transform.Find("Cloned_Canvas");
                if (oldCanvas != null) GameObject.Destroy(oldCanvas.gameObject);

                var newRenderersList = new System.Collections.Generic.List<Renderer>();

                // Get JobCover for TV visuals source
                var jobCovers = Resources.FindObjectsOfTypeAll<_scripts._by_scene._game._job.JobCover>();
                var jobCover = jobCovers.FirstOrDefault(jc => jc.gameObject.activeInHierarchy && jc.gameObject.scene.name != null)
                            ?? jobCovers.FirstOrDefault(jc => jc.gameObject.scene.name != null)
                            ?? jobCovers.FirstOrDefault();

                if (jobCover == null)
                {
                    Debug.LogWarning("[EditableScreen] No JobCover found in active scene, keeping default setup.");
                    return;
                }

                // ---- LOCATE SOURCE OBJECTS ----
                Transform sourcePoleTransform = jobCover.transform.Cast<Transform>().FirstOrDefault(c => c.name.StartsWith("linepost_0001_clean_a"));
                Transform sourceTvVisuals = jobCover.transform.Find("TV")?.Find("Visuals");
                Transform sourceCanvas = jobCover.transform.Find("Canvas - Target Airports");

                // DIAGNOSTIC: log key transforms
                if (sourceTvVisuals != null)
                {
                    Debug.Log($"[EditableScreen][DIAG] sourceTvVisuals localPos={sourceTvVisuals.localPosition}, localRot={sourceTvVisuals.localRotation.eulerAngles}, localScale={sourceTvVisuals.localScale}, lossyScale={sourceTvVisuals.lossyScale}");
                }
                else
                {
                    Debug.LogWarning("[EditableScreen][DIAG] sourceTvVisuals is NULL!");
                }

                // DIAGNOSTIC: log D_TV_wall hierarchy
                Transform dtvForDiag = transform.Find("D_TV_wall");
                if (dtvForDiag != null)
                {
                    Debug.Log($"[EditableScreen][DIAG] D_TV_wall localPos={dtvForDiag.localPosition}, localRot={dtvForDiag.localRotation.eulerAngles}, localScale={dtvForDiag.localScale}");
                    LogChildHierarchy(dtvForDiag.gameObject, 0);
                }

                if (type == "large")
                {
                    // --- Clone twin poles ---
                    foreach (Transform child in jobCover.transform)
                    {
                        if (child.name.StartsWith("linepost_0001_clean_a"))
                        {
                            GameObject poleClone = GameObject.Instantiate(child.gameObject);
                            poleClone.name = child.name;
                            poleClone.transform.SetParent(transform, false);
                            // Keep x/y from JobCover but move z back so poles are behind the screen
                            poleClone.transform.localPosition = new Vector3(child.localPosition.x, child.localPosition.y, -0.15f);
                            poleClone.transform.localRotation = child.localRotation;
                            poleClone.transform.localScale = child.localScale;
                            poleClone.SetActive(true);
                            var r = poleClone.GetComponent<Renderer>();
                            if (r != null) newRenderersList.Add(r);
                        }
                    }

                    // --- Clone TV screen (Visuals mesh only) ---
                    if (sourceTvVisuals != null)
                    {
                        GameObject tvClone = GameObject.Instantiate(sourceTvVisuals.gameObject);
                        tvClone.name = "TV_Visuals";
                        tvClone.transform.SetParent(transform, false);
                        tvClone.transform.localPosition = new Vector3(0f, 1.45f, -0.05f); // Moved back flush with poles
                        // Preserve the mesh's original local rotation so the screen face direction is correct
                        tvClone.transform.localRotation = sourceTvVisuals.localRotation;
                        tvClone.transform.localScale = sourceTvVisuals.lossyScale;
                        DisableOldUI(tvClone);
                        tvClone.SetActive(true);

                        _visualParent = tvClone.transform;

                        var r = tvClone.GetComponent<Renderer>();
                        if (r != null) newRenderersList.Add(r);

                        // Compute dynamic relative alignment
                        Vector3 relativeCanvasPos = new Vector3(0f, 0f, 0.23f);
                        Quaternion relativeCanvasRot = Quaternion.Euler(0f, 180f, 0f);
                        if (sourceCanvas != null)
                        {
                            relativeCanvasPos = sourceTvVisuals.InverseTransformPoint(sourceCanvas.position);
                            relativeCanvasRot = Quaternion.Inverse(sourceTvVisuals.rotation) * sourceCanvas.rotation;
                            Debug.Log($"[EditableScreen] Computed relative Canvas from JobCover (large): pos={relativeCanvasPos}, rot={relativeCanvasRot.eulerAngles}");
                        }
                        else
                        {
                            Debug.LogWarning("[EditableScreen] sourceCanvas is NULL (large)!");
                        }

                        // Transform relative canvas coordinates to root space based on tvClone's transform
                        Vector3 worldCanvasPos = tvClone.transform.TransformPoint(relativeCanvasPos);
                        Quaternion worldCanvasRot = tvClone.transform.rotation * relativeCanvasRot;

                        _textLocalPosition = transform.InverseTransformPoint(worldCanvasPos);
                        _textLocalRotation = Quaternion.Inverse(transform.rotation) * worldCanvasRot;
                        Debug.Log($"[EditableScreen] Final text localPos (large)={_textLocalPosition}, rot={_textLocalRotation.eulerAngles}");
                    }
                    else
                    {
                        _textLocalPosition = new Vector3(0f, 1.45f, 0.20f);
                        _textLocalRotation = Quaternion.Euler(0f, 180f, 0f);
                    }

                    _textLocalScale = Vector3.one;
                    _textVisualParent = transform;

                    if (textComponent != null)
                    {
                        // Parent text to the SCREEN ROOT (not TV_Visuals) to avoid TV mesh rotation/scaling issues
                        textComponent.transform.SetParent(transform, false);
                        var rect = textComponent.GetComponent<RectTransform>();
                        if (rect != null) rect.sizeDelta = new Vector2(1.3f, 0.85f);
                        textComponent.enableWordWrapping = true;
                        textComponent.transform.localPosition = _textLocalPosition;
                        textComponent.transform.localRotation = _textLocalRotation;
                        textComponent.transform.localScale = _textLocalScale;
                    }

                    if (sourceCanvas != null)
                    {
                        _canvasLocalPosition = _textLocalPosition;
                        _canvasLocalRotation = _textLocalRotation;
                        _canvasOriginalLocalScale = sourceCanvas.localScale;
                        var savedParent = _visualParent;
                        _visualParent = transform;
                        CloneCanvasFrom(sourceCanvas);
                        _visualParent = savedParent;
                    }
                }
                else if (type == "tall")
                {
                    // --- Single center pole ---
                    if (sourcePoleTransform != null)
                    {
                        GameObject poleClone = GameObject.Instantiate(sourcePoleTransform.gameObject);
                        poleClone.name = "Stand_Pole";
                        poleClone.transform.SetParent(transform, false);
                        poleClone.transform.localPosition = new Vector3(0f, 0f, -0.15f); // Moved back behind the screen
                        poleClone.transform.localRotation = Quaternion.identity;
                        poleClone.transform.localScale = new Vector3(1f, 2.0f, 1f);
                        poleClone.SetActive(true);
                        var r = poleClone.GetComponent<Renderer>();
                        if (r != null) newRenderersList.Add(r);
                    }

                    // --- Clone TV screen, scaled vertically to 9:16 portrait ratio ---
                    if (sourceTvVisuals != null)
                    {
                        GameObject tvClone = GameObject.Instantiate(sourceTvVisuals.gameObject);
                        tvClone.name = "TV_Visuals";
                        tvClone.transform.SetParent(transform, false);
                        tvClone.transform.localPosition = new Vector3(0f, 2.3f, -0.05f); // High on post, moved back flush with poles
                        // Preserve mesh orientation
                        tvClone.transform.localRotation = sourceTvVisuals.localRotation;
                        // 9:16 portrait ratio: scale local Z (width) by 0.6f, local Y (height) by 2.0f, local X (depth) by 1.0f
                        tvClone.transform.localScale = new Vector3(sourceTvVisuals.lossyScale.x * 1.0f, sourceTvVisuals.lossyScale.y * 2.0f, sourceTvVisuals.lossyScale.z * 0.6f);
                        DisableOldUI(tvClone);
                        tvClone.SetActive(true);

                        _visualParent = tvClone.transform;

                        var r = tvClone.GetComponent<Renderer>();
                        if (r != null) newRenderersList.Add(r);

                        // Compute dynamic relative alignment
                        Vector3 relativeCanvasPos = new Vector3(0f, 0f, 0.23f);
                        Quaternion relativeCanvasRot = Quaternion.Euler(0f, 180f, 0f);
                        if (sourceCanvas != null)
                        {
                            relativeCanvasPos = sourceTvVisuals.InverseTransformPoint(sourceCanvas.position);
                            relativeCanvasRot = Quaternion.Inverse(sourceTvVisuals.rotation) * sourceCanvas.rotation;
                            Debug.Log($"[EditableScreen] Computed relative Canvas from JobCover (tall): pos={relativeCanvasPos}, rot={relativeCanvasRot.eulerAngles}");
                        }
                        else
                        {
                            Debug.LogWarning("[EditableScreen] sourceCanvas is NULL (tall)!");
                        }

                        // Transform relative canvas coordinates to root space based on tvClone's transform (which scales it appropriately)
                        Vector3 worldCanvasPos = tvClone.transform.TransformPoint(relativeCanvasPos);
                        Quaternion worldCanvasRot = tvClone.transform.rotation * relativeCanvasRot;

                        _textLocalPosition = transform.InverseTransformPoint(worldCanvasPos);
                        _textLocalRotation = Quaternion.Inverse(transform.rotation) * worldCanvasRot;
                        Debug.Log($"[EditableScreen] Final text localPos (tall)={_textLocalPosition}, rot={_textLocalRotation.eulerAngles}");
                    }
                    else
                    {
                        _textLocalPosition = new Vector3(0f, 2.3f, 0.20f);
                        _textLocalRotation = Quaternion.Euler(0f, 180f, 0f);
                    }

                    _textLocalScale = Vector3.one;
                    _textVisualParent = transform;

                    if (textComponent != null)
                    {
                        textComponent.transform.SetParent(transform, false);
                        var rect = textComponent.GetComponent<RectTransform>();
                        if (rect != null) rect.sizeDelta = new Vector2(0.7f, 1.2f);
                        textComponent.enableWordWrapping = true;
                        textComponent.transform.localPosition = _textLocalPosition;
                        textComponent.transform.localRotation = _textLocalRotation;
                        textComponent.transform.localScale = _textLocalScale;
                    }

                    if (sourceCanvas != null)
                    {
                        _canvasLocalPosition = _textLocalPosition;
                        _canvasLocalRotation = _textLocalRotation;
                        _canvasOriginalLocalScale = sourceCanvas.localScale;
                        var savedParent = _visualParent;
                        _visualParent = transform;
                        CloneCanvasFrom(sourceCanvas);
                        _visualParent = savedParent;
                    }
                }
                else if (type == "medium")
                {
                    // Use the D_TV_wall from the airline-table source (it's a small monitor)
                    Transform oldTvForMedium = transform.Find("D_TV_wall");

                    // Find the black gradient material from JobCover TV visuals
                    Material blackCasingMat = sourceTvVisuals?.GetComponent<Renderer>()?.sharedMaterial;

                    if (oldTvForMedium != null)
                    {
                        // Destroy ALL children of D_TV_wall - stickers, labels, canvases, everything
                        // We'll render our own text on the surface, so we need a clean mesh
                        for (int ci = oldTvForMedium.childCount - 1; ci >= 0; ci--)
                        {
                            GameObject.DestroyImmediate(oldTvForMedium.GetChild(ci).gameObject);
                        }

                        oldTvForMedium.gameObject.name = "TV_Visuals";
                        oldTvForMedium.SetParent(transform, false);
                        oldTvForMedium.localPosition = new Vector3(0f, 1.0f, -0.06f); // Moved back a bit more from player
                        oldTvForMedium.localRotation = Quaternion.identity;
                        oldTvForMedium.localScale = Vector3.one * 0.75f;

                        // Apply black casing material to monitor
                        var oldTvRenderer = oldTvForMedium.GetComponent<Renderer>();
                        if (oldTvRenderer != null && blackCasingMat != null)
                        {
                            Material[] newMats = new Material[oldTvRenderer.sharedMaterials.Length];
                            for (int i = 0; i < newMats.Length; i++) newMats[i] = blackCasingMat;
                            oldTvRenderer.sharedMaterials = newMats;
                        }

                        var r = oldTvForMedium.GetComponent<Renderer>();
                        if (r != null) newRenderersList.Add(r);
                        _visualParent = oldTvForMedium;
                    }

                    // Clone one pole
                    if (sourcePoleTransform != null)
                    {
                        GameObject poleClone = GameObject.Instantiate(sourcePoleTransform.gameObject);
                        poleClone.name = "Stand_Pole";
                        poleClone.transform.SetParent(transform, false);
                        poleClone.transform.localPosition = new Vector3(0f, 0f, -0.06f);
                        poleClone.transform.localRotation = Quaternion.identity;
                        poleClone.transform.localScale = new Vector3(1f, 0.75f, 1f);
                        poleClone.SetActive(true);
                        var r = poleClone.GetComponent<Renderer>();
                        if (r != null) newRenderersList.Add(r);
                    }

                    // Text in front of D_TV_wall: screen face is at Z=0.12f (scaled to 0.09f), facing Y=180.
                    // So we place text further forward (Z=0.32f) facing Y=180 to prevent it rendering inside the model.
                    _textLocalPosition = new Vector3(0f, 0.04f, 0.32f);
                    _textLocalRotation = Quaternion.Euler(0f, 180f, 0f);
                    _textVisualParent = _visualParent;
                    _textLocalScale = new Vector3(0.65f, 0.65f, 0.65f);

                    if (textComponent != null && _visualParent != null)
                    {
                        textComponent.transform.SetParent(_visualParent, false);
                        var rect = textComponent.GetComponent<RectTransform>();
                        if (rect != null) rect.sizeDelta = new Vector2(1.0f, 0.65f);
                        textComponent.enableWordWrapping = true;
                        textComponent.transform.localPosition = _textLocalPosition;
                        textComponent.transform.localRotation = _textLocalRotation;
                        textComponent.transform.localScale = _textLocalScale;
                        textComponent.fontSizeMax = 1.2f;
                        textComponent.fontSizeMin = 0.3f;
                    }

                    if (sourceCanvas != null)
                    {
                        // Canvas placed slightly in front of text (Z=0.325f) facing Y=180.
                        _canvasLocalPosition = new Vector3(0f, 0.04f, 0.325f);
                        _canvasLocalRotation = Quaternion.Euler(0f, 180f, 0f);
                        _canvasOriginalLocalScale = sourceCanvas.localScale * 0.65f;
                        CloneCanvasFrom(sourceCanvas);
                    }
                }

                // Adjust collider based on screen size
                AdjustCollider(type);

                // Update outline renderers
                var outline = GetComponentInParent<Building>()?.GetComponent<Outline>();
                if (outline != null)
                {
                    try
                    {
                        var renderersField = typeof(Outline).GetField("renderers", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (renderersField != null)
                        {
                            renderersField.SetValue(outline, newRenderersList.ToArray());
                            Debug.Log($"[EditableScreen] Re-cached {newRenderersList.Count} renderers in Outline component using reflection.");
                        }

                        var loadSmoothNormalsMethod = typeof(Outline).GetMethod("LoadSmoothNormals", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (loadSmoothNormalsMethod != null)
                        {
                            loadSmoothNormalsMethod.Invoke(outline, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[EditableScreen] Failed to load smooth outline normals: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditableScreen] Error setting up screen visuals: {ex.Message}\n{ex.StackTrace}");
            }
        }


        private void CleanVisuals(GameObject go)
        {
            var comps = go.GetComponents<Component>();
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                if (comp is Transform || comp is Renderer || comp is MeshFilter)
                {
                    continue;
                }
                GameObject.DestroyImmediate(comp);
            }

            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i).gameObject;
                if (child.name.Contains("Canvas") || child.name.Contains("Text") || child.name.Contains("Default Texts"))
                {
                    GameObject.DestroyImmediate(child);
                }
                else
                {
                    CleanVisuals(child);
                }
            }
        }

        private void DisableOldUI(GameObject go)
        {
            if (go == null) return;
            
            // 1. Disable all Canvas components
            foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = false;
            }
            
            // 2. Disable all TextMeshPro / TMP_Text components
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.enabled = false;
            }
            
            // 3. Disable any UI Images
            foreach (var img in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                img.enabled = false;
            }

            // 4. Disable any SpriteRenderers
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.enabled = false;
            }
            
            // 5. Disable MeshRenderers of sticker or label children to clear them out
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            {
                string name = child.gameObject.name.ToLowerInvariant();
                if (name.Contains("sticker") || name.Contains("label"))
                {
                    var mr = child.GetComponent<MeshRenderer>();
                    if (mr != null) mr.enabled = false;
                }
            }
        }

        private void AdjustCollider(string type)
        {
            var boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null) boxCollider = gameObject.AddComponent<BoxCollider>();

            if (type == "tall")
            {
                boxCollider.center = new Vector3(0f, 1.1f, 0f);
                boxCollider.size = new Vector3(1.0f, 2.2f, 0.5f);
            }
            else if (type == "medium")
            {
                boxCollider.center = new Vector3(0f, 0.7f, 0f);
                boxCollider.size = new Vector3(1.0f, 1.4f, 0.4f);
            }
            else
            {
                boxCollider.center = new Vector3(0f, 0.9f, 0f);
                boxCollider.size = new Vector3(1.6f, 1.8f, 0.5f);
            }
        }

        private void CloneCanvasFrom(Transform sourceCanvas)
        {
            if (sourceCanvas == null) return;

            Transform oldCanvas = transform.Find("Cloned_Canvas");
            if (oldCanvas != null) GameObject.Destroy(oldCanvas.gameObject);

            _sourcePillSprite = null;
            _sourcePillSize = new Vector2(250f, 140f);
            _sourceImageSize = new Vector2(250f, 140f);

            var sourceGridGroup = sourceCanvas.transform.Find("Grid Layout")?.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            if (sourceGridGroup != null)
            {
                _sourcePillSize = sourceGridGroup.cellSize;
                _sourceImageSize = sourceGridGroup.cellSize;
            }

            var sourceAirportText = sourceCanvas.transform.Find("Grid Layout/Target Airport/Text - Airport")?.GetComponent<TMP_Text>();
            if (sourceAirportText != null)
            {
                _sourceAirportFontSize = sourceAirportText.fontSize;
            }
            var sourceCityText = sourceCanvas.transform.Find("Grid Layout/Target Airport/Text - City")?.GetComponent<TMP_Text>();
            if (sourceCityText != null)
            {
                _sourceCityFontSize = sourceCityText.fontSize;
            }

            var sourcePillImage = sourceCanvas.transform.Find("Grid Layout/Target Airport/Image - Pill")?.GetComponent<UnityEngine.UI.Image>();
            if (sourcePillImage != null)
            {
                _sourcePillSprite = sourcePillImage.sprite;
                var sourceImageRect = sourcePillImage.GetComponent<RectTransform>();
                if (sourceImageRect != null && sourceImageRect.sizeDelta.x > 0.1f && sourceImageRect.sizeDelta.y > 0.1f)
                {
                    _sourceImageSize = sourceImageRect.sizeDelta;
                }
            }

            _clonedCanvas = GameObject.Instantiate(sourceCanvas.gameObject);
            _clonedCanvas.name = "Cloned_Canvas";
            _canvasOriginalLocalScale = sourceCanvas.localScale;

            var displayComp = _clonedCanvas.GetComponent<_scripts._by_scene._game._baggage_receiver.JobCoverForBaggageGateDisplay>();
            if (displayComp != null) GameObject.Destroy(displayComp);

            _clonedCanvas.transform.SetParent(_visualParent != null ? _visualParent : transform, false);
            _clonedCanvas.transform.localPosition = _canvasLocalPosition;
            _clonedCanvas.transform.localRotation = _canvasLocalRotation;
            _clonedCanvas.transform.localScale = _canvasOriginalLocalScale;
            _clonedCanvas.SetActive(false);

            Transform gridLayout = _clonedCanvas.transform.Find("Grid Layout");
            if (gridLayout != null)
            {
                var gridLayoutGroup = gridLayout.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                if (gridLayoutGroup != null)
                {
                    gridLayoutGroup.enabled = false;
                }

                _clonedPills.Clear();
                for (int i = 0; i < gridLayout.childCount; i++)
                {
                    Transform child = gridLayout.GetChild(i);
                    if (child.name.StartsWith("Target Airport"))
                    {
                        var pill = new ClonedPill();
                        pill.gameObject = child.gameObject;
                        pill.airportText = child.Find("Text - Airport")?.GetComponent<TMP_Text>();
                        pill.cityText = child.Find("Text - City")?.GetComponent<TMP_Text>();
                        _clonedPills.Add(pill);

                        var rectTransform = child.GetComponent<RectTransform>();
                        if (rectTransform != null)
                        {
                            rectTransform.pivot = new Vector2(0.5f, 0.5f);
                            rectTransform.sizeDelta = new Vector2(_sourcePillSize.x * 0.60f, _sourcePillSize.y);
                        }

                        var pillImage = child.Find("Image - Pill")?.GetComponent<UnityEngine.UI.Image>();
                        if (pillImage != null)
                        {
                            if (_sourcePillSprite != null)
                            {
                                pillImage.sprite = _sourcePillSprite;
                            }
                            pillImage.color = Color.white;
                            pillImage.enabled = true;

                            var imageRect = pillImage.GetComponent<RectTransform>();
                            if (imageRect != null)
                            {
                                imageRect.sizeDelta = new Vector2(_sourceImageSize.x * 0.60f, _sourceImageSize.y);
                                var sourceImageRect = sourceCanvas.transform.Find("Grid Layout/Target Airport/Image - Pill")?.GetComponent<RectTransform>();
                                if (sourceImageRect != null)
                                {
                                    imageRect.anchorMin = sourceImageRect.anchorMin;
                                    imageRect.anchorMax = sourceImageRect.anchorMax;
                                    imageRect.anchoredPosition = sourceImageRect.anchoredPosition;
                                    imageRect.pivot = sourceImageRect.pivot;
                                }
                            }
                        }

                        if (pill.airportText != null)
                        {
                            pill.airportText.fontSize = _sourceAirportFontSize * 1.45f;
                        }
                        if (pill.cityText != null)
                        {
                            pill.cityText.fontSize = _sourceCityFontSize * 1.85f;
                        }

                        var sourceAirportRect = sourceCanvas.transform.Find("Grid Layout/Target Airport/Text - Airport")?.GetComponent<RectTransform>();
                        var airportRect = child.Find("Text - Airport")?.GetComponent<RectTransform>();
                        if (sourceAirportRect != null && airportRect != null)
                        {
                            airportRect.sizeDelta = sourceAirportRect.sizeDelta;
                            airportRect.anchorMin = sourceAirportRect.anchorMin;
                            airportRect.anchorMax = sourceAirportRect.anchorMax;
                            airportRect.anchoredPosition = sourceAirportRect.anchoredPosition + new Vector2(0f, 12f);
                            airportRect.pivot = sourceAirportRect.pivot;
                        }

                        var sourceCityRect = sourceCanvas.transform.Find("Grid Layout/Target Airport/Text - City")?.GetComponent<RectTransform>();
                        var cityRect = child.Find("Text - City")?.GetComponent<RectTransform>();
                        if (sourceCityRect != null && cityRect != null)
                        {
                            cityRect.sizeDelta = sourceCityRect.sizeDelta;
                            cityRect.anchorMin = sourceCityRect.anchorMin;
                            cityRect.anchorMax = sourceCityRect.anchorMax;
                            cityRect.anchoredPosition = sourceCityRect.anchoredPosition - new Vector2(0f, 12f);
                            cityRect.pivot = sourceCityRect.pivot;
                        }
                    }
                }
            }
        }

        private void Update()
        {
            // Continuously update interactable state based on Build Mode
            UpdateInteractableState();

            if (!_isEditing) return;

            if (_waitingForKeysRelease)
            {
                if (!UnityEngine.Input.anyKey)
                {
                    _waitingForKeysRelease = false;
                    Debug.Log("[EditableScreen] All keys released, starting text input monitoring.");
                }
                return;
            }

            _framesSinceStartEditing++;
            if (_framesSinceStartEditing < 5) return; // Cooldown of 5 frames to prevent immediate exit

            // Handle blink cursor
            _blinkTimer += Time.deltaTime;
            if (_blinkTimer >= 0.5f)
            {
                _blinkTimer = 0f;
                _showCursor = !_showCursor;
                UpdateVisualText();
            }

            // Check for exit/cancel controls via InputService or Escape
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("[EditableScreen] Escape pressed, cancelling edit.");
                StopEditing(saveChanges: false);
                return;
            }

            if (_inputService.GetButtonDown("Leave Whiteboard") || _inputService.GetButtonDown("Back"))
            {
                Debug.Log("[EditableScreen] Leave or Back pressed, saving edit.");
                StopEditing(saveChanges: true);
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                // Ctrl+Enter saves
                if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
                {
                    Debug.Log("[EditableScreen] Ctrl+Enter pressed, saving edit.");
                    StopEditing(saveChanges: true);
                    return;
                }

                _screenText += "\n";
                UpdateVisualText();
                return;
            }

            if (_inputService.GetButtonDown("Clear Whiteboard") && (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)))
            {
                _screenText = "";
                UpdateVisualText();
                return;
            }

            // Handle color tag shortcuts (Ctrl + G to cycle colors)
            bool ctrlPressed = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            if (ctrlPressed && UnityEngine.Input.GetKeyDown(KeyCode.G))
            {
                int currentTagIndex = -1;
                for (int i = 0; i < ColorTags.Length; i++)
                {
                    if (_screenText.EndsWith(ColorTags[i], StringComparison.OrdinalIgnoreCase))
                    {
                        currentTagIndex = i;
                        break;
                    }
                }

                if (currentTagIndex != -1)
                {
                    // Replace the tag at the end with the next one in the cycle
                    string currentTag = ColorTags[currentTagIndex];
                    int nextTagIndex = (currentTagIndex + 1) % ColorTags.Length;
                    string nextTag = ColorTags[nextTagIndex];
                    
                    _screenText = _screenText.Substring(0, _screenText.Length - currentTag.Length) + nextTag;
                }
                else
                {
                    // Append the first color tag
                    _screenText += ColorTags[0];
                }

                UpdateVisualText();
                return;
            }

            // Capture keyboard typing character input
            string input = UnityEngine.Input.inputString;
            if (!string.IsNullOrEmpty(input))
            {
                foreach (char c in input)
                {
                    if (c == '\b') // Backspace
                    {
                        if (_screenText.Length > 0)
                        {
                            bool removedTag = false;
                            foreach (var tag in ColorTags)
                            {
                                if (_screenText.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
                                {
                                    _screenText = _screenText.Substring(0, _screenText.Length - tag.Length);
                                    removedTag = true;
                                    break;
                                }
                            }

                            if (!removedTag)
                            {
                                _screenText = _screenText.Substring(0, _screenText.Length - 1);
                            }
                        }
                    }
                    else if (char.IsControl(c))
                    {
                        // Ignore any control characters (like Ctrl shortcuts, tabs, newlines)
                    }
                    else
                    {
                        _screenText += c;
                    }
                }
                UpdateVisualText();
            }
        }

        public void OnInteract()
        {
            Debug.Log($"[EditableScreen] OnInteract called! isEditing: {_isEditing}, interactionBlocker: {_interactionBlocker != null}");
            if (_isEditing) return;

            // Check if interaction is allowed
            if (_interactionBlocker != null && _interactionBlocker.IsAnyCommonSceneElementVisible())
            {
                Debug.Log("[EditableScreen] Interaction blocked because a common scene UI element is visible.");
                return;
            }

            _isEditing = true;
            _waitingForKeysRelease = true;
            _framesSinceStartEditing = 0;
            _blinkTimer = 0f;
            _showCursor = true;
            _savedText = _screenText; // Remember text in case we cancel

            // Freeze player
            Debug.Log($"[EditableScreen] Freezing player. Controller: {_firstPersonController != null}, MouseLook: {_mouseLook != null}");
            if (_firstPersonController != null) _firstPersonController.SetFrozen(true);
            if (_mouseLook != null) _mouseLook.SetFrozen(true);
            
            if (_targetDot != null) _targetDot.Hide();
            if (_interactor != null) _interactor.enabled = false;
            if (_tablet != null) _tablet.gameObject.SetActive(false);
            if (_interactionBlocker != null) _interactionBlocker.StartInteractionWithWhiteboard(); // block other interactions
            
            if (_onScreenControls != null) _onScreenControls.ShowForWhiteboard();

            UpdateVisualText();
        }

        private void StopEditing(bool saveChanges)
        {
            Debug.Log($"[EditableScreen] StopEditing called! isEditing: {_isEditing}, saveChanges: {saveChanges}");
            if (!_isEditing) return;

            _isEditing = false;

            // Unfreeze player
            if (_firstPersonController != null) _firstPersonController.SetFrozen(false);
            if (_mouseLook != null) _mouseLook.SetFrozen(false);
            
            if (_targetDot != null) _targetDot.Show();
            if (_interactor != null) _interactor.enabled = true;
            if (_tablet != null) _tablet.gameObject.SetActive(true);
            if (_interactionBlocker != null) _interactionBlocker.StopInteractionWithWhiteboard();
            
            if (_onScreenControls != null) _onScreenControls.EndUsingWhiteboard();

            if (!saveChanges)
            {
                _screenText = _savedText; // Restore original
            }
            else
            {
                // Save to persistence
                if (_buildingSaveManager != null)
                {
                    _buildingSaveManager.ForceAdditionalUpdateOfPersistenceData();
                }
            }

            UpdateVisualText();
        }

        private struct TagLocation
        {
            public string type; // "A" or "I"
            public string code;
            public int startIndex;
            public int endIndex;
            public int pillIndex;
            public Color32 color;
        }

        private void SetCanvasActive(bool active)
        {
            if (_clonedCanvas != null && _clonedCanvas.activeSelf != active)
            {
                _clonedCanvas.SetActive(active);
            }
        }

        private int GetRenderedLength(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Strip all TMPro tags like <color=...> or <size=...>
            string stripped = Regex.Replace(text, @"<[^>]*>", "");
            return stripped.Length;
        }

        private Color32 GetActiveColorAt(string text, int index)
        {
            Color32 activeColor = Color.white;
            System.Collections.Generic.Stack<Color32> colorStack = new System.Collections.Generic.Stack<Color32>();
            
            // Match any tag: <color=...> or </color>
            var tagRegex = new Regex(@"<(color|/color)(?:=([^>]+))?>", RegexOptions.IgnoreCase);
            var matches = tagRegex.Matches(text.Substring(0, index));
            
            foreach (Match m in matches)
            {
                if (m.Groups[1].Value.Equals("/color", StringComparison.OrdinalIgnoreCase))
                {
                    if (colorStack.Count > 0)
                    {
                        activeColor = colorStack.Pop();
                    }
                    else
                    {
                        activeColor = Color.white;
                    }
                }
                else // <color=...>
                {
                    colorStack.Push(activeColor);
                    string colorVal = m.Groups[2].Value.Trim().ToLowerInvariant();
                    activeColor = ParseColor(colorVal, activeColor);
                }
            }
            return activeColor;
        }

        private Color32 ParseColor(string val, Color32 fallback)
        {
            if (string.IsNullOrEmpty(val)) return fallback;
            
            if (val.StartsWith("#"))
            {
                if (ColorUtility.TryParseHtmlString(val, out Color col))
                {
                    return col;
                }
            }
            else
            {
                switch (val)
                {
                    case "red": return Color.red;
                    case "green": return Color.green;
                    case "blue": return Color.blue;
                    case "yellow": return Color.yellow;
                    case "white": return Color.white;
                    case "black": return Color.black;
                    case "orange": return new Color32(255, 165, 0, 255);
                    case "cyan": return Color.cyan;
                    case "magenta": return Color.magenta;
                    case "grey":
                    case "gray": return Color.gray;
                }
            }
            return fallback;
        }

        private void UpdateVisualText()
        {
            if (textComponent == null) return;

            // 1. Process tags and get the cleaned text with placeholders + tag locations
            string processedText = "";
            var tagLocations = new System.Collections.Generic.List<TagLocation>();
            int lastIndex = 0;
            int pillCount = 0;

            string originalText = _screenText;
            var matches = Regex.Matches(originalText, @"\[(?:([AI])=)?([A-Za-z0-9_-]+)\]");

            if (matches.Count > 0)
            {
                SetCanvasActive(true);

                foreach (Match match in matches)
                {
                    if (pillCount >= _clonedPills.Count)
                    {
                        if (_clonedPills.Count > 0)
                        {
                            var template = _clonedPills[0];
                            GameObject newPillGo = GameObject.Instantiate(template.gameObject, template.gameObject.transform.parent);
                            newPillGo.name = $"Target Airport ({_clonedPills.Count})";
                            
                            var newPill = new ClonedPill
                            {
                                gameObject = newPillGo,
                                airportText = newPillGo.transform.Find("Text - Airport")?.GetComponent<TMP_Text>(),
                                cityText = newPillGo.transform.Find("Text - City")?.GetComponent<TMP_Text>()
                            };
                            _clonedPills.Add(newPill);
                            Debug.Log($"[EditableScreen] Dynamically spawned new pill: {newPillGo.name}. Total: {_clonedPills.Count}");
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Append text before the match
                    processedText += originalText.Substring(lastIndex, match.Index - lastIndex);

                    // Get the start rendered character index (before placeholder)
                    int startCharIndex = GetRenderedLength(processedText);

                    // For layout placeholder, use 3 M's (MMM) for Airport pills and 2 M's (MM) for Icon-only pills to keep them compact
                    string type = match.Groups[1].Value.ToUpper();
                    if (string.IsNullOrEmpty(type)) type = "A"; // Default to Airport
                    
                    string placeholder = (type == "I") ? "MM" : "MMM";
                    processedText += $"<color=#00000000>{placeholder}</color>";

                    // Get the end rendered character index (after placeholder)
                    int endCharIndex = GetRenderedLength(processedText) - 1;

                    Color32 activeColor = GetActiveColorAt(originalText, match.Index);

                    tagLocations.Add(new TagLocation
                    {
                        type = type,
                        code = match.Groups[2].Value.ToUpper(),
                        startIndex = startCharIndex,
                        endIndex = endCharIndex,
                        pillIndex = pillCount,
                        color = activeColor
                    });

                    pillCount++;
                    lastIndex = match.Index + match.Length;
                }
                // Append remainder
                processedText += originalText.Substring(lastIndex);
            }
            else
            {
                SetCanvasActive(false);
                processedText = originalText;
            }

            textComponent.text = processedText;

            // Ensure parent is correct - use _textVisualParent (may differ from _visualParent for large/tall)
            Transform textParent = _textVisualParent ?? _visualParent ?? transform;
            if (textComponent.transform.parent != textParent)
            {
                textComponent.transform.SetParent(textParent, false);
            }
            if (_clonedCanvas != null && _clonedCanvas.transform.parent != textParent)
            {
                _clonedCanvas.transform.SetParent(textParent, false);
            }

            // Center the text based on size type settings
            textComponent.transform.localPosition = _textLocalPosition;
            textComponent.transform.localRotation = _textLocalRotation;
            textComponent.transform.localScale = _textLocalScale;

            if (_clonedCanvas != null)
            {
                _clonedCanvas.transform.localPosition = _canvasLocalPosition;
                _clonedCanvas.transform.localRotation = _canvasLocalRotation;
                _clonedCanvas.transform.localScale = _canvasOriginalLocalScale;
            }

            // 3. Force mesh update so characterInfo is calculated
            textComponent.ForceMeshUpdate();

            // 4. Populate and position each pill
            for (int i = 0; i < _clonedPills.Count; i++)
            {
                var tagLoc = tagLocations.FirstOrDefault(t => t.pillIndex == i);
                if (tagLoc.code != null) // If this pill is used
                {
                    var pill = _clonedPills[i];
                    pill.gameObject.SetActive(true);

                    string code = tagLoc.code;
                    string type = tagLoc.type;

                    var pillImage = pill.gameObject.transform.Find("Image - Pill")?.GetComponent<UnityEngine.UI.Image>();
                    var rectTransform = pill.gameObject.GetComponent<RectTransform>();
                    var imageRect = pillImage?.GetComponent<RectTransform>();

                    if (type == "I")
                    {
                        // Icon-only mode
                        // 1. Hide City text
                        if (pill.cityText != null) pill.cityText.gameObject.SetActive(false);

                        // 2. See if we have a custom sprite in memory for this icon
                        Sprite customSprite = FindIconSprite(code);
                        Color32 activeColor = tagLoc.color;
                        if (customSprite != null)
                        {
                            // If we have a physical sprite, use it on the image and hide the airport text
                            if (pill.airportText != null) pill.airportText.gameObject.SetActive(false);
                            if (pillImage != null)
                            {
                                pillImage.sprite = customSprite;
                                pillImage.color = activeColor; // Respect active font color!
                                pillImage.enabled = true;
                            }
                        }
                        else
                        {
                            // Fallback: use TMPro text/sprite
                            if (pillImage != null) pillImage.enabled = false; // Hide background pill
                            if (pill.airportText != null)
                            {
                                pill.airportText.gameObject.SetActive(true);
                                pill.airportText.fontSize = _sourceAirportFontSize * 1.65f; // Keep it large

                                string hexColor = ColorUtility.ToHtmlStringRGB(activeColor);
                                if (code == "LOST")
                                {
                                    // Use 'question' sprite from sprite-asset
                                    pill.airportText.text = $"<color=#{hexColor}><sprite name=\"question\"></color>";
                                }
                                else if (code == "SUB" || code == "SUBSTANCE" || code == "ILLICIT")
                                {
                                    // Use 'paw' sprite (inspection dog icon from the game's sprite-asset)
                                    pill.airportText.text = $"<color=#{hexColor}><sprite name=\"paw\"></color>";
                                }
                                else if (code == "WEAPON" || code == "WEAPONS")
                                {
                                    // Use 'x-ray' sprite from game's sprite-asset
                                    pill.airportText.text = $"<color=#{hexColor}><sprite name=\"x-ray\"></color>";
                                }
                                else if (code == "WEIGHT" || code == "HEAVY" || code == "OVERWEIGHT")
                                {
                                    pill.airportText.text = $"<color=#{hexColor}>⚖</color>";
                                }
                                else
                                {
                                    pill.airportText.text = $"<color=#{hexColor}>{code}</color>";
                                }
                            }
                        }

                        // Make the pill RectTransform square (scale to cell height)
                        if (rectTransform != null)
                        {
                            rectTransform.sizeDelta = new Vector2(_sourcePillSize.y, _sourcePillSize.y);
                        }
                        if (imageRect != null)
                        {
                            imageRect.sizeDelta = new Vector2(_sourcePillSize.y, _sourcePillSize.y);
                        }
                    }
                    else
                    {
                        // Airport pill mode (default)
                        if (pill.cityText != null) pill.cityText.gameObject.SetActive(true);
                        if (pill.airportText != null) pill.airportText.gameObject.SetActive(true);
                        
                        if (pillImage != null)
                        {
                            pillImage.sprite = _sourcePillSprite;
                            pillImage.color = Color.white;
                            pillImage.enabled = true;
                        }

                        if (rectTransform != null)
                        {
                            rectTransform.sizeDelta = new Vector2(_sourcePillSize.x * 0.60f, _sourcePillSize.y);
                        }
                        if (imageRect != null)
                        {
                            imageRect.sizeDelta = new Vector2(_sourceImageSize.x * 0.60f, _sourceImageSize.y);
                        }

                        // Resolve localized city name
                        string cityKey = "airport.city." + code.ToLowerInvariant();
                        string cityName = _localizationService != null ? _localizationService.ResolveLocalizationKey(cityKey) : code;
                        if (cityName != null && cityName.StartsWith("airport.city.", StringComparison.OrdinalIgnoreCase))
                        {
                            cityName = code;
                        }

                        if (pill.airportText != null)
                        {
                            pill.airportText.fontSize = _sourceAirportFontSize * 1.45f;
                            pill.airportText.text = code;
                        }
                        if (pill.cityText != null)
                        {
                            pill.cityText.fontSize = _sourceCityFontSize * 1.85f;
                            pill.cityText.text = cityName;
                        }
                    }

                    // Position manually
                    if (textComponent.textInfo != null &&
                        tagLoc.startIndex < textComponent.textInfo.characterInfo.Length &&
                        tagLoc.endIndex < textComponent.textInfo.characterInfo.Length)
                    {
                        var charInfoStart = textComponent.textInfo.characterInfo[tagLoc.startIndex];
                        var charInfoEnd = textComponent.textInfo.characterInfo[tagLoc.endIndex];

                        Vector3 startPos = charInfoStart.bottomLeft;
                        Vector3 endPos = charInfoEnd.topRight;

                        // Calculate line height
                        float lineHeight = 0.4f; // Fallback
                        int lineIndex = charInfoStart.lineNumber;
                        if (lineIndex >= 0 && lineIndex < textComponent.textInfo.lineInfo.Length)
                        {
                            float lH = textComponent.textInfo.lineInfo[lineIndex].lineHeight;
                            if (lH > 0.05f)
                            {
                                lineHeight = lH;
                            }
                        }

                        // Align center of the pill with center of the spaces
                        Vector3 localCenter = (startPos + endPos) / 2f;
                        
                        // Shift up to center the pill vertically on the text line
                        // Adjusted from 0.15 to 0.08 * lineHeight to lower it slightly and center it perfectly.
                        localCenter.y += lineHeight * 0.08f;

                        // Push slightly forward towards the player to prevent Z-fighting (local Z points inwards)
                        localCenter.z -= 0.005f;

                        // Convert local coordinate of text component to world space
                        Vector3 worldPos = textComponent.transform.TransformPoint(localCenter);
                        pill.gameObject.transform.position = worldPos;

                        // Scale based on line height
                        float scaleFactor = lineHeight / 0.4f;
                        scaleFactor = Mathf.Clamp(scaleFactor, 0.2f, 2.0f);
                        pill.gameObject.transform.localScale = Vector3.one * scaleFactor;
                    }
                }
                else
                {
                    _clonedPills[i].gameObject.SetActive(false);
                }
            }
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform current = obj.transform;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            return path;
        }

        private void ScanGameIcons()
        {
            Debug.Log("[EditableScreen] Starting Scene Icon Scan...");
            try
            {
                // 1. Scan TMPro sprite assets
                var spriteAssets = Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>();
                Debug.Log($"[EditableScreen] Found {spriteAssets.Length} TMPro Sprite Assets.");
                foreach (var asset in spriteAssets)
                {
                    Debug.Log($"[EditableScreen] SpriteAsset: '{asset.name}'");
                    if (asset.spriteCharacterTable != null)
                    {
                        foreach (var sprite in asset.spriteCharacterTable)
                        {
                            Debug.Log($"[EditableScreen]   Sprite: '{sprite.name}' (unicode: {sprite.unicode})");
                        }
                    }
                }

                // 2. Scan all active text components in the scene
                var allTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
                Debug.Log($"[EditableScreen] Found {allTexts.Length} TMP_Text components.");
                foreach (var txt in allTexts)
                {
                    if (txt.gameObject.scene.name != null && !string.IsNullOrEmpty(txt.text))
                    {
                        string tLower = txt.text.ToLowerInvariant();
                        if (txt.text.Contains("?") || tLower.Contains("lost") || tLower.Contains("substance") || tLower.Contains("weapon") || tLower.Contains("illegal") || tLower.Contains("illicit") || tLower.Contains("weight") || tLower.Contains("heavy") || txt.text.Contains("<sprite"))
                        {
                            Debug.Log($"[EditableScreen] Text Match: '{txt.text}' on '{txt.name}' at path '{GetGameObjectPath(txt.gameObject)}'");
                        }
                    }
                }

                // 3. Scan all Image components in the scene
                var allImages = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>();
                Debug.Log($"[EditableScreen] Found {allImages.Length} UI Image components.");
                foreach (var img in allImages)
                {
                    if (img.gameObject.scene.name != null && img.sprite != null)
                    {
                        string sLower = img.sprite.name.ToLowerInvariant();
                        if (sLower.Contains("lost") || sLower.Contains("substance") || sLower.Contains("weapon") || sLower.Contains("illegal") || sLower.Contains("illicit") || sLower.Contains("question") || sLower.Contains("unknown") || sLower.Contains("dog") || sLower.Contains("scale") || sLower.Contains("weight") || sLower.Contains("heavy") || sLower.Contains("balance"))
                        {
                            Debug.Log($"[EditableScreen] Image Sprite Match: '{img.sprite.name}' on '{img.name}' at path '{GetGameObjectPath(img.gameObject)}'");
                        }
                    }
                }

                // 4. Scan all Sprite assets in memory
                var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
                Debug.Log($"[EditableScreen] Found {allSprites.Length} Sprites in memory.");
                foreach (var spr in allSprites)
                {
                    if (spr != null)
                    {
                        string sLower = spr.name.ToLowerInvariant();
                        if (sLower.Contains("lost") || sLower.Contains("substance") || sLower.Contains("weapon") || 
                            sLower.Contains("illegal") || sLower.Contains("illicit") || sLower.Contains("question") || 
                            sLower.Contains("unknown") || sLower.Contains("dog") || sLower.Contains("scale") || 
                            sLower.Contains("weight") || sLower.Contains("heavy") || sLower.Contains("balance") || 
                            sLower.Contains("xray") || sLower.Contains("x-ray") || sLower.Contains("pistol") ||
                            sLower.Contains("gun") || sLower.Contains("skull") || sLower.Contains("danger") ||
                            sLower.Contains("knife") || sLower.Contains("icon") || sLower.Contains("symbol"))
                        {
                            Debug.Log($"[EditableScreen] Sprite Asset Match: '{spr.name}' (texture: {spr.texture?.name})");
                        }
                    }
                }

                // 5. Scan Baggage Receivers hierarchy in scene
                GameObject spawnerAndReceiver = GameObject.Find("[2] Baggage Spawner and Receiver");
                if (spawnerAndReceiver == null)
                {
                    spawnerAndReceiver = GameObject.Find("/[2] Baggage Spawner and Receiver");
                }
                if (spawnerAndReceiver != null)
                {
                    Debug.Log("[EditableScreen] Found [2] Baggage Spawner and Receiver. Logging hierarchy...");
                    LogReceiverHierarchy(spawnerAndReceiver, 0);
                }
                else
                {
                    // Fallback search for any BaggageReceiver components in active scene
                    var receivers = Resources.FindObjectsOfTypeAll<_scripts._by_scene._game._baggage_receiver.BaggageReceiver>();
                    Debug.Log($"[EditableScreen] Found {receivers.Length} BaggageReceiver components in scene.");
                    foreach (var rec in receivers)
                    {
                        if (rec.gameObject.scene.name != null)
                        {
                            Debug.Log($"[EditableScreen] BaggageReceiver: '{rec.name}' at '{GetGameObjectPath(rec.gameObject)}'");
                            LogReceiverHierarchy(rec.gameObject, 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditableScreen] Exception during ScanGameIcons: {ex.Message}\n{ex.StackTrace}");
            }
            Debug.Log("[EditableScreen] Scene Icon Scan completed.");
        }

        private void LogChildHierarchy(GameObject go, int indent)
        {
            if (go == null) return;
            string pad = new string(' ', indent * 2);
            var mr = go.GetComponent<MeshRenderer>();
            var sr = go.GetComponent<SpriteRenderer>();
            string info = "";
            if (mr != null) info += $" MeshRenderer(enabled={mr.enabled}, mats=[{string.Join(",", System.Array.ConvertAll(mr.sharedMaterials, m => m?.name ?? "null"))}])";
            if (sr != null) info += $" SpriteRenderer(enabled={sr.enabled}, sprite={sr.sprite?.name ?? "null"})";
            Debug.Log($"[EditableScreen][DIAG]{pad} '{go.name}' localRot={go.transform.localRotation.eulerAngles} localPos={go.transform.localPosition}{info}");
            for (int i = 0; i < go.transform.childCount; i++)
                LogChildHierarchy(go.transform.GetChild(i).gameObject, indent + 1);
        }

        private void LogReceiverHierarchy(GameObject go, int indent)
        {
            if (go == null) return;
            string indentStr = new string(' ', indent * 2);
            var image = go.GetComponent<UnityEngine.UI.Image>();
            var spriteRenderer = go.GetComponent<SpriteRenderer>();
            var meshRenderer = go.GetComponent<MeshRenderer>();
            var text = go.GetComponent<TMP_Text>();
            
            string info = "";
            if (image != null && image.sprite != null) info += $", UI Image Sprite: '{image.sprite.name}'";
            if (spriteRenderer != null && spriteRenderer.sprite != null) info += $", SpriteRenderer Sprite: '{spriteRenderer.sprite.name}'";
            if (meshRenderer != null && meshRenderer.sharedMaterial != null) info += $", Material: '{meshRenderer.sharedMaterial.name}'";
            if (text != null) info += $", Text: '{text.text.Replace('\n', ' ')}'";

            Debug.Log($"[EditableScreen] {indentStr}- {go.name} (Active: {go.activeSelf}{info})");

            for (int i = 0; i < go.transform.childCount; i++)
            {
                LogReceiverHierarchy(go.transform.GetChild(i).gameObject, indent + 1);
            }
        }

        private bool DoesSpriteExist(string spriteName)
        {
            try
            {
                var defaultAsset = TMPro.TMP_Settings.defaultSpriteAsset;
                if (defaultAsset != null && defaultAsset.GetSpriteIndexFromName(spriteName) != -1)
                {
                    return true;
                }
                
                var assets = Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>();
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        if (asset != null && asset.GetSpriteIndexFromName(spriteName) != -1)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditableScreen] Error checking sprite existence: {ex.Message}");
            }
            return false;
        }

        private string GetBestSpriteTag(string[] candidateNames, string fallbackText)
        {
            foreach (var name in candidateNames)
            {
                if (DoesSpriteExist(name))
                {
                    return $"<sprite name=\"{name}\">";
                }
            }
            return fallbackText;
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        }

        private static System.Collections.Generic.Dictionary<string, Sprite> _dynamicSpriteCache = new System.Collections.Generic.Dictionary<string, Sprite>();

        private Sprite FindIconSprite(string code)
        {
            try
            {
                if (_dynamicSpriteCache.TryGetValue(code, out var cachedSprite) && cachedSprite != null)
                {
                    return cachedSprite;
                }

                string[] candidates = null;
                if (code == "LOST" || code == "FOUND")
                {
                    candidates = new string[] { "Lost and Found", "lost-and-found", "lost_and_found" };
                }
                else if (code == "SUB" || code == "SUBSTANCE" || code == "ILLICIT" || code == "ILLEGAL" || code == "DOG")
                {
                    candidates = new string[] { "inspection-dog", "inspection_dog", "Illegal Substance", "illegal-substance", "illegal_substance" };
                }
                else if (code == "WEAPON" || code == "WEAPONS" || code == "GUN" || code == "DANGER" || code == "XRAY" || code == "X-RAY")
                {
                    candidates = new string[] { "X-Ray", "xray", "x-ray" };
                }
                else if (code == "WEIGHT" || code == "HEAVY" || code == "OVERWEIGHT" || code == "SCALE")
                {
                    candidates = new string[] { "Weight", "weight" };
                }
                else if (code == "AIRLINE" || code == "STICKER" || code == "TICKET")
                {
                    candidates = new string[] { "Airline Sticker", "airline-sticker", "airline_sticker" };
                }

                if (candidates != null)
                {
                    var normalizedCandidates = candidates.Select(NormalizeName).ToList();

                    // 1. Search Sprites
                    var allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
                    foreach (var spr in allSprites)
                    {
                        if (spr != null && normalizedCandidates.Contains(NormalizeName(spr.name)))
                        {
                            Debug.Log($"[EditableScreen] FindIconSprite matched Sprite: code='{code}', sprite='{spr.name}'");
                            _dynamicSpriteCache[code] = spr;
                            return spr;
                        }
                    }

                    // 2. Search Materials (and extract mainTexture)
                    var allMaterials = Resources.FindObjectsOfTypeAll<Material>();
                    foreach (var mat in allMaterials)
                    {
                        if (mat != null && normalizedCandidates.Contains(NormalizeName(mat.name)))
                        {
                            var tex = mat.mainTexture as Texture2D;
                            if (tex != null)
                            {
                                Debug.Log($"[EditableScreen] FindIconSprite matched Material: code='{code}', material='{mat.name}', texture='{tex.name}'. Creating Sprite dynamically.");
                                Sprite dynamicSpr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                                _dynamicSpriteCache[code] = dynamicSpr;
                                return dynamicSpr;
                            }
                        }
                    }

                    // 3. Search Texture2D
                    var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();
                    foreach (var tex in allTextures)
                    {
                        if (tex != null && normalizedCandidates.Contains(NormalizeName(tex.name)))
                        {
                            Debug.Log($"[EditableScreen] FindIconSprite matched Texture2D: code='{code}', texture='{tex.name}'. Creating Sprite dynamically.");
                            Sprite dynamicSpr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                            _dynamicSpriteCache[code] = dynamicSpr;
                            return dynamicSpr;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditableScreen] Error searching for icon sprite '{code}': {ex.Message}");
            }
            return null;
        }

        // --- IAdditionalDataForBuilding Save/Load Integration ---

        public string ProvideForSaveGame()
        {
            var data = new ScreenSaveData { text = _screenText };
            return JsonConvert.SerializeObject(data, Formatting.None, SaveService.JsonSerializerSettings);
        }

        public void ApplyFromSaveGame(string additionalData)
        {
            if (string.IsNullOrWhiteSpace(additionalData)) return;

            try
            {
                var data = JsonConvert.DeserializeObject<ScreenSaveData>(additionalData, SaveService.JsonSerializerSettings);
                if (data != null && !string.IsNullOrEmpty(data.text))
                {
                    _screenText = data.text;
                    UpdateVisualText();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScreenMod] Failed to deserialize save data: {ex.Message}");
            }
        }

        public void ApplyForBuildingPreview(string additionalData)
        {
            ApplyFromSaveGame(additionalData);
        }

        [Serializable]
        public class ScreenSaveData
        {
            public string text;
        }
    }
}
