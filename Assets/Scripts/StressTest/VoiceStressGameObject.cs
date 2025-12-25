using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;
using System.Collections;
using DataOrientedAudio.Voice.Authoring;

[RequireComponent(typeof(AudioListener))]
public class VoiceStressGameObject : MonoBehaviour
{
    [Header("Test Setup")]
    [SerializeField] private VoiceStressTestConfig testConfig;
    [SerializeField] private GameObject prefab2D;
    [SerializeField] private GameObject prefab3D;

    // movement data in SoA form for ECS-ish fairness
    private readonly List<Transform> movingTransforms = new List<Transform>(255);
    private readonly List<Vector3> movementDirection = new List<Vector3>(255);
    private readonly List<float> movementSpeed = new List<float>(255);

    // keep references to all AudioSources for analysis if you need it
    private readonly List<AudioSource> allAudioSources = new List<AudioSource>(255);

    // tracks how many "steps" of stress we've applied so far
    private int currentStressStep = 0;

    private void Start()
    {
        if (testConfig == null)
        {
            Debug.LogError("VoiceStressGameObject: No testConfig assigned.");
            return;
        }

        // Start with first level automatically if you want instant load.
        IncreaseStressLevel();
    }

    private void Update()
    {
        UpdateMovement();
    }

    /// <summary>
    /// Call this (manually / debug button / key) to add another batch of voices.
    /// Step 1 spawns ratio * 1.
    /// Step 2 spawns ratio * 1 again (total ratio * 2).
    /// Step 3 spawns ratio * 1 again (total ratio * 3).
    /// etc.
    /// We stop spawning if we would exceed 255 total voices.
    /// </summary>
    public void IncreaseStressLevel()
    {
        if (testConfig == null)
        {
            Debug.LogError("IncreaseStressLevel called but no config assigned.");
            return;
        }

        // next step
        currentStressStep++;

        // Figure out how many TOTAL voices we already spawned.
        int totalSpawnedSoFar = allAudioSources.Count;

        if (totalSpawnedSoFar >= 255)
        {
            Debug.LogWarning("Reached cap of 255 voices. Cannot increase stress further.");
            return;
        }

        // For each config: we always add exactly (amountRatio) more on each step.
        foreach (var voiceConfig in testConfig.voiceConfigs)
        {
            int voicesToAddThisStep = voiceConfig.amountRatio;

            // enforce global 255 cap
            int maxAllowedMore = 255 - totalSpawnedSoFar;

            if (voicesToAddThisStep > maxAllowedMore)
            {
                voicesToAddThisStep = maxAllowedMore;
            }

            SpawnVoicesOfType(voiceConfig, voicesToAddThisStep);

            totalSpawnedSoFar += voicesToAddThisStep;

            if (totalSpawnedSoFar >= 255)
            {
                Debug.LogWarning("Reached cap of 255 voices. Cannot increase stress further.");
                return;
            }
        }
    }

