// DspMath.cs
// Scope: AUDIO-RATE helpers ONLY (Burst-safe). No UI conversions here.
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace DataOrientedAudio.Common
{
    [BurstCompile(FloatPrecision = FloatPrecision.Low, FloatMode = FloatMode.Fast)]
    public static class DspMath
    {
        private const float DENORMAL_EPS = 1e-30f;

        // ---------- Core tiny utilities ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Saturate(float x) => math.clamp(x, 0f, 1f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpFast(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Zero out denormals to avoid denormal slowdowns.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DenormalGuard(float x)
        {
            // Avoid branches: if |x| < eps -> 0, else x
            return math.select(0f, x, math.abs(x) >= DENORMAL_EPS);
        }

        // ---------- One-pole smoothing ----------

        /// <summary>
        /// Compute one-pole smoothing coefficient given time (seconds) to reach ~63% (1 - 1/e).
        /// Use once per parameter change / rate change, not per-sample if avoidable.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float OnePoleCoeffByTau(float tauSeconds, float sampleRate)
        {
            // alpha = 1 - exp(-1 / (tau * fs))
            float inv = math.rcp(math.max(tauSeconds * sampleRate, 1e-9f));
            return 1f - math.exp(-inv);
        }

        /// <summary>Step a one-pole smoother: y += a * (x - y).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float OnePoleStep(float current, float target, float alpha)
        {
            return current + alpha * (target - current);
        }

        // ---------- Biquad design (a0 normalized to 1) ----------

        public struct BiquadCoeffs
        {
            public float b0, b1, b2; // feedforward
            public float a1, a2;     // feedback (a0==1)
        }

        /// <summary>
        /// Low-pass biquad (TPT/BLT-style) with cutoff in Hz and Q. Avoids per-sample transcendentals.
        /// Call only when params change.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BiquadCoeffs LowPass(float cutoffHz, float q, float sampleRate)
        {
            cutoffHz = math.clamp(cutoffHz, 10f, 0.49f * sampleRate);
            q = math.max(q, 1e-3f);

            float w0 = 2f * math.PI * (cutoffHz / sampleRate);
            float cosw0 = math.cos(w0);
            float sinw0 = math.sin(w0);
            float alpha = sinw0 / (2f * q);

            float b0 = (1f - cosw0) * 0.5f;
            float b1 = 1f - cosw0;
            float b2 = (1f - cosw0) * 0.5f;
            float a0 = 1f + alpha;
            float a1 = -2f * cosw0;
            float a2 = 1f - alpha;

            float a0Inv = math.rcp(a0);
            BiquadCoeffs c;
            c.b0 = b0 * a0Inv;
            c.b1 = b1 * a0Inv;
            c.b2 = b2 * a0Inv;
            c.a1 = a1 * a0Inv;
            c.a2 = a2 * a0Inv;
            return c;
        }

        // ---------- First-order allpass (useful for min-phase tricks / fractional delay) ----------

        /// <summary>
        /// First-order allpass coefficient from a cutoff-like frequency using bilinear transform.
        /// Useful building block; call on parameter change only.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AllpassA1FromHz(float fHz, float sampleRate)
        {
            fHz = math.clamp(fHz, 10f, 0.49f * sampleRate);
            float g = math.tan(math.PI * (fHz / sampleRate));   // prewarp
            return (1f - g) / (1f + g);
        }

        // ---------- Safe multiply-add variants for inner loops ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Madd(float acc, float x, float y) => acc + x * y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MaddDn(float acc, float x, float y) => DenormalGuard(acc + x * y);
    }
}
