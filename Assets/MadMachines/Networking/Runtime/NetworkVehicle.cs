using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    // Run before the unchanged VehiclePhysics.FixedUpdate so it consumes this step's command.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(VehiclePhysics))]
    public sealed class NetworkVehicle : NetworkBehaviour
    {
        private const double CommandTimeout = 0.25;
        private const float ShotInterval = 1.15f;
        [SerializeField] private Transform turret;
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform[] wheelMeshes;
        [SerializeField] private Renderer hullRenderer;
        [SerializeField] private NetworkBallisticProjectile projectilePrefab;
        private readonly NetworkVariable<int> slot = new NetworkVariable<int>();
        private readonly NetworkVariable<Vector3> aim = new NetworkVariable<Vector3>(Vector3.forward);
        private readonly NetworkVariable<float> steering = new NetworkVariable<float>();
        private readonly NetworkVariable<Vector4> wheelHeights = new NetworkVariable<Vector4>(Vector4.one * -0.65f);
        private readonly NetworkVariable<float> speed = new NetworkVariable<float>();
        private readonly NetworkVariable<int> hits = new NetworkVariable<int>();
        private readonly NetworkVariable<int> shots = new NetworkVariable<int>();
        private readonly NetworkCommandGate commandGate = new NetworkCommandGate();
        private VehiclePhysics vehicle;
        private NetworkVehicleCommand latest;
        private double lastCommandAt = double.NegativeInfinity;
        private double nextShot;
        private double nextSpeedUpdate;

        public int Slot => slot.Value;
        public int Hits => hits.Value;
        public int Shots => shots.Value;
        public float Speed => speed.Value;
        public Vector3 AimDirection => muzzle.forward;
        public Vector3 MuzzlePosition => muzzle.position;
        public Rigidbody Body => vehicle.Body;

        public void Configure(Transform mount, Transform gun, Transform end, Transform[] wheels, Renderer hull, NetworkBallisticProjectile projectile)
        {
            turret = mount;
            barrel = gun;
            muzzle = end;
            wheelMeshes = wheels;
            hullRenderer = hull;
            projectilePrefab = projectile;
        }

        private void Awake()
        {
            vehicle = GetComponent<VehiclePhysics>();
            // A prefab can exist for a frame before spawn: do not run local physics commands.
            vehicle.Authority = false;
            vehicle.enabled = false;
        }

        public void InitializeServer(int playerSlot)
        {
            slot.Value = playerSlot;
            aim.Value = transform.forward;
        }

        public override void OnNetworkSpawn()
        {
            vehicle.Authority = IsServer;
            vehicle.enabled = IsServer;
            ApplyAim(aim.Value, true);
            if (IsClient && hullRenderer != null)
            {
                Color color = Slot == 0 ? new Color(1f, 0.3f, 0.08f) : new Color(0.08f, 0.5f, 1f);
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
                hullRenderer.SetPropertyBlock(properties);
            }
        }

        public override void OnNetworkDespawn()
        {
            vehicle.Submit(default);
            vehicle.Authority = false;
            vehicle.enabled = false;
        }

        public void SendCommand(NetworkVehicleCommand command)
        {
            if (IsSpawned && IsClient && IsOwner) SubmitCommandRpc(command);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitCommandRpc(NetworkVehicleCommand command, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!commandGate.Accept(command, now)) return;
            latest = command;
            lastCommandAt = now;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool fresh = now - lastCommandAt <= CommandTimeout;
            vehicle.Submit(fresh ? new VehicleCommand
            {
                Throttle = latest.Throttle,
                Steering = latest.Steering,
                Handbrake = latest.Handbrake
            } : new VehicleCommand { Handbrake = true });
            steering.Value = fresh ? latest.Steering : 0f;
            wheelHeights.Value = new Vector4(vehicle.GetWheelPosition(0).y, vehicle.GetWheelPosition(1).y,
                vehicle.GetWheelPosition(2).y, vehicle.GetWheelPosition(3).y);
            if (fresh)
            {
                ApplyAim(latest.Aim, true);
                aim.Value = muzzle.forward;
                if (latest.Fire && now >= nextShot) Fire(now);
            }
            if (now >= nextSpeedUpdate)
            {
                speed.Value = vehicle.Speed;
                nextSpeedUpdate = now + 0.1;
            }
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsClient) return;
            ApplyAim(aim.Value, false);
            // Sandbox-only primitive wheels; the single-player visual system is not involved.
            if (wheelMeshes == null) return;
            for (int i = 0; i < wheelMeshes.Length; i++)
            {
                if (wheelMeshes[i] == null) continue;
                wheelMeshes[i].localRotation = Quaternion.Euler(0f, i < 2 ? steering.Value * 28f : 0f, 90f);
                Vector3 position = wheelMeshes[i].localPosition;
                position.y = Mathf.Lerp(position.y, wheelHeights.Value[i], 1f - Mathf.Exp(-20f * Time.deltaTime));
                wheelMeshes[i].localPosition = position;
            }
        }

        private void ApplyAim(Vector3 direction, bool immediate)
        {
            Vector3 local = transform.InverseTransformDirection(direction.normalized);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -45f, 15f);
            Quaternion targetYaw = Quaternion.Euler(0f, yaw, 0f);
            Quaternion targetPitch = Quaternion.Euler(pitch, 0f, 0f);
            float blend = immediate ? 1f : 1f - Mathf.Exp(-20f * Time.deltaTime);
            turret.localRotation = Quaternion.Slerp(turret.localRotation, targetYaw, blend);
            barrel.localRotation = Quaternion.Slerp(barrel.localRotation, targetPitch, blend);
        }

        private void Fire(double now)
        {
            nextShot = now + ShotInterval;
            // Do not allow the barrel to shoot through a wall or through another chassis.
            Vector3 path = muzzle.position - turret.position;
            foreach (RaycastHit hit in Physics.SphereCastAll(turret.position, NetworkBallisticProjectile.Radius,
                path.normalized, path.magnitude, 1, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody != Body) return;
            }
            foreach (Collider overlap in Physics.OverlapSphere(muzzle.position, NetworkBallisticProjectile.Radius, 1, QueryTriggerInteraction.Ignore))
            {
                if (overlap.attachedRigidbody != Body) return;
            }
            NetworkBallisticProjectile projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.LookRotation(muzzle.forward));
            projectile.NetworkObject.Spawn(true);
            projectile.Launch(this, muzzle.forward * 38f + Body.linearVelocity);
            Body.AddForceAtPosition(-muzzle.forward * 1600f, muzzle.position, ForceMode.Impulse);
            shots.Value++;
        }

        public void RegisterHit()
        {
            if (IsServer && IsSpawned) hits.Value++;
        }
    }
}
