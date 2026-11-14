using System;
using Unity.Networking.Transport;
using UnityEngine;

namespace MadMachines.Networking
{
    internal sealed class MatchLobbyClient : IDisposable
    {
        private NetworkDriver driver;
        private NetworkPipeline pipeline;
        private NetworkConnection connection;
        private NetworkEndpoint endpoint;
        private double nextAttempt;
        private double nextUpdate;
        private double lastReply;
        private bool previousRunInBackground;
        public string Address { get; private set; }
        public ushort Port { get; private set; }
        public bool Connected => driver.IsCreated && connection.IsCreated && Time.realtimeSinceStartupAsDouble - lastReply < 4;
        private MatchIntent intent;
        public bool WantsMatch => intent != MatchIntent.None;
        public bool WantsBot => intent == MatchIntent.Bot;
        public MatchLobbyPhase Phase { get; private set; }
        public int Players { get; private set; }
        public int RequiredPlayers { get; private set; } = MatchLobbyServer.RequiredPlayers;

        public bool Configure(string address, string port)
        {
            if (!NetworkLaunchOptions.IsClientAddress(address) || !ushort.TryParse(port, out ushort number) ||
                number == 0 || number == ushort.MaxValue ||
                !NetworkEndpoint.TryParse(address, (ushort)(number + 1), out NetworkEndpoint destination, NetworkFamily.Ipv4)) return false;
            if (driver.IsCreated && Address == address && Port == number) return true;

            Dispose();
            Address = address;
            Port = number;
            endpoint = destination;
            driver = MatchLobbyServer.CreateDriver();
            pipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            nextAttempt = 0;
            return true;
        }

        public void SetWaiting(bool waiting, bool withBot = false)
        {
            intent = waiting ? (withBot ? MatchIntent.Bot : MatchIntent.Players) : MatchIntent.None;
            nextUpdate = 0;
            if (!waiting) Players = 0;
            SendIntent();
        }

        public void Update()
        {
            if (!driver.IsCreated) return;
            driver.ScheduleUpdate().Complete();
            double now = Time.realtimeSinceStartupAsDouble;
            if (!connection.IsCreated)
            {
                if (now < nextAttempt) return;
                connection = driver.Connect(endpoint);
                lastReply = double.NegativeInfinity;
                nextAttempt = now + 2;
                return;
            }

            NetworkEvent.Type type;
            while ((type = connection.PopEvent(driver, out var packet)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Disconnect)
                {
                    connection = default;
                    nextAttempt = now + 2;
                    Phase = MatchLobbyPhase.Waiting;
                    Players = 0;
                    if (GameFlow.MatchTicket == Guid.Empty) intent = MatchIntent.None;
                    else if (GameFlow.Loading)
                    {
                        intent = MatchIntent.None;
                        GameFlow.CancelMatchPreparation("Связь с сервером потеряна до старта.");
                    }
                    break;
                }
                if (type == NetworkEvent.Type.Data && packet.Length >= 4 && packet.ReadByte() == MatchLobbyServer.Protocol)
                {
                    Phase = (MatchLobbyPhase)packet.ReadByte();
                    Players = packet.ReadByte();
                    RequiredPlayers = packet.ReadByte();
                    lastReply = now;
                    if (Phase == MatchLobbyPhase.Loading && WantsMatch && GameFlow.MatchTicket == Guid.Empty && packet.Length == 20)
                    {
                        byte[] ticket = new byte[16];
                        packet.ReadBytes(ticket.AsSpan());
                        GameFlow.StartMatch(Address, Port, new Guid(ticket));
                    }
                    else if (Phase == MatchLobbyPhase.Failed)
                    {
                        SetWaiting(false);
                        GameFlow.CancelMatchPreparation("Сервер не смог начать матч. Подайте заявку ещё раз.");
                    }
                    else if (Phase == MatchLobbyPhase.Waiting && packet.Length == 4)
                    {
                        GameFlow.CancelMatchPreparation();
                    }
                }
            }
            if (now >= nextUpdate) SendIntent();
        }

        private void SendIntent()
        {
            if (!driver.IsCreated || !connection.IsCreated ||
                connection.GetState(driver) != NetworkConnection.State.Connected ||
                driver.BeginSend(pipeline, connection, out var writer) != 0) return;
            writer.WriteByte(MatchLobbyServer.Protocol);
            writer.WriteByte((byte)intent);
            if (GameFlow.MatchTicket != Guid.Empty)
                foreach (byte value in GameFlow.MatchTicket.ToByteArray()) writer.WriteByte(value);
            driver.EndSend(writer);
            nextUpdate = Time.realtimeSinceStartupAsDouble + 0.5;
        }

        public void Dispose()
        {
            if (driver.IsCreated)
            {
                if (connection.IsCreated) connection.Disconnect(driver);
                driver.ScheduleUpdate().Complete();
                driver.Dispose();
                driver = default;
                Application.runInBackground = previousRunInBackground;
            }
            connection = default;
            intent = MatchIntent.None;
            Players = 0;
            Phase = MatchLobbyPhase.Waiting;
            lastReply = double.NegativeInfinity;
        }
    }
}
