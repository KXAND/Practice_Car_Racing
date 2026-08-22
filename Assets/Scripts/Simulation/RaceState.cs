namespace CarRacing.Simulation
{
    public enum RacePhase : byte
    {
        Racing,
        Finished
    }

    public readonly struct VehicleState
    {
        public VehicleState(int positionX, int positionZ, int speed, int headingTenths)
        {
            PositionX = positionX;
            PositionZ = positionZ;
            Speed = speed;
            HeadingTenths = headingTenths;
        }

        public int PositionX { get; }

        public int PositionZ { get; }

        public int Speed { get; }

        public int HeadingTenths { get; }
    }

    public readonly struct RaceState
    {
        public const int DefaultStartX = 0;
        public const int DefaultStartZ = -8_000;

        public RaceState(int frame, VehicleState vehicle, int nextCheckpoint, int lap, RacePhase phase)
        {
            Frame = frame;
            Vehicle = vehicle;
            NextCheckpoint = nextCheckpoint;
            Lap = lap;
            Phase = phase;
        }

        public int Frame { get; }

        public VehicleState Vehicle { get; }

        public int NextCheckpoint { get; }

        public int Lap { get; }

        public RacePhase Phase { get; }

        public static RaceState CreateDefault()
        {
            return new RaceState(0, new VehicleState(DefaultStartX, DefaultStartZ, 0, 900), 0, 0, RacePhase.Racing);
        }
    }
}
