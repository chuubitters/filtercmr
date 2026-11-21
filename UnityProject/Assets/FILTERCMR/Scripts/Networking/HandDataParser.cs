// FILTER CMR — zero-allocation parser for the tracker's UDP JSON (protocol v1).
//
// Why not JsonUtility? This parser:
//   * allocates nothing per packet (writes into a reused TrackingMessage),
//   * runs on the receiver's background thread,
//   * validates the message (version, 63 landmark floats, finite numbers),
//   * ignores unknown keys, so new optional fields don't break older builds,
//   * is pure C#, so it is tested outside Unity against real Python packets.
//
// It is a small, strict JSON reader specialised to the schema produced by
// HandTracking/udp_sender.py (see Documentation/UDP_PROTOCOL.md).

using System;
using System.Text;

namespace FilterCMR.Networking
{
    public sealed class HandDataParser
    {
        const int MaxDepth = 32;

        static readonly byte[] K_type = A("type"), K_v = A("v"), K_seq = A("seq"), K_t = A("t"),
            K_cam = A("cam"), K_trk = A("trk"), K_hands = A("hands"),
            K_status = A("status"), K_w = A("w"), K_h = A("h"), K_mirrored = A("mirrored"),
            K_fps = A("fps"), K_latency = A("latency"),
            K_id = A("id"), K_score = A("score"), K_state = A("state"), K_age = A("age"),
            K_ext = A("ext"), K_pose = A("pose"), K_lm = A("lm"), K_pinch = A("pinch");

        static readonly byte[] V_frame = A("frame"), V_heartbeat = A("heartbeat"), V_bye = A("bye"),
            V_Left = A("Left"), V_Right = A("Right"), V_tracked = A("tracked"), V_held = A("held"),
            V_point = A("point"), V_peace = A("peace"), V_pinch = A("pinch"), V_open = A("open"), V_fist = A("fist"), V_other = A("other"),
            V_starting = A("starting"), V_ok = A("ok"), V_reconnecting = A("reconnecting"), V_error = A("error"),
            V_restarting = A("restarting"), V_stopped = A("stopped");

        static byte[] A(string s) => Encoding.ASCII.GetBytes(s);

        // Scratch hand: hands are parsed here first because "id" may appear after "lm".
        readonly HandData _scratch = new HandData(Handedness.Left);
        byte[] _b;
        int _i, _end;

        /// <summary>Reason for the last failure (static strings only — no allocation).</summary>
        public string LastError { get; private set; } = "";
        /// <summary>Hands skipped in the last message (wrong landmark count, duplicate id, ...).</summary>
        public int LastSkippedHands { get; private set; }

        public bool TryParse(byte[] data, int length, TrackingMessage msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            msg.Reset();
            LastSkippedHands = 0;
            if (data == null || length <= 0 || length > data.Length) return Fail("empty datagram");
            _b = data;
            _i = 0;
            _end = length;

            // Tolerate a UTF-8 BOM.
            if (_end >= 3 && _b[0] == 0xEF && _b[1] == 0xBB && _b[2] == 0xBF) _i = 3;

            bool gotType = false, gotVersion = false;
            SkipWs();
            if (!Consume((byte)'{')) return Fail("expected '{'");
            SkipWs();
            if (!Consume((byte)'}'))
            {
                while (true)
                {
                    if (!ReadKey(out int ks, out int kl)) return Fail("malformed key");
                    if (Eq(ks, kl, K_type))
                    {
                        if (!ReadString(out int s, out int l)) return Fail("type: expected string");
                        msg.type = Eq(s, l, V_frame) ? MessageType.Frame
                                 : Eq(s, l, V_heartbeat) ? MessageType.Heartbeat
                                 : Eq(s, l, V_bye) ? MessageType.Bye : MessageType.Unknown;
                        gotType = true;
                    }
                    else if (Eq(ks, kl, K_v))
                    {
                        if (!ReadNumber(out double d)) return Fail("v: expected number");
                        msg.version = (int)d;
                        gotVersion = true;
                    }
                    else if (Eq(ks, kl, K_seq))
                    {
                        if (!ReadNumber(out double d)) return Fail("seq: expected number");
                        msg.seq = (long)d;
                    }
                    else if (Eq(ks, kl, K_t))
                    {
                        if (!ReadNumber(out double d)) return Fail("t: expected number");
                        msg.senderTimeMs = d;
                    }
                    else if (Eq(ks, kl, K_cam)) { if (!ParseCam(msg)) return false; }
                    else if (Eq(ks, kl, K_trk)) { if (!ParseTrk(msg)) return false; }
                    else if (Eq(ks, kl, K_hands)) { if (!ParseHands(msg)) return false; }
                    else if (!SkipValue(0)) return Fail("malformed value");

                    SkipWs();
                    if (Consume((byte)',')) continue;
                    if (Consume((byte)'}')) break;
                    return Fail("expected ',' or '}'");
                }
            }
            SkipWs();
            if (_i != _end) return Fail("trailing data");
            if (!gotType || msg.type == MessageType.Unknown) return Fail("missing or unknown type");
            if (!gotVersion) return Fail("missing version");
            if (msg.version != TrackingMessage.SupportedVersion) return Fail("unsupported protocol version");
            LastError = "";
            return true;
        }

