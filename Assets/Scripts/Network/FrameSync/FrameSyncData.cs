namespace CarRacing.Network.FrameSync
{
    using CarRacing.Race;

    public readonly struct InputFrame
    {
        public InputFrame(int frameIndex, byte playerId, QuantizedInput input)
        {
            FrameIndex = frameIndex;
            PlayerId = playerId;
            Input = input;
        }

        public int FrameIndex { get; }
        public byte PlayerId { get; }
        public QuantizedInput Input { get; }
    }

    public struct RaceFrame
    {
        public RaceFrame(RaceState frameStartState, RaceInputs inputs)
            : this(
                frameStartState.FrameIndex,
                inputs,
                frameStartState.Phase,
                frameStartState.Player0.Status,
                frameStartState.Player1.Status,
                frameStartState.WinnerPlayerId)
        {
        }

        public RaceFrame(
            int frameIndex,
            RaceInputs inputs,
            RacePhase phase,
            PlayerStatus player0Status,
            PlayerStatus player1Status,
            byte winnerPlayerId)
        {
            FrameIndex = frameIndex;
            Inputs = inputs;
            Phase = phase;
            Player0Status = player0Status;
            Player1Status = player1Status;
            WinnerPlayerId = winnerPlayerId;
        }

        public int FrameIndex;
        public RaceInputs Inputs;
        public RacePhase Phase;
        public PlayerStatus Player0Status;
        public PlayerStatus Player1Status;
        public byte WinnerPlayerId;
        public QuantizedInput Player0 => Inputs.Player0;
        public QuantizedInput Player1 => Inputs.Player1;

        public RaceState ApplyFrameStartState(RaceState state)
        {
            PlayerRaceState player0 = new(
                state.Player0.Vehicle,
                state.Player0.NextCheckpoint,
                state.Player0.Lap,
                Player0Status);
            PlayerRaceState player1 = new(
                state.Player1.Vehicle,
                state.Player1.NextCheckpoint,
                state.Player1.Lap,
                Player1Status);
            return new RaceState(FrameIndex, Phase, WinnerPlayerId, player0, player1);
        }
    }

    public readonly struct FrameSyncDebugInfo
    {
        public FrameSyncDebugInfo(byte playerId, int localFrameIndex, int confirmedFrameIndex, int predictionDepth, int rollbackCount, int lastRollbackLength, ulong confirmedHash, bool hashMismatch)
        {
            PlayerId = playerId;
            LocalFrameIndex = localFrameIndex;
            ConfirmedFrameIndex = confirmedFrameIndex;
            PredictionDepth = predictionDepth;
            RollbackCount = rollbackCount;
            LastRollbackLength = lastRollbackLength;
            ConfirmedHash = confirmedHash;
            HashMismatch = hashMismatch;
        }

        public byte PlayerId { get; }
        public int LocalFrameIndex { get; }
        public int ConfirmedFrameIndex { get; }
        public int PredictionDepth { get; }
        public int RollbackCount { get; }
        public int LastRollbackLength { get; }
        public ulong ConfirmedHash { get; }
        public bool HashMismatch { get; }
    }
}
