using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Entities;
using DataOrientedAudio.StressTest.Systems;
using UnityEngine.InputSystem;

namespace DataOrientedAudio.StressTest
{
    public enum TestMode
    {
        GameObject,
        ECS
    }
    public class StessTestSequencer : MonoBehaviour
    {
        [SerializeField] private TestMode testMode;
        [SerializeField] private bool manual;
        [SerializeField] private string nextScene;
        [SerializeField] private float startupDelay = 5f;
        [SerializeField] private int steps = 20;
        [SerializeField] private float stepDuration = 10f;
        [SerializeField] private VoiceStressGameObject voiceStressGameObject;

        private void OnValidate()
        {
            if (testMode == TestMode.GameObject)
            {
                voiceStressGameObject = GetComponent<VoiceStressGameObject>();
            }
        }

        private void Start()
        {
            if (!manual)
            {
                StartCoroutine(Sequence());
            }
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                IncreaseStressLevel();
            }
            if (Keyboard.current.nKey.wasPressedThisFrame)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(nextScene);
            }
        }

        private IEnumerator Sequence()
        {
            yield return new WaitForSeconds(startupDelay);

            for (int i = 0; i < steps; i++)
            {
                // Increase stress level
                IncreaseStressLevel();

                // Wait for step duration
                yield return new WaitForSeconds(stepDuration);
            }

            if (nextScene != null)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(nextScene);
            }
        }

        public void IncreaseStressLevel()
        {
            if (testMode == TestMode.GameObject)
            {
                voiceStressGameObject.IncreaseStressLevel();
            }
            else if (testMode == TestMode.ECS)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<VoiceStressTestSpawnSystem>();
                system?.IncreaseStressLevel();
            }
        }
    }
}
