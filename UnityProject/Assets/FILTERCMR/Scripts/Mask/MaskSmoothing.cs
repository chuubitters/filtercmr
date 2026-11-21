// FILTER CMR — temporal smoothing for mask control points and mask fades. Pure C#.
//
// Python already smooths landmarks (One Euro) and HandView adds display
// smoothing. The mask adds one more, deliberately different, layer:
//   * frame-rate independent exponential smoothing of each control point, and
//   * a speed limit, so a single bad tracking frame (a "jump") cannot snap the
//     mask across the screen — the point glides there instead.

using System;
using System.Numerics;

namespace FilterCMR.Mask
{
    public struct PointSmoother
    {
        public Vector2 Value;
        public bool Initialised;

        public void Snap(Vector2 target)
        {
            Value = target;
            Initialised = true;
        }

        /// <param name="rate">Smoothing rate in 1/s (0 = no smoothing).</param>
        /// <param name="maxSpeed">Maximum movement in ISO units per second (0 = unlimited).</param>
        public Vector2 Update(Vector2 target, float dt, float rate, float maxSpeed)
        {
            if (!Initialised || dt <= 0f) { if (!Initialised) Snap(target); return Value; }
            Vector2 step = rate > 0f ? (target - Value) * (1f - (float)Math.Exp(-rate * dt)) : target - Value;
            if (maxSpeed > 0f)
            {
                float max = maxSpeed * dt, len = step.Length();
                if (len > max) step *= max / len;
            }
            Value += step;
            return Value;
        }

        public void Reset() => Initialised = false;
    }

    /// <summary>Linear 0..1 fade with separate in/out durations, eased for display.</summary>
    public struct MaskFader
    {
        public float Value;

        /// <summary>Eased value (smoothstep) used for rendering.</summary>
        public float Eased => MaskMath.SmoothStep01(Value);
        public bool Visible => Value > 0f;

        public float Update(float target, float dt, float fadeInSeconds, float fadeOutSeconds)
        {
            target = MaskMath.Clamp01(target);
            if (dt <= 0f) return Value;
            if (target > Value)
                Value = fadeInSeconds <= 0f ? target : Math.Min(target, Value + dt / fadeInSeconds);
            else if (target < Value)
                Value = fadeOutSeconds <= 0f ? target : Math.Max(target, Value - dt / fadeOutSeconds);
            return Value;
        }

        public void Reset() => Value = 0f;
    }
}