    private void SpawnVoicesOfType(VoiceSpawnConfig voiceConfig, int countToSpawn)
    {
        GameObject prefabToUse = voiceConfig.spatialized ? prefab3D : prefab2D;

        for (int i = 0; i < countToSpawn; i++)
        {
            Vector3 startPosition = voiceConfig.spatialized
                ? GetRandomPositionInBounds()
                : transform.position;

            GameObject voiceObject = Instantiate(prefabToUse, startPosition, Quaternion.identity, transform);

            AudioSource audioSource = voiceObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                Debug.LogWarning("Voice prefab is missing AudioSource.", voiceObject);
                continue;
            }

            VoiceDataScriptable voiceData = voiceConfig.voiceData;

            // initial random params
            AudioClip clip = voiceData.GetRandomClip();
            if (clip == null)
            {
                Debug.LogWarning("VoiceData has no clips assigned.", voiceData);
                continue;
            }

            audioSource.clip = clip;
            audioSource.volume = voiceData.GetRandomGain();
            audioSource.pitch = voiceData.GetRandomPlaybackSpeed();
            audioSource.spatialBlend = voiceConfig.spatialized ? 1f : 0f;
            audioSource.playOnAwake = false;

            switch (voiceData.TriggerMode)
            {
                case Triggermode.Once:
                    {
                        audioSource.loop = false;

                        float startDelay = Random.Range(0f, voiceConfig.maxStartDelay);
                        audioSource.PlayDelayed(startDelay);
                        break;
                    }

                case Triggermode.Loop:
                    {
                        audioSource.loop = true;

                        float startDelay = Random.Range(0f, voiceConfig.maxStartDelay);
                        audioSource.PlayDelayed(startDelay);
                        break;
                    }

                case Triggermode.Repeat:
                    {
                        // Not looped: we manually retrigger with random delay via coroutine
                        audioSource.loop = false;

                        StartCoroutine(RepeatPlayRoutine(audioSource, voiceData, voiceConfig));
                        break;
                    }
            }

            allAudioSources.Add(audioSource);

            // only spatialized voices can move in space
            if (voiceConfig.spatialized && voiceConfig.moving)
            {
                movingTransforms.Add(voiceObject.transform);
                movementDirection.Add(Random.onUnitSphere);
                movementSpeed.Add(Random.Range(voiceConfig.minSpeed, voiceConfig.maxSpeed));
            }
        }
    }

    /// <summary>
    /// Handles Triggermode.Repeat: repeatedly picks random clip/params, plays,
    /// waits for clip length + random repeat delay, then repeats.
    /// </summary>
    private IEnumerator RepeatPlayRoutine(
        AudioSource audioSource,
        VoiceDataScriptable voiceData,
        VoiceSpawnConfig voiceConfig)
    {
        // initial random start delay
        float startDelay = Random.Range(0f, voiceConfig.maxStartDelay);
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        while (audioSource != null && voiceData != null)
        {
            AudioClip clip = voiceData.GetRandomClip();
            if (clip == null)
            {
                yield break; // nothing to play anymore
            }

            audioSource.clip = clip;
            audioSource.volume = voiceData.GetRandomGain();
            audioSource.pitch = voiceData.GetRandomPlaybackSpeed();
            audioSource.loop = false;
            audioSource.Play();

            // approximate effective duration accounting for pitch
            float pitch = Mathf.Max(0.01f, audioSource.pitch);
            float clipDuration = clip.length / pitch;

            float repeatDelay = voiceData.GetRandomRepeatDelay();
            float waitTime = Mathf.Max(0f, clipDuration + repeatDelay);

            yield return new WaitForSeconds(waitTime);
        }
    }

    private void UpdateMovement()
    {
        Vector3 origin = transform.position;
        Vector3 halfExtents = testConfig.movementBounds;

        // ECS-ish single pass over packed arrays; no per-voice Update()
        for (int i = 0; i < movingTransforms.Count; i++)
        {
            Transform currentTransform = movingTransforms[i];
            Vector3 direction = movementDirection[i];
            float speed = movementSpeed[i];

            Vector3 localPosition = currentTransform.position - origin;
            localPosition += direction * (speed * Time.deltaTime);

            // X bounce
            if (localPosition.x > halfExtents.x)
            {
                localPosition.x = halfExtents.x;
                direction.x = -direction.x;
            }
            else if (localPosition.x < -halfExtents.x)
            {
                localPosition.x = -halfExtents.x;
                direction.x = -direction.x;
            }

            // Y bounce
            if (localPosition.y > halfExtents.y)
            {
                localPosition.y = halfExtents.y;
                direction.y = -direction.y;
            }
            else if (localPosition.y < -halfExtents.y)
            {
                localPosition.y = -halfExtents.y;
                direction.y = -direction.y;
            }

            // Z bounce
            if (localPosition.z > halfExtents.z)
            {
                localPosition.z = halfExtents.z;
                direction.z = -direction.z;
            }
            else if (localPosition.z < -halfExtents.z)
            {
                localPosition.z = -halfExtents.z;
                direction.z = -direction.z;
            }

            currentTransform.position = origin + localPosition;
            movementDirection[i] = direction;
        }
    }

    private Vector3 GetRandomPositionInBounds()
    {
        Vector3 halfExtents = testConfig.movementBounds;

        return transform.position + new Vector3(
            Random.Range(-halfExtents.x, halfExtents.x),
            Random.Range(-halfExtents.y, halfExtents.y),
            Random.Range(-halfExtents.z, halfExtents.z)
        );
    }
}
