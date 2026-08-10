using Unity.Entities;
using UnityEngine;
using UnityEngine.Audio;
using DataOrientedAudio.DSP.RootOutput;
using DataOrientedAudio.Voice.Runtime.Systems;
using DataOrientedAudio.Voice.Runtime;
using static UnityEngine.Audio.ProcessorInstance;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(AudioTopologySystem))] // Don’t start running until the topology system has created its singleton
public partial class EcsVoiceRootOutputSystem : SystemBase
{
    private RootOutputInstance _instance;
    private bool _created;
    private bool _shutdownIssued;
    private bool _allowQuit;

    protected override void OnCreate()
    {
        base.OnCreate();

        RequireForUpdate<AudioTopologySingleton>();
        Application.wantsToQuit += OnWantsToQuit;
    }

    protected override void OnStartRunning()
    {
        base.OnStartRunning();

        AudioShutdownState.Reset();
        _shutdownIssued = false;
        _allowQuit = false;

        // Get actual topology from AudioTopologySystem
        var topologySystem = World.GetExistingSystemManaged<AudioTopologySystem>();
        var topology = topologySystem.GetTopologyData();

        if (topology.MaxArchetypes == 0 || topology.TotalVoices == 0)
        {
            Debug.LogWarning("[EcsVoiceRootOutputSystem] Topology is empty, not creating root output yet.");
            return;
        }

        // Estimate buffer size based on AudioSettings
        var config = AudioSettings.GetConfiguration();

        var realtime = new EcsVoiceRootOutput.Realtime(topology.MaxArchetypes, topology.TotalVoices, config.dspBufferSize, config.speakerMode, topology.MaxBuses);

        var control = new EcsVoiceRootOutput.Control(topology.MaxArchetypes, topology.TotalVoices);

        var creationParams = new CreationParameters
        {
            controlUpdateSetting = UpdateSetting.UpdateAlways,
            realtimeUpdateSetting = UpdateSetting.UpdateAlways
        };

        _instance = ControlContext.builtIn.AllocateRootOutput(realtime, control, creationParams);

        _created = ControlContext.builtIn.Exists(_instance);
        //Debug.Log($"[EcsVoiceRootOutputSystem] Allocated root output. Exists={_created}, " +
        //          $"Archetypes={topology.MaxArchetypes}, Voices={topology.TotalVoices}");

#if PARALLELAUDIO
        EcsVoiceRootOutput.UseParallelScheduling.Data = true;
#else
        EcsVoiceRootOutput.UseParallelScheduling.Data = false;
#endif

    }

    protected override void OnUpdate()
    {
        if (_shutdownIssued && AudioShutdownState.IsRealtimeRemoved)
        {
            _allowQuit = true;
            Application.Quit();
        }
    }

    protected override void OnDestroy()
    {
        //Debug.Log("[EcsVoiceRootOutputSystem] OnDestroy");

        Application.wantsToQuit -= OnWantsToQuit;
        BeginShutdown();

        // Idempotent; this also prevents the bridge from being lazily recreated
        // by a late control/ECS callback during world teardown.
        EcsAudioBridge.Shutdown();

        base.OnDestroy();
    }

    private bool OnWantsToQuit()
    {
        if (_allowQuit || !_created)
            return true;

        BeginShutdown();
        return AudioShutdownState.IsRealtimeRemoved;
    }

    private void BeginShutdown()
    {
        if (_shutdownIssued)
            return;

        _shutdownIssued = true;
        EcsAudioBridge.BeginShutdown();

        if (_created && ControlContext.builtIn.Exists(_instance))
        {
            ControlContext.builtIn.Destroy(_instance);
            //Debug.Log("[EcsVoiceRootOutputSystem] Shutdown requested for root output.");
        }
    }

}
