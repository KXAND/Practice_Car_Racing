namespace CarRacing.Simulation
{
    public readonly struct QuantizedInput
    {
        public const int MaxSteering = 127;
        public const int MaxPedal = 127;

        public static QuantizedInput Empty => new(0, 0, 0);

        public QuantizedInput(int steering, int throttle, int brake)
        {
            Steering = (sbyte)Clamp(steering, -MaxSteering, MaxSteering);
            Throttle = (byte)Clamp(throttle, 0, MaxPedal);
            Brake = (byte)Clamp(brake, 0, MaxPedal);
        }

        public sbyte Steering { get; }

        public byte Throttle { get; }

        public byte Brake { get; }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }
    }
}
