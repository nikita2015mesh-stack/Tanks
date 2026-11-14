using System.Collections;
using UnityEngine;

namespace MadMachines
{
    public sealed class TestAsphaltSession : MonoBehaviour
    {
        [SerializeField] private LocalPlayerInput localPlayer;

        private void Awake()
        {
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
