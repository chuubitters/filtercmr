// End-to-end checks: real Python sender processes -> C# receivers over real UDP sockets.
// Not part of the Unity project. Run with: dotnet run -- integration
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FilterCMR.Networking;
using NUnit.Framework;

[TestFixture]
public class Integration
{
    static readonly string Repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    static string HandTracking => Path.Combine(Repo, "HandTracking");
    static string Python => Environment.GetEnvironmentVariable("FILTERCMR_PYTHON") ?? "python3";

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    static Process StartPython(string args)
    {
        var psi = new ProcessStartInfo(Python, args)
        {
            WorkingDirectory = HandTracking,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi);
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine("    [py] " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null && !e.Data.Contains("VIDEOIO") && !e.Data.StartsWith("[ WARN") && !e.Data.StartsWith("[ERROR")) Console.WriteLine("    [py!] " + e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    [Test]
    public void FakeTrackerPacketsAndVideoArriveAndParse()
    {
        int port = FreePort(), vport = FreePort();
        using var rx = new UdpReceiverCore();
        using var vrx = new VideoReceiverCore();
        Assert.IsTrue(rx.Start(port), rx.LastError);
        Assert.IsTrue(vrx.Start(vport), vrx.LastError);

        var py = StartPython($"tools/fake_tracker.py --port {port} --video-port {vport} --stream-video " +
                             "--duration 4.4 --pose cycle --cycle-seconds 1.0 --drop-every 2");
        var msg = new TrackingMessage();
        byte[] jpeg = null;
        bool sawPoint = false, sawPeace = false, sawOpen = false, sawFrame = false, sawBoth = false, sawRightMissing = false, sawBye = false;
        int polls = 0, got = 0, videoFrames = 0, vw = 0, vh = 0;
        bool jpegMarkersOk = true;
        float minX = 1, maxX = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 8 && !sawBye)
        {
            polls++;
            if (rx.TryGetLatest(msg))
            {
                got++;
                if (msg.type == MessageType.Bye) { sawBye = true; break; }
                Assert.AreEqual(MessageType.Frame, msg.type);
                Assert.IsTrue(msg.mirrored);
                Assert.AreEqual(1280, msg.frameWidth);
                if (msg.left.present && msg.right.present) sawBoth = true;
                if (msg.left.present && !msg.right.present) sawRightMissing = true;
                for (int h = 0; h < 2; h++)
                {
                    var hand = msg.Get((Handedness)h);
                    if (!hand.present) continue;
                    if (hand.pose == HandPose.Point) sawPoint = true;
                    if (hand.pose == HandPose.Peace) sawPeace = true;
                    if (hand.pose == HandPose.Open) sawOpen = true;
                    if (hand.pose == HandPose.Frame) sawFrame = true;
                    for (int i = 0; i < 21; i++)
                    {
                        float x = hand.X(i), y = hand.Y(i);
                        Assert.IsTrue(x >= 0 && x <= 1 && y >= 0 && y <= 1, "landmark outside 0-1");
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                    }
                }
                // left hand is on the left of the (mirrored) screen in the fake tracker
                if (msg.left.present && msg.right.present)
                    Assert.Less(msg.left.X(0), msg.right.X(0));
            }
            if (vrx.TryGetLatest(ref jpeg, out int len, out vw, out vh))
            {
                videoFrames++;
                jpegMarkersOk &= len > 4 && jpeg[0] == 0xFF && jpeg[1] == 0xD8 && jpeg[len - 2] == 0xFF && jpeg[len - 1] == 0xD9;
            }
            Thread.Sleep(16); // ~60 Hz "Unity frame"
        }
        py.WaitForExit(5000);
        Console.WriteLine($"    tracking: {rx.PacketsReceived} received, {got} consumed by {polls} polls, malformed={rx.MalformedPackets}, stale={rx.StalePackets}");
        Console.WriteLine($"    video: {vrx.FramesReceived} frames ({vw}x{vh}), dropped={vrx.DroppedFrames}, bad={vrx.BadDatagrams}; landmark x range {minX:F2}..{maxX:F2}");

        Assert.Greater(rx.PacketsReceived, 90, "expected ~30 packets/s for 4 s");
        Assert.AreEqual(0L, rx.MalformedPackets);
        Assert.IsTrue(sawPoint && sawFrame && sawPeace && sawOpen, "expected point, frame (thumb+index L), peace and open poses");
        Assert.IsTrue(sawBoth, "expected both hands");
        Assert.IsTrue(sawRightMissing, "expected right hand to drop out");
        Assert.IsTrue(sawBye, "expected bye");
        Assert.Greater(videoFrames, 60, "expected video frames");
        Assert.IsTrue(jpegMarkersOk, "JPEG SOI/EOI markers");
        Assert.AreEqual(640, vw);
        Assert.AreEqual(360, vh);
        Assert.Less(rx.SecondsSinceLastPacket, 2.0);
    }

    [Test]
    public void RealTrackerWithoutCameraSendsHeartbeatsThenBye()
    {
        int port = FreePort();
        using var rx = new UdpReceiverCore();
        Assert.IsTrue(rx.Start(port), rx.LastError);
        var py = StartPython($"main.py --no-preview --camera-index 7 --udp-port {port}");
        var msg = new TrackingMessage();
        bool sawHeartbeat = false, sawCamProblem = false;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 3)
        {
            if (rx.TryGetLatest(msg))
            {
                Assert.AreEqual(MessageType.Heartbeat, msg.type);
                sawHeartbeat = true;
                sawCamProblem |= msg.cameraStatus == CameraStatus.Error || msg.cameraStatus == CameraStatus.Reconnecting;
                Assert.AreEqual(0, msg.HandCount);
            }
            Thread.Sleep(20);
        }
        // Ctrl+C equivalent: SIGINT -> KeyboardInterrupt -> bye
        Process.Start("kill", $"-INT {py.Id}").WaitForExit();
        bool sawBye = false;
        sw.Restart();
        while (sw.Elapsed.TotalSeconds < 5 && !sawBye)
        {
            if (rx.TryGetLatest(msg) && msg.type == MessageType.Bye) sawBye = true;
            Thread.Sleep(20);
        }
        py.WaitForExit(5000);
        Console.WriteLine($"    heartbeats received: {rx.PacketsReceived}, malformed={rx.MalformedPackets}");
        Assert.IsTrue(sawHeartbeat);
        Assert.IsTrue(sawCamProblem, "camera status should report error/reconnecting");
        Assert.IsTrue(sawBye, "bye after SIGINT");
        Assert.AreEqual(0L, rx.MalformedPackets);
    }

