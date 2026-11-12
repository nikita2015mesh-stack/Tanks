using System;
using System.Collections;
using UnityEngine;

namespace MadMachines
{
    public sealed class TestAsphaltSession : MonoBehaviour
    {
        [SerializeField] private LocalPlayerInput localPlayer;
        [SerializeField] private GameObject networkSession;

        private void Awake()
        {
#if UNITY_EDITOR
            // The selected build target can define UNITY_SERVER in Editor Play Mode.
            bool dedicated = false;
#elif UNITY_SERVER
            bool dedicated = true;
#else
            bool dedicated = Array.IndexOf(Environment.GetCommandLineArgs(), "-mm-server") >= 0;
#endif
            if (dedicated || (GameFlow.Loading && GameFlow.OnlineLaunchPending))
            {
                networkSession.SetActive(true);
                return;
            }

            localPlayer.gameObject.SetActive(true);
            if (!GameFlow.LocalLaunchPending) StartCoroutine(BeginDirectDrive());
        }

        private IEnumerator BeginDirectDrive()
        {
            yield return null;
            if (localPlayer.isActiveAndEnabled) localPlayer.Begin(ChallengeMode.Free);
        }
    }
}
