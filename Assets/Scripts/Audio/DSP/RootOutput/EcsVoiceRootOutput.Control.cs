using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.Audio.ProcessorInstance;
using DataOrientedAudio.Voice.Runtime;

namespace DataOrientedAudio.DSP.RootOutput
{
    public static partial class EcsVoiceRootOutput
    {
        /// <summary>
        /// Control side for the ECS-driven root output.
        /// It owns allocation/disposal and translates ECS commands → pipe messages.
        /// </summary>
        public struct Control : RootOutputInstance.IControl<Realtime>
        {
            #region Fields

            private int _maxArchetypes;
            private int _totalVoices;
            private bool _bootstrapSent;
            private AudioTopologyData _topology; // Cache topology to access Blob references

            #endregion

            public Control(int maxArchetypes, int totalVoices)
            {
                _maxArchetypes = maxArchetypes;
                _totalVoices = totalVoices;
                _bootstrapSent = false;
                _topology = default;
            }

            #region Lifecycle

            public JobHandle Configure(ControlContext context, ref Realtime realtime, in AudioFormat format)
            {
                var topology = DataOrientedAudio.Voice.Runtime.EcsAudioBridge.GetTopology();

                // If topology is ready, use it. Otherwise fall back or wait.
                if (topology.MaxArchetypes > 0)
                {
                    _maxArchetypes = topology.MaxArchetypes;
                    _totalVoices = topology.TotalVoices;
                    _topology = topology;
                }

                if (_maxArchetypes <= 0 || _totalVoices <= 0)
                {
                    _maxArchetypes = 1;
                    _totalVoices = 1;
                }

                // (Re)allocate native memory on the realtime side.
                realtime.Format = format;

                EnsureArray(ref realtime.Archetypes, _maxArchetypes);
                EnsureArray(ref realtime.GainsL, _totalVoices);
                EnsureArray(ref realtime.GainsR, _totalVoices);
                EnsureArray(ref realtime.VoiceActiveFlags, _totalVoices);
                EnsureArray(ref realtime.PlaybackPositions, _totalVoices);

                // Mix buffer: one sample per channel per frame.
                var bufferSamples = format.bufferFrameCount * format.channelCount;
                EnsureArray(ref realtime.MixBuffer, bufferSamples);

                // Temp buffers for archetype mixing: maxArchetypes * bufferSamples
                EnsureArray(ref realtime.TempBuffers, _maxArchetypes * bufferSamples);
                EnsureArray(ref realtime.Handles, _maxArchetypes);
                EnsureArray(ref realtime.ArchetypeActiveFlags, _maxArchetypes);

                // Reset bootstrap flag so we send messages in the first Update
                _bootstrapSent = false;

                return default;
            }

            private void EnsureArray<T>(ref NativeArray<T> array, int length) where T : struct
            {
                if (array.IsCreated && array.Length == length)
                    return;

                if (array.IsCreated)
                    array.Dispose();

                array = new NativeArray<T>(length, Allocator.Persistent);
            }

            public void Dispose(ControlContext context, ref Realtime realtime)
            {
                if (realtime.Archetypes.IsCreated)
                {
                    realtime.Archetypes.Dispose();
                }

                if (realtime.GainsL.IsCreated)
                {
                    realtime.GainsL.Dispose();
                }

                if (realtime.GainsR.IsCreated)
                {
                    realtime.GainsR.Dispose();
                }

                if (realtime.VoiceActiveFlags.IsCreated)
                {
                    realtime.VoiceActiveFlags.Dispose();
                }

                if (realtime.ArchetypeActiveFlags.IsCreated)
                {
                    realtime.ArchetypeActiveFlags.Dispose();
                }

                if (realtime.MixBuffer.IsCreated)
                {
                    realtime.MixBuffer.Dispose();
                }

                if (realtime.PlaybackPositions.IsCreated)
                {
                    realtime.PlaybackPositions.Dispose();
                }

                if (realtime.TempBuffers.IsCreated)
                {
                    realtime.TempBuffers.Dispose();
                }

                if (realtime.Handles.IsCreated)
                {
                    realtime.Handles.Dispose();
                }
            }

            public void Update(ControlContext context, Pipe pipe)
            {
                // 2. For each archetype, if anything changed, send RegisterArchetypeMessage.
                // We do this once on bootstrap for now.
                if (!_bootstrapSent && _topology.MaxArchetypes > 0 && _topology.Archetypes.IsCreated)
                {
                    for (int i = 0; i < _topology.Archetypes.Length; i++)
                    {
                        var a = _topology.Archetypes[i];
                        var msg = new RegisterArchetypeMessage
                        {
                            ArchetypeIndex = a.ArchetypeIndex,
                            Blob = a.Blob,
                            Start = a.Start,
                            Count = a.Count,
                        };
                        pipe.SendData(context, msg);
                    }
                    _bootstrapSent = true;
                }

                // 3. Read ECS audio command buffer with per-voice gain/active changes.
                // We use a local buffer to copy commands safely from the bridge.
                var commands = new NativeList<DataOrientedAudio.Voice.Runtime.VoiceCommand>(Allocator.Temp);
                DataOrientedAudio.Voice.Runtime.EcsAudioBridge.GetCommands(commands);

                if (commands.IsCreated && commands.Length > 0)
                {
                    for (int i = 0; i < commands.Length; i++)
                    {
                        var cmd = commands[i];

                        // Get topology for this archetype to compute global index.
                        // We need to look up the archetype info.
                        // We cached _topology.
                        if (cmd.ArchetypeIndex >= 0 && cmd.ArchetypeIndex < _topology.Archetypes.Length)
                        {
                            var topo = _topology.Archetypes[cmd.ArchetypeIndex];
                            int globalIndex = topo.Start + cmd.LocalVoiceIndex;

                            switch (cmd.Type)
                            {
                                case DataOrientedAudio.Voice.Runtime.VoiceCommandType.SetGain:
                                    pipe.SendData(context, new SetVoiceGainMessage
                                    {
                                        GlobalVoiceIndex = globalIndex,
                                        ChannelIndex = cmd.ChannelIndex,
                                        Value = cmd.Value
                                    });
                                    break;

                                case DataOrientedAudio.Voice.Runtime.VoiceCommandType.SetActive:
                                    pipe.SendData(context, new SetVoiceActiveMessage
                                    {
                                        GlobalVoiceIndex = globalIndex,
                                        IsActive = cmd.Value != 0f,
                                    });
                                    UnityEngine.Debug.Log($"Processed active request for global voice: {globalIndex}");
                                    break;
                            }
                        }
                    }

                    // No need to clear bridge commands, the swap handled it.
                }
                commands.Dispose();
            }

            public Response OnMessage(ControlContext context, Pipe pipe, Message message)
            {
                // Optional: handle messages from the realtime side back to ECS.
                return Response.Unhandled;
            }

            #endregion
        }
    }
}
