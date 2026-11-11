using System.Collections;
using UnityEngine;

namespace MadMachines
{
    [RequireComponent(typeof(LocalVehicleSession))]
    public sealed class QuarryPreviewStart : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // The direct scene launch normally waits for the main menu to choose a mode.
            // Start after camera, rules, and input have initialized.
            yield return null;
            LocalPlayerInput input = GetComponent<LocalPlayerInput>();
            if (input.isActiveAndEnabled) input.Begin(ChallengeMode.Free);
        }
    }
}
