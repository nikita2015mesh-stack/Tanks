using UnityEngine;
using UnityEngine.InputSystem;

namespace MadMachines
{
    public sealed class LocalPlayerInput : MonoBehaviour
    {
        [SerializeField] private VehiclePhysics vehicle;
        [SerializeField] private WeaponSystem weapons;
        [SerializeField] private VehicleCamera view;
        [SerializeField] private ChallengeManager rules;
        public bool Paused { get; private set; }
        public bool Menu { get; private set; } = true;

        public void Configure(VehiclePhysics car, WeaponSystem gun, VehicleCamera camera, ChallengeManager session)
        {
            vehicle = car; weapons = gun; view = camera; rules = session;
        }

        private void Start() { weapons.Fired += view.Shake; SetCursor(); }

        public void Begin(ChallengeMode mode)
        {
            Paused = Menu = false;
            rules.Begin(mode);
            view.ResetView();
            SetCursor();
        }

        public void Resume() { Paused = false; Time.timeScale = 1f; SetCursor(); }
        public void OpenMenu() { Menu = true; Paused = false; Time.timeScale = 1f; rules.Begin(ChallengeMode.Free); SetCursor(); }
        private void SetCursor()
        {
            bool locked = !Menu && !Paused && rules.Phase != SessionPhase.Results;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame && !Menu && rules.Phase != SessionPhase.Results)
            {
                Paused = !Paused;
                Time.timeScale = Paused ? 0f : 1f;
                SetCursor();
            }
            if (Menu || Paused || rules.Phase == SessionPhase.Results)
            {
                vehicle.Submit(default);
                SetCursor();
                return;
            }
            view.Look(mouse.delta.ReadValue());
            weapons.Aim(view.AimDirection);
            if (rules.Phase == SessionPhase.Countdown) return;
            vehicle.Submit(new VehicleCommand
            {
                Throttle = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f),
                Steering = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                Handbrake = keyboard.spaceKey.isPressed
            });
            if (keyboard.digit1Key.wasPressedThisFrame) weapons.Select(0);
            if (keyboard.digit2Key.wasPressedThisFrame) weapons.Select(1);
            if (keyboard.digit3Key.wasPressedThisFrame) weapons.Select(2);
            if (mouse.leftButton.isPressed) weapons.RequestFire();
            if (keyboard.rKey.wasPressedThisFrame || vehicle.transform.position.y < -15f) rules.Recover();
        }

        private void OnDisable() { Time.timeScale = 1f; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
