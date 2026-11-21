// FILTER CMR — scene root: holds references to every system, distributes the
// settings asset, and handles keyboard shortcuts.
//
//   D  toggle debug panel          H  toggle landmark visibility
//   R  restart UDP receiver         C  cycle cameras (Unity webcam mode)
//   M  flip video horizontally (alignment aid)
//   1  ribbon mask mode             2  quad mask mode         0  mask off
//      (pressing the active mode key again returns to AUTO = gesture-driven)
//   T  halftone on/off              G  grain on/off           X  glitch on/off
//   [  ]  halftone intensity −/+    V  mask debug view        S  settings panel
//   P  presentation mode (hide all panels; P or the corner dot to leave)
// All shortcuts are handled here only (one input listener).

using FilterCMR.CameraInput;
using FilterCMR.Gestures;
using FilterCMR.Mask;
using FilterCMR.Networking;
using FilterCMR.Rendering;
using FilterCMR.Tracking;
using FilterCMR.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR
{
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class FilterCMRManager : MonoBehaviour
    {
        [SerializeField] Core.ApplicationSettings settings;

        [Header("Systems")]
        [SerializeField] UDPReceiver receiver;
        [SerializeField] VideoStreamReceiver videoStream;
        [SerializeField] WebcamController webcam;
        [SerializeField] HandTrackingManager tracking;
        [SerializeField] GestureManager gestures;

        [Header("Display")]
        [SerializeField] WebcamDisplay display;
        [SerializeField] HandLandmarkVisualizer landmarkVisualizer;
        [SerializeField] HandConnectionVisualizer connectionVisualizer;

        [Header("UI")]
        [SerializeField] DebugPanel debugPanel;
        [SerializeField] TrackingStatusUI statusUI;

        [Header("Masks & effects (Milestone 3/4)")]
        [SerializeField] InteractiveMaskManager masks;
        [SerializeField] VisualEffectController effects;
        [SerializeField] WebcamCompositeController composite;
        [SerializeField] MaskDebugVisualizer maskDebug;
        [SerializeField] VisualSettingsPanel settingsPanel;
        [Tooltip("Small corner button shown only in presentation mode, to leave it without a keyboard.")]
        [SerializeField] Button presentationExit;

        bool _landmarksVisible = true;
        bool _presentation;
        bool _debugBeforePresentation, _settingsBeforePresentation, _landmarksBeforePresentation;
        static bool _legacyInputUnavailable;

        public Core.ApplicationSettings Settings => settings != null ? settings : Core.ApplicationSettings.Fallback;
        public UDPReceiver Receiver => receiver;
        public VideoStreamReceiver VideoStream => videoStream;
        public WebcamController Webcam => webcam;
        public HandTrackingManager Tracking => tracking;
        public GestureManager Gestures => gestures;
        public WebcamDisplay Display => display;
        public TrackingStatusUI StatusUI => statusUI;
        public DebugPanel DebugPanel => debugPanel;
        public bool LandmarksVisible => _landmarksVisible;
        public bool DebugVisible => debugPanel != null && debugPanel.gameObject.activeSelf;
        public InteractiveMaskManager Masks => masks;
        public VisualEffectController Effects => effects;
        public WebcamCompositeController Composite => composite;
        public bool SettingsVisible => settingsPanel != null && settingsPanel.gameObject.activeSelf;
        public bool PresentationMode => _presentation;
        public bool MaskDebugVisible => effects != null && effects.debugMask;

        void Awake()
        {
            // Keep receiving and rendering when the window loses focus (e.g. while using the tracker terminal).
            Application.runInBackground = true;

            var s = Settings;
            if (settings == null)
                Debug.LogWarning("[FILTER CMR] No settings asset assigned; using defaults. Run FILTER CMR > Setup Main Scene.");

            // Hand every component the same settings asset.
            if (receiver != null) receiver.Settings = s;
            if (videoStream != null) videoStream.Settings = s;
            if (webcam != null) webcam.Settings = s;
            if (tracking != null) tracking.Settings = s;
            if (gestures != null) gestures.Settings = s;
            if (display != null) display.Settings = s;
            if (landmarkVisualizer != null) landmarkVisualizer.Settings = s;
            if (connectionVisualizer != null) connectionVisualizer.Settings = s;

            ReportMissing(receiver, nameof(receiver));
            ReportMissing(webcam, nameof(webcam));
            ReportMissing(tracking, nameof(tracking));
            ReportMissing(gestures, nameof(gestures));
            ReportMissing(display, nameof(display));
            ReportMissing(landmarkVisualizer, nameof(landmarkVisualizer));
            ReportMissing(connectionVisualizer, nameof(connectionVisualizer));

            ReportMissing(masks, nameof(masks));
            ReportMissing(effects, nameof(effects));
            ReportMissing(composite, nameof(composite));

            SetLandmarksVisible(s.showLandmarks);
            if (debugPanel != null) debugPanel.gameObject.SetActive(s.showDebugPanel);
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(s.showSettingsPanel);
            if (maskDebug != null) maskDebug.enabled = effects != null && effects.debugMask;
            if (presentationExit != null)
            {
                presentationExit.gameObject.SetActive(false);
                presentationExit.onClick.AddListener(() => SetPresentation(false));
            }
            if (s.startInPresentationMode) SetPresentation(true);
        }

        void ReportMissing(Object o, string field)
        {
            if (o == null) Debug.LogError($"[FILTER CMR] FilterCMRManager.{field} is not assigned. Run FILTER CMR > Setup Visual Effects (it also repairs the main scene).", this);
        }

        void Update()
        {
            if (KeyDown(KeyCode.D)) ToggleDebugPanel();
            if (KeyDown(KeyCode.R)) RestartReceiver();
            if (KeyDown(KeyCode.H)) SetLandmarksVisible(!_landmarksVisible);
            if (KeyDown(KeyCode.C)) CycleCamera();
            if (KeyDown(KeyCode.M) && display != null) display.ToggleExtraFlip();

            if (masks != null)
            {
                if (KeyDown(KeyCode.Alpha1) || KeyDown(KeyCode.Keypad1)) masks.SelectOrToggle(MaskMode.Ribbon);
                if (KeyDown(KeyCode.Alpha2) || KeyDown(KeyCode.Keypad2)) masks.SelectOrToggle(MaskMode.Quad);
                if (KeyDown(KeyCode.Alpha0) || KeyDown(KeyCode.Keypad0)) masks.SelectOrToggle(MaskMode.Off);
            }
            if (effects != null)
            {
                if (KeyDown(KeyCode.T)) effects.ToggleHalftone();
                if (KeyDown(KeyCode.G)) effects.ToggleGrain();
                if (KeyDown(KeyCode.X)) effects.ToggleGlitch();
                if (KeyDown(KeyCode.LeftBracket)) effects.AdjustHalftoneIntensity(-0.1f);
                if (KeyDown(KeyCode.RightBracket)) effects.AdjustHalftoneIntensity(0.1f);
            }
            if (KeyDown(KeyCode.V)) ToggleMaskDebug();
            if (KeyDown(KeyCode.S)) ToggleSettingsPanel();
            if (KeyDown(KeyCode.P)) SetPresentation(!_presentation);
        }

        public void ToggleMaskDebug()
        {
            if (effects == null) return;
            effects.ToggleDebugMask();
            if (maskDebug != null) maskDebug.enabled = effects.debugMask;
        }

        public void ToggleSettingsPanel()
        {
            if (_presentation) SetPresentation(false);
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(!settingsPanel.gameObject.activeSelf);
        }

        /// <summary>Presentation mode: hide every panel, the status bar, landmarks and mask debug.</summary>
        public void SetPresentation(bool on)
        {
            if (on == _presentation) return;
            _presentation = on;
            if (on)
            {
                _debugBeforePresentation = DebugVisible;
                _settingsBeforePresentation = SettingsVisible;
                _landmarksBeforePresentation = _landmarksVisible;
                if (debugPanel != null) debugPanel.gameObject.SetActive(false);
                if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
                if (statusUI != null) statusUI.gameObject.SetActive(false);
                if (maskDebug != null) maskDebug.enabled = false;
                SetLandmarksVisible(false);
            }
            else
            {
                if (debugPanel != null) debugPanel.gameObject.SetActive(_debugBeforePresentation);
                if (settingsPanel != null) settingsPanel.gameObject.SetActive(_settingsBeforePresentation);
                if (statusUI != null) statusUI.gameObject.SetActive(true);
                if (maskDebug != null) maskDebug.enabled = effects != null && effects.debugMask;
                SetLandmarksVisible(_landmarksBeforePresentation);
            }
            if (presentationExit != null) presentationExit.gameObject.SetActive(on);
            Debug.Log("[FILTER CMR] presentation mode " + (on ? "on (P or the corner dot to leave)" : "off"));
        }

        public void ToggleDebugPanel()
        {
            if (_presentation) SetPresentation(false);
            if (debugPanel != null) debugPanel.gameObject.SetActive(!debugPanel.gameObject.activeSelf);
        }

        /// <summary>R: restart UDP (and video) receivers and clear tracking + gesture state.</summary>
        public void RestartReceiver()
        {
            Debug.Log("[FILTER CMR] restarting UDP receiver");
            if (tracking != null) tracking.ResetState();
            if (gestures != null) gestures.ResetGesture();
            if (masks != null) masks.ResetMasks();
            if (receiver != null) receiver.Restart();
            if (videoStream != null) videoStream.Restart();
        }

        public void SetLandmarksVisible(bool visible)
        {
            _landmarksVisible = visible;
            if (landmarkVisualizer != null) landmarkVisualizer.enabled = visible;
            if (connectionVisualizer != null) connectionVisualizer.enabled = visible;
        }

        public void CycleCamera()
        {
            if (Settings.cameraSource == Core.CameraSourceMode.PythonStream)
            {
                Debug.Log("[FILTER CMR] C ignored: Camera Source is Python Stream (choose the camera with ./run.sh --camera-index N)");
                return;
            }
            if (webcam != null) webcam.CycleCamera();
        }

        static bool KeyDown(KeyCode key)
        {
            if (_legacyInputUnavailable) return false;
            try
            {
                return Input.GetKeyDown(key);
            }
            catch (System.InvalidOperationException)
            {
                // Player Settings > Active Input Handling is set to "Input System Package (New)" only.
                _legacyInputUnavailable = true;
                Debug.LogWarning("[FILTER CMR] Keyboard shortcuts need the legacy Input Manager. " +
                                 "Set Project Settings > Player > Active Input Handling to \"Input Manager (Old)\" or \"Both\".");
                return false;
            }
        }
    }
}
