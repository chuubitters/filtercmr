// FILTER CMR — opens the Mac webcam with WebCamTexture and keeps it alive.
//
// * Requests camera authorization, picks a device (preferred name or first), plays it.
// * Reports a clear status: no devices, permission denied, no frames (camera
//   busy — e.g. held by another app), stalled, disconnected.
// * Recovers automatically: stalled -> restart; error -> retry every N seconds.
// * CycleCamera() switches to the next device (C key).
//
// Not used when Camera Source = PythonStream (Unity then never opens the camera).

using System.Collections;
using FilterCMR.Core;
using UnityEngine;

namespace FilterCMR.CameraInput
{
    public enum WebcamStatus
    {
        Idle = 0,
        /// <summary>Camera Source is PythonStream; Unity does not open the camera.</summary>
        Disabled,
        RequestingPermission,
        PermissionDenied,
        NoDevices,
        Starting,
        Running,
        /// <summary>Was running, but no new frames for cameraStallSeconds.</summary>
        Stalled,
        Error,
    }

    [DefaultExecutionOrder(-180)]
    [DisallowMultipleComponent]
    public sealed class WebcamController : MonoBehaviour
    {
        [SerializeField] ApplicationSettings settings;

        WebCamTexture _texture;
        WebCamDevice[] _devices = new WebCamDevice[0];
        int _deviceIndex = -1;
        Coroutine _startRoutine;
        float _lastFrameTime;
        float _nextRetryTime;
        int _framesThisSecond;
        float _fpsTime;

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }

        public WebcamStatus Status { get; private set; } = WebcamStatus.Idle;
        public string StatusMessage { get; private set; } = "";
        public string DeviceName { get; private set; } = "";
        public int DeviceCount => _devices.Length;
        public int DeviceIndex => _deviceIndex;
        public float FramesPerSecond { get; private set; }

        /// <summary>The live texture while Running/Stalled, else null.</summary>
        public Texture Texture => (Status == WebcamStatus.Running || Status == WebcamStatus.Stalled) ? _texture : null;
        public int Width => _texture != null ? _texture.width : 0;
        public int Height => _texture != null ? _texture.height : 0;
        public int RotationAngle => _texture != null ? _texture.videoRotationAngle : 0;
        public bool VerticallyMirrored => _texture != null && _texture.videoVerticallyMirrored;
        public bool IsRunning => Status == WebcamStatus.Running;

        /// <summary>
        /// Automatic retries after an error. WebcamDisplay turns this off in Auto mode while the Python
        /// video stream is live, so Unity stops competing with the tracker for the camera.
        /// </summary>
        public bool RetryEnabled { get; set; } = true;

        void OnEnable()
        {
            if (Settings.cameraSource == CameraSourceMode.PythonStream)
            {
                SetStatus(WebcamStatus.Disabled, "Camera Source = Python Stream: Unity does not open the webcam.");
                return;
            }
            Begin(-1);
        }

        void OnDisable() => StopCamera();

        void OnDestroy() => StopCamera();

        /// <summary>Restart the current (or preferred) camera.</summary>
        public void Restart() => Begin(_deviceIndex);

        /// <summary>Switch to the next available camera (C key).</summary>
        public void CycleCamera()
        {
            if (Settings.cameraSource == CameraSourceMode.PythonStream) return;
            _devices = WebCamTexture.devices;
            if (_devices.Length == 0) { Begin(-1); return; }
            int next = (_deviceIndex + 1) % _devices.Length;
            Debug.Log($"[FILTER CMR] switching camera to #{next}: {_devices[next].name}");
            Begin(next);
        }

        void Begin(int index)
        {
            if (_startRoutine != null) StopCoroutine(_startRoutine);
            StopCamera();
            _startRoutine = StartCoroutine(StartRoutine(index));
        }

        IEnumerator StartRoutine(int requestedIndex)
        {
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                SetStatus(WebcamStatus.RequestingPermission, "Requesting camera permission…");
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                SetStatus(WebcamStatus.PermissionDenied,
                    "Camera permission denied. macOS: System Settings > Privacy & Security > Camera > enable Unity (or the built app), then restart it.");
                ScheduleRetry();
                yield break;
            }

            _devices = WebCamTexture.devices;
            if (_devices.Length == 0)
            {
                SetStatus(WebcamStatus.NoDevices, "No camera found. Connect a webcam (retrying automatically).");
                ScheduleRetry();
                yield break;
            }

            int index = requestedIndex >= 0 && requestedIndex < _devices.Length ? requestedIndex : PreferredIndex();
            _deviceIndex = index;
            DeviceName = _devices[index].name;

