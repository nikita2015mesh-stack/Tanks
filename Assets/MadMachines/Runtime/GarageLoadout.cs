namespace MadMachines
{
    internal enum GarageWeapon
    {
        Factory, Cannon, HeavyCannon, Machinegun, GrenadeLauncher, GuidedRocket, UnguidedRocket
    }

    internal enum GarageEngine { Standard, Economy, Sport }
    internal enum GarageRunningGear { Standard, Road, OffRoad }

    internal struct GarageLoadout
    {
        public GarageWeapon Weapon;
        public GarageEngine Engine;
        public GarageRunningGear RunningGear;
        public string TurretId;

        public bool Matches(GarageLoadout other) =>
            Weapon == other.Weapon && Engine == other.Engine && RunningGear == other.RunningGear &&
            (TurretId ?? "") == (other.TurretId ?? "");
    }
}
