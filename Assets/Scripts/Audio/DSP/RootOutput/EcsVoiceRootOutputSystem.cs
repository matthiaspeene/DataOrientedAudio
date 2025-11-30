using Unity.Entities;
using UnityEngine;
using UnityEngine.Audio;
using DataOrientedAudio.DSP.RootOutput;
using static UnityEngine.Audio.ProcessorInstance; // Brings CreationParameters + UpdateSetting into scope

public partial class EcsVoiceRootOutputSystem : SystemBase
{
    // You can later pull these from ECS bootstrap/topology, they’re just initial guesses.
    private const int DefaultMaxArchetypes = 16;
    private const int DefaultTotalVoices = 1024;

    private RootOutputInstance _instance;
    private bool _created;

    protected override void OnCreate()
    {
        base.OnCreate();

        Debug.Log("[EcsVoiceRootOutputSystem] OnCreate");

        // Your realtime + control types from EcsVoiceRootOutput
        var realtime = new EcsVoiceRootOutput.Realtime();
        var control = new EcsVoiceRootOutput.Control(DefaultMaxArchetypes, DefaultTotalVoices);

        // IMPORTANT: Use ProcessorInstance.UpdateSetting, not ControlContext.ProcessorUpdateSetting
        var creationParams = new CreationParameters
        {
            // Make Control.Update + Realtime.Update run every frame / mix cycle
            controlUpdateSetting = UpdateSetting.UpdateAlways,
            realtimeUpdateSetting = UpdateSetting.UpdateAlways
        };

        // Allocate as a RootOutput using the ref + CreationParameters overload
        _instance = ControlContext.builtIn.AllocateRootOutput(realtime, control, creationParams);

        // RootOutputInstance implicitly converts to ProcessorInstance, so Exists(...) works
        _created = ControlContext.builtIn.Exists(_instance);
        Debug.Log($"[EcsVoiceRootOutputSystem] Allocated root output. Exists={_created}");
    }

    protected override void OnUpdate()
    {
        // No per-frame work needed here – audio system drives Control/Realtime.
        // You can add debug checks or config updates later if you want.
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
