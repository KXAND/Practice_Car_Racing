using System.Collections.Generic;
using CarRacing.Race;

namespace CarRacing.Network.FrameSync
{
    public sealed class FrameSyncClient
    {
        private const int HistoryCapacity = 120;
        private readonly Dictionary<int, RaceFrame> pendingFrames = new();
        private readonly FrameHistory<QuantizedInput> localInputs = new(HistoryCapacity);
        private readonly FrameHistory<RaceFrame> predictedFrames = new(HistoryCapacity);
        private readonly FrameHistory<RaceFrame> confirmedFrames = new(HistoryCapacity);
        private readonly FrameHistory<RaceState> confirmedStates = new(HistoryCapacity);

        public FrameSyncClient(byte playerId)
        {
            PlayerId = playerId;
            RaceState initialState = RaceState.CreateDefault();
            NextConfirmedFrameIndex = initialState.FrameIndex;
            NextPredictedFrameIndex = initialState.FrameIndex;
            CurrentState = initialState;
            ConfirmedState = CurrentState;
            ConfirmedHash = ConfirmedState.DeterministicHash();
            confirmedStates.Store(initialState.FrameIndex - 1, ConfirmedState);
        }

        public byte PlayerId { get; }
        public RaceState CurrentState { get; private set; } = RaceState.CreateDefault();
        public int NextLocalInputFrameIndex => NextPredictedFrameIndex + GlobalSettings.InputDelayFrames;
        public int RollbackCount { get; private set; }
        public int LastRollbackLength { get; private set; }
        public int LastRollbackFrameIndex { get; private set; } = -1;
        public ulong ConfirmedHash { get; private set; } = RaceState.CreateDefault().DeterministicHash();
        public bool HashMismatch { get; private set; }
        public int HashCheckCount { get; private set; }
        public int LastHashCheckFrameIndex { get; private set; } = -1;
        public ulong LastServerHash { get; private set; }

        // private var
        private RaceState ConfirmedState { get; set; }
        private int NextConfirmedFrameIndex { get; set; }
        private int NextPredictedFrameIndex { get; set; }
        #region Public
        public InputFrame Predict(QuantizedInput input)
        {
            InputFrame inputFrame = new(NextLocalInputFrameIndex, PlayerId, input);
            localInputs.Store(inputFrame.FrameIndex, input);
            PredictFrame(NextPredictedFrameIndex);
            return inputFrame;
        }

        public void StartRacePrediction()
        {
            if (CurrentState.Phase != RacePhase.WaitingForPlayers)
            {
                return;
            }

            CurrentState = new RaceState(
                NextPredictedFrameIndex,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                CurrentState.Player0.WithStatus(PlayerStatus.Playing),
                CurrentState.Player1.WithStatus(PlayerStatus.Playing));
        }

        public bool ApplyConfirmed(RaceFrame frame)
        {
            if (frame.FrameIndex >= NextConfirmedFrameIndex && !pendingFrames.ContainsKey(frame.FrameIndex))
            {
                pendingFrames.Add(frame.FrameIndex, frame);
            }

            bool applied = false;
            while (pendingFrames.Remove(NextConfirmedFrameIndex, out RaceFrame next))// 连续应用帧
            {
                ApplyConfirmedFrame(next);
                applied = true;
            }

            return applied;
        }

        public bool PushConfirmedHash(int frameIndex, ulong hash)
        {
            if (!confirmedStates.TryGet(frameIndex, out RaceState state))
            {
                return false;
            }

            ConfirmedHash = state.DeterministicHash();
            HashMismatch = ConfirmedHash != hash;
            HashCheckCount++;
            LastHashCheckFrameIndex = frameIndex;
            LastServerHash = hash;
            return true;
        }

