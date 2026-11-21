// FILTER CMR — scene root: holds references to every system, distributes the
// settings asset, and handles keyboard shortcuts.
//
//   D  toggle debug panel          H  toggle landmark visibility
//   R  restart UDP receiver         C  cycle cameras (Unity webcam mode)
//   M  flip video horizontally (alignment aid)

using FilterCMR.CameraInput;
using FilterCMR.Gestures;
using FilterCMR.Networking;
using FilterCMR.Tracking;
using FilterCMR.UI;
using UnityEngine;

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

        bool _landmarksVisible = true;
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

            SetLandmarksVisible(s.showLandmarks);
            if (debugPanel != null) debugPanel.gameObject.SetActive(s.showDebugPanel);
        }

        void ReportMissing(Object o, string field)
        {
            if (o == null) Debug.LogError($"[FILTER CMR] FilterCMRManager.{field} is not assigned. Run FILTER CMR > Setup Main Scene.", this);
        }

        void Update()
        {
            if (KeyDown(KeyCode.D)) ToggleDebugPanel();
            if (KeyDown(KeyCode.R)) RestartReceiver();
            if (KeyDown(KeyCode.H)) SetLandmarksVisible(!_landmarksVisible);
            if (KeyDown(KeyCode.C)) CycleCamera();
            if (KeyDown(KeyCode.M) && display != null) display.ToggleExtraFlip();
        }

        public void ToggleDebugPanel()
        {
            if (debugPanel != null) debugPanel.gameObject.SetActive(!debugPanel.gameObject.activeSelf);
        }

        /// <summary>R: restart UDP (and video) receivers and clear tracking + gesture state.</summary>
        public void RestartReceiver()
        {
            Debug.Log("[FILTER CMR] restarting UDP receiver");
            if (tracking != null) tracking.ResetState();
            if (gestures != null) gestures.ResetGesture();
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