    [Test]
    public void ReceiverRestartPortConflictAndCleanShutdown()
    {
        int port = FreePort();
        var a = new UdpReceiverCore();
        Assert.IsTrue(a.Start(port));
        var b = new UdpReceiverCore();
        Assert.IsFalse(b.Start(port), "second bind on the same port must fail");
        Assert.AreEqual(ReceiverState.Error, b.State);
        Assert.IsTrue(b.LastError.Contains("already in use"), b.LastError);

        for (int i = 0; i < 5; i++) // restart repeatedly (the R key)
        {
            Assert.IsTrue(a.Start(port), a.LastError);
            Assert.AreEqual(ReceiverState.Listening, a.State);
        }
        int threadsBefore = Process.GetCurrentProcess().Threads.Count;
        a.Stop();
        Assert.AreEqual(ReceiverState.Stopped, a.State);
        Assert.IsFalse(a.IsRunning);
        // After Stop the port is free again (socket released).
        Assert.IsTrue(b.Start(port), "port should be free after Stop: " + b.LastError);
        b.Stop();
        a.Stop(); // idempotent
        Console.WriteLine($"    threads before stop={threadsBefore}, after={Process.GetCurrentProcess().Threads.Count}");
    }

    [Test]
    public void SequenceHandlingDropsStaleAcceptsRestart()
    {
        int port = FreePort();
        using var rx = new UdpReceiverCore();
        Assert.IsTrue(rx.Start(port));
        using var tx = new UdpClient();
        void Send(long seq, string type = "frame")
        {
            byte[] d = System.Text.Encoding.UTF8.GetBytes($"{{\"type\":\"{type}\",\"v\":1,\"seq\":{seq},\"hands\":[]}}");
            tx.Send(d, d.Length, new IPEndPoint(IPAddress.Loopback, port));
            Thread.Sleep(15);
        }
        var m = new TrackingMessage();
        Send(500); Send(501);
        Send(499);              // reordered/stale -> dropped
        Send(501);              // duplicate -> dropped
        Send(502, "bye");       // bye resets the sequence
        Send(1);                // restarted tracker
        Thread.Sleep(50);
        Assert.IsTrue(rx.TryGetLatest(m));
        Assert.AreEqual(1L, m.seq);
        Assert.AreEqual(2L, rx.StalePackets);
        Assert.AreEqual(4L, rx.PacketsReceived);
        Send(10_000); Send(3);  // jump back > 1000 => treated as restart, accepted
        Thread.Sleep(50);
        Assert.IsTrue(rx.TryGetLatest(m));
        Assert.AreEqual(3L, m.seq);
        byte[] junk = { 1, 2, 3 };
        tx.Send(junk, junk.Length, new IPEndPoint(IPAddress.Loopback, port));
        Thread.Sleep(50);
        Assert.AreEqual(1L, rx.MalformedPackets);
    }
}
