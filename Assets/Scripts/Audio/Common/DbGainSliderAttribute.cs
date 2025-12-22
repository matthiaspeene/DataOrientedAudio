// Assets/Scripts/Audio/Runtime/UI/DbGainSliderAttribute.cs
using UnityEngine;

namespace DataOrientedAudio.Common
{
    /// <summary>
    /// Apply to a float (stored as linear gain) to draw a dB slider in inspectors.
    /// The slider range defaults to DbMath.DbMin..DbMath.DbMax.
    /// db <= DbMin maps to EXACT linear 0.0 per requirement.
    /// </summary>
    public sealed class DbGainSliderAttribute : PropertyAttribute
    {
        public readonly float MinDb;
        public readonly float MaxDb;
        public readonly bool ShowLinearReadout;

        /// <param name="minDb">Optional custom min in dB (defaults to DbMath.DbMin)</param>
        /// <param name="maxDb">Optional custom max in dB (defaults to DbMath.DbMax)</param>
        /// <param name="showLinearReadout">Show read-only linear value under the dB slider</param>
        public DbGainSliderAttribute(float minDb = float.NaN, float maxDb = float.NaN, bool showLinearReadout = true)
        {
            MinDb = float.IsNaN(minDb) ? DbMath.DbMin : minDb;
            MaxDb = float.IsNaN(maxDb) ? DbMath.DbMax : maxDb;
            ShowLinearReadout = showLinearReadout;
        }
    }
}
