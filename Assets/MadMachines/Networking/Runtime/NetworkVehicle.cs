using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    // Run before VehiclePhysics.FixedUpdate so it consumes this step's command.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(VehiclePhysics))]
    public sealed class NetworkVehicle : NetworkBehaviour
    {
        // The T100 chassis uses Ignore Raycast so its own suspension probes miss it.
        internal const int HitLayers = (1 << 0) | (1 << 2);
        [SerializeField] private VehicleDefinition definition;
        [SerializeField] private NetworkBallisticProjectile projectilePrefab;
        private readonly NetworkVariable<ulong> controller = new NetworkVariable<ulong>(NetworkManager.ServerClientId);
        private readonly NetworkVariable<int> slot = new NetworkVariable<int>();
        private readonly NetworkVariable<float> speed = new NetworkVariable<float>();
        private readonly NetworkVariable<int> hits = new NetworkVariable<int>();
        private readonly NetworkVariable<int> shots = new NetworkVariable<int>();
        private readonly NetworkCommandBuffer commands = new NetworkCommandBuffer();
        private readonly int[] rejected = new int[7];
        private VehiclePhysics vehicle;
        private VehicleVisuals visuals;
        private WeaponSystem weapons;
        private WeaponSystem.ProjectileLauncher launcher;
        private double nextSpeedUpdate;
        private double nextDiagnostic;
        private int accepted;
        private int timeouts;
        private bool hadFreshInput;
        private int initialSlot;
        private ulong initialController;

        public string CommandDiagnostics =>
            $"accepted={accepted}, rate_limited={rejected[(int)CommandRejection.Rate]}, " +
            $"invalid={rejected[(int)CommandRejection.Values]}, out_of_order={rejected[(int)CommandRejection.Sequence]}, " +
            $"wrong_sender={rejected[(int)CommandRejection.Sender]}, inactive={rejected[(int)CommandRejection.Inactive]}, timeouts={timeouts}";

        public ulong ControllerClientId => controller.Value;
        public bool IsLocalController => IsSpawned && IsClient && ControllerClientId == NetworkManager.LocalClientId;
        public int Slot => slot.Value;
        public int Hits => hits.Value;
        public int Shots => shots.Value;
        public float Speed => speed.Value;
        public string DisplayName => definition.DisplayName;
        public Vector3 MuzzlePosition => weapons.MuzzlePosition;
        public Rigidbody Body => vehicle.Body;

        public void Configure(VehicleDefinition vehicleDefinition, NetworkBallisticProjectile projectile)
        {
            definition = vehicleDefinition;
            projectilePrefab = projectile;
        }

        public void ValidateConfiguration(GameObject expectedProjectile)
        {
            VehiclePhysics physics = GetComponent<VehiclePhysics>();
            VehicleVisuals presentation = GetComponentInChildren<VehicleVisuals>(true);
            WeaponSystem armament = GetComponent<WeaponSystem>();
            if (definition == null || definition.Prefab == null || definition.Prefab.WheelCount != physics.WheelCount ||
                presentation == null || presentation.Vehicle != physics || presentation.WheelAnchorCount != physics.WheelCount ||
                physics.WheelCount > default(NetworkPhysicsPose).Wheels.Capacity)
                throw new System.InvalidOperationException("NetworkVehicle requires a shared vehicle definition and visual bindings for every wheel.");
            if (projectilePrefab == null || projectilePrefab.gameObject != expectedProjectile ||
                projectilePrefab.Definition == null || armament == null || !armament.HasValidMount ||
                armament.IndexOf(projectilePrefab.Definition) < 0)
                throw new System.InvalidOperationException("NetworkVehicle requires its registered projectile and a matching weapon on the shared vehicle.");
        }

        private void Awake()
        {
            vehicle = GetComponent<VehiclePhysics>();
            visuals = GetComponentInChildren<VehicleVisuals>(true);
            weapons = GetComponent<WeaponSystem>();
            launcher = LaunchProjectile;
            weapons.Select(weapons.IndexOf(projectilePrefab.Definition));
            // A prefab can exist for a frame before spawn: do not run local physics commands.
            vehicle.Authority = false;
            vehicle.enabled = false;
            weapons.Authority = false;
            visuals.enabled = false;
        }

        public void InitializeServer(int playerSlot, ulong clientId)
        {
            initialSlot = playerSlot;
            initialController = clientId;
        }

        public override void OnNetworkSpawn()
        {
            commands.Reset();
            System.Array.Clear(rejected, 0, rejected.Length);
            accepted = timeouts = 0;
            hadFreshInput = false;
            weapons.ResetReload();
            nextSpeedUpdate = 0;
            nextDiagnostic = Time.realtimeSinceStartupAsDouble + 5;
            vehicle.Authority = IsServer;
            vehicle.enabled = IsServer;
            weapons.Authority = IsServer;
            if (IsServer)
            {
                controller.Value = initialController;
                slot.Value = initialSlot;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Debug.Log($"[MM NET] Vehicle {NetworkObjectId} final command counts: {CommandDiagnostics}.");
            commands.Reset();
            vehicle.Submit(default);
            vehicle.Authority = false;
            vehicle.enabled = false;
            weapons.Authority = false;
        }

        public void SendCommand(NetworkVehicleCommand command)
        {
            if (IsLocalController && isActiveAndEnabled && !NetworkManager.ShutdownInProgress) SubmitCommandRpc(command);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCommandRpc(NetworkVehicleCommand command, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool active = IsSpawned && isActiveAndEnabled && !NetworkManager.ShutdownInProgress &&
                ControllerClientId != NetworkManager.ServerClientId && NetworkManager.ConnectedClients.ContainsKey(ControllerClientId);
            CommandRejection rejection = commands.Receive(command, now, rpcParams.Receive.SenderClientId, ControllerClientId, active);
            if (rejection == CommandRejection.None) accepted++;
            else rejected[(int)rejection]++;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || NetworkManager.ShutdownInProgress) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool fresh = commands.Sample(now, out NetworkVehicleCommand latest);
            if (!fresh && hadFreshInput) timeouts++;
            hadFreshInput = fresh;
            vehicle.Submit(fresh ? new VehicleCommand
            {
                Throttle = latest.Throttle,
                Steering = latest.Steering,
                Handbrake = latest.Handbrake
            } : new VehicleCommand { Handbrake = true });
            if (fresh)
            {
                weapons.Aim(latest.Aim);
                if (latest.Fire && weapons.RequestFire(launcher))
                {
                    shots.Value++;
                    ShowMuzzleFlashRpc(weapons.MuzzlePosition);
                }
            }
            if (now >= nextSpeedUpdate)
            {
                speed.Value = vehicle.Speed;
                nextSpeedUpdate = now + 0.1;
            }
            if (now >= nextDiagnostic)
            {
                Debug.Log($"[MM NET] Vehicle {NetworkObjectId}, client {ControllerClientId}: {CommandDiagnostics}.");
                nextDiagnostic = now + 5;
            }
        }

        private void OnDisable()
        {
            // Retain sequence/rate protection across disable/enable of this same spawn.
            commands.ClearInput();
            if (vehicle == null) return;
            vehicle.Submit(new VehicleCommand { Handbrake = true });
            vehicle.Authority = false;
            vehicle.enabled = false;
            weapons.Authority = false;
        }

        private void OnEnable()
        {
            if (!IsSpawned || vehicle == null) return;
            vehicle.Authority = IsServer;
            vehicle.enabled = IsServer;
            weapons.Authority = IsServer;
        }

        internal void CapturePresentation(ref NetworkPhysicsPose pose)
        {
            pose.AimAngles = weapons.AimAngles;
            for (int i = 0; i < vehicle.WheelCount; i++)
            {
                VehicleWheelState wheel = vehicle.GetWheelVisualState(i);
                pose.Wheels.Add(new Vector3(wheel.Extension, wheel.Steering, wheel.Rotation));
            }
        }

        internal void ApplyPresentation(NetworkPhysicsPose pose)
        {
            if (pose.Wheels.Length != vehicle.WheelCount) return;
            weapons.ApplyAimAngles(pose.AimAngles);
            for (int i = 0; i < pose.Wheels.Length; i++)
            {
                Vector3 wheel = pose.Wheels[i];
                visuals.ApplyWheelPose(i, new VehicleWheelState { Extension = wheel.x, Steering = wheel.y, Rotation = wheel.z });
            }
        }

        private bool LaunchProjectile(WeaponDefinition weapon, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId)
        {
            // Do not allow the barrel to shoot through a wall or through another chassis.
            Vector3 mount = weapons.MountPosition;
            Vector3 path = position - mount;
            // SphereCast does not report colliders already overlapping its starting sphere.
            foreach (Collider overlap in Physics.OverlapSphere(mount, weapon.Size, HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (overlap.attachedRigidbody != Body) return false;
            }
            foreach (RaycastHit hit in Physics.SphereCastAll(mount, weapon.Size, path.normalized,
                path.magnitude, HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != Body) return false;
            }
            foreach (Collider overlap in Physics.OverlapSphere(position, weapon.Size, HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (overlap.attachedRigidbody != Body) return false;
            }
            NetworkBallisticProjectile projectile = null;
            try
            {
                projectile = Instantiate(projectilePrefab, position, Quaternion.LookRotation(direction));
                projectile.NetworkObject.Spawn(true);
                if (!projectile.IsSpawned) throw new System.InvalidOperationException("Netcode did not spawn the projectile.");
                projectile.Launch(this, owner, direction, shotId);
            }
            catch (System.Exception error)
            {
                if (projectile != null)
                {
                    if (projectile.NetworkObject.IsSpawned) projectile.NetworkObject.Despawn(true);
                    else Destroy(projectile.gameObject);
                }
                Debug.LogError($"[MM NET] Could not spawn a projectile for vehicle {NetworkObjectId}: {error}");
                return false;
            }
            return true;
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ShowMuzzleFlashRpc(Vector3 position)
        {
            if (IsClient && !IsServer && !NetworkManager.ShutdownInProgress)
                PlaygroundEffects.Flash(position, projectilePrefab.Definition.Color, 0.8f, 0.1f);
        }

        public void RegisterHit()
        {
            if (IsServer && IsSpawned) hits.Value++;
        }
    }
}
