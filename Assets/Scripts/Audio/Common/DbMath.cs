// Scope: UI/editor-facing math ONLY (display + conversions). No DSP helpers here.
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace DataOrientedAudio.Common
{
    public static class DbMath
    {
        // Constants (use naturals to avoid System.Math): 20 * log10(x) == 20/ln(10) * ln(x)
        private const float LN10 = 2.302585092994046f;
        private const float INV20 = 1f / 20f;

        // Exposed UI range for sliders/meters.
        public const float DbMin = -80f;   // floor exposed to UI
        public const float DbMax = 20f;    // ceiling exposed to UI

        /// <summary>
        /// Convert dB to linear gain for UI. If db <= DbMin, returns EXACT 0 (for slider bottom).
        /// Otherwise clamps to [DbMin, DbMax] and converts.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DbToLinear(float db)
        {
            if (db <= DbMin) return 0f;
            db = math.clamp(db, DbMin, DbMax);
            // pow(10, db/20) == exp(ln(10) * db/20)
            return math.exp(LN10 * (db * INV20));
        }

        /// <summary>
        /// Convert linear gain to dB for UI. If lin <= 0, returns EXACT DbMin.
        /// Otherwise converts and clamps to [DbMin, DbMax].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LinearToDb(float lin)
        {
            if (lin <= 0f) return DbMin;
            // Guard: avoid log(0). Clamp to small epsilon for numeric stability.
            lin = math.max(lin, 1e-20f);
            float db = 20f * (math.log(lin) / LN10);
            return math.clamp(db, DbMin, DbMax);
        }

        /// <summary>Clamp a dB value to the UI range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ClampDb(float db, float minDb = DbMin, float maxDb = DbMax)
        {
            return math.clamp(db, minDb, maxDb);
        }

        /// <summary>Round a value to a display step (e.g., 0.1 dB).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RoundToStep(float value, float step = 0.1f)
        {
            // round(value / step) * step
            float q = value / step;
            return math.round(q) * step;
        }

        /// <summary>Soft clamp for UI meters: snaps near -Inf dB to DbMin.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SoftFloorDb(float db, float floorDb = DbMin)
        {
            // Display concern only.
            return math.max(db, floorDb);
        }
    }
}
