using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace MadMachines.Networking
{
    // Capture the completed PhysX step before NGO advances its clock and sends variables in PreUpdate.
    // A Tick callback is too late (the variable updater subscribed first) and labels 50 Hz poses as 30 Hz samples.
    internal static class NetworkPhysicsSnapshotLoop
    {
        private static readonly List<NetworkPhysicsState> states = new List<NetworkPhysicsState>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            states.Clear();
            RemoveLoop();
        }

        internal static void Register(NetworkPhysicsState state)
        {
            if (states.Contains(state)) return;
            if (states.Count == 0)
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                List<PlayerLoopSystem> systems = new List<PlayerLoopSystem>(loop.subSystemList);
                systems.RemoveAll(system => system.type == typeof(NetworkPhysicsSnapshotLoop));
                int index = systems.FindIndex(system => system.type == typeof(UnityEngine.PlayerLoop.PreUpdate));
                if (index < 0) throw new InvalidOperationException("Не найдена фаза PreUpdate для сетевых снимков физики.");
                systems.Insert(index, new PlayerLoopSystem
                {
                    type = typeof(NetworkPhysicsSnapshotLoop),
                    updateDelegate = Capture
                });
                loop.subSystemList = systems.ToArray();
                PlayerLoop.SetPlayerLoop(loop);
            }
            states.Add(state);
        }

        internal static void Unregister(NetworkPhysicsState state)
        {
            if (states.Remove(state) && states.Count == 0) RemoveLoop();
        }

        private static void RemoveLoop()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null) return;
            List<PlayerLoopSystem> systems = new List<PlayerLoopSystem>(loop.subSystemList);
            if (systems.RemoveAll(system => system.type == typeof(NetworkPhysicsSnapshotLoop)) == 0) return;
            loop.subSystemList = systems.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void Capture()
        {
            // No fixed step this frame means no new pose. Multiple fixed steps produce one latest snapshot.
            foreach (NetworkPhysicsState state in states) state.CaptureAfterPhysics();
        }
    }
}
