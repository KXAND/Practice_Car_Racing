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

    public readonly struct FrameSyncDebugInfo
    {
        public FrameSyncDebugInfo(
            byte playerId,
            int localFrame,
            int confirmedFrame,
            int predictionDepth,
            int rollbackCount,
            int lastRollbackLength,
            ulong confirmedHash,
            bool hashMismatch)
        {
            PlayerId = playerId;
            LocalFrame = localFrame;
            ConfirmedFrame = confirmedFrame;
            PredictionDepth = predictionDepth;
            RollbackCount = rollbackCount;
            LastRollbackLength = lastRollbackLength;
            ConfirmedHash = confirmedHash;
            HashMismatch = hashMismatch;
        }

        public byte PlayerId { get; }
        public int LocalFrame { get; }
        public int ConfirmedFrame { get; }
        public int PredictionDepth { get; }
        public int RollbackCount { get; }
        public int LastRollbackLength { get; }
        public ulong ConfirmedHash { get; }
        public bool HashMismatch { get; }
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
        private const int HistoryCapacity = 120;

        private readonly Dictionary<int, ConfirmedFrame> pendingFrames = new();
        private readonly FrameHistory<QuantizedInput> localInputs = new(HistoryCapacity);
        private readonly FrameHistory<ConfirmedFrame> predictedFrames = new(HistoryCapacity);
        private readonly FrameHistory<ConfirmedFrame> confirmedFrames = new(HistoryCapacity);
        private readonly FrameHistory<TwoPlayerRaceState> confirmedStates = new(HistoryCapacity);
        private readonly Dictionary<int, ulong> pendingHashes = new();

        public FrameSyncClient(byte playerId, int startFrame = 0)
        {
            PlayerId = playerId;
            NextConfirmedFrame = startFrame;
            NextPredictedFrame = startFrame;
            ConfirmedState = State;
            confirmedStates.Store(startFrame - 1, ConfirmedState);
        }

        public byte PlayerId { get; }
        public TwoPlayerRaceState State { get; private set; } = TwoPlayerRaceState.CreateDefault();
        public TwoPlayerRaceState ConfirmedState { get; private set; }
        public int NextConfirmedFrame { get; private set; }
        public int NextPredictedFrame { get; private set; }
        public int PredictionDepth => NextPredictedFrame - NextConfirmedFrame;
        public int RollbackCount { get; private set; }
        public int LastRollbackLength { get; private set; }
        public int LastRollbackFrame { get; private set; } = -1;
        public ulong ConfirmedHash { get; private set; } = TwoPlayerRaceStateHasher.Compute(TwoPlayerRaceState.CreateDefault());
        public bool HashMismatch { get; private set; }
        public int HashCheckCount { get; private set; }
        public int LastHashCheckFrame { get; private set; } = -1;
        public ulong LastServerHash { get; private set; }

        public InputFrame CreateLocalInput(QuantizedInput input, int currentFrame)
        {
            return new InputFrame(currentFrame + InputDelayFrames, PlayerId, input);
        }

        public InputFrame SampleLocalInput(QuantizedInput input)
        {
            InputFrame frame = CreateLocalInput(input, NextPredictedFrame);
            localInputs.Store(frame.Frame, input);
            return frame;
        }

        public void PredictNextFrame()
        {
            PredictFrame(NextPredictedFrame);
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

            while (NextPredictedFrame <= frame.Frame)
            {
                PredictNextFrame();
            }

            bool predictionMatches = predictedFrames.TryGet(frame.Frame, out ConfirmedFrame prediction) &&
                                     InputsMatch(prediction, frame);
            int predictedHorizon = NextPredictedFrame;
            ConfirmedState = TwoPlayerRaceSimulation.Step(ConfirmedState, frame);
            confirmedFrames.Store(frame.Frame, frame);
            confirmedStates.Store(frame.Frame, ConfirmedState);
            NextConfirmedFrame++;

            if (!predictionMatches)
            {
                Reconcile(frame, predictedHorizon);
            }

            ApplyPendingHash(frame.Frame);

            return true;
        }

        public void ReceiveServerHash(int frame, ulong hash)
        {
            pendingHashes[frame] = hash;
            ApplyPendingHash(frame);
        }

        public FrameSyncDebugInfo GetDebugInfo()
        {
            return new FrameSyncDebugInfo(
                PlayerId,
                NextPredictedFrame - 1,
                NextConfirmedFrame - 1,
                PredictionDepth,
                RollbackCount,
                LastRollbackLength,
                ConfirmedHash,
                HashMismatch);
        }

        private void Reconcile(ConfirmedFrame confirmedFrame, int predictedHorizon)
        {
            if (!confirmedStates.TryGet(confirmedFrame.Frame - 1, out TwoPlayerRaceState stateBeforeFrame))
            {
                throw new KeyNotFoundException($"Missing confirmed snapshot for frame {confirmedFrame.Frame - 1}.");
            }

            State = stateBeforeFrame;
            NextPredictedFrame = confirmedFrame.Frame;
            SimulateFrame(confirmedFrame.Frame, confirmedFrame);
            while (NextPredictedFrame < predictedHorizon)
            {
                PredictNextFrame();
            }

            RollbackCount++;
            LastRollbackLength = predictedHorizon - confirmedFrame.Frame;
            LastRollbackFrame = confirmedFrame.Frame;
        }

        private void PredictFrame(int frame)
        {
            QuantizedInput localInput = localInputs.TryGet(frame, out QuantizedInput storedInput)
                ? storedInput
                : QuantizedInput.Empty;
            QuantizedInput remoteInput = GetPreviousRemoteInput(frame);
            ConfirmedFrame prediction = PlayerId == 0
                ? new ConfirmedFrame(frame, localInput, remoteInput)
                : new ConfirmedFrame(frame, remoteInput, localInput);
            SimulateFrame(frame, prediction);
        }

        private void SimulateFrame(int frame, ConfirmedFrame input)
        {
            State = TwoPlayerRaceSimulation.Step(State, input);
            predictedFrames.Store(frame, input);
            NextPredictedFrame = frame + 1;
        }

        private QuantizedInput GetPreviousRemoteInput(int frame)
        {
            int previousFrame = frame - 1;
            if (confirmedFrames.TryGet(previousFrame, out ConfirmedFrame confirmed))
            {
                return PlayerId == 0 ? confirmed.Player1 : confirmed.Player0;
            }

            if (predictedFrames.TryGet(previousFrame, out ConfirmedFrame prediction))
            {
                return PlayerId == 0 ? prediction.Player1 : prediction.Player0;
            }

            return QuantizedInput.Empty;
        }

        private static bool InputsMatch(ConfirmedFrame first, ConfirmedFrame second)
        {
            return InputMatches(first.Player0, second.Player0) && InputMatches(first.Player1, second.Player1);
        }

        private static bool InputMatches(QuantizedInput first, QuantizedInput second)
        {
            return first.Steering == second.Steering &&
                   first.Throttle == second.Throttle &&
                   first.Brake == second.Brake;
        }

        private void ApplyPendingHash(int frame)
        {
            if (!pendingHashes.TryGetValue(frame, out ulong expectedHash) ||
                !confirmedStates.TryGet(frame, out TwoPlayerRaceState state))
            {
                return;
            }

            pendingHashes.Remove(frame);
            ConfirmedHash = TwoPlayerRaceStateHasher.Compute(state);
            HashMismatch = ConfirmedHash != expectedHash;
            HashCheckCount++;
            LastHashCheckFrame = frame;
            LastServerHash = expectedHash;
        }
    }

    internal sealed class FrameHistory<T>
    {
        private readonly int[] frames;
        private readonly T[] values;

        public FrameHistory(int capacity)
        {
            frames = new int[capacity];
            values = new T[capacity];
            for (int index = 0; index < frames.Length; index++)
            {
                frames[index] = int.MinValue;
            }
        }

        public void Store(int frame, T value)
        {
            int index = GetIndex(frame);
            frames[index] = frame;
            values[index] = value;
        }

        public bool TryGet(int frame, out T value)
        {
            int index = GetIndex(frame);
            if (frames[index] == frame)
            {
                value = values[index];
                return true;
            }

            value = default;
            return false;
        }

        private int GetIndex(int frame)
        {
            int remainder = frame % frames.Length;
            return remainder < 0 ? remainder + frames.Length : remainder;
        }
    }

    public sealed class UpstreamInputConditioner
    {
        private readonly Queue<ScheduledInput> scheduledInputs = new();
        private readonly int delayTicks;
        private readonly int dropEveryNthInput;
        private int submittedInputCount;

        public UpstreamInputConditioner(int delayTicks, int dropEveryNthInput)
        {
            WasDelayClamped = delayTicks >= 2;
            this.delayTicks = delayTicks is >= 0 and <= 1 ? delayTicks : 0;
            this.dropEveryNthInput = dropEveryNthInput < 0 ? 0 : dropEveryNthInput;
        }

        public bool WasDelayClamped { get; }

        public int DelayTicks => delayTicks;
        public int DropEveryNthInput => dropEveryNthInput;

        public bool Enqueue(InputFrame input, int currentTick)
        {
            submittedInputCount++;
            if (dropEveryNthInput > 0 && submittedInputCount % dropEveryNthInput == 0)
            {
                return false;
            }

            scheduledInputs.Enqueue(new ScheduledInput(input, currentTick + delayTicks));
            return true;
        }

        public bool TryDequeueDue(int currentTick, out InputFrame input)
        {
            if (scheduledInputs.Count > 0 && scheduledInputs.Peek().DeliveryTick <= currentTick)
            {
                input = scheduledInputs.Dequeue().Input;
                return true;
            }

            input = default;
            return false;
        }

        private readonly struct ScheduledInput
        {
            public ScheduledInput(InputFrame input, int deliveryTick)
            {
                Input = input;
                DeliveryTick = deliveryTick;
            }

            public InputFrame Input { get; }
            public int DeliveryTick { get; }
        }
    }
}
