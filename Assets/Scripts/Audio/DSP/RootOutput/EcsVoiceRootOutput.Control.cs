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
                EnsureArray(ref realtime.VoiceActiveFlags, _totalVoices);
                EnsureArray(ref realtime.Gains, _totalVoices * format.channelCount);
                EnsureArray(ref realtime.PlaybackSpeeds, _totalVoices);
                EnsureArray(ref realtime.PlaybackPositions, _totalVoices);

                // Bus data
                var bufferSamples = format.bufferFrameCount * format.channelCount;
                EnsureArray(ref realtime.BusMeta, _topology.MaxBuses);
                EnsureArray(ref realtime.BusBuffers, bufferSamples * _topology.MaxBuses);
                EnsureArray(ref realtime.BusJobHandles, _topology.MaxBuses);

                EnsureNestedListArray(ref realtime.BusActiveArchetypes, _topology.MaxBuses);
                EnsureNestedListArray(ref realtime.BusActiveVoices, _topology.MaxBuses);

                // Initialize BusMeta values
                for (int i = 0; i < _topology.MaxBuses; i++)
                {
                    realtime.BusMeta[i] = new BusMeta
                    {
                        Start = i * bufferSamples,
                        Size = bufferSamples,
                        ChannelCount = format.channelCount
                    };
                }

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

            private void EnsureNestedListArray<T>(ref NativeArray<NativeList<T>> array, int length) where T : unmanaged
            {
                if (array.IsCreated && array.Length == length)
                    return;

                if (array.IsCreated)
                {
                    for (int i = 0; i < array.Length; i++)
                    {
                        if (array[i].IsCreated)
                            array[i].Dispose();
                    }
                    array.Dispose();
                }

                array = new NativeArray<NativeList<T>>(length, Allocator.Persistent);
                for (int i = 0; i < length; i++)
                {
                    array[i] = new NativeList<T>(Allocator.Persistent);
                }
            }

            public void Dispose(ControlContext context, ref Realtime realtime)
            {
                if (realtime.Archetypes.IsCreated) realtime.Archetypes.Dispose();

                if (realtime.Gains.IsCreated) realtime.Gains.Dispose();
                if (realtime.VoiceActiveFlags.IsCreated) realtime.VoiceActiveFlags.Dispose();
                if (realtime.PlaybackPositions.IsCreated) realtime.PlaybackPositions.Dispose();
                if (realtime.PlaybackSpeeds.IsCreated) realtime.PlaybackSpeeds.Dispose();

                if (realtime.BusMeta.IsCreated) realtime.BusMeta.Dispose();
                if (realtime.BusBuffers.IsCreated) realtime.BusBuffers.Dispose();
                if (realtime.BusJobHandles.IsCreated) realtime.BusJobHandles.Dispose();

                if (realtime.BusActiveArchetypes.IsCreated)
                {
                    for (int i = 0; i < realtime.BusActiveArchetypes.Length; i++)
                    {
                        if (realtime.BusActiveArchetypes[i].IsCreated)
                            realtime.BusActiveArchetypes[i].Dispose();
                    }
                    realtime.BusActiveArchetypes.Dispose();
                }

                if (realtime.BusActiveVoices.IsCreated)
                {
                    for (int i = 0; i < realtime.BusActiveVoices.Length; i++)
                    {
                        if (realtime.BusActiveVoices[i].IsCreated)
                            realtime.BusActiveVoices[i].Dispose();
                    }
                    realtime.BusActiveVoices.Dispose();
                }

                if (realtime.FinishedVoiceIndices.IsCreated) realtime.FinishedVoiceIndices.Dispose();
            }

            public void Update(ControlContext context, Pipe pipe)
            {
                // Safety check: if the pipe/context is invalid (teardown), don't process.
                // Moving the system to PresentationSystemGroup should largely prevent this, 
                // but we add a try-catch for robustness.
                try
                {
                    // 1. Read messages from Realtime
                    foreach (var element in pipe.GetAvailableData(context))
                    {
                        if (element.TryGetData(out VoiceFinishedMessage finishedMsg))
                        {
                            int globalIndex = finishedMsg.GlobalVoiceIndex;
                            if (_topology.MaxArchetypes > 0 && _topology.Archetypes.IsCreated)
                            {
                                for (int i = 0; i < _topology.Archetypes.Length; i++)
                                {
                                    var a = _topology.Archetypes[i];
                                    if (globalIndex >= a.Start && globalIndex < a.Start + a.Count)
                                    {
                                        DataOrientedAudio.Voice.Runtime.EcsAudioBridge.GetFinishedCommandList().Add(new DataOrientedAudio.Voice.Runtime.EcsAudioBridge.VoiceFinishedCommand
                                        {
                                            ArchetypeIndex = a.ArchetypeIndex,
                                            LocalVoiceIndex = globalIndex - a.Start
                                        });
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // 2. Register archetypes on bootstrap
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

                    // 3. Process commands from ECS
                    var commands = new NativeList<DataOrientedAudio.Voice.Runtime.VoiceCommand>(Allocator.Temp);
                    DataOrientedAudio.Voice.Runtime.EcsAudioBridge.GetCommands(commands);

                    if (commands.IsCreated && commands.Length > 0)
                    {
                        for (int i = 0; i < commands.Length; i++)
                        {
                            var cmd = commands[i];
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
                                            ArchetypeIndex = cmd.ArchetypeIndex,
                                            IsActive = cmd.Value != 0f,
                                        });
                                        break;

                                    case DataOrientedAudio.Voice.Runtime.VoiceCommandType.SetPlaybackSpeed:
                                        pipe.SendData(context, new SetVoicePlaybackSpeedMessage
                                        {
                                            GlobalVoiceIndex = globalIndex,
                                            Value = cmd.Value
                                        });
                                        break;
                                }
                            }
                        }
                    }
                    commands.Dispose();
                }
                catch (System.Exception)
                {
                    // Ignore errors during teardown
                }
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