            var s = Settings;
            _texture = new WebCamTexture(DeviceName, s.requestedWidth, s.requestedHeight, s.requestedFps)
            {
                name = "FilterCMR Webcam",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            SetStatus(WebcamStatus.Starting, $"Opening \"{DeviceName}\"…");
            _texture.Play();

            float deadline = Time.unscaledTime + s.cameraStartTimeoutSeconds;
            // WebCamTexture reports a 16x16 placeholder size until the first real frame arrives.
            while (_texture != null && (_texture.width <= 16 || !_texture.isPlaying) && Time.unscaledTime < deadline)
                yield return null;

            if (_texture == null) yield break; // stopped meanwhile
            if (_texture.width <= 16)
            {
                SetStatus(WebcamStatus.Error,
                    $"\"{DeviceName}\" opened but sent no frames. It may be in use by another app (e.g. the Python tracker). " +
                    "Try Camera Source = Python Stream and start the tracker with ./run.sh --stream-video.");
                StopCamera(keepStatus: true);
                ScheduleRetry();
                yield break;
            }

            _lastFrameTime = Time.unscaledTime;
            SetStatus(WebcamStatus.Running, $"{DeviceName} · {_texture.width}×{_texture.height}");
            Debug.Log($"[FILTER CMR] webcam running: {DeviceName} {_texture.width}x{_texture.height} " +
                      $"(rotation {_texture.videoRotationAngle}, vflip {_texture.videoVerticallyMirrored})");
            _startRoutine = null;
        }

        int PreferredIndex()
        {
            string want = Settings.preferredCameraName;
            if (!string.IsNullOrEmpty(want))
            {
                for (int i = 0; i < _devices.Length; i++)
                    if (_devices[i].name.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return 0;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            switch (Status)
            {
                case WebcamStatus.Running:
                case WebcamStatus.Stalled:
                    if (_texture == null) break;
                    if (_texture.didUpdateThisFrame)
                    {
                        _lastFrameTime = now;
                        _framesThisSecond++;
                        if (Status == WebcamStatus.Stalled)
                            SetStatus(WebcamStatus.Running, $"{DeviceName} · {_texture.width}×{_texture.height}");
                    }
                    else if (Status == WebcamStatus.Running && now - _lastFrameTime > Settings.cameraStallSeconds)
                    {
                        SetStatus(WebcamStatus.Stalled, $"No frames from \"{DeviceName}\" for {Settings.cameraStallSeconds:0.#} s.");
                        _nextRetryTime = now + Settings.cameraRetrySeconds;
                    }
                    else if (Status == WebcamStatus.Stalled && now >= _nextRetryTime)
                    {
                        if (!DeviceStillPresent())
                        {
                            SetStatus(WebcamStatus.Error, $"\"{DeviceName}\" was disconnected. Reconnect it (retrying automatically).");
                            StopCamera(keepStatus: true);
                            ScheduleRetry();
                        }
                        else
                        {
                            Debug.LogWarning("[FILTER CMR] webcam stalled; restarting it");
                            Begin(_deviceIndex);
                        }
                    }
                    break;

                case WebcamStatus.Error:
                case WebcamStatus.NoDevices:
                case WebcamStatus.PermissionDenied:
                    if (RetryEnabled && now >= _nextRetryTime && _startRoutine == null) Begin(-1);
                    break;
            }

            if (now - _fpsTime >= 1f)
            {
                FramesPerSecond = _framesThisSecond / (now - _fpsTime);
                _framesThisSecond = 0;
                _fpsTime = now;
            }
        }

        bool DeviceStillPresent()
        {
            _devices = WebCamTexture.devices; // allocates; only called while stalled
            for (int i = 0; i < _devices.Length; i++)
                if (_devices[i].name == DeviceName) { _deviceIndex = i; return true; }
            return false;
        }

        void ScheduleRetry()
        {
            _nextRetryTime = Time.unscaledTime + Settings.cameraRetrySeconds;
            _startRoutine = null;
        }

        void SetStatus(WebcamStatus status, string message)
        {
            if (status != Status && (status == WebcamStatus.Error || status == WebcamStatus.NoDevices || status == WebcamStatus.PermissionDenied))
                Debug.LogWarning("[FILTER CMR] webcam: " + message);
            Status = status;
            StatusMessage = message;
        }

        void StopCamera(bool keepStatus = false)
        {
            if (_texture != null)
            {
                if (_texture.isPlaying) _texture.Stop();
                Destroy(_texture);
                _texture = null;
            }
            if (!keepStatus && Status != WebcamStatus.Disabled) SetStatus(WebcamStatus.Idle, "");
        }
    }
}
