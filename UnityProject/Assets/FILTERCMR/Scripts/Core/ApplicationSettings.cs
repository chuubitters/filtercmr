// FILTER CMR — all tunable settings in one ScriptableObject asset
// (Assets/FILTERCMR/Settings/FilterCMRSettings.asset, created by FILTER CMR > Setup Main Scene).
//
// Runtime code never writes to this asset (changes made in Play Mode to a
// ScriptableObject persist in the Editor). Runtime toggles live on the components.

using UnityEngine;

namespace FilterCMR.Core
{
    public enum CameraSourceMode
    {
        /// <summary>Use Unity's WebCamTexture; if it delivers no frames, show the Python video stream if one is arriving.</summary>
        Auto = 0,
        /// <summary>Only Unity's WebCamTexture.</summary>
        UnityWebcam,
        /// <summary>Only frames forwarded by the tracker (./run.sh --stream-video). Unity never opens the camera.</summary>
        PythonStream,
    }

    [CreateAssetMenu(menuName = "FILTER CMR/Application Settings", fileName = "FilterCMRSettings")]
    public sealed class ApplicationSettings : ScriptableObject
    {
        [Header("Tracking (UDP from Python)")]
        [Tooltip("Must match the tracker's --udp-port (default 5065).")]
        public int trackingPort = 5065;
        [Tooltip("Listen on all network interfaces instead of 127.0.0.1 only.")]
        public bool listenOnAllInterfaces = false;
        [Tooltip("Seconds without any packet before the tracker is considered disconnected.")]
        [Min(0.2f)] public float trackerTimeoutSeconds = 1.0f;

        [Header("Camera")]
        public CameraSourceMode cameraSource = CameraSourceMode.Auto;
        [Tooltip("Part of the webcam name to prefer (case-insensitive). Empty = first camera.")]
        public string preferredCameraName = "";
        [Tooltip("Request the same size as the tracker's --capture-width/--capture-height so both see the same field of view.")]
        public int requestedWidth = 1280;
        public int requestedHeight = 720;
        public int requestedFps = 30;
        [Tooltip("Seconds to wait for the first webcam frame before reporting an error.")]
        [Min(1f)] public float cameraStartTimeoutSeconds = 6f;
        [Tooltip("Seconds without a new webcam frame before the camera is considered stalled.")]
        [Min(0.5f)] public float cameraStallSeconds = 2f;
        [Tooltip("Seconds between automatic recovery attempts after a camera error.")]
        [Min(1f)] public float cameraRetrySeconds = 4f;
        [Tooltip("Extra horizontal flip of the video, applied on top of the tracker's mirroring. Use if landmarks appear mirrored relative to the image.")]
        public bool flipDisplayX = false;
        [Tooltip("Fill the screen (crop) instead of fitting the whole image (letterbox).")]
        public bool fillScreen = false;

        [Header("Video forwarding fallback (Python --stream-video)")]
        public int videoStreamPort = 5066;
        [Min(0.2f)] public float videoStreamTimeoutSeconds = 1.0f;

        [Header("Landmarks")]
        public bool showLandmarks = true;
        [Min(0.5f)] public float landmarkRadius = 5f;
        [Min(0.5f)] public float fingertipRadius = 10f;
        [Min(0.5f)] public float connectionWidth = 3f;
        public Color leftHandColor = Palette.Cyan;
        public Color rightHandColor = Palette.ElectricBlueBright;
        [Range(0f, 1f)] public float heldHandAlpha = 0.35f;
        [Tooltip("Display smoothing rate (1/s) between 30 Hz tracking packets and the render frame rate. 0 = off. Python already smooths with a One Euro Filter.")]
        [Min(0f)] public float displaySmoothing = 30f;

        [Header("Gestures")]
        [Tooltip("Fingers that form the FRAME gesture and its four corners. Index + Thumb = an \"L\" with each hand (default). Index + Middle = peace signs.")]
        public FilterCMR.Gestures.FrameFingers frameFingers = FilterCMR.Gestures.FrameFingers.IndexAndThumb;
        [Tooltip("Touch thumb tip to index tip (either hand) to toggle the halftone effect.")]
        public bool pinchTogglesHalftone = true;
        [Tooltip("How long the pinch must be held before it toggles.")]
        [Min(0f)] public float pinchHoldSeconds = 0.12f;
        [Tooltip("After a toggle, both hands must stop pinching this long before the next toggle.")]
        [Min(0f)] public float pinchRearmSeconds = 0.3f;
        [Tooltip("Both hands must show the pose this long before the gesture activates.")]
        [Min(0f)] public float gestureConfirmSeconds = 0.2f;
        [Tooltip("Grace period before an active gesture ends after the pose is lost.")]
        [Min(0f)] public float gestureReleaseSeconds = 0.25f;

        [Header("UI")]
        public bool showDebugPanel = true;
        [Tooltip("Show the visual settings panel (S) at start.")]
        public bool showSettingsPanel = true;
        [Tooltip("Start in presentation mode (all panels hidden, P to leave).")]
        public bool startInPresentationMode = false;

        static ApplicationSettings _fallback;

        /// <summary>Default settings used when a component has no asset assigned.</summary>
        public static ApplicationSettings Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = CreateInstance<ApplicationSettings>();
                    _fallback.name = "FilterCMRSettings (defaults)";
                    _fallback.hideFlags = HideFlags.DontSave;
                }
                return _fallback;
            }
        }
    }
}
