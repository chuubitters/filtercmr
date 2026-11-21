// FILTER CMR — always-on minimal status layer: title bar with CAM / TRK / UDP
// indicators and FPS, the current gesture, and a centred hint ("RAISE BOTH HANDS",
// "START THE TRACKER", camera problems). Separate from the debug panel.

using System.Text;
using FilterCMR.CameraInput;
using FilterCMR.Core;
using FilterCMR.Gestures;
using FilterCMR.Mask;
using FilterCMR.Networking;
using FilterCMR.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class TrackingStatusUI : MonoBehaviour
    {
        [SerializeField] FilterCMRManager manager;

        Text _status, _gesture, _hint, _subHint;
        readonly StringBuilder _sb = new StringBuilder(256);
        float _next;
        float _fpsAccum;
        int _fpsFrames;
        float _fps;

        public FilterCMRManager Manager { get => manager; set => manager = value; }

        bool _bound;

        void Awake()
        {
            Build();
            if (_bound || !Application.isPlaying) return;
            _bound = true;
            Transform bar = transform.Find("Top Bar");
            bar.Find("Settings Button").GetComponent<Button>().onClick.AddListener(() => { if (manager != null) manager.ToggleSettingsPanel(); });
            bar.Find("Debug Button").GetComponent<Button>().onClick.AddListener(() => { if (manager != null) manager.ToggleDebugPanel(); });
            bar.Find("Present Button").GetComponent<Button>().onClick.AddListener(() => { if (manager != null) manager.SetPresentation(true); });
        }

        public void Build()
        {
            var rt = (RectTransform)transform;
            UIFactory.Stretch(rt);

            Image bar = UIFactory.CreateImage(transform, "Top Bar", Palette.WithAlpha(Palette.Navy, 0.78f));
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(0, 48);

            Image rule = UIFactory.CreateImage(brt, "Rule", Palette.ElectricBlue);
            var rrt = rule.rectTransform;
            rrt.anchorMin = new Vector2(0, 0); rrt.anchorMax = new Vector2(1, 0); rrt.pivot = new Vector2(0.5f, 0);
            rrt.anchoredPosition = Vector2.zero; rrt.sizeDelta = new Vector2(0, 1);

            Text title = UIFactory.CreateText(brt, "Title", 24, Palette.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Stretch(title.rectTransform, 24, 0, 0, 0);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.text = "FILTER CMR";

            Text sub = UIFactory.CreateText(brt, "Subtitle", 11, Palette.WithAlpha(Palette.LightBlue, 0.7f), TextAnchor.MiddleLeft);
            UIFactory.Stretch(sub.rectTransform, 182, 0, 0, 0);
            sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            sub.text = "// INTERACTIVE HALFTONE CAMERA";

            // Mouse access to the panels (the app stays usable without a keyboard).
            string[] names = { "Settings Button", "Debug Button", "Present Button" };
            string[] labels = { "SETTINGS", "DEBUG", "PRESENT" };
            for (int i = 0; i < 3; i++)
            {
                Button b = UIFactory.CreateButton(brt, names[i], labels[i], 11, out _);
                var r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f);
                r.pivot = new Vector2(0, 0.5f);
                r.anchoredPosition = new Vector2(420 + i * 84, 0);
                r.sizeDelta = new Vector2(78, 24);
            }

            _status = UIFactory.CreateText(brt, "Status", 13, Palette.LightBlue, TextAnchor.MiddleRight);
            UIFactory.Stretch(_status.rectTransform, 0, 24, 0, 0);
            _status.horizontalOverflow = HorizontalWrapMode.Overflow;

            _gesture = UIFactory.CreateText(transform, "Gesture", 20, Palette.Cyan, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Anchor(_gesture.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(900, 40));

            _hint = UIFactory.CreateText(transform, "Hint", 30, Palette.WithAlpha(Palette.Cyan, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1400, 60));
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;

            _subHint = UIFactory.CreateText(transform, "Sub Hint", 15, Palette.WithAlpha(Palette.LightBlue, 0.8f), TextAnchor.UpperCenter);
            UIFactory.Anchor(_subHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(1100, 80));
        }

        void Update()
        {
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.5f) { _fps = _fpsFrames / _fpsAccum; _fpsAccum = 0; _fpsFrames = 0; }
            if (manager == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            Refresh();
        }

        void Refresh()
        {
            var m = manager;
            WebcamDisplay disp = m.Display;
            WebcamController cam = m.Webcam;
            HandTrackingManager trk = m.Tracking;
            UDPReceiver rx = m.Receiver;
            GestureManager g = m.Gestures;

            bool videoOk = disp != null && disp.ActiveSource != VideoSource.None;
            bool trackerOk = trk != null && trk.Connection == TrackerConnection.Connected;
            bool udpOk = rx != null && rx.State == ReceiverState.Listening;

            _sb.Clear();
            Dot("CAM", videoOk ? Palette.OkTag : Palette.ErrorTag);
            Dot("TRK", trackerOk ? Palette.OkTag : (trk != null && trk.Connection == TrackerConnection.Waiting ? Palette.WarnTag : Palette.ErrorTag));
            Dot("UDP", udpOk ? Palette.OkTag : Palette.ErrorTag);
            InteractiveMaskManager mk = m.Masks;
            if (mk != null)
                _sb.Append("MASK <color=").Append(mk.Mode == MaskMode.Off ? Palette.WarnTag : Palette.OkTag).Append('>')
                   .Append(MaskTransitions.Label(mk.Mode)).Append("</color>    ");
            _sb.Append("<color=").Append(Palette.DimTag).Append(">").Append(_fps.ToString("0")).Append(" FPS</color>");
            Set(_status, _sb.ToString());

            // gesture
            string gesture = "";
            if (g != null)
            {
                if (g.CurrentGesture != GestureType.None) gesture = "◆ " + GestureManager.Label(g.CurrentGesture);
                else if (g.State == GestureState.Confirming)
                    gesture = "<color=" + Palette.DimTag + ">◇ " + GestureManager.Label(g.PendingGesture) + "</color>";
            }
            Set(_gesture, gesture);

            // hint
            string hint = "", sub = "";
            if (rx != null && rx.State == ReceiverState.Error) { hint = "UDP ERROR"; sub = rx.LastError; }
            else if (trk == null || trk.Connection == TrackerConnection.Waiting) { hint = "START THE TRACKER"; sub = "cd HandTracking && ./run.sh"; }
            else if (trk.Connection == TrackerConnection.Lost) { hint = "TRACKER DISCONNECTED"; sub = "waiting for packets on UDP " + m.Settings.trackingPort + "…"; }
            else if (trk.Connection == TrackerConnection.Stopped) { hint = "TRACKER STOPPED"; sub = "restart it with ./run.sh"; }
            else if (trk.TrackerCameraStatus != Networking.CameraStatus.Ok) { hint = "TRACKER CAMERA " + trk.TrackerCameraStatus.ToString().ToUpperInvariant(); sub = "see the tracker terminal for details"; }
            else if (trk.HandCount == 0) hint = "RAISE BOTH HANDS";
            else if (trk.HandCount == 1) hint = "RAISE YOUR OTHER HAND";
            else if (g != null && g.CurrentGesture == GestureType.None && m.Masks != null && m.Masks.Mode == MaskMode.Auto)
                sub = "<color=" + Palette.DimTag + ">POINT BOTH INDEX FINGERS FOR A RIBBON  ·  TWO PEACE SIGNS FOR A FRAME</color>";

            if (!videoOk && cam != null && cam.StatusMessage.Length > 0 && m.Settings.cameraSource != CameraSourceMode.PythonStream)
                sub = (sub.Length > 0 ? sub + "\n" : "") + "<color=" + Palette.WarnTag + ">CAMERA: " + cam.StatusMessage + "</color>";
            else if (!videoOk && m.Settings.cameraSource == CameraSourceMode.PythonStream)
                sub = (sub.Length > 0 ? sub + "\n" : "") + "<color=" + Palette.WarnTag + ">NO VIDEO STREAM: start the tracker with ./run.sh --stream-video</color>";

            Set(_hint, hint);
            Set(_subHint, sub);
        }

        void Dot(string label, string color)
        {
            _sb.Append(label).Append(" <color=").Append(color).Append(">●</color>    ");
        }

        static void Set(Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }
    }
}
