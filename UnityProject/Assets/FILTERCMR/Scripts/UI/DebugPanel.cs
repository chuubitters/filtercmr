// FILTER CMR — technical debug panel (D to toggle).
// Builds its own uGUI children (idempotent) and refreshes at 10 Hz from the
// live components via FilterCMRManager. Independent of the webcam display so
// it can be hidden without affecting anything else.

using System.Text;
using FilterCMR.CameraInput;
using FilterCMR.Core;
using FilterCMR.Gestures;
using FilterCMR.Mask;
using FilterCMR.Networking;
using FilterCMR.Rendering;
using FilterCMR.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class DebugPanel : MonoBehaviour
    {
        [SerializeField] FilterCMRManager manager;
        [SerializeField, Min(0.02f)] float refreshInterval = 0.1f;

        const float Width = 520f;
        const float LabelWidth = 118f;
        const float PanelHeight = 720f;

        static readonly string[] Labels =
        {
            "WEBCAM", "VIDEO SOURCE", "PY TRACKER", "PY CAMERA", "UDP :{0}", "VIDEO :{0}",
            "", "LEFT HAND", "RIGHT HAND", "GESTURE", "CANDIDATE", "",
            "MASK MODE", "MASK", "HALFTONE", "EFFECTS", "SHADER", "",
            "L INDEX TIP", "R INDEX TIP", "L MIDDLE TIP", "R MIDDLE TIP", "",
            "RENDER FPS", "LAST MESSAGE", "LANDMARKS", "MASK DEBUG",
        };

        Text _labels, _values, _warning;
        readonly StringBuilder _sb = new StringBuilder(2048);
        float _nextRefresh;
        float _fpsAccum;
        int _fpsFrames;
        float _fps;
        int _labelsPortTracking = -1, _labelsPortVideo = -1;

        public FilterCMRManager Manager { get => manager; set => manager = value; }

        void Awake() => Build();

        /// <summary>Creates (or finds) the panel's children. Safe to call repeatedly, also from the Editor.</summary>
        public void Build()
        {
            var rt = (RectTransform)transform;
            UIFactory.Anchor(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -72), new Vector2(Width, PanelHeight));

            var bg = UIFactory.GetOrAdd<Image>(gameObject);
            bg.color = Palette.WithAlpha(Palette.Navy, 0.9f);
            bg.raycastTarget = false;
            var outline = UIFactory.GetOrAdd<Outline>(gameObject);
            outline.effectColor = Palette.WithAlpha(Palette.ElectricBlue, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            Image header = UIFactory.CreateImage(transform, "Header", Palette.ElectricBlue);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = new Vector2(0, 26);
            Text title = UIFactory.CreateText(hrt, "Title", 13, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Stretch(title.rectTransform, 12, 12, 0, 0);
            title.text = "FILTER CMR  //  DEBUG";
            Text tag = UIFactory.CreateText(hrt, "Tag", 11, Palette.LightBlue, TextAnchor.MiddleRight);
            UIFactory.Stretch(tag.rectTransform, 12, 12, 0, 0);
            tag.text = "M2 · HAND TRACKING · [D] HIDE";

            _labels = UIFactory.CreateText(transform, "Labels", 13, Palette.WithAlpha(Palette.LightBlue, 0.55f), TextAnchor.UpperLeft);
            UIFactory.Stretch(_labels.rectTransform, 14, Width - 14 - LabelWidth, 38, 150);
            _labels.horizontalOverflow = HorizontalWrapMode.Overflow;

            _values = UIFactory.CreateText(transform, "Values", 13, Palette.LightBlue, TextAnchor.UpperLeft);
            UIFactory.Stretch(_values.rectTransform, 14 + LabelWidth, 12, 38, 150);
            _values.horizontalOverflow = HorizontalWrapMode.Overflow;

            _warning = UIFactory.CreateText(transform, "Warning", 12, Palette.Warning, TextAnchor.LowerLeft);
            UIFactory.Stretch(_warning.rectTransform, 14, 12, PanelHeight - 150, 50);

            Text footer = UIFactory.CreateText(transform, "Footer", 11, Palette.WithAlpha(Palette.LightBlue, 0.6f), TextAnchor.LowerLeft);
            UIFactory.Stretch(footer.rectTransform, 14, 12, PanelHeight - 46, 8);
            footer.text = "[D] debug  [R] restart UDP  [H] landmarks  [C] camera  [M] flip video  [V] mask debug\n" +
                          "[1] ribbon  [2] quad  [0] off  [T] halftone  [G] grain  [X] glitch  [ ] [ ] intensity  [S] settings  [P] present";

            RefreshLabels(5065, 5066);
            if (_values.text.Length == 0) _values.text = "<color=#5E7BA8>— enter Play Mode —</color>";
        }

        void RefreshLabels(int trackingPort, int videoPort)
        {
            if (trackingPort == _labelsPortTracking && videoPort == _labelsPortVideo) return;
            _labelsPortTracking = trackingPort;
            _labelsPortVideo = videoPort;
            _sb.Clear();
            for (int i = 0; i < Labels.Length; i++)
            {
                if (i > 0) _sb.Append('\n');
                string l = Labels[i];
                if (l.StartsWith("UDP")) _sb.AppendFormat(l, trackingPort);
                else if (l.StartsWith("VIDEO :")) _sb.AppendFormat(l, videoPort);
                else _sb.Append(l);
            }
            _labels.text = _sb.ToString();
        }

        void Update()
        {
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.5f)
            {
                _fps = _fpsFrames / _fpsAccum;
                _fpsAccum = 0;
                _fpsFrames = 0;
            }
            if (Time.unscaledTime < _nextRefresh || manager == null) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            Refresh();
        }

        // ---------------------------------------------------------------------------------
        void Refresh()
        {
            var m = manager;
            var s = m.Settings;
            RefreshLabels(s.trackingPort, s.videoStreamPort);
            _sb.Clear();

            // WEBCAM
            WebcamController cam = m.Webcam;
            if (cam == null) Err("missing component");
            else
            {
                switch (cam.Status)
                {
                    case WebcamStatus.Running: Ok("RUNNING"); break;
                    case WebcamStatus.Starting:
                    case WebcamStatus.RequestingPermission: Warn(cam.Status.ToString().ToUpperInvariant()); break;
                    case WebcamStatus.Disabled: Dim("OFF (python stream mode)"); break;
                    case WebcamStatus.Idle: Dim("IDLE"); break;
                    default: Err(cam.Status.ToString().ToUpperInvariant()); break;
                }
                if (cam.Status == WebcamStatus.Running || cam.Status == WebcamStatus.Stalled)
                    _sb.Append("  ").Append(cam.DeviceName).Append("  ").Append(cam.Width).Append('×').Append(cam.Height)
                       .Append("  ").Append(cam.FramesPerSecond.ToString("0")).Append(" fps");
                if (cam.DeviceCount > 1) _sb.Append("  [").Append(cam.DeviceIndex + 1).Append('/').Append(cam.DeviceCount).Append(']');
            }
            _sb.Append('\n');

            // VIDEO SOURCE
            WebcamDisplay disp = m.Display;
            _sb.Append(s.cameraSource.ToString().ToUpperInvariant()).Append(" → ");
            if (disp == null) Err("missing display");
            else
            {
                if (disp.ActiveSource == VideoSource.None) Err("NO VIDEO");
                else Ok(disp.ActiveSource == VideoSource.UnityWebcam ? "UNITY WEBCAM" : "PYTHON STREAM");
                _sb.Append(disp.IsMirrored ? "  mirrored" : "  not mirrored");
                if (disp.ExtraFlip) Warn("  +flip");
            }
            _sb.Append('\n');

            // PY TRACKER
            HandTrackingManager trk = m.Tracking;
            if (trk == null) Err("missing component");
            else
            {
                switch (trk.Connection)
                {
                    case TrackerConnection.Connected: Ok("CONNECTED"); break;
                    case TrackerConnection.Waiting: Warn("WAITING"); _sb.Append("  run: cd HandTracking && ./run.sh"); break;
                    case TrackerConnection.Lost: Err("DISCONNECTED"); break;
                    case TrackerConnection.Stopped: Err("STOPPED (bye)"); break;
                }
                if (trk.HasMessage && trk.Connection == TrackerConnection.Connected)
                {
                    _sb.Append("  ").Append(StatusName(trk.TrackerStatus)).Append("  ")
                       .Append(trk.Latest.trackerFps.ToString("0.0")).Append(" fps  ")
                       .Append(trk.Latest.latencyMs.ToString("0")).Append(" ms");
                }
            }
            _sb.Append('\n');

            // PY CAMERA
            if (trk != null && trk.HasMessage)
            {
                var cs = trk.TrackerCameraStatus;
                if (cs == Networking.CameraStatus.Ok) Ok("OK"); else Err(cs.ToString().ToUpperInvariant());
                _sb.Append("  ").Append(trk.TrackerFrameWidth).Append('×').Append(trk.TrackerFrameHeight)
                   .Append(trk.Mirrored ? "  mirrored" : "  not mirrored");
            }
            else Dim("—");
            _sb.Append('\n');

            // UDP
            UDPReceiver rx = m.Receiver;
            if (rx == null) Err("missing component");
            else
            {
                switch (rx.State)
                {
                    case ReceiverState.Listening: Ok("LISTENING"); break;
                    case ReceiverState.Stopped: Warn("STOPPED"); break;
                    default: Err("ERROR"); break;
                }
                _sb.Append("  ").Append(rx.PacketsPerSecond.ToString("0.0")).Append(" pkt/s  total ").Append(rx.PacketsReceived)
                   .Append("  bad ").Append(rx.MalformedPackets).Append("  stale ").Append(rx.StalePackets);
            }
            _sb.Append('\n');

            // VIDEO STREAM
            VideoStreamReceiver vs = m.VideoStream;
            if (vs == null || s.cameraSource == CameraSourceMode.UnityWebcam) Dim("OFF");
            else if (vs.State == ReceiverState.Error) Err("ERROR");
            else if (vs.IsLive) { Ok("LIVE"); _sb.Append("  ").Append(vs.FramesPerSecond.ToString("0")).Append(" fps  ").Append(vs.Width).Append('×').Append(vs.Height); }
            else Dim("listening (start tracker with --stream-video)");
            _sb.Append("\n\n");

            // HANDS
            HandLine(trk != null ? trk.Left : null);
            HandLine(trk != null ? trk.Right : null);

            // GESTURE
            GestureManager g = m.Gestures;
            if (g == null) { Err("missing component"); _sb.Append("\n\n\n"); } // gesture, candidate, blank
            else
            {
                if (g.CurrentGesture != GestureType.None) Ok(GestureManager.Label(g.CurrentGesture)); else Dim("NONE");
                _sb.Append("  ").Append(g.State.ToString().ToUpperInvariant()).Append('\n');
                _sb.Append(GestureManager.Label(g.Candidate));
                if (g.State == GestureState.Confirming)
                    _sb.Append("  confirming ").Append(Mathf.RoundToInt(g.Progress * 100)).Append('%');
                _sb.Append("\n\n");
            }

            // MASK MODE / MASK / HALFTONE / EFFECTS / SHADER
            MaskBlock(m);

            // FINGERTIPS
            Tip(g, Handedness.Left, HandLandmark.IndexTip);
            Tip(g, Handedness.Right, HandLandmark.IndexTip);
            Tip(g, Handedness.Left, HandLandmark.MiddleTip);
            Tip(g, Handedness.Right, HandLandmark.MiddleTip);
            _sb.Append('\n');

            // FPS
            if (_fps >= 50) Ok(_fps.ToString("0.0")); else if (_fps >= 30) Warn(_fps.ToString("0.0")); else Err(_fps.ToString("0.0"));
            _sb.Append("  (").Append((_fps > 0 ? 1000f / _fps : 0f).ToString("0.0")).Append(" ms)\n");

            // LAST MESSAGE
            if (trk == null || !trk.HasMessage) Dim("never");
            else
            {
                float ago = trk.SecondsSinceLastMessage;
                string t = ago.ToString("0.00") + " s ago";
                if (ago < 0.25f) Ok(t); else if (ago < s.trackerTimeoutSeconds) Warn(t); else Err(t);
                _sb.Append("  seq ").Append(trk.Latest.seq).Append("  ").Append(trk.Latest.type.ToString().ToLowerInvariant());
            }
            _sb.Append('\n');

            // LANDMARKS
            if (m.LandmarksVisible) Ok("VISIBLE"); else Dim("HIDDEN");
            _sb.Append('\n');

            // MASK DEBUG
            if (m.MaskDebugVisible) Warn("ON (magenta = mask, markers = control points)"); else Dim("OFF  [V]");

            _values.text = _sb.ToString();

            string warn = "";
            if (rx != null && rx.State == ReceiverState.Error) warn = rx.LastError;
            else if (disp != null && disp.AspectWarning.Length > 0) warn = disp.AspectWarning;
            else if (cam != null && disp != null && disp.ActiveSource == VideoSource.None && cam.StatusMessage.Length > 0) warn = cam.StatusMessage;
            if (_warning.text != warn) _warning.text = warn;
        }

        void MaskBlock(FilterCMRManager m)
        {
            InteractiveMaskManager mk = m.Masks;
            VisualEffectController fx = m.Effects;
            WebcamCompositeController comp = m.Composite;
            if (mk == null)
            {
                Err("not set up: FILTER CMR > Setup Visual Effects");
                _sb.Append("\n\n\n\n\n\n");
                return;
            }

            // MASK MODE
            if (mk.Mode == MaskMode.Off) Warn(MaskTransitions.Label(mk.Mode)); else Ok(MaskTransitions.Label(mk.Mode));
            _sb.Append('\n');

            // MASK
            float rw = mk.RibbonWeight, qw = mk.QuadWeight;
            if (rw <= 0f && qw <= 0f) Dim("none: normal webcam");
            else
            {
                if (rw > 0f) { Ok("RIBBON"); _sb.Append(' ').Append(Mathf.RoundToInt(rw * 100)).Append('%'); }
                if (rw > 0f && qw > 0f) _sb.Append("  ");
                if (qw > 0f)
                {
                    Ok("QUAD"); _sb.Append(' ').Append(Mathf.RoundToInt(qw * 100)).Append("%  ").Append(mk.Quad.VertexCount).Append(" pts");
                }
            }
            _sb.Append('\n');

            // HALFTONE
            if (fx == null) Err("missing VisualEffectController");
            else
            {
                if (fx.halftoneEnabled) Ok("ON"); else Dim("OFF");
                _sb.Append("  ").Append(Mathf.RoundToInt(fx.halftoneIntensity * 100)).Append("%  ")
                   .Append(Mathf.RoundToInt(fx.dotSpacing)).Append(" px  ").Append(fx.dotShape.ToString().ToLowerInvariant())
                   .Append("  ").Append(EffectValues.Label(fx.palette).ToLowerInvariant());
            }
            _sb.Append('\n');

            // EFFECTS
            if (fx != null)
            {
                Flag("RGB", fx.rgbSplitEnabled); Flag("GLITCH", fx.glitchEnabled); Flag("GRAIN", fx.grainEnabled);
                _sb.Append(" opacity ").Append(Mathf.RoundToInt(fx.opacity * 100)).Append('%');
            }
            _sb.Append('\n');

            // SHADER
            if (comp == null) Err("no WebcamCompositeController");
            else if (comp.IsActive) { Ok("OK"); _sb.Append("  image ").Append(Mathf.RoundToInt(comp.ImageHeightPixels)).Append(" px tall"); }
            else Err(comp.Status);
            _sb.Append("\n\n");
        }

        void Flag(string name, bool on)
        {
            if (on) Ok(name); else Dim(name.ToLowerInvariant());
            _sb.Append("  ");
        }

        void HandLine(HandView h)
        {
            if (h == null) { Err("missing"); _sb.Append('\n'); return; }
            switch (h.Status)
            {
                case HandStatus.Detected: Ok("DETECTED"); break;
                case HandStatus.Held: Warn("HELD"); break;
                default: Dim("LOST"); break;
            }
            if (h.Present)
            {
                _sb.Append("  pose ").Append(GestureManager.PoseName(h.Pose)).Append("  conf ").Append(h.Score.ToString("0.00"))
                   .Append("  [");
                for (int i = 0; i < h.Extended.Length; i++) _sb.Append(h.Extended[i] ? "TIMRP"[i] : '·');
                _sb.Append(']');
            }
            _sb.Append('\n');
        }

        void Tip(GestureManager g, Handedness hand, int lm)
        {
            if (g != null && g.TryGetFingertip(hand, lm, out Vector2 p))
                _sb.Append(p.x.ToString("0.000")).Append(", ").Append(p.y.ToString("0.000"));
            else Dim("—");
            _sb.Append('\n');
        }

        static string StatusName(TrackerStatus t) => t.ToString().ToLowerInvariant();

        void Ok(string s) => _sb.Append("<color=").Append(Palette.OkTag).Append('>').Append(s).Append("</color>");
        void Warn(string s) => _sb.Append("<color=").Append(Palette.WarnTag).Append('>').Append(s).Append("</color>");
        void Err(string s) => _sb.Append("<color=").Append(Palette.ErrorTag).Append('>').Append(s).Append("</color>");
        void Dim(string s) => _sb.Append("<color=").Append(Palette.DimTag).Append('>').Append(s).Append("</color>");
    }
}
