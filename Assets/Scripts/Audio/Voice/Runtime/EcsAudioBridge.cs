using Unity.Entities;
using Unity.Collections;
using DataOrientedAudio.Voice.Runtime.Systems;

namespace DataOrientedAudio.Voice.Runtime
{
    public static class EcsAudioBridge
    {
        // Helper to get a managed (SystemBase) system from the default world
        private static T GetSystem<T>() where T : ComponentSystemBase
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return null;

            // Returns null if the system doesn't exist
            return world.GetExistingSystemManaged<T>();
        }

        public struct VoiceFinishedCommand
        {
            public int ArchetypeIndex;
            public int LocalVoiceIndex;
        }

        private static NativeList<VoiceCommand> _commands;
        private static NativeList<VoiceFinishedCommand> _finishedCommands;
        private static NativeQueue<Entity> _reclaimQueue;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _commands = new NativeList<VoiceCommand>(Allocator.Persistent);
            _finishedCommands = new NativeList<VoiceFinishedCommand>(Allocator.Persistent);
            _reclaimQueue = new NativeQueue<Entity>(Allocator.Persistent);
            _initialized = true;
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            if (_commands.IsCreated) _commands.Dispose();
            if (_finishedCommands.IsCreated) _finishedCommands.Dispose();
            if (_reclaimQueue.IsCreated) _reclaimQueue.Dispose();
            _initialized = false;
        }

        public static AudioTopologyData GetTopology()
        {
            var system = GetSystem<AudioTopologySystem>();
            if (system == null)
            {
                return new AudioTopologyData
                {
                    MaxArchetypes = 0,
                    TotalVoices = 0,
                    Archetypes = default
                };
            }

            return system.GetTopologyData();
        }

        /// <summary>
        /// Returns the shared command list.
        /// Not thread safe. Assumes strict execution order between Producer (System) and Consumer (Control).
        /// </summary>
        public static NativeList<VoiceCommand> GetCommandList()
        {
            if (!_initialized) Initialize();
            return _commands;
        }

        public static NativeList<VoiceFinishedCommand> GetFinishedCommandList()
        {
            if (!_initialized) Initialize();
            return _finishedCommands;
        }

        public static NativeQueue<Entity> GetReclaimQueue()
        {
            if (!_initialized) Initialize();
            return _reclaimQueue;
        }

        // Deprecated
        public static NativeList<VoiceCommand> GetVoiceCommands() => GetCommandList();

        public static void ClearVoiceCommands()
        {
            if (_initialized)
            {
                _commands.Clear();
                _finishedCommands.Clear(); // Also clear finished commands? Or should the consumer clear it?
                // Consumer (AudioVoiceCommandSystem) should clear it after processing.
            }
        }

        public static void GetCommands(NativeList<VoiceCommand> output)
        {
            if (!_initialized) return;
            output.Clear();
            output.AddRange(_commands.AsArray());
        }
    }
}
