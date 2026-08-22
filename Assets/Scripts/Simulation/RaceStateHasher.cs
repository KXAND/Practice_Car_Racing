namespace CarRacing.Simulation
{
    public static class RaceStateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(RaceState state)
        {
            ulong hash = OffsetBasis;
            hash = Append(hash, state.Frame);
            hash = Append(hash, state.Vehicle.PositionX);
            hash = Append(hash, state.Vehicle.PositionZ);
            hash = Append(hash, state.Vehicle.Speed);
            hash = Append(hash, state.Vehicle.HeadingTenths);
            hash = Append(hash, state.NextCheckpoint);
            hash = Append(hash, state.Lap);
            return Append(hash, (int)state.Phase);
        }

        private static ulong Append(ulong hash, int value)
        {
            unchecked
            {
                uint bits = (uint)value;
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(bits >> shift);
                    hash *= Prime;
                }
            }

            return hash;
        }
    }
}
