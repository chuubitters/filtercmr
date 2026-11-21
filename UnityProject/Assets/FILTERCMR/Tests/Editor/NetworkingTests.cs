// EditMode tests for the protocol parser and video chunk assembler.
// Fixtures below were produced by the real Python encoder (HandTracking/udp_sender.build_packet).
// Run in Unity: Window > General > Test Runner > EditMode > Run All.

using System.Text;
using FilterCMR.Networking;
using NUnit.Framework;

namespace FilterCMR.Tests
{
    [TestFixture]
    public class NetworkingTests
    {
        public const string TwoHands =
            "{\"type\":\"frame\",\"v\":1,\"seq\":1234,\"t\":10000.0,\"cam\":{\"status\":\"ok\",\"w\":1280,\"h\":720,\"mirrored\":true},\"trk\":{\"status\":\"ok\",\"fps\":29.9,\"latency\":14.2},\"hands\":[{\"id\":\"Left\",\"score\":0.97,\"state\":\"tracked\",\"age\":0.0,\"ext\":[0,1,0,0,0],\"pose\":\"point\",\"lm\":[0.3,0.5,0.0,0.2831,0.476,0.0,0.2696,0.452,0.0,0.273,0.428,0.0,0.2932,0.422,0.0,0.2798,0.38,0.0,0.2798,0.332,0.0,0.2798,0.302,0.0,0.2798,0.278,0.0,0.2932,0.374,0.0,0.2932,0.338,0.0,0.2932,0.362,0.0,0.2932,0.38,0.0,0.3068,0.38,0.0,0.3068,0.344,0.0,0.3068,0.368,0.0,0.3068,0.386,0.0,0.3203,0.392,0.0,0.3203,0.356,0.0,0.3203,0.38,0.0,0.3203,0.398,0.0]},{\"id\":\"Right\",\"score\":0.88,\"state\":\"held\",\"age\":50.0,\"ext\":[0,1,1,0,0],\"pose\":\"peace\",\"lm\":[0.7,0.55,0.0,0.6831,0.526,0.0,0.6696,0.502,0.0,0.673,0.478,0.0,0.6932,0.472,0.0,0.6797,0.43,0.0,0.6797,0.382,0.0,0.6797,0.352,0.0,0.6797,0.328,0.0,0.6932,0.424,0.0,0.6932,0.376,0.0,0.6932,0.346,0.0,0.6932,0.322,0.0,0.7067,0.43,0.0,0.7067,0.394,0.0,0.7067,0.418,0.0,0.7067,0.436,0.0,0.7202,0.442,0.0,0.7202,0.406,0.0,0.7202,0.43,0.0,0.7202,0.448,0.0]}]}";

        public const string Heartbeat =
            "{\"type\":\"heartbeat\",\"v\":1,\"seq\":7,\"t\":3500.0,\"cam\":{\"status\":\"reconnecting\",\"w\":0,\"h\":0,\"mirrored\":true},\"trk\":{\"status\":\"starting\",\"fps\":0.0,\"latency\":0.0},\"hands\":[]}";

        public const string Bye =
            "{\"type\":\"bye\",\"v\":1,\"seq\":99,\"t\":4000.0,\"cam\":{\"status\":\"stopped\",\"w\":0,\"h\":0,\"mirrored\":true},\"trk\":{\"status\":\"stopped\",\"fps\":0.0,\"latency\":0.0},\"hands\":[]}";

        static bool Parse(string json, TrackingMessage msg, HandDataParser parser = null)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            return (parser ?? new HandDataParser()).TryParse(b, b.Length, msg);
        }

