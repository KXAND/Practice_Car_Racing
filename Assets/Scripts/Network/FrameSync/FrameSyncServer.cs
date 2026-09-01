using System;
using System.Collections.Generic;
using CarRacing.Race;

namespace CarRacing.Network.FrameSync
{
    public sealed class FrameSyncServer
    {
        private readonly Dictionary<int, byte> playerIdsByConnection = new();
        private readonly Dictionary<int, QuantizedInput>[] pendingInputs;
        private readonly QuantizedInput[] previousInputs;

        public RaceState State { get; private set; } = RaceState.CreateDefault();
        public int NextFrameIndex { get; private set; }

        public FrameSyncServer()
        {
            pendingInputs = new Dictionary<int, QuantizedInput>[GlobalSettings.TotalPlayerCount];
            previousInputs = new QuantizedInput[GlobalSettings.TotalPlayerCount];
            for (int playerId = 0; playerId < GlobalSettings.TotalPlayerCount; playerId++)
            {
                pendingInputs[playerId] = new Dictionary<int, QuantizedInput>();
                previousInputs[playerId] = QuantizedInput.Empty;
            }
        }

        public bool TryAssignPlayer(int connectionId, out byte playerId)
        {
            if (State.Phase != RacePhase.WaitingForPlayers)
            {
                playerId = default;
                return false;
            }

            if (playerIdsByConnection.TryGetValue(connectionId, out playerId))
            {
                return true;
            }

            for (byte candidate = 0; candidate < GlobalSettings.TotalPlayerCount; candidate++)
            {
                if (State.GetPlayer(candidate).Status == PlayerStatus.Unassigned)
                {
                    playerIdsByConnection.Add(connectionId, candidate);
                    playerId = candidate;
                    UpdatePlayerStatus(playerId, PlayerStatus.Loading);
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

        public bool MarkPlayerReady(int connectionId)
        {
            if (State.Phase != RacePhase.WaitingForPlayers ||
                !TryGetPlayerId(connectionId, out byte playerId))
            {
                return false;
            }

            PlayerStatus status = State.GetPlayer(playerId).Status;
            if (status == PlayerStatus.Ready)
            {
                return false;
            }

            if (status != PlayerStatus.Loading)
            {
                return false;
            }

            UpdatePlayerStatus(playerId, PlayerStatus.Ready);
            return true;
        }

        public bool CanStartRace()
        {
            if (State.Phase != RacePhase.WaitingForPlayers)
            {
                return false;
            }

            return State.Player0.Status == PlayerStatus.Ready &&
                State.Player1.Status == PlayerStatus.Ready;
        }

        public bool StartRace()
        {
            if (!CanStartRace())
            {
                return false;
            }

            State = new RaceState(
                NextFrameIndex,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                State.Player0.WithStatus(PlayerStatus.Playing),
                State.Player1.WithStatus(PlayerStatus.Playing));
            return true;
        }

        public bool DisconnectPlayer(int connectionId)
        {
            if (!TryGetPlayerId(connectionId, out byte playerId))
            {
                return false;
            }

            playerIdsByConnection.Remove(connectionId);
            ClearPlayerInputs(playerId);

            if (State.Phase == RacePhase.WaitingForPlayers)
            {
                UpdatePlayerStatus(playerId, PlayerStatus.Unassigned);
                return true;
            }

            UpdatePlayerStatus(playerId, PlayerStatus.Disconnected);
            AbortRaceWhenAllPlayersDisconnected();
            return true;
        }

        public bool TrySubmit(InputFrame input, byte connectionPlayerId)
        {
            if (connectionPlayerId >= GlobalSettings.TotalPlayerCount ||
                input.PlayerId != connectionPlayerId ||
                input.FrameIndex < NextFrameIndex ||
                State.Phase == RacePhase.WaitingForPlayers ||
                State.Phase == RacePhase.Aborted)
            {
                return false;
            }

            PlayerStatus status = State.GetPlayer(connectionPlayerId).Status;
            if (status != PlayerStatus.Playing && status != PlayerStatus.Finished)
            {
                return false;
            }

            pendingInputs[connectionPlayerId][input.FrameIndex] = input.Input;
            return true;
        }

        public RaceFrame AdvanceFrame()
        {
            if (State.Phase == RacePhase.WaitingForPlayers || State.Phase == RacePhase.Aborted)
            {
                throw new InvalidOperationException($"Cannot advance race in phase {State.Phase}.");
            }

            RaceInputs inputs = new(ConsumeInput(playerId: 0), ConsumeInput(playerId: 1));
            RaceFrame confirmed = new(State, inputs);
            State = RaceFrameRules.AdvanceOneFrame(State, inputs);
            NextFrameIndex = State.FrameIndex;
            return confirmed;
        }

        private void UpdatePlayerStatus(byte playerId, PlayerStatus status)
        {
            PlayerRaceState player0 = State.Player0;
            PlayerRaceState player1 = State.Player1;
            if (playerId == 0)
            {
                player0 = player0.WithStatus(status);
            }
            else
            {
                player1 = player1.WithStatus(status);
            }

            State = new RaceState(
                State.FrameIndex,
                State.Phase,
                State.WinnerPlayerId,
                player0,
                player1);
        }

        private void AbortRaceWhenAllPlayersDisconnected()
        {
            if (State.Phase == RacePhase.Finished || State.Phase == RacePhase.Aborted ||
                State.Player0.Status != PlayerStatus.Disconnected ||
                State.Player1.Status != PlayerStatus.Disconnected)
            {
                return;
            }

            State = new RaceState(
                State.FrameIndex,
                RacePhase.Aborted,
                State.WinnerPlayerId,
                State.Player0,
                State.Player1);
        }

        private void ClearPlayerInputs(byte playerId)
        {
            pendingInputs[playerId].Clear();
            previousInputs[playerId] = QuantizedInput.Empty;
        }

        private QuantizedInput ConsumeInput(byte playerId)
        {
            if (pendingInputs[playerId].Remove(NextFrameIndex, out QuantizedInput input))// 消费下一帧
            {
                previousInputs[playerId] = input;
            }
            return previousInputs[playerId];// 否则沿用上一帧
        }
    }
}
