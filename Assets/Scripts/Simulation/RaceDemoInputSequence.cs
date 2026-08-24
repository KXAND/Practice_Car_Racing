namespace CarRacing.Simulation
{
    public static class RaceDemoInputSequence
    {
        public static QuantizedInput ForFrame(int frame)
        {
            bool turnLeft = IsTurnFrame(frame);
            return new QuantizedInput(
                turnLeft ? -QuantizedInput.MaxSteering : 0,
                QuantizedInput.MaxPedal,
                0);
        }

        private static bool IsTurnFrame(int frame)
        {
            return (frame >= 76 && frame < 110) ||
                   (frame >= 123 && frame < 160) ||
                   (frame >= 209 && frame < 248) ||
                   (frame >= 263 && frame < 288);
        }
    }
}
