// FILTER CMR — receives camera frames forwarded by Python (./run.sh --stream-video)
// on port 5066 and decodes them into a Texture2D. Only used as a fallback when
// Unity cannot open the webcam itself (see Documentation/ARCHITECTURE.md).

using FilterCMR.Core;
using UnityEngine;

namespace FilterCMR.Networking
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class VideoStreamReceiver : MonoBehaviour
    {
        [SerializeField] ApplicationSettings settings;

        readonly VideoReceiverCore _core = new VideoReceiverCore();
        Texture2D _texture;
        byte[] _jpeg;
        int _decoded;
        float _rateTime;
        int _rateFrames;
        float _fps;

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }

        /// <summary>Last decoded frame (raw, un-mirrored), or null before the first frame.</summary>
        public Texture2D Texture => _decoded > 0 ? _texture : null;
        public bool Listening => _core.State == ReceiverState.Listening;
        public bool IsLive => _decoded > 0 && _core.SecondsSinceLastFrame < Settings.videoStreamTimeoutSeconds;
        public ReceiverState State => _core.State;
        public string LastError => _core.LastError;
        public int Port => Settings.videoStreamPort;
        public long FramesReceived => _core.FramesReceived;
        public long DroppedFrames => _core.DroppedFrames;
        public float FramesPerSecond => _fps;
        public int Width => _texture != null ? _texture.width : 0;
        public int Height => _texture != null ? _texture.height : 0;

        void OnEnable()
        {
            // In UnityWebcam mode the stream is never needed.
            if (Settings.cameraSource == CameraSourceMode.UnityWebcam) return;
            StartReceiver();
        }

        void OnDisable() => _core.Stop();
        void OnApplicationQuit() => _core.Stop();

        void OnDestroy()
        {
            _core.Stop();
            if (_texture != null) Destroy(_texture);
        }

        public bool StartReceiver()
        {
            bool ok = _core.Start(Settings.videoStreamPort,
                Settings.listenOnAllInterfaces ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback);
            if (!ok) Debug.LogWarning("[FILTER CMR] " + _core.LastError);
            return ok;
        }

        public void Restart()
        {
            _core.Stop();
            if (Settings.cameraSource != CameraSourceMode.UnityWebcam) StartReceiver();
        }

        void Update()
        {
            if (_core.TryGetLatest(ref _jpeg, out int length, out _, out _) && length > 0)
            {
                if (_texture == null)
                {
                    _texture = new Texture2D(2, 2, TextureFormat.RGB24, false)
                    {
                        name = "FilterCMR Python Stream",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                    };
                }
                // LoadImage resizes the texture to the JPEG's size and uploads it.
                if (_texture.LoadImage(_jpeg, false)) { _decoded++; _rateFrames++; }
            }
            float now = Time.unscaledTime;
            if (now - _rateTime >= 1f)
            {
                _fps = _rateFrames / (now - _rateTime);
                _rateFrames = 0;
                _rateTime = now;
            }
        }
    }
}
