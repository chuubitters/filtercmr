// EditMode tests for the two-hand gesture state machine.

using FilterCMR.Gestures;
using FilterCMR.Networking;
using NUnit.Framework;

namespace FilterCMR.Tests
{
    [TestFixture]
    public class GestureTests
    {
        const HandStatus D = HandStatus.Detected, H = HandStatus.Held, L = HandStatus.Lost;
        const float Dt = 1f / 60f;

        static GestureStateMachine Make() => new GestureStateMachine { ConfirmSeconds = 0.2f, ReleaseSeconds = 0.25f };

        static void Run(GestureStateMachine m, float seconds, HandStatus l, HandPose lp, HandStatus r, HandPose rp)
        {
            for (float t = 0; t < seconds - 1e-6f; t += Dt) m.Update(l, lp, r, rp, Dt);
        }

        [Test]
        public void ClassifyCombinesPythonPoses()
        {
            Assert.AreEqual(GestureType.TwoFingerPoint, GestureStateMachine.Classify(D, HandPose.Point, D, HandPose.Point));
            Assert.AreEqual(GestureType.TwoFingerFrame, GestureStateMachine.Classify(D, HandPose.Frame, H, HandPose.Frame));
            Assert.AreEqual(GestureType.None, GestureStateMachine.Classify(D, HandPose.Point, D, HandPose.Frame));
            Assert.AreEqual(GestureType.None, GestureStateMachine.Classify(D, HandPose.Point, L, HandPose.Point));
            Assert.AreEqual(GestureType.None, GestureStateMachine.Classify(D, HandPose.Open, D, HandPose.Open));
        }

        [Test]
        public void FrameIsThumbIndexLShapeByDefaultAndPeaceOnlyWhenConfigured()
        {
            Assert.AreEqual(GestureType.TwoFingerFrame, GestureStateMachine.Classify(D, HandPose.Frame, D, HandPose.Frame));
            Assert.AreEqual(GestureType.None, GestureStateMachine.Classify(D, HandPose.Peace, D, HandPose.Peace), "peace no longer frames by default");
            Assert.AreEqual(GestureType.TwoFingerFrame,
                GestureStateMachine.Classify(D, HandPose.Peace, D, HandPose.Peace, FrameFingers.IndexAndMiddle));
            Assert.AreEqual(GestureType.None,
                GestureStateMachine.Classify(D, HandPose.Frame, D, HandPose.Frame, FrameFingers.IndexAndMiddle));
            Assert.AreEqual(GestureType.TwoFingerPoint, GestureStateMachine.Classify(D, HandPose.Point, D, HandPose.Point, FrameFingers.IndexAndMiddle));
            Assert.AreEqual(HandLandmark.ThumbTip, GestureStateMachine.FrameSecondTip(FrameFingers.IndexAndThumb));
            Assert.AreEqual(HandLandmark.MiddleTip, GestureStateMachine.FrameSecondTip(FrameFingers.IndexAndMiddle));

            var m = new GestureStateMachine { FrameFingers = FrameFingers.IndexAndMiddle };
            Run(m, 0.3f, D, HandPose.Peace, D, HandPose.Peace);
            Assert.AreEqual(GestureType.TwoFingerFrame, m.Current);
        }

        [Test]
        public void PointRequiresHoldTimeBeforeActivating()
        {
            var m = Make();
            Run(m, 0.1f, D, HandPose.Point, D, HandPose.Point);
            Assert.AreEqual(GestureState.Confirming, m.State);
            Assert.AreEqual(GestureType.None, m.Current);
            Assert.Greater(m.Progress, 0.3);
            Run(m, 0.15f, D, HandPose.Point, D, HandPose.Point);
            Assert.AreEqual(GestureState.Active, m.State);
            Assert.AreEqual(GestureType.TwoFingerPoint, m.Current);
            Assert.AreEqual(1.0, m.Progress, 1e-6);
        }

