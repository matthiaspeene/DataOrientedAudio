using UnityEngine;
using UnityEngine.Audio;
using DataOrientedAudio.Voice.Authoring;

[System.Serializable]
public struct VoiceSpawnConfig
{
    [Tooltip("Base ratio amount for this voice type. e.g. 3 means: step1=3 voices, step2=6, step3=9, etc.")]
    public int amountRatio;

    public VoiceDataScriptable voiceData;
    public bool spatialized;
    public bool moving;
    public float minSpeed;
    public float maxSpeed;
    public float maxStartDelay;
    public int maxConcurrentVoices;
}

// This asset defines one stress *profile*.
// The profile does NOT define how many total voices are spawned.
// It only defines the mix between different voice types (the ratio) and movement bounds.
[CreateAssetMenu(
    fileName = "VoiceStressTestConfig",
    menuName = "Audio/Voice Stress Test Config",
    order = 0)]
public class VoiceStressTestConfig : ScriptableObject
{
    [Header("Voice Ratios")]
    [Tooltip("Example: [ (ratio=3,2D static), (ratio=1,3D moving) ].\nStep1 spawns 3+1 voices total.\nStep2 adds another 3+1 (now 6+2 total).\nStep3 adds another 3+1 (now 9+3 total)... etc.")]
    public VoiceSpawnConfig[] voiceConfigs;

    [Header("Movement Bounds (half extents)")]
    [Tooltip("Voices marked 'moving' will bounce inside this AABB, centered on the VoiceStressGameObject in the scene.")]
    public Vector3 movementBounds = new Vector3(20f, 10f, 20f);
}
