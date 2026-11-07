using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    // Run before the unchanged VehiclePhysics.FixedUpdate so it consumes this step's command.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(VehiclePhysics))]
    public sealed class NetworkVehicle : NetworkBehaviour
    {
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
        private readonly NetworkCommandBuffer commands = new NetworkCommandBuffer();
        private readonly NetworkShotCooldown shotCooldown = new NetworkShotCooldown();
        private readonly int[] rejected = new int[7];
        private VehiclePhysics vehicle;
        private double nextSpeedUpdate;
        private double nextDiagnostic;
        private int accepted;
        private int timeouts;
        private bool hadFreshInput;
        private int initialSlot;

        public string CommandDiagnostics => $"принято {accepted}; лимит {rejected[(int)CommandRejection.Rate]}; значения {rejected[(int)CommandRejection.Values]}; порядок {rejected[(int)CommandRejection.Sequence]}; владелец {rejected[(int)CommandRejection.Sender]}; неактивна {rejected[(int)CommandRejection.Inactive]}; тайм-ауты {timeouts}";

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

        public void ValidateConfiguration(GameObject expectedProjectile)
        {
            if (turret == null || barrel == null || muzzle == null || hullRenderer == null ||
                projectilePrefab == null || projectilePrefab.gameObject != expectedProjectile ||
                wheelMeshes == null || wheelMeshes.Length != 4)
                throw new System.InvalidOperationException("NetworkVehicle: отсутствуют ссылки башни, дула, корпуса, четырёх колёс или зарегистрированного снаряда.");
            if (!turret.IsChildOf(transform) || !barrel.IsChildOf(turret) || !muzzle.IsChildOf(barrel))
                throw new System.InvalidOperationException("NetworkVehicle: нарушена иерархия башни, ствола и дула.");
            foreach (Transform wheel in wheelMeshes)
                if (wheel == null || !wheel.IsChildOf(transform))
                    throw new System.InvalidOperationException("NetworkVehicle: колесо не принадлежит машине.");
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
            initialSlot = playerSlot;
        }

        public override void OnNetworkSpawn()
        {
            commands.Reset();
            System.Array.Clear(rejected, 0, rejected.Length);
            accepted = timeouts = 0;
            hadFreshInput = false;
            shotCooldown.Reset();
            nextSpeedUpdate = 0;
            nextDiagnostic = Time.realtimeSinceStartupAsDouble + 5;
            vehicle.Authority = IsServer;
            vehicle.enabled = IsServer;
            if (IsServer)
            {
                slot.Value = initialSlot;
                aim.Value = transform.forward;
            }
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
            if (IsServer) Debug.Log($"[MM NET] Машина {NetworkObjectId}, итог команд: {CommandDiagnostics}.");
            commands.Reset();
            vehicle.Submit(default);
            vehicle.Authority = false;
            vehicle.enabled = false;
        }

        public void SendCommand(NetworkVehicleCommand command)
        {
            if (IsSpawned && isActiveAndEnabled && IsClient && IsOwner && !NetworkManager.ShutdownInProgress) SubmitCommandRpc(command);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitCommandRpc(NetworkVehicleCommand command, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool active = IsSpawned && isActiveAndEnabled && !NetworkManager.ShutdownInProgress &&
                NetworkManager.ConnectedClients.TryGetValue(OwnerClientId, out NetworkClient client) && client.PlayerObject == NetworkObject;
            CommandRejection rejection = commands.Receive(command, now, rpcParams.Receive.SenderClientId, OwnerClientId, active);
            if (rejection == CommandRejection.None) accepted++;
            else rejected[(int)rejection]++;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer) return;
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
            steering.Value = fresh ? latest.Steering : 0f;
            wheelHeights.Value = new Vector4(vehicle.GetWheelPosition(0).y, vehicle.GetWheelPosition(1).y,
                vehicle.GetWheelPosition(2).y, vehicle.GetWheelPosition(3).y);
            if (fresh)
            {
                ApplyAim(latest.Aim, true);
                aim.Value = muzzle.forward;
                if (latest.Fire) Fire(now);
            }
            if (now >= nextSpeedUpdate)
            {
                speed.Value = vehicle.Speed;
                nextSpeedUpdate = now + 0.1;
            }
            if (now >= nextDiagnostic)
            {
                Debug.Log($"[MM NET] Машина {NetworkObjectId}, владелец {OwnerClientId}: {CommandDiagnostics}.");
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
        }

        private void OnEnable()
        {
            if (!IsSpawned || vehicle == null) return;
            vehicle.Authority = IsServer;
            vehicle.enabled = IsServer;
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsClient) return;
            ApplyAim(aim.Value, false);
            // Sandbox-only primitive wheels; the single-player visual system is not involved.
            if (wheelMeshes == null) return;
            for (int i = 0; i < Mathf.Min(4, wheelMeshes.Length); i++)
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
            if (!shotCooldown.TryBegin(now)) return;
            // Do not allow the barrel to shoot through a wall or through another chassis.
            Vector3 path = muzzle.position - turret.position;
            // SphereCast does not report colliders already overlapping its starting sphere.
            foreach (Collider overlap in Physics.OverlapSphere(turret.position, NetworkBallisticProjectile.Radius, 1, QueryTriggerInteraction.Ignore))
            {
                if (overlap.attachedRigidbody != Body) return;
            }
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
            try
            {
                projectile.NetworkObject.Spawn(true);
                projectile.Launch(this, muzzle.forward * 38f + Body.linearVelocity);
            }
            catch (System.Exception error)
            {
                if (projectile.NetworkObject.IsSpawned) projectile.NetworkObject.Despawn(true);
                else Destroy(projectile.gameObject);
                Debug.LogError($"[MM NET] Ошибка создания снаряда машины {NetworkObjectId}: {error}");
                return;
            }
            Body.AddForceAtPosition(-muzzle.forward * 1600f, muzzle.position, ForceMode.Impulse);
            shots.Value++;
        }

        public void RegisterHit()
        {
            if (IsServer && IsSpawned) hits.Value++;
        }
    }
}
