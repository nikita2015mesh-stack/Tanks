using System.Collections;
using UnityEngine;

namespace MadMachines
{
    [RequireComponent(typeof(LocalVehicleSession))]
    public sealed class LocalFreeDriveStart : MonoBehaviour
    {
        private IEnumerator Start()
        {
            if (GameFlow.LocalLaunchPending) yield break;
            // Direct launches start after camera, rules, and input have initialized.
            yield return null;
            LocalPlayerInput input = GetComponent<LocalPlayerInput>();
            if (input.isActiveAndEnabled) input.Begin(ChallengeMode.Free);
        }
    }
}