        // ------------------------------------------------------------ sections
        bool ParseCam(TrackingMessage msg)
        {
            SkipWs();
            if (!Consume((byte)'{')) return Fail("cam: expected object");
            SkipWs();
            if (Consume((byte)'}')) return true;
            while (true)
            {
                if (!ReadKey(out int ks, out int kl)) return Fail("cam: malformed key");
                if (Eq(ks, kl, K_status))
                {
                    if (!ReadString(out int s, out int l)) return Fail("cam.status: expected string");
                    msg.cameraStatus = Eq(s, l, V_ok) ? CameraStatus.Ok
                        : Eq(s, l, V_starting) ? CameraStatus.Starting
                        : Eq(s, l, V_reconnecting) ? CameraStatus.Reconnecting
                        : Eq(s, l, V_error) ? CameraStatus.Error
                        : Eq(s, l, V_stopped) ? CameraStatus.Stopped : CameraStatus.Unknown;
                }
                else if (Eq(ks, kl, K_w)) { if (!ReadNumber(out double d)) return Fail("cam.w: expected number"); msg.frameWidth = (int)d; }
                else if (Eq(ks, kl, K_h)) { if (!ReadNumber(out double d)) return Fail("cam.h: expected number"); msg.frameHeight = (int)d; }
                else if (Eq(ks, kl, K_mirrored)) { if (!ReadBool(out bool b)) return Fail("cam.mirrored: expected bool"); msg.mirrored = b; }
                else if (!SkipValue(1)) return Fail("cam: malformed value");
                SkipWs();
                if (Consume((byte)',')) continue;
                if (Consume((byte)'}')) return true;
                return Fail("cam: expected ',' or '}'");
            }
        }

        bool ParseTrk(TrackingMessage msg)
        {
            SkipWs();
            if (!Consume((byte)'{')) return Fail("trk: expected object");
            SkipWs();
            if (Consume((byte)'}')) return true;
            while (true)
            {
                if (!ReadKey(out int ks, out int kl)) return Fail("trk: malformed key");
                if (Eq(ks, kl, K_status))
                {
                    if (!ReadString(out int s, out int l)) return Fail("trk.status: expected string");
                    msg.trackerStatus = Eq(s, l, V_ok) ? TrackerStatus.Ok
                        : Eq(s, l, V_starting) ? TrackerStatus.Starting
                        : Eq(s, l, V_error) ? TrackerStatus.Error
                        : Eq(s, l, V_restarting) ? TrackerStatus.Restarting
                        : Eq(s, l, V_stopped) ? TrackerStatus.Stopped : TrackerStatus.Unknown;
                }
                else if (Eq(ks, kl, K_fps)) { if (!ReadNumber(out double d)) return Fail("trk.fps: expected number"); msg.trackerFps = (float)d; }
                else if (Eq(ks, kl, K_latency)) { if (!ReadNumber(out double d)) return Fail("trk.latency: expected number"); msg.latencyMs = (float)d; }
                else if (!SkipValue(1)) return Fail("trk: malformed value");
                SkipWs();
                if (Consume((byte)',')) continue;
                if (Consume((byte)'}')) return true;
                return Fail("trk: expected ',' or '}'");
            }
        }

        bool ParseHands(TrackingMessage msg)
        {
            SkipWs();
            if (!Consume((byte)'[')) return Fail("hands: expected array");
            SkipWs();
            if (Consume((byte)']')) return true;
            while (true)
            {
                if (!ParseHand(out bool valid, out bool hasId)) return false;
                if (valid && hasId)
                {
                    HandData slot = msg.Get(_scratch.handedness);
                    if (!slot.present) { slot.CopyFrom(_scratch); slot.present = true; }
                    else LastSkippedHands++; // duplicate id: keep the first (Python never sends this)
                }
                else LastSkippedHands++;
                SkipWs();
                if (Consume((byte)',')) continue;
                if (Consume((byte)']')) return true;
                return Fail("hands: expected ',' or ']'");
            }
        }

