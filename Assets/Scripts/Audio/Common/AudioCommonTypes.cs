using Unity.Mathematics;
namespace DataOrientedAudio.Common
{
    public enum AudioEventSpace
    {
        Stereo2D,
        World3D,
        Attached3D
    }

    [System.Serializable]
    public struct RandomRange
    {
        public float Min;
        public float Max;

        public RandomRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public readonly float Clamp(float value)
        {
            if (value < Min) return Min;
            if (value > Max) return Max;
            return value;
        }

        public readonly float Lerp(float t)
        {
            return Min + (Max - Min) * t;
        }
    }
}