        [Test]
        public void FlickerDuringConfirmResets()
        {
            var m = Make();
            Run(m, 0.15f, D, HandPose.Frame, D, HandPose.Frame);
            m.Update(D, HandPose.Other, D, HandPose.Frame, Dt);
            Assert.AreEqual(GestureState.Idle, m.State);
            Run(m, 0.15f, D, HandPose.Frame, D, HandPose.Frame);
            Assert.AreEqual(GestureState.Confirming, m.State, "timer must restart after the flicker");
        }

        [Test]
        public void BriefDropoutDoesNotBreakActiveGesture()
        {
            var m = Make();
            Run(m, 0.3f, D, HandPose.Frame, D, HandPose.Frame);
            Assert.AreEqual(GestureType.TwoFingerFrame, m.Current);
            Run(m, 0.1f, D, HandPose.Frame, L, HandPose.Unknown); // right hand lost for 100 ms
            Assert.AreEqual(GestureState.Releasing, m.State);
            Assert.AreEqual(GestureType.TwoFingerFrame, m.Current);
            Run(m, 0.05f, D, HandPose.Frame, D, HandPose.Frame);
            Assert.AreEqual(GestureState.Active, m.State);
        }

        [Test]
        public void ReleasesAfterGracePeriod()
        {
            var m = Make();
            Run(m, 0.3f, D, HandPose.Point, D, HandPose.Point);
            bool changed = false;
            for (int i = 0; i < 30; i++) changed |= m.Update(L, HandPose.Unknown, L, HandPose.Unknown, Dt);
            Assert.IsTrue(changed);
            Assert.AreEqual(GestureState.Idle, m.State);
            Assert.AreEqual(GestureType.None, m.Current);
        }

        [Test]
        public void SwitchingPointToFrameGoesThroughReleaseThenConfirm()
        {
            var m = Make();
            Run(m, 0.3f, D, HandPose.Point, D, HandPose.Point);
            Run(m, 0.3f, D, HandPose.Frame, D, HandPose.Frame);
            Assert.AreEqual(GestureState.Confirming, m.State);
            Assert.AreEqual(GestureType.TwoFingerFrame, m.Tracked);
            Run(m, 0.25f, D, HandPose.Frame, D, HandPose.Frame);
            Assert.AreEqual(GestureType.TwoFingerFrame, m.Current);
        }

        [Test]
        public void HeldHandsKeepButCannotStartGesture()
        {
            var m = Make();
            Run(m, 0.5f, H, HandPose.Point, D, HandPose.Point);
            Assert.AreEqual(GestureState.Idle, m.State, "held hand must not start a gesture");
            Assert.AreEqual(GestureType.TwoFingerPoint, m.Candidate);
            Run(m, 0.3f, D, HandPose.Point, D, HandPose.Point);
            Run(m, 1.0f, H, HandPose.Point, H, HandPose.Point);
            Assert.AreEqual(GestureType.TwoFingerPoint, m.Current, "held hands keep it active");
        }

        [Test]
        public void ResetClearsEverything()
        {
            var m = Make();
            Run(m, 0.3f, D, HandPose.Point, D, HandPose.Point);
            m.Reset();
            Assert.AreEqual(GestureState.Idle, m.State);
            Assert.AreEqual(GestureType.None, m.Current);
            Assert.AreEqual(0.0, m.Progress, 1e-9);
        }

        [Test]
        public void ZeroConfirmTimeActivatesImmediately()
        {
            var m = new GestureStateMachine { ConfirmSeconds = 0f, ReleaseSeconds = 0f };
            Assert.IsTrue(m.Update(D, HandPose.Frame, D, HandPose.Frame, Dt));
            Assert.AreEqual(GestureType.TwoFingerFrame, m.Current);
            Assert.IsTrue(m.Update(D, HandPose.Fist, D, HandPose.Frame, Dt));
            Assert.AreEqual(GestureType.None, m.Current);
        }
    }
}
