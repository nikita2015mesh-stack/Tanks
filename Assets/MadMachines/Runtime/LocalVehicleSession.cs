using UnityEngine;

namespace MadMachines
{
    [RequireComponent(typeof(ChallengeManager), typeof(LocalPlayerInput), typeof(PlaygroundHud))]
    public sealed class LocalVehicleSession : MonoBehaviour
    {
        [SerializeField] private VehicleDefinition vehicle;
        [SerializeField] private Transform startPoint;
        [SerializeField] private VehicleCamera view;
        [SerializeField] private WeaponDefinition guidedRocket;
        [SerializeField] private WeaponDefinition unguidedRocket;

        public void Configure(VehicleDefinition definition, Transform start, VehicleCamera camera)
        {
            vehicle = definition;
            startPoint = start;
            view = camera;
        }

        private void Awake()
        {
            ChallengeManager rules = GetComponent<ChallengeManager>();
            LocalPlayerInput input = GetComponent<LocalPlayerInput>();
            PlaygroundHud hud = GetComponent<PlaygroundHud>();
            VehicleDefinition selected = GameFlow.LocalLaunchPending ? GameFlow.CurrentVehicle : vehicle;
            VehiclePhysics prefab = selected != null ? selected.Prefab : null;
            if (startPoint == null || view == null || prefab == null ||
                !prefab.gameObject.activeSelf || !prefab.TryGetComponent<WeaponSystem>(out _))
            {
                Debug.LogError("Для запуска сессии нужны активный префаб машины с оружием, точка старта и камера.", this);
                rules.enabled = false;
                input.enabled = false;
                hud.enabled = false;
                return;
            }

            VehiclePhysics car = Instantiate(prefab, startPoint.position, startPoint.rotation, transform);
            car.name = prefab.name;
            if (!Application.isBatchMode) car.gameObject.AddComponent<VehicleFeedback>().Bind(car);
            if (GameFlow.LocalLaunchPending) car.DriveMode = GameFlow.SelectedDrive;
            WeaponSystem weapons = car.GetComponent<WeaponSystem>();
            weapons.EnableGuidedRocket(guidedRocket);
            weapons.EnableUnguidedRocket(unguidedRocket);
            rules.BindParticipant(car, startPoint);
            if (TryGetComponent(out ShootingRange range)) range.Bind(car);
            view.Configure(car.transform);
            view.ResetView();
            input.Configure(car, weapons, view, rules, range);
            hud.Configure(input, car, weapons, rules);
        }
    }
}