        [Test]
        public void ParsesTwoHandFrame()
        {
            var m = new TrackingMessage();
            Assert.IsTrue(Parse(TwoHands, m));
            Assert.AreEqual(MessageType.Frame, m.type);
            Assert.AreEqual(1, m.version);
            Assert.AreEqual(1234L, m.seq);
            Assert.AreEqual(10000.0, m.senderTimeMs, 1e-9);
            Assert.AreEqual(CameraStatus.Ok, m.cameraStatus);
            Assert.AreEqual(1280, m.frameWidth);
            Assert.AreEqual(720, m.frameHeight);
            Assert.IsTrue(m.mirrored);
            Assert.AreEqual(TrackerStatus.Ok, m.trackerStatus);
            Assert.AreEqual(29.9f, m.trackerFps, 1e-4f);
            Assert.AreEqual(14.2f, m.latencyMs, 1e-4f);
            Assert.AreEqual(2, m.HandCount);

            Assert.IsTrue(m.left.present);
            Assert.AreEqual(Handedness.Left, m.left.handedness);
            Assert.AreEqual(HandPose.Point, m.left.pose);
            Assert.AreEqual(HandState.Tracked, m.left.state);
            Assert.AreEqual(0.97f, m.left.score, 1e-5f);
            Assert.AreEqual(0.2798f, m.left.X(HandLandmark.IndexTip), 1e-6f);
            Assert.AreEqual(0.278f, m.left.Y(HandLandmark.IndexTip), 1e-6f);
            Assert.AreEqual(0.3203f, m.left.X(HandLandmark.PinkyTip), 1e-6f);
            Assert.IsFalse(m.left.extended[0]);
            Assert.IsTrue(m.left.extended[1]);
            Assert.IsFalse(m.left.extended[2]);

            Assert.IsTrue(m.right.present);
            Assert.AreEqual(HandPose.Peace, m.right.pose);
            Assert.AreEqual(HandState.Held, m.right.state);
            Assert.AreEqual(50f, m.right.ageMs, 1e-5f);
            Assert.AreEqual(0.6932f, m.right.X(HandLandmark.MiddleTip), 1e-6f);
            Assert.AreEqual(0.322f, m.right.Y(HandLandmark.MiddleTip), 1e-6f);
            Assert.IsTrue(m.right.extended[2]);
        }

        [Test]
        public void ParsesHeartbeatAndBye()
        {
            var m = new TrackingMessage();
            Assert.IsTrue(Parse(Heartbeat, m));
            Assert.AreEqual(MessageType.Heartbeat, m.type);
            Assert.AreEqual(CameraStatus.Reconnecting, m.cameraStatus);
            Assert.AreEqual(TrackerStatus.Starting, m.trackerStatus);
            Assert.AreEqual(0, m.HandCount);

            Assert.IsTrue(Parse(Bye, m));
            Assert.AreEqual(MessageType.Bye, m.type);
            Assert.AreEqual(CameraStatus.Stopped, m.cameraStatus);
            Assert.AreEqual(TrackerStatus.Stopped, m.trackerStatus);
        }

        [Test]
        public void ReusingMessageClearsOldHands()
        {
            var m = new TrackingMessage();
            var p = new HandDataParser();
            Assert.IsTrue(Parse(TwoHands, m, p));
            Assert.IsTrue(Parse(Heartbeat, m, p));
            Assert.IsFalse(m.left.present);
            Assert.IsFalse(m.right.present);
        }

        [Test]
        public void IgnoresUnknownFieldsAndKeyOrder()
        {
            string json = "{ \"extra\" : {\"a\":[1,2,{\"b\":null}],\"s\":\"x\\\"y\"}, \"hands\":[{\"lm\":"
                + Floats(63, 0.5f) + ",\"future\":true,\"pose\":\"fist\",\"id\":\"Right\"}],"
                + "\"v\":1, \"type\":\"frame\", \"seq\": 3 }";
            var m = new TrackingMessage();
            Assert.IsTrue(Parse(json, m));
            Assert.IsTrue(m.right.present);
            Assert.IsFalse(m.left.present);
            Assert.AreEqual(HandPose.Fist, m.right.pose);
            Assert.AreEqual(0.5f, m.right.X(20), 1e-6f);
        }

