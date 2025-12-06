using Unity.Entities;
using Unity.Collections;
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
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();

        // Get actual topology from AudioTopologySystem
        var topologySystem = World.GetExistingSystemManaged<AudioTopologySystem>();
        var topology = topologySystem.GetTopologyData();

        if (topology.MaxArchetypes == 0 || topology.TotalVoices == 0)
        {
            Debug.LogWarning("[EcsVoiceRootOutputSystem] Topology is empty, not creating root output yet.");
            return;
        }

        var realtime = new EcsVoiceRootOutput.Realtime();

        // Pre-allocate arrays to avoid "Jobs can only create Temp memory" error in Configure
        realtime.Archetypes = new NativeArray<ArchetypeMeta>(topology.MaxArchetypes, Allocator.Persistent);
        realtime.GainsL = new NativeArray<float>(topology.TotalVoices, Allocator.Persistent);
        realtime.GainsR = new NativeArray<float>(topology.TotalVoices, Allocator.Persistent);
        realtime.ActiveFlags = new NativeArray<byte>(topology.TotalVoices, Allocator.Persistent);

        // Estimate buffer size based on AudioSettings
        var config = AudioSettings.GetConfiguration();
        int channels = GetChannelCount(config.speakerMode);
        int bufferLength = config.dspBufferSize;
        int bufferSamples = bufferLength * channels;

        realtime.MixBuffer = new NativeArray<float>(bufferSamples, Allocator.Persistent);

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
    private int GetChannelCount(AudioSpeakerMode mode)
    {
        switch (mode)
        {
            case AudioSpeakerMode.Mono: return 1;
            case AudioSpeakerMode.Stereo: return 2;
            case AudioSpeakerMode.Quad: return 4;
            case AudioSpeakerMode.Surround: return 5;
            case AudioSpeakerMode.Mode5point1: return 6;
            case AudioSpeakerMode.Mode7point1: return 8;
            case AudioSpeakerMode.Prologic: return 2;
            default: return 2; // Fallback to stereo
        }
    }
}
