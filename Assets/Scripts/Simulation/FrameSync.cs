using System.Collections.Generic;

namespace CarRacing.Simulation
{
    public readonly struct InputFrame
    {
        public InputFrame(int frame, byte playerId, QuantizedInput input)
        {
            Frame = frame;
            PlayerId = playerId;
            Input = input;
        }

        public int Frame { get; }
        public byte PlayerId { get; }
        public QuantizedInput Input { get; }
    }

    public readonly struct ConfirmedFrame
    {
        public ConfirmedFrame(int frame, QuantizedInput player0, QuantizedInput player1)
        {
            Frame = frame;
            Player0 = player0;
            Player1 = player1;
        }

        public int Frame { get; }
        public QuantizedInput Player0 { get; }
        public QuantizedInput Player1 { get; }
    }

    public readonly struct TwoPlayerRaceState
    {
        public TwoPlayerRaceState(RaceState player0, RaceState player1)
        {
            Player0 = player0;
            Player1 = player1;
        }

        public RaceState Player0 { get; }
        public RaceState Player1 { get; }

        public static TwoPlayerRaceState CreateDefault()
        {
            return new TwoPlayerRaceState(CreatePlayerState(-7_000), CreatePlayerState(-9_000));
        }

        private static RaceState CreatePlayerState(int startZ)
        {
            return new RaceState(0, new VehicleState(0, startZ, 0, 900), 0, 0, RacePhase.Racing);
        }
    }

    public static class TwoPlayerRaceSimulation
    {
        public static TwoPlayerRaceState Step(TwoPlayerRaceState state, ConfirmedFrame frame)
        {
            return new TwoPlayerRaceState(
                RaceSimulation.Step(state.Player0, frame.Player0),
                RaceSimulation.Step(state.Player1, frame.Player1));
        }
    }

    public static class TwoPlayerRaceStateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(TwoPlayerRaceState state)
        {
            ulong hash = Append(OffsetBasis, RaceStateHasher.Compute(state.Player0));
            return Append(hash, RaceStateHasher.Compute(state.Player1));
        }

        private static ulong Append(ulong hash, ulong value)
        {
            unchecked
            {
                for (int shift = 0; shift < 64; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= Prime;
                }
            }

            return hash;
        }
    }

    public sealed class PlayerSlotRegistry
    {
        private const byte PlayerCount = 2;
        private readonly Dictionary<int, byte> playerIdsByConnection = new();

        public bool IsMatchEnded { get; private set; }

        public int Count => playerIdsByConnection.Count;

        public bool TryAssign(int connectionId, out byte playerId)
        {
            if (IsMatchEnded)
            {
                playerId = default;
                return false;
            }

            if (playerIdsByConnection.TryGetValue(connectionId, out playerId))
            {
                return true;
            }

            for (byte candidate = 0; candidate < PlayerCount; candidate++)
            {
                if (!playerIdsByConnection.ContainsValue(candidate))
                {
                    playerIdsByConnection.Add(connectionId, candidate);
                    playerId = candidate;
                    return true;
                }
            }

            playerId = default;
            return false;
        }

        public bool TryGetPlayerId(int connectionId, out byte playerId)
        {
            return playerIdsByConnection.TryGetValue(connectionId, out playerId);
        }

        public void EndMatch()
        {
            IsMatchEnded = true;
        }
    }

    public sealed class FrameSyncServer
    {
        private const int PlayerCount = 2;
        private readonly Dictionary<int, QuantizedInput>[] pendingInputs = { new(), new() };
        private readonly QuantizedInput[] previousInputs = { QuantizedInput.Empty, QuantizedInput.Empty };

        public TwoPlayerRaceState State { get; private set; } = TwoPlayerRaceState.CreateDefault();
        public int NextFrame { get; private set; }

        public bool TrySubmit(InputFrame input, byte connectionPlayerId)
        {
            if (connectionPlayerId >= PlayerCount || input.PlayerId != connectionPlayerId || input.Frame < NextFrame)
            {
                return false;
            }

            pendingInputs[connectionPlayerId][input.Frame] = input.Input;
            return true;
        }

        public ConfirmedFrame AdvanceFrame()
        {
            QuantizedInput player0 = ConsumeInput(0);
            QuantizedInput player1 = ConsumeInput(1);
            ConfirmedFrame confirmed = new(NextFrame, player0, player1);
            State = TwoPlayerRaceSimulation.Step(State, confirmed);
            NextFrame++;
            return confirmed;
        }

        private QuantizedInput ConsumeInput(int playerId)
        {
            if (pendingInputs[playerId].Remove(NextFrame, out QuantizedInput input))
            {
                previousInputs[playerId] = input;
            }

            return previousInputs[playerId];
        }
    }

    public sealed class FrameSyncClient
    {
        public const int InputDelayFrames = 2;

        private readonly Dictionary<int, ConfirmedFrame> pendingFrames = new();

        public FrameSyncClient(byte playerId, int startFrame = 0)
        {
            PlayerId = playerId;
            NextConfirmedFrame = startFrame;
        }

        public byte PlayerId { get; }
        public TwoPlayerRaceState State { get; private set; } = TwoPlayerRaceState.CreateDefault();
        public int NextConfirmedFrame { get; private set; }

        public InputFrame CreateLocalInput(QuantizedInput input, int currentFrame)
        {
            return new InputFrame(currentFrame + InputDelayFrames, PlayerId, input);
        }

        public void Receive(ConfirmedFrame frame)
        {
            if (frame.Frame < NextConfirmedFrame || pendingFrames.ContainsKey(frame.Frame))
            {
                return;
            }

            pendingFrames.Add(frame.Frame, frame);
        }

        public bool TryAdvance()
        {
            if (!pendingFrames.Remove(NextConfirmedFrame, out ConfirmedFrame frame))
            {
                return false;
            }

            State = TwoPlayerRaceSimulation.Step(State, frame);
            NextConfirmedFrame++;
            return true;
        }
    }
}
