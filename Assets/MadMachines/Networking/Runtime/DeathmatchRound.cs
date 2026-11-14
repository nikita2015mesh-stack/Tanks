using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    internal sealed class DeathmatchRound
    {
        private const double Duration = 300;
        private const double ResultsRetention = 90;
        internal const double RespawnDelay = 3;
        internal const int MaxHealth = 1000;

        internal sealed class Participant
        {
            public Guid Ticket;
            public ulong? ClientId;
            public VehicleDrive Drive;
            public NetworkObject Vehicle;
            public double RespawnAt;
            public float Health = MaxHealth;
            public DeathmatchStanding Standing;
            public bool IsBot => Standing.isBot;
        }

        private readonly double endsAt;
        private readonly List<Participant> participants = new List<Participant>(MatchLobbyServer.RequiredPlayers);
        private double finishedAt;

        public DeathmatchRound(double now) => endsAt = now + Duration;

        public IReadOnlyList<Participant> Participants => participants;
        public string Result { get; private set; }
        public bool Finished => Result != null;
        public bool CanFight(double now) => !Finished && now < endsAt;
        public bool ResultsExpired(double now) => Finished && now >= finishedAt + ResultsRetention;

        public void Add(Guid ticket, ulong? clientId, int slot, VehicleDrive drive, NetworkObject vehicle)
        {
            participants.Add(new Participant
            {
                Ticket = ticket,
                ClientId = clientId,
                Drive = drive,
                Vehicle = vehicle,
                Standing = new DeathmatchStanding { slot = slot, health = MaxHealth, isBot = !clientId.HasValue }
            });
            participants.Sort((first, second) => first.Standing.slot.CompareTo(second.Standing.slot));
        }

        public Participant Find(Guid ticket) => participants.Find(item => item.Ticket == ticket);
        public Participant Find(int slot) => participants.Find(item => item.Standing.slot == slot);

        public void Hit(Participant target, int shooterSlot, float damage, double now)
        {
            if (!CanFight(now) || target.Health <= 0 || !(damage > 0)) return;
            target.Health = Mathf.Max(0, target.Health - damage);
            target.Standing.health = Mathf.CeilToInt(target.Health);
            if (target.Health > 0) return;
            target.Standing.deaths++;
            target.RespawnAt = now + RespawnDelay;
            if (target.ClientId.HasValue || target.IsBot) target.Standing.status = DeathmatchPlayerStatus.Respawning;
            Participant shooter = Find(shooterSlot);
            if (shooter != null && shooter != target) shooter.Standing.kills++;
        }

        public void Advance(double now)
        {
            if (Finished || now < endsAt) return;
            Participant winner = null;
            bool tied = false;
            foreach (Participant participant in participants)
            {
                if (participant.Standing.status == DeathmatchPlayerStatus.Left) continue;
                if (winner == null || participant.Standing.kills > winner.Standing.kills)
                {
                    winner = participant;
                    tied = false;
                }
                else if (participant.Standing.kills == winner.Standing.kills) tied = true;
            }
            string outcome = winner == null || tied ? "Ничья" : $"{winner.Standing.Name} победил";
            Finish(outcome, "Время вышло.", now);
        }

        public void PlayerLeft(Participant participant, double now)
        {
            if (Finished) return;
            participant.Standing.status = DeathmatchPlayerStatus.Left;
            Participant remaining = null;
            int count = 0;
            foreach (Participant item in participants)
            {
                if (item.Standing.status == DeathmatchPlayerStatus.Left) continue;
                remaining = item;
                count++;
            }
            if (count == 1) Finish($"Техническая победа — {remaining.Standing.Name}", "Соперник покинул матч.", now);
        }

        private void Finish(string outcome, string reason, double now)
        {
            Result = $"{outcome}\n{reason}";
            finishedAt = now;
        }

        public void Describe(ServerSessionInfo info, double now)
        {
            info.maxHealth = MaxHealth;
            info.secondsLeft = Mathf.Max(0, Mathf.CeilToInt((float)(endsAt - now)));
            info.result = Result;
            info.standings = new DeathmatchStanding[participants.Count];
            for (int i = 0; i < participants.Count; i++) info.standings[i] = participants[i].Standing;
        }
    }
}
