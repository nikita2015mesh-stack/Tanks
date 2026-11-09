using UnityEngine;

namespace MadMachines
{
    [RequireComponent(typeof(ChallengeManager), typeof(LocalPlayerInput), typeof(PlaygroundHud))]
    public sealed class LocalVehicleSession : MonoBehaviour
    {
        [SerializeField] private VehicleDefinition vehicle;
        [SerializeField] private Transform startPoint;
        [SerializeField] private VehicleCamera view;

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
            VehiclePhysics prefab = vehicle != null ? vehicle.Prefab : null;
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
            WeaponSystem weapons = car.GetComponent<WeaponSystem>();
            rules.BindParticipant(car, startPoint);
            view.Configure(car.transform);
            view.ResetView();
            input.Configure(car, weapons, view, rules);
            hud.Configure(input, car, weapons, rules);
        }
    }
}