        bool ParseHand(out bool valid, out bool hasId)
        {
            valid = true;
            hasId = false;
            HandData h = _scratch;
            h.Clear();
            int lmCount = 0;
            SkipWs();
            if (!Consume((byte)'{')) return Fail("hand: expected object");
            SkipWs();
            if (Consume((byte)'}')) { valid = false; return true; }
            while (true)
            {
                if (!ReadKey(out int ks, out int kl)) return Fail("hand: malformed key");
                if (Eq(ks, kl, K_id))
                {
                    if (!ReadString(out int s, out int l)) return Fail("hand.id: expected string");
                    if (Eq(s, l, V_Left)) { h.handedness = Handedness.Left; hasId = true; }
                    else if (Eq(s, l, V_Right)) { h.handedness = Handedness.Right; hasId = true; }
                    else valid = false;
                }
                else if (Eq(ks, kl, K_score)) { if (!ReadNumber(out double d)) return Fail("hand.score: expected number"); h.score = (float)d; }
                else if (Eq(ks, kl, K_age)) { if (!ReadNumber(out double d)) return Fail("hand.age: expected number"); h.ageMs = (float)d; }
                else if (Eq(ks, kl, K_pinch)) { if (!ReadNumber(out double d)) return Fail("hand.pinch: expected number"); h.pinch = (float)d; }
                else if (Eq(ks, kl, K_state))
                {
                    if (!ReadString(out int s, out int l)) return Fail("hand.state: expected string");
                    h.state = Eq(s, l, V_held) ? HandState.Held : HandState.Tracked;
                }
                else if (Eq(ks, kl, K_pose))
                {
                    if (!ReadString(out int s, out int l)) return Fail("hand.pose: expected string");
                    h.pose = Eq(s, l, V_point) ? HandPose.Point
                        : Eq(s, l, V_frame) ? HandPose.Frame          // "frame": thumb + index L shape
                        : Eq(s, l, V_pinch) ? HandPose.Pinch          // "pinch": thumb tip on index tip
                        : Eq(s, l, V_peace) ? HandPose.Peace
                        : Eq(s, l, V_open) ? HandPose.Open
                        : Eq(s, l, V_fist) ? HandPose.Fist
                        : Eq(s, l, V_other) ? HandPose.Other : HandPose.Unknown;
                }
                else if (Eq(ks, kl, K_ext))
                {
                    SkipWs();
                    if (!Consume((byte)'[')) return Fail("hand.ext: expected array");
                    int n = 0;
                    SkipWs();
                    if (!Consume((byte)']'))
                    {
                        while (true)
                        {
                            SkipWs();
                            if (!ReadNumber(out double d)) return Fail("hand.ext: expected number");
                            if (n < h.extended.Length) h.extended[n] = d != 0.0;
                            n++;
                            SkipWs();
                            if (Consume((byte)',')) continue;
                            if (Consume((byte)']')) break;
                            return Fail("hand.ext: expected ',' or ']'");
                        }
                    }
                }
                else if (Eq(ks, kl, K_lm))
                {
                    SkipWs();
                    if (!Consume((byte)'[')) return Fail("hand.lm: expected array");
                    SkipWs();
                    if (!Consume((byte)']'))
                    {
                        while (true)
                        {
                            SkipWs();
                            if (!ReadNumber(out double d)) return Fail("hand.lm: expected number");
                            if (lmCount < HandData.LandmarkFloats) h.landmarks[lmCount] = (float)d;
                            lmCount++;
                            SkipWs();
                            if (Consume((byte)',')) continue;
                            if (Consume((byte)']')) break;
                            return Fail("hand.lm: expected ',' or ']'");
                        }
                    }
                }
                else if (!SkipValue(2)) return Fail("hand: malformed value");
                SkipWs();
                if (Consume((byte)',')) continue;
                if (Consume((byte)'}')) break;
                return Fail("hand: expected ',' or '}'");
            }
            if (lmCount != HandData.LandmarkFloats) valid = false;
            return true;
        }

        // ------------------------------------------------------------- lexing
        bool Fail(string reason)
        {
            LastError = reason;
            return false;
        }

        byte Peek() => _i < _end ? _b[_i] : (byte)0;

