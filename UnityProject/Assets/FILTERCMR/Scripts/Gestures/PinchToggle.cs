// FILTER CMR — turns Python's per-hand "pinch" pose (thumb tip touching index
// tip) into a single toggle event. Pure C#.
//
//   * Fires once when either hand has been pinching for HoldSeconds
//     (short gaps up to GapTolerance are ignored, so tracking jitter
//     does not restart the timer).
//   * Then it is disarmed until no hand has pinched for RearmSeconds, so holding
//     a pinch, or a flickering pinch, can never toggle twice.

namespace FilterCMR.Gestures
{
    public sealed class PinchToggle
    {
        public float HoldSeconds = 0.12f;
        public float RearmSeconds = 0.30f;
        public float GapTolerance = 0.10f;

        float _held, _gap, _released;
        bool _armed = true;

        public bool Pinching { get; private set; }
        public bool Armed => _armed;
        /// <summary>0..1 while a pinch is building up to a toggle.</summary>
        public float Progress => _armed && HoldSeconds > 0 ? System.Math.Min(1f, _held / HoldSeconds) : 0f;

        /// <summary>Returns true on the frame the toggle fires.</summary>
        public bool Update(bool leftPinch, bool rightPinch, float dt)
        {
            if (dt < 0) dt = 0;
            bool pinch = leftPinch || rightPinch;
            Pinching = pinch;
            if (pinch)
            {
                _gap = 0f;
                _released = 0f;
                if (!_armed) return false;
                _held += dt;
                if (_held >= HoldSeconds)
                {
                    _armed = false;
                    _held = 0f;
                    return true;
                }
                return false;
            }

            _gap += dt;
            if (_gap > GapTolerance) _held = 0f;
            if (!_armed)
            {
                _released += dt;
                if (_released >= RearmSeconds) _armed = true;
            }
            return false;
        }

        public void Reset()
        {
            _held = _gap = _released = 0f;
            _armed = true;
            Pinching = false;
        }
    }
}