        [Test]
        public void SkipsHandsWithWrongLandmarkCountOrDuplicateId()
        {
            string json = "{\"type\":\"frame\",\"v\":1,\"hands\":["
                + "{\"id\":\"Left\",\"lm\":" + Floats(60, 0.1f) + "},"
                + "{\"id\":\"Right\",\"lm\":" + Floats(63, 0.2f) + "},"
                + "{\"id\":\"Right\",\"lm\":" + Floats(63, 0.9f) + "}]}";
            var m = new TrackingMessage();
            var p = new HandDataParser();
            Assert.IsTrue(Parse(json, m, p));
            Assert.IsFalse(m.left.present);
            Assert.IsTrue(m.right.present);
            Assert.AreEqual(0.2f, m.right.X(0), 1e-6f); // first Right wins
            Assert.AreEqual(2, p.LastSkippedHands);
        }

        [Test]
        public void ParsesFramePoseAndUnknownPoses()
        {
            string lm = Floats(63, 0.4f);
            string json = "{\"type\":\"frame\",\"v\":1,\"hands\":[{\"id\":\"Left\",\"pose\":\"frame\",\"lm\":" + lm
                + "},{\"id\":\"Right\",\"pose\":\"wave\",\"lm\":" + lm + "}]}";
            var m = new TrackingMessage();
            Assert.IsTrue(Parse(json, m));
            Assert.AreEqual(HandPose.Frame, m.left.pose);
            Assert.AreEqual(HandPose.Unknown, m.right.pose, "future pose names are tolerated");
            Assert.AreEqual(-1f, m.left.pinch, 0, "pinch absent -> -1");

            string pinch = "{\"type\":\"frame\",\"v\":1,\"hands\":[{\"id\":\"Left\",\"pose\":\"pinch\",\"pinch\":0.083,\"lm\":" + lm + "}]}";
            Assert.IsTrue(Parse(pinch, m));
            Assert.AreEqual(HandPose.Pinch, m.left.pose);
            Assert.AreEqual(0.083f, m.left.pinch, 1e-6f);
        }

