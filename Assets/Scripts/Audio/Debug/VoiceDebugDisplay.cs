using UnityEngine;
using Unity.Entities;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.Debugging
{
    public class VoiceDebugDisplay : MonoBehaviour
    {
        private EntityManager _entityManager;
        private EntityQuery _voiceQuery;
        private bool _isInitialized;

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _entityManager = world.EntityManager;
                _voiceQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<VoiceActive>());
                _isInitialized = true;
            }
        }

        private void OnGUI()
        {
            if (!_isInitialized)
            {
                // Try to initialize lazily if Start failed (e.g. World wasn't ready)
                Initialize();
                if (!_isInitialized) return;
            }

            // Simple style for the label
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 24;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;

            // Calculate active voices
            // We count entities that have the VoiceActive component
            // Note: This counts all entities with the component, regardless of whether the component itself is enabled or not.
            // If we want ONLY enabled components (which signifies an active voice), we need to handle that.
            // By default, generic queries match enabled components. 
            // Options = EntityQueryOptions.IgnoreComponentEnabledState would match disabled ones too.
            // So default behavior is correct for "Active" voices if "Active" means "Component Enabled".

            // However, VoiceActive might be a tag or data. Let's assume standard behavior.
            int count = _voiceQuery.CalculateEntityCount();

            // Draw shadow for better visibility
            GUI.color = Color.black;
            GUI.Label(new Rect(22, 22, 300, 50), $"Active Voices: {count}", style);

            // Draw text
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 20, 300, 50), $"Active Voices: {count}", style);
        }
    }
}
