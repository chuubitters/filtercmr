// FILTER CMR — shows the camera image on a RawImage without distortion and in
// the same orientation as the tracker's coordinates.
//
// Alignment contract (see Documentation/UDP_PROTOCOL.md):
//   * Both the webcam texture and the Python stream deliver the RAW camera image.
//   * The tracker mirrors landmark x when cam.mirrored is true.
//   * So the image is flipped horizontally (uvRect) exactly when the tracker mirrors,
//     and landmarks can then be drawn in the image's rect with x,y used as-is.
//   * The AspectRatioFitter keeps the RawImage at the texture's aspect ratio, and
//     the landmark overlay is a child stretched over the RawImage, so landmark
//     (x, y) maps to the same pixel of the image at any window size.

using FilterCMR.Core;
using FilterCMR.Networking;
using FilterCMR.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.CameraInput
{
    public enum VideoSource { None = 0, UnityWebcam, PythonStream }

    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    [RequireComponent(typeof(AspectRatioFitter))]
    public sealed class WebcamDisplay : MonoBehaviour
    {
        [SerializeField] ApplicationSettings settings;
        [SerializeField] WebcamController webcam;
        [SerializeField] VideoStreamReceiver videoStream;
        [SerializeField] HandTrackingManager tracking;
        [Tooltip("Child RectTransform stretched over the image; landmark visualizers live here.")]
        [SerializeField] RectTransform overlay;

        RawImage _raw;
        AspectRatioFitter _fitter;
        bool _flipX;
        bool _flipInitialised;
        Rect _uv = new Rect(0, 0, 1, 1);
        float _aspect = 16f / 9f;
        string _aspectWarning = "";
        int _warnTrackW, _warnTrackH, _warnTexW, _warnTexH;

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }
        public RectTransform Overlay => overlay;
        public VideoSource ActiveSource { get; private set; }
        /// <summary>Whether the image is currently shown mirrored.</summary>
        public bool IsMirrored { get; private set; }
        /// <summary>Runtime extra flip (M key). Starts from settings.flipDisplayX.</summary>
        public bool ExtraFlip => _flipX;
        public float Aspect => _aspect;
        public int SourceWidth { get; private set; }
        public int SourceHeight { get; private set; }
        /// <summary>Non-empty when the video and the tracker's camera have different aspect ratios.</summary>
        public string AspectWarning => _aspectWarning;

        void Awake()
        {
            _raw = GetComponent<RawImage>();
            _fitter = GetComponent<AspectRatioFitter>();
            _raw.raycastTarget = false;
            _raw.texture = null;
            _raw.color = Color.clear;
        }

        void OnEnable()
        {
            if (!_flipInitialised) { _flipX = Settings.flipDisplayX; _flipInitialised = true; }
        }

        public void ToggleExtraFlip() => _flipX = !_flipX;

        void LateUpdate()
        {
            var s = Settings;
            bool webcamOk = webcam != null && webcam.Texture != null;
            bool streamOk = videoStream != null && videoStream.IsLive;

            VideoSource src;
            switch (s.cameraSource)
            {
                case CameraSourceMode.UnityWebcam: src = webcamOk ? VideoSource.UnityWebcam : VideoSource.None; break;
                case CameraSourceMode.PythonStream: src = streamOk ? VideoSource.PythonStream : VideoSource.None; break;
                default: src = webcamOk ? VideoSource.UnityWebcam : streamOk ? VideoSource.PythonStream : VideoSource.None; break;
            }
            ActiveSource = src;
            // In Auto mode, stop retrying the webcam while the Python stream works (avoid fighting over the camera).
            if (webcam != null) webcam.RetryEnabled = !(s.cameraSource == CameraSourceMode.Auto && streamOk && !webcamOk);

            Texture tex = src == VideoSource.UnityWebcam ? webcam.Texture
                        : src == VideoSource.PythonStream ? (Texture)videoStream.Texture : null;
            if (_raw.texture != tex) _raw.texture = tex;
            Color want = tex != null ? Color.white : Color.clear;
            if (_raw.color != want) _raw.color = want;

            // --- aspect ratio -------------------------------------------------
            int w = 0, h = 0, rotation = 0;
            bool vflip = false;
            if (src == VideoSource.UnityWebcam) { w = webcam.Width; h = webcam.Height; rotation = webcam.RotationAngle; vflip = webcam.VerticallyMirrored; }
            else if (src == VideoSource.PythonStream) { w = videoStream.Width; h = videoStream.Height; }
            SourceWidth = w;
            SourceHeight = h;

            float aspect;
            if (w > 16 && h > 16)
            {
                bool sideways = rotation == 90 || rotation == 270;
                aspect = sideways ? (float)h / w : (float)w / h;
            }
            else if (tracking != null && tracking.TrackerFrameWidth > 0 && tracking.TrackerFrameHeight > 0)
            {
                // No video yet: keep the overlay at the tracker camera's shape so landmarks still map correctly.
                aspect = (float)tracking.TrackerFrameWidth / tracking.TrackerFrameHeight;
            }
            else aspect = (float)s.requestedWidth / Mathf.Max(1, s.requestedHeight);

            if (Mathf.Abs(aspect - _aspect) > 1e-4f || Mathf.Abs(_fitter.aspectRatio - aspect) > 1e-4f)
            {
                _aspect = aspect;
                _fitter.aspectRatio = aspect;
            }
            var mode = s.fillScreen ? AspectRatioFitter.AspectMode.EnvelopeParent : AspectRatioFitter.AspectMode.FitInParent;
            if (_fitter.aspectMode != mode) _fitter.aspectMode = mode;

            // --- orientation ------------------------------------------------------
            bool trackerMirrors = tracking == null || tracking.Mirrored;
            IsMirrored = trackerMirrors ^ _flipX;
            var uv = new Rect(IsMirrored ? 1f : 0f, vflip ? 1f : 0f, IsMirrored ? -1f : 1f, vflip ? -1f : 1f);
            if (uv != _uv) { _uv = uv; _raw.uvRect = uv; }
            float z = src == VideoSource.UnityWebcam ? -rotation : 0f;
            if (!Mathf.Approximately(transform.localEulerAngles.z, Mathf.Repeat(z, 360f)))
                transform.localEulerAngles = new Vector3(0f, 0f, z);

            UpdateAspectWarning(w, h);
        }

        void UpdateAspectWarning(int texW, int texH)
        {
            int tw = tracking != null ? tracking.TrackerFrameWidth : 0;
            int th = tracking != null ? tracking.TrackerFrameHeight : 0;
            if (tw == _warnTrackW && th == _warnTrackH && texW == _warnTexW && texH == _warnTexH) return;
            _warnTrackW = tw; _warnTrackH = th; _warnTexW = texW; _warnTexH = texH;
            _aspectWarning = "";
            if (tw <= 0 || th <= 0 || texW <= 16 || texH <= 16) return;
            float a = (float)tw / th, b = (float)texW / texH;
            if (Mathf.Abs(a - b) / a > 0.02f)
            {
                _aspectWarning = $"Aspect mismatch: tracker {tw}×{th} vs video {texW}×{texH}. " +
                                 "Landmarks will be offset; request the same resolution in both.";
                Debug.LogWarning("[FILTER CMR] " + _aspectWarning);
            }
        }
    }
}