        void SkipWs()
        {
            while (_i < _end)
            {
                byte c = _b[_i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                else break;
            }
        }

        bool Consume(byte c)
        {
            if (_i < _end && _b[_i] == c) { _i++; return true; }
            return false;
        }

        bool ReadKey(out int start, out int len)
        {
            SkipWs();
            if (!ReadString(out start, out len)) return false;
            SkipWs();
            if (!Consume((byte)':')) return false;
            SkipWs();
            return true;
        }

        /// <summary>Reads a JSON string; returns the raw (still-escaped) byte range between the quotes.</summary>
        bool ReadString(out int start, out int len)
        {
            start = len = 0;
            SkipWs();
            if (!Consume((byte)'"')) return false;
            start = _i;
            while (_i < _end)
            {
                byte c = _b[_i];
                if (c == '\\')
                {
                    _i += 2; // skip escaped char (\uXXXX's hex digits are plain chars)
                    continue;
                }
                if (c == '"')
                {
                    len = _i - start;
                    _i++;
                    return true;
                }
                if (c < 0x20) return false; // control characters are not allowed in JSON strings
                _i++;
            }
            return false;
        }

        bool ReadBool(out bool value)
        {
            value = false;
            SkipWs();
            if (Match("true")) { value = true; return true; }
            if (Match("false")) return true;
            return false;
        }

        bool Match(string literal)
        {
            if (_end - _i < literal.Length) return false;
            for (int k = 0; k < literal.Length; k++)
                if (_b[_i + k] != literal[k]) return false;
            _i += literal.Length;
            return true;
        }

        static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
            1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        /// <summary>Parses a JSON number (RFC 8259 grammar) without allocating.</summary>
        bool ReadNumber(out double value)
        {
            value = 0;
            SkipWs();
            int i = _i;
            bool neg = false;
            if (i < _end && _b[i] == '-') { neg = true; i++; }
            if (i >= _end) return false;

            double mant = 0;
            int digits = 0, exp10 = 0;
            if (_b[i] == '0') { i++; digits++; }
            else if (_b[i] >= '1' && _b[i] <= '9')
            {
                while (i < _end && _b[i] >= '0' && _b[i] <= '9')
                {
                    if (digits < 18) mant = mant * 10 + (_b[i] - '0'); else exp10++;
                    digits++;
                    i++;
                }
            }
            else return false;

            if (i < _end && _b[i] == '.')
            {
                i++;
                int frac = 0;
                while (i < _end && _b[i] >= '0' && _b[i] <= '9')
                {
                    if (digits < 18) { mant = mant * 10 + (_b[i] - '0'); exp10--; }
                    digits++;
                    frac++;
                    i++;
                }
                if (frac == 0) return false;
            }

            if (i < _end && (_b[i] == 'e' || _b[i] == 'E'))
            {
                i++;
                bool eneg = false;
                if (i < _end && (_b[i] == '+' || _b[i] == '-')) { eneg = _b[i] == '-'; i++; }
                int e = 0, ed = 0;
                while (i < _end && _b[i] >= '0' && _b[i] <= '9')
                {
                    if (e < 10000) e = e * 10 + (_b[i] - '0');
                    ed++;
                    i++;
                }
                if (ed == 0) return false;
                exp10 += eneg ? -e : e;
            }

            double v = mant;
            if (exp10 != 0)
            {
                int ae = exp10 < 0 ? -exp10 : exp10;
                double scale = ae < Pow10.Length ? Pow10[ae] : Math.Pow(10, ae);
                v = exp10 < 0 ? v / scale : v * scale;
            }
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            value = neg ? -v : v;
            _i = i;
            return true;
        }

        bool SkipValue(int depth)
        {
            if (depth > MaxDepth) return false;
            SkipWs();
            byte c = Peek();
            if (c == '"') return ReadString(out _, out _);
            if (c == '{')
            {
                _i++;
                SkipWs();
                if (Consume((byte)'}')) return true;
                while (true)
                {
                    if (!ReadKey(out _, out _)) return false;
                    if (!SkipValue(depth + 1)) return false;
                    SkipWs();
                    if (Consume((byte)',')) continue;
                    return Consume((byte)'}');
                }
            }
            if (c == '[')
            {
                _i++;
                SkipWs();
                if (Consume((byte)']')) return true;
                while (true)
                {
                    if (!SkipValue(depth + 1)) return false;
                    SkipWs();
                    if (Consume((byte)',')) continue;
                    return Consume((byte)']');
                }
            }
            if (c == 't') return Match("true");
            if (c == 'f') return Match("false");
            if (c == 'n') return Match("null");
            return ReadNumber(out _);
        }

        bool Eq(int start, int len, byte[] lit)
        {
            if (len != lit.Length) return false;
            for (int k = 0; k < len; k++)
                if (_b[start + k] != lit[k]) return false;
            return true;
        }
    }
}
