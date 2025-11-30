using Unity.Entities;
using UnityEngine;
using UnityEngine.Audio;
using DataOrientedAudio.DSP.RootOutput;
using DataOrientedAudio.Voice.Runtime.Systems;
using DataOrientedAudio.Voice.Runtime;
using static UnityEngine.Audio.ProcessorInstance;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(AudioTopologySystem))] // Don’t start running until the topology system has created its singleton
public partial class EcsVoiceRootOutputSystem : SystemBase
{
    private RootOutputInstance _instance;
    private bool _created;

    protected override void OnCreate()
    {
        base.OnCreate();

        RequireForUpdate<AudioTopologySingleton>();
        Debug.Log("[EcsVoiceRootOutputSystem] OnCreate");
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();

        Debug.Log("[EcsVoiceRootOutputSystem] OnStartRunning – creating root output");

        // Get actual topology from AudioTopologySystem
        var topologySystem = World.GetExistingSystemManaged<AudioTopologySystem>();
        var topology = topologySystem.GetTopologyData();

        // If for some reason topology is still empty, you can guard here
        if (topology.MaxArchetypes == 0 || topology.TotalVoices == 0)
        {
            Debug.LogWarning("[EcsVoiceRootOutputSystem] Topology is empty, not creating root output yet.");
            return;
        }

        var realtime = new EcsVoiceRootOutput.Realtime();
        var control = new EcsVoiceRootOutput.Control(topology.MaxArchetypes, topology.TotalVoices);

        var creationParams = new CreationParameters
        {
            controlUpdateSetting = UpdateSetting.UpdateAlways,
            realtimeUpdateSetting = UpdateSetting.UpdateAlways
        };

        _instance = ControlContext.builtIn.AllocateRootOutput(realtime, control, creationParams);

        _created = ControlContext.builtIn.Exists(_instance);
        Debug.Log($"[EcsVoiceRootOutputSystem] Allocated root output. Exists={_created}, " +
                  $"Archetypes={topology.MaxArchetypes}, Voices={topology.TotalVoices}");
    }

    protected override void OnUpdate()
    {
        // No per-frame work for now
    }

    protected override void OnDestroy()
    {
        Debug.Log("[EcsVoiceRootOutputSystem] OnDestroy");

        if (_created && ControlContext.builtIn.Exists(_instance))
        {
            ControlContext.builtIn.Destroy(_instance);
            Debug.Log("[EcsVoiceRootOutputSystem] Destroyed root output.");
        }

        base.OnDestroy();
    }
}
