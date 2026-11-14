using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadMachines
{
    public enum ChallengeMode
    {
        Free, Race, Shooting, Combat
    }

    public enum SessionPhase
    {
        Free, Countdown, Running, Results
    }

    public sealed class ChallengeManager : MonoBehaviour
    {
        [SerializeField] private VehiclePhysics participant;
        [SerializeField] private Checkpoint[] checkpoints;
        [SerializeField] private Target[] targets;
        [SerializeField] private Vector3 spawn = new Vector3(0f, 1.5f, -42f);
        [SerializeField] private float shootingDuration = 60f;
        [SerializeField] private string trackId;
        private Quaternion spawnRotation = Quaternion.identity;
        private float startTime;
        private int destroyed;
        private int shots;
        private int hits;
        private readonly HashSet<int> hitShots = new HashSet<int>();
        private bool wasKinematic;
        public bool Authority { get; set; } = true;
        public bool PersistRecords { get; set; } = true;
        public string TrackId => trackId;
        public ChallengeMode Mode { get; private set; }
        public SessionPhase Phase { get; private set; }
        public int NextCheckpoint { get; private set; }
        public int CheckpointCount => checkpoints.Length;
        public int Score { get; private set; }
        public float Result { get; private set; }
        public float LastHitTime { get; private set; } = -10f;
        public float Elapsed => Phase == SessionPhase.Running ? Time.time - startTime : elapsed;
        public float Remaining => Mathf.Max(0f, shootingDuration - Elapsed);
        public float Countdown => Mathf.Max(0f, startTime - Time.time);
        public int MissedTargets => targets.Length - destroyed;
        public float Accuracy => shots == 0 ? 0f : Mathf.Min(100f, hits * 100f / shots);
        public string ResultDetail { get; private set; }
        public event Action<ChallengeMode, float> Finished;
        private float elapsed;

        public void Configure(VehiclePhysics vehicle, Checkpoint[] gates, Target[] rangeTargets)
        {
            participant = vehicle;
            checkpoints = gates;
            targets = rangeTargets;
        }

        public void BindParticipant(VehiclePhysics vehicle, Transform startPoint)
        {
            participant = vehicle;
            startPoint.GetPositionAndRotation(out spawn, out spawnRotation);
        }

        private void Start()
        {
            participant.GetComponent<WeaponSystem>().Fired += CountShot;
            Highlight();
        }

        private void CountShot()
        {
            if (Phase == SessionPhase.Running) shots++;
        }

        public void Begin(ChallengeMode mode)
        {
            if (!Authority) return;
            Time.timeScale = 1f;
            if (Phase == SessionPhase.Countdown) participant.Body.isKinematic = wasKinematic;
            Mode = mode;
            Score = destroyed = shots = hits = NextCheckpoint = 0;
            hitShots.Clear();
            participant.GetComponent<WeaponSystem>().ResetReload();
            elapsed = Result = 0f;
            ResultDetail = "";
            foreach (Projectile projectile in FindObjectsByType<Projectile>()) Destroy(projectile.gameObject);
            ResetTargets();
            participant.Recover(spawn, spawnRotation);
            Phase = mode == ChallengeMode.Free ? SessionPhase.Free : SessionPhase.Countdown;
            startTime = Time.time + 3f;
            if (Phase == SessionPhase.Countdown)
            {
                wasKinematic = participant.Body.isKinematic;
                participant.Body.isKinematic = true;
            }
            Highlight();
        }

        internal void ResetTargets()
        {
            foreach (Target target in targets) target.ResetTarget();
            LastHitTime = -10f;
        }

        private void Update()
        {
            if (!Authority) return;
            if (Phase == SessionPhase.Countdown && Time.time >= startTime)
            {
                participant.Body.isKinematic = wasKinematic;
                Phase = SessionPhase.Running;
                startTime = Time.time;
            }
            if (Phase == SessionPhase.Running && Mode == ChallengeMode.Shooting && Remaining <= 0f) Finish();
        }

        public void Recover()
        {
            if (!Authority || Phase == SessionPhase.Countdown) return;
            participant.GetComponent<WeaponSystem>().StopRocketGuidance();
            if (NextCheckpoint > 0 && Mode != ChallengeMode.Shooting)
            {
                Transform gate = checkpoints[NextCheckpoint - 1].transform;
                participant.Recover(gate.position + Vector3.up * 1.5f + gate.forward * 3f, gate.rotation);
            }
            else participant.Recover(spawn, spawnRotation);
            if (Phase == SessionPhase.Running && Mode != ChallengeMode.Shooting) startTime -= 3f;
        }

        public void PassCheckpoint(int index, VehiclePhysics vehicle)
        {
            if (!Authority || vehicle != participant || Phase != SessionPhase.Running || Mode == ChallengeMode.Shooting || index != NextCheckpoint) return;
            NextCheckpoint++;
            Highlight();
            if (NextCheckpoint == checkpoints.Length) Finish();
        }

        public void RegisterHit(VehiclePhysics attacker, int shotId)
        {
            if (!Authority || attacker != participant) return;
            LastHitTime = Time.time;
            if (Phase == SessionPhase.Running && shotId > 0 && hitShots.Add(shotId)) hits++;
        }

        public void TargetDestroyed(Target target, VehiclePhysics attacker)
        {
            if (!Authority || attacker != participant || Phase != SessionPhase.Running || Mode == ChallengeMode.Race) return;
            Score += target.Points;
            destroyed++;
            if (Mode == ChallengeMode.Shooting && destroyed == targets.Length) Finish();
        }

        private void Finish()
        {
            elapsed = Elapsed;
            Result = Mode == ChallengeMode.Shooting ? Score : elapsed + (Mode == ChallengeMode.Combat ? MissedTargets * 8f + Mathf.Max(0, shots - hits) * 0.25f : 0f);
            ResultDetail = Mode switch
            {
                ChallengeMode.Shooting => $"Поражено целей — {destroyed}/{targets.Length}  •  Точность — {Accuracy:0} %",
                ChallengeMode.Combat => $"Заезд — {PlaygroundText.Seconds(elapsed)}\nОставшиеся цели — {MissedTargets} × 8 с\nПромахи — {Mathf.Max(0, shots - hits)} × 0,25 с",
                _ => "Все ворота пройдены"
            };
            Phase = SessionPhase.Results;
            if (PersistRecords) LocalRecords.Save(Mode, Result, trackId);
            Finished?.Invoke(Mode, Result);
        }

        private void Highlight()
        {
            for (int i = 0; i < checkpoints.Length; i++) checkpoints[i].Highlight(i == NextCheckpoint && Mode != ChallengeMode.Shooting && Mode != ChallengeMode.Free);
        }
    }
}