        [Test]
        public void ParsesNumberFormats()
        {
            string json = "{\"type\":\"frame\",\"v\":1,\"seq\":2147483647,\"t\":-1.5e3,\"trk\":{\"fps\":1E1,\"latency\":0.000125}}";
            var m = new TrackingMessage();
            Assert.IsTrue(Parse(json, m));
            Assert.AreEqual(2147483647L, m.seq);
            Assert.AreEqual(-1500.0, m.senderTimeMs, 1e-9);
            Assert.AreEqual(10f, m.trackerFps, 1e-6f);
            Assert.AreEqual(0.000125f, m.latencyMs, 1e-9f);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{")]
        [TestCase("{\"type\":\"frame\"}")]                                   // no version
        [TestCase("{\"type\":\"frame\",\"v\":2}")]                           // unsupported version
        [TestCase("{\"type\":\"party\",\"v\":1}")]                           // unknown type
        [TestCase("{\"type\":\"frame\",\"v\":1,}")]                          // trailing comma
        [TestCase("{\"type\":\"frame\",\"v\":1} junk")]                     // trailing data
        [TestCase("{\"type\":\"frame\",\"v\":1,\"hands\":[{\"id\":\"Left\",\"lm\":[0.1,NaN]}]}")]
        [TestCase("{\"type\":\"frame\",\"v\":1,\"seq\":01}")]                // leading zero
        [TestCase("{\"type\":\"frame\",\"v\":1,\"cam\":{\"mirrored\":\"yes\"}}")]
        [TestCase("{\"type\":\"frame\",\"v\":1,\"hands\":{}}")]
        [TestCase("{\"type\":\"fra")]                                        // truncated datagram
        public void RejectsMalformed(string json)
        {
            var m = new TrackingMessage();
            var p = new HandDataParser();
            Assert.IsFalse(Parse(json, m, p));
            Assert.IsTrue(p.LastError.Length > 0);
        }

        [Test]
        public void RejectsEveryTruncationOfARealPacket()
        {
            var p = new HandDataParser();
            var m = new TrackingMessage();
            byte[] full = Encoding.UTF8.GetBytes(TwoHands);
            for (int len = 1; len < full.Length; len++)
                Assert.IsFalse(p.TryParse(full, len, m), "accepted truncated packet of length " + len);
            Assert.IsTrue(p.TryParse(full, full.Length, m));
        }

        [Test]
        public void AssemblerReassemblesOutOfOrderAndDropsIncomplete()
        {
            var asm = new VideoFrameAssembler();
            byte[] jpeg = new byte[20000];
            for (int i = 0; i < jpeg.Length; i++) jpeg[i] = (byte)(i * 7);
            var chunks = MakeChunks(jpeg, 5, 640, 360, 8000); // 3 chunks

            // frame 5 incomplete, then frame 6 starts -> frame 5 dropped
            Assert.IsFalse(asm.Accept(chunks[0], chunks[0].Length));
            var next = MakeChunks(jpeg, 6, 640, 360, 8000);
            Assert.IsFalse(asm.Accept(next[2], next[2].Length));
            Assert.AreEqual(1L, asm.DroppedFrames);
            // stale chunk from frame 5 ignored
            Assert.IsFalse(asm.Accept(chunks[1], chunks[1].Length));
            Assert.IsFalse(asm.Accept(next[0], next[0].Length));
            Assert.IsFalse(asm.Accept(next[0], next[0].Length)); // duplicate
            Assert.IsTrue(asm.Accept(next[1], next[1].Length));
            Assert.AreEqual(jpeg.Length, asm.CompletedLength);
            Assert.AreEqual(6u, asm.CompletedFrameId);
            Assert.AreEqual(640, asm.Width);
            Assert.AreEqual(360, asm.Height);
            for (int i = 0; i < jpeg.Length; i++)
                if (asm.Buffer[i] != jpeg[i]) Assert.Fail("byte mismatch at " + i);
        }

        [Test]
        public void AssemblerRejectsGarbage()
        {
            var asm = new VideoFrameAssembler();
            Assert.IsFalse(asm.Accept(new byte[10], 10));
            byte[] bad = Encoding.ASCII.GetBytes("XXXX0000000000000000payload");
            Assert.IsFalse(asm.Accept(bad, bad.Length));
            Assert.AreEqual(2L, asm.BadDatagrams);
        }

        // --- helpers ---------------------------------------------------------
        static string Floats(int n, float v)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        /// <summary>C# port of video_stream.make_chunks (cross-checked against Python in the integration test).</summary>
        public static byte[][] MakeChunks(byte[] jpeg, uint frameId, int w, int h, int payload)
        {
            int count = (jpeg.Length + payload - 1) / payload;
            var result = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                int len = System.Math.Min(payload, jpeg.Length - i * payload);
                var d = new byte[20 + len];
                d[0] = (byte)'F'; d[1] = (byte)'C'; d[2] = (byte)'V'; d[3] = (byte)'1';
                d[4] = (byte)(frameId >> 24); d[5] = (byte)(frameId >> 16); d[6] = (byte)(frameId >> 8); d[7] = (byte)frameId;
                d[8] = (byte)(i >> 8); d[9] = (byte)i;
                d[10] = (byte)(count >> 8); d[11] = (byte)count;
                uint t = (uint)jpeg.Length;
                d[12] = (byte)(t >> 24); d[13] = (byte)(t >> 16); d[14] = (byte)(t >> 8); d[15] = (byte)t;
                d[16] = (byte)(w >> 8); d[17] = (byte)w; d[18] = (byte)(h >> 8); d[19] = (byte)h;
                System.Buffer.BlockCopy(jpeg, i * payload, d, 20, len);
                result[i] = d;
            }
            return result;
        }
    }
}
