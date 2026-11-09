#if UNITY_EDITOR
using System.Collections;
using UnityEngine;

namespace MadMachines
{
    // Runs only through the explicit editor verification entry point; absent from player builds.
    public sealed class PlaygroundVerification : MonoBehaviour
    {
        public static bool Done { get; private set; }
        public static bool Passed { get; private set; } = true;
        public static string Report { get; private set; } = "";
        private void Check(bool condition, string message)
        {
            Passed &= condition;
            Report += (condition ? "PASS " : "FAIL ") + message + "\n";
            Debug.Log((condition ? "PASS " : "FAIL ") + message);
        }

        private IEnumerator Start()
        {
            Done = false;
            Passed = true;
            Report = "";
            yield return null;
            LocalPlayerInput input = FindAnyObjectByType<LocalPlayerInput>();
            input.enabled = false;
            VehiclePhysics car = FindAnyObjectByType<VehiclePhysics>();
            ChallengeManager rules = FindAnyObjectByType<ChallengeManager>();
            WeaponSystem gun = car.GetComponent<WeaponSystem>();
            rules.PersistRecords = false;
            rules.Begin(ChallengeMode.Free);
            yield return new WaitForSeconds(1.5f);
            Check(car.GroundedWheels == car.WheelCount && car.transform.position.y > 0.4f, "Suspension supports stationary chassis");
            Vector3 initial = car.transform.position;
            car.Submit(new VehicleCommand { Throttle = 1 });
            yield return new WaitForSeconds(1.5f);
            Check(car.transform.position.z > initial.z + 3 && car.Speed > 12, "Throttle accelerates Rigidbody forward");
            car.Submit(new VehicleCommand { Throttle = 1, Steering = 0.65f });
            yield return new WaitForSeconds(0.7f);
            Check(Mathf.Abs(Mathf.DeltaAngle(0, car.transform.eulerAngles.y)) > 4, "Steering turns chassis through tire forces");
            car.Recover(new Vector3(0, 1.5f, -42), Quaternion.Euler(0, 0, 180));
            rules.Recover();
            yield return new WaitForSeconds(1);
            Check(Vector3.Dot(car.transform.up, Vector3.up) > 0.98f && car.Body.linearVelocity.magnitude < 2, "Recovery resets overturned chassis");
            // Use the fixed target in a clear lane; a random moving target makes this
            // collision test depend on its current motion phase and intervening props.
            Target target = GameObject.Find("Target 1").GetComponent<Target>();
            Vector3 targetPosition = target.transform.position;
            car.Recover(targetPosition + new Vector3(0, -0.5f, -9), Quaternion.identity);
            yield return new WaitForSeconds(0.2f);
            gun.Select(1);
            gun.Aim((target.transform.position - gun.MuzzlePosition).normalized);
            Check(gun.RequestFire(), "Authority accepts cannon shot");
            Check(!gun.RequestFire(), "Reload blocks immediate repeated fire");
            Projectile shot = FindAnyObjectByType<Projectile>();
            Check(shot != null && shot.GetComponent<Rigidbody>().useGravity, "Cannon launches gravity-driven projectile");
            yield return new WaitForSeconds(1.2f);
            Check(target.Destroyed, "Physical projectile collision destroys a target");
            gun.Authority = false;
            Check(!gun.RequestFire(), "Non-authoritative weapon rejects shot");
            gun.Authority = true;
            gun.Select(0);
            Check(gun.RequestFire(), "Machine gun fires");
            yield return new WaitForSeconds(0.2f);
            gun.Select(2);
            gun.Aim(Vector3.forward + Vector3.up * 0.4f);
            Check(gun.RequestFire(), "Heavy weapon fires");
            yield return new WaitForSeconds(3.5f);
            Check(FindObjectsByType<Projectile>().Length == 0, "Timed grenade detonates and cleans up");

            rules.Begin(ChallengeMode.Race);
            Check(rules.Phase == SessionPhase.Countdown && car.Body.isKinematic, "Countdown locks vehicle");
            yield return new WaitForSeconds(3.2f);
            Check(rules.Phase == SessionPhase.Running && !car.Body.isKinematic, "Countdown starts race and unlocks vehicle");
            rules.PassCheckpoint(1, car);
            Check(rules.NextCheckpoint == 0, "Out-of-order checkpoint rejected");
            Checkpoint[] gates = FindObjectsByType<Checkpoint>();
            System.Array.Sort(gates, (a, b) => a.Index.CompareTo(b.Index));
            foreach (Checkpoint gate in gates)
            {
                car.Recover(gate.transform.position + Vector3.up * 1.2f - gate.transform.forward * 5, gate.transform.rotation);
                yield return new WaitForFixedUpdate();
                car.Body.linearVelocity = gate.transform.forward * 12;
                yield return new WaitForSeconds(0.5f);
            }
            Check(rules.Phase == SessionPhase.Results && rules.Result > 0, "Physical checkpoint triggers finish race with result");
            rules.Begin(ChallengeMode.Combat);
            yield return new WaitForSeconds(3.2f);
            for (int i = 0; i < rules.CheckpointCount; i++) rules.PassCheckpoint(i, car);
            Check(rules.Phase == SessionPhase.Results && rules.Result >= 64, "Combat result penalizes eight missed targets");
            rules.Begin(ChallengeMode.Shooting);
            yield return new WaitForSeconds(3.2f);
            foreach (Target item in FindObjectsByType<Target>()) item.Hit(200, car);
            Check(rules.Phase == SessionPhase.Results && rules.Result == 1200, "Shooting rules score static/moving targets and finish");
            rules.Begin(ChallengeMode.Shooting);
            yield return new WaitForSeconds(3.2f);
            Time.timeScale = 20;
            yield return new WaitForSeconds(61);
            Time.timeScale = 1;
            Check(rules.Phase == SessionPhase.Results && rules.Result == 0, "Shooting timeout finishes empty attempt");
            rules.Begin(ChallengeMode.Free);
            Check(rules.Phase == SessionPhase.Free && FindObjectsByType<Target>().Length == 8, "Retry resets all targets and session state");
            Done = true;
        }
    }
}
#endif