        public FrameSyncDebugInfo GetDebugInfo()
        {
            return new FrameSyncDebugInfo(
                PlayerId, NextPredictedFrameIndex - 1,
                NextConfirmedFrameIndex - 1, NextPredictedFrameIndex - NextConfirmedFrameIndex,
                RollbackCount, LastRollbackLength,
                ConfirmedHash, HashMismatch
                );
        }
        #endregion
        #region Private
        private void ApplyConfirmedFrame(RaceFrame frame)
        {
            // 确认的帧比预测帧更领先，先补充预测
            while (NextPredictedFrameIndex <= frame.FrameIndex)
            {
                PredictFrame(NextPredictedFrameIndex);
            }

            // 更新 confirmed frame
            ConfirmedState = frame.ApplyFrameStartState(ConfirmedState);
            ConfirmedState = RaceFrameRules.AdvanceOneFrame(ConfirmedState, frame.Inputs);
            confirmedFrames.Store(frame.FrameIndex, frame);
            confirmedStates.Store(frame.FrameIndex, ConfirmedState);
            NextConfirmedFrameIndex++;

            // 检查 predict
            bool isPredictionMatchConfirmed = predictedFrames.TryGet(frame.FrameIndex, out RaceFrame prediction) &&
                                    prediction.Equals(frame);
            if (!isPredictionMatchConfirmed)// 预测不一致，从最新的 confirmed 状态开始重新预测
            {
                Reconcile();
            }

        }

        private void Reconcile()
        {
            int horizon = NextPredictedFrameIndex;
            int confirmedFrameIndex = NextConfirmedFrameIndex - 1;
            CurrentState = ConfirmedState;
            NextPredictedFrameIndex = NextConfirmedFrameIndex;

            while (NextPredictedFrameIndex < horizon)
            {
                PredictFrame(NextPredictedFrameIndex);
            }

            RollbackCount++;
            LastRollbackLength = horizon - confirmedFrameIndex;
            LastRollbackFrameIndex = confirmedFrameIndex;
        }

        private void PredictFrame(int frameIndex)
        {
            QuantizedInput localInput = localInputs.TryGet(frameIndex, out QuantizedInput stored) ? stored : QuantizedInput.Empty;
            QuantizedInput remoteInput = GetPreviousRemoteInput(frameIndex);
            RaceInputs inputs = PlayerId == 0 ? new RaceInputs(localInput, remoteInput) : new RaceInputs(remoteInput, localInput);

            RaceFrame predicted = new(CurrentState, inputs);
            CurrentState = RaceFrameRules.AdvanceOneFrame(CurrentState, inputs);
            predictedFrames.Store(frameIndex, predicted);
            NextPredictedFrameIndex = frameIndex + 1;
        }

        private QuantizedInput GetPreviousRemoteInput(int frameIndex)
        {
            int previousFrameIndex = frameIndex - 1;
            if (confirmedFrames.TryGet(previousFrameIndex, out RaceFrame confirmed))
            {
                return PlayerId == 0 ? confirmed.Player1 : confirmed.Player0;
            }

            return predictedFrames.TryGet(previousFrameIndex, out RaceFrame prediction)
                ? PlayerId == 0 ? prediction.Player1 : prediction.Player0
                : QuantizedInput.Empty;
        }

        #endregion

        private sealed class FrameHistory<T>
        {
            private readonly int[] frameIndexes;
            private readonly T[] values;

            public FrameHistory(int capacity)
            {
                frameIndexes = new int[capacity];
                values = new T[capacity];
                for (int index = 0; index < frameIndexes.Length; index++) frameIndexes[index] = int.MinValue;
            }

            public void Store(int frameIndex, T value)
            {
                int index = GetIndex(frameIndex);
                frameIndexes[index] = frameIndex;
                values[index] = value;
            }
            public bool TryGet(int frameIndex, out T value)
            {
                int index = GetIndex(frameIndex);
                if (frameIndexes[index] == frameIndex)
                {
                    value = values[index];
                    return true;
                }
                value = default;
                return false;
            }

            private int GetIndex(int frameIndex)
            {
                int remainder = frameIndex % frameIndexes.Length;
                return remainder < 0 ? remainder + frameIndexes.Length : remainder;
            }
        }
    }
}
