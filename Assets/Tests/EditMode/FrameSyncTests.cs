using CarRacing.Input;
using CarRacing.Network.FrameSync;
using CarRacing.Race;
using NUnit.Framework;

namespace CarRacing.Tests
{
    public sealed class FrameSyncTests
    {
        [Test]
        public void ServerMovesAssignedPlayersThroughReadyAndRaceStart()
        {
            FrameSyncServer server = new();
            Assert.That(server.TryAssignPlayer(10, out byte first), Is.True);
            Assert.That(server.TryAssignPlayer(11, out byte second), Is.True);

            Assert.That(first, Is.EqualTo(0));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Loading));
            Assert.That(server.State.Player1.Status, Is.EqualTo(PlayerStatus.Loading));
            Assert.That(server.MarkPlayerReady(10), Is.True);
            Assert.That(server.MarkPlayerReady(11), Is.True);
            Assert.That(server.CanStartRace(), Is.True);
            Assert.That(server.StartRace(), Is.True);
            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.Racing));
            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Playing));
            Assert.That(server.State.Player1.Status, Is.EqualTo(PlayerStatus.Playing));
        }

        [Test]
        public void PreRaceDisconnectReleasesThePlayerSlot()
        {
            FrameSyncServer server = new();
            Assert.That(server.TryAssignPlayer(10, out byte playerId), Is.True);
            Assert.That(server.MarkPlayerReady(10), Is.True);

            Assert.That(server.DisconnectPlayer(10), Is.True);
            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.WaitingForPlayers));
            Assert.That(server.State.GetPlayer(playerId).Status, Is.EqualTo(PlayerStatus.Unassigned));
            Assert.That(server.TryAssignPlayer(12, out byte replacement), Is.True);
            Assert.That(replacement, Is.EqualTo(playerId));
        }

        [Test]
        public void UnknownDisconnectDoesNotChangeRacePhase()
        {
            FrameSyncServer server = CreateStartedRace();
            RacePhase phase = server.State.Phase;

            Assert.That(server.DisconnectPlayer(42), Is.False);
            Assert.That(server.State.Phase, Is.EqualTo(phase));
        }

        [Test]
        public void WorldHashDistinguishesPlayerOrder()
        {
            RaceState state = RaceState.CreateDefault();
            RaceState swapped = new(
                state.FrameIndex,
                state.Phase,
                state.WinnerPlayerId,
                state.Player1,
                state.Player0);
            Assert.That(swapped.DeterministicHash(), Is.Not.EqualTo(state.DeterministicHash()));
        }

        [Test]
        public void ServerRepeatsPreviousInputWhenCurrentFrameIsMissing()
        {
            FrameSyncServer server = CreateStartedRace();
            Assert.That(server.TrySubmit(new InputFrame(0, 0, new QuantizedInput(0, GlobalSettings.MaxPedal, 0)), 0), Is.True);
            RaceFrame first = server.AdvanceFrame();
            RaceFrame second = server.AdvanceFrame();
            Assert.That(first.Player0.Throttle, Is.EqualTo(GlobalSettings.MaxPedal));
            Assert.That(second.Player0.Throttle, Is.EqualTo(GlobalSettings.MaxPedal));
            Assert.That(second.Player1.Throttle, Is.EqualTo(0));
        }

        [Test]
        public void ServerRejectsInputForAnotherPlayer()
        {
            FrameSyncServer server = CreateStartedRace();
            Assert.That(server.TrySubmit(new InputFrame(0, 1, QuantizedInput.Empty), 0), Is.False);
        }

        [Test]
        public void ClientConsumesContiguousConfirmedFramesInternally()
        {
            FrameSyncClient client = CreateStartedClient(0);
            Assert.That(client.ApplyConfirmed(Frame(1)), Is.False);
            Assert.That(client.ApplyConfirmed(Frame(0)), Is.True);
            Assert.That(client.GetDebugInfo().ConfirmedFrameIndex, Is.EqualTo(1));
        }

        [Test]
        public void ClientTargetsInputsTwoFramesAheadAndPredictsInOneCall()
        {
            FrameSyncClient client = CreateStartedClient(1);
            InputFrame input = client.Predict(new QuantizedInput(10, 20, 30));
            Assert.That(input.FrameIndex, Is.EqualTo(GlobalSettings.InputDelayFrames));
            Assert.That(input.PlayerId, Is.EqualTo(1));
            Assert.That(client.GetDebugInfo().LocalFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void ClientBeginsPredictionAtTheScheduledStartFrame()
        {
            FrameSyncClient client = new(1);

            client.StartRacePrediction();
            InputFrame input = client.Predict(QuantizedInput.Empty);

            Assert.That(input.FrameIndex, Is.EqualTo(GlobalSettings.InputDelayFrames));
            Assert.That(client.CurrentState.Phase, Is.EqualTo(RacePhase.Racing));
            Assert.That(client.GetDebugInfo().LocalFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void ServerAndClientProduceSameWorldForConfirmedFrames()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient client = CreateStartedClient(0);
            for (int frameIndex = 0; frameIndex < 120; frameIndex++)
            {
                QuantizedInput input = frameIndex < 90 ? new QuantizedInput(0, GlobalSettings.MaxPedal, 0) : QuantizedInput.Empty;
                server.TrySubmit(new InputFrame(frameIndex, 0, input), 0);
                Assert.That(client.ApplyConfirmed(server.AdvanceFrame()), Is.True);
                AssertWorldStateEqual(server.State, client.CurrentState);
            }
        }

        [Test]
        public void ClientRollsBackMispredictedRemoteInput()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient client = CreateStartedClient(0);
            for (int frameIndex = 0; frameIndex < 12; frameIndex++)
            {
                client.Predict(QuantizedInput.Empty);
                if (frameIndex == 2) server.TrySubmit(new InputFrame(frameIndex, 1, new QuantizedInput(0, GlobalSettings.MaxPedal, 0)), 1);
            }

            for (int frameIndex = 0; frameIndex < 12; frameIndex++) client.ApplyConfirmed(server.AdvanceFrame());
            Assert.That(client.RollbackCount, Is.EqualTo(1));
            Assert.That(client.LastRollbackLength, Is.GreaterThan(0));
            AssertWorldStateEqual(server.State, client.CurrentState);
        }

        [Test]
        public void UpstreamConditionerDelaysAndDropsEveryNthInput()
        {
            UpstreamInputConditioner conditioner = new(1, 3);
            InputFrame first = new(2, 0, QuantizedInput.Empty);
            InputFrame second = new(3, 0, new QuantizedInput(0, GlobalSettings.MaxPedal, 0));
            InputFrame dropped = new(4, 0, new QuantizedInput(GlobalSettings.MaxSteering, 0, 0));
            Assert.That(conditioner.ScheduleInput(first, 0), Is.True);
            Assert.That(conditioner.ScheduleInput(second, 1), Is.True);
            Assert.That(conditioner.ScheduleInput(dropped, 2), Is.False);
            Assert.That(conditioner.PopReadyInput(0, out _), Is.False);
            Assert.That(conditioner.PopReadyInput(1, out InputFrame deliveredFirst), Is.True);
            Assert.That(deliveredFirst.FrameIndex, Is.EqualTo(first.FrameIndex));
            Assert.That(conditioner.PopReadyInput(2, out InputFrame deliveredSecond), Is.True);
            Assert.That(deliveredSecond.FrameIndex, Is.EqualTo(second.FrameIndex));
        }

        [Test]
        public void InvalidUpstreamDelayFallsBackToImmediateDelivery()
        {
            UpstreamInputConditioner conditioner = new(2, 0);
            InputFrame input = new(2, 0, QuantizedInput.Empty);
            conditioner.ScheduleInput(input, 10);
            Assert.That(conditioner.PopReadyInput(10, out InputFrame delivered), Is.True);
            Assert.That(delivered.FrameIndex, Is.EqualTo(input.FrameIndex));
        }

        [Test]
        public void WeakUpstreamConditionsRollbackAndRecoverHashes()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient client = CreateStartedClient(0);
            UpstreamInputConditioner conditioner = new(1, 3);
            int hashComparisons = 0;
            for (int tick = 0; tick < 90; tick++)
            {
                QuantizedInput sampled = tick % 2 == 0 ? new QuantizedInput(GlobalSettings.MaxSteering, GlobalSettings.MaxPedal, 0) : new QuantizedInput(0, GlobalSettings.MaxPedal, 0);
                InputFrame input = client.Predict(sampled);
                conditioner.ScheduleInput(input, tick);
                while (conditioner.PopReadyInput(tick, out InputFrame due)) server.TrySubmit(due, due.PlayerId);
                RaceFrame confirmed = server.AdvanceFrame();
                client.ApplyConfirmed(confirmed);
                if ((confirmed.FrameIndex + 1) % GlobalSettings.HashIntervalFrames == 0)
                {
                    Assert.That(client.PushConfirmedHash(confirmed.FrameIndex, server.State.DeterministicHash()), Is.True);
                    Assert.That(client.HashMismatch, Is.False);
                    hashComparisons++;
                }
            }

            Assert.That(client.RollbackCount, Is.GreaterThan(0));
            Assert.That(server.State.Player0.Vehicle.PositionZ, Is.Not.EqualTo(RaceState.Player0StartZ));
            Assert.That(hashComparisons, Is.GreaterThan(1));
        }

        [Test]
        public void CorrectHashClearsEarlierDesync()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient client = CreateStartedClient(0);
            RaceFrame confirmed = server.AdvanceFrame();
            client.ApplyConfirmed(confirmed);
            ulong correctHash = server.State.DeterministicHash();
            Assert.That(client.PushConfirmedHash(confirmed.FrameIndex, correctHash ^ 1UL), Is.True);
            Assert.That(client.HashMismatch, Is.True);
            Assert.That(client.PushConfirmedHash(confirmed.FrameIndex, correctHash), Is.True);
            Assert.That(client.HashMismatch, Is.False);
            Assert.That(client.ConfirmedHash, Is.EqualTo(correctHash));
        }

        [Test]
        public void ClientReportsHashReceivedBeforeItsConfirmedFrame()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient client = CreateStartedClient(0);
            RaceFrame confirmed = server.AdvanceFrame();
            ulong expectedHash = server.State.DeterministicHash();

            Assert.That(client.PushConfirmedHash(confirmed.FrameIndex, expectedHash), Is.False);
            client.ApplyConfirmed(confirmed);

            Assert.That(client.HashCheckCount, Is.EqualTo(0));
            Assert.That(client.PushConfirmedHash(confirmed.FrameIndex, expectedHash), Is.True);
            Assert.That(client.HashMismatch, Is.False);
        }

        [Test]
        public void DisconnectedPlayerCannotSubmitOrReuseItsPreviousInput()
        {
            FrameSyncServer server = CreateStartedRace();
            Assert.That(server.TrySubmit(new InputFrame(0, 0, new QuantizedInput(0, GlobalSettings.MaxPedal, 0)), 0), Is.True);
            server.AdvanceFrame();

            Assert.That(server.DisconnectPlayer(10), Is.True);
            Assert.That(server.TrySubmit(new InputFrame(1, 0, new QuantizedInput(0, GlobalSettings.MaxPedal, 0)), 0), Is.False);
            RaceFrame confirmed = server.AdvanceFrame();

            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Disconnected));
            Assert.That(confirmed.Player0, Is.EqualTo(QuantizedInput.Empty));
            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.Racing));
        }

        [Test]
        public void ConfirmedControlStateMismatchTriggersRollback()
        {
            FrameSyncClient client = CreateStartedClient(0);
            client.Predict(QuantizedInput.Empty);
            RaceState state = CreateRacingState(0);
            PlayerRaceState disconnectedPlayer = new(
                state.Player1.Vehicle,
                state.Player1.NextCheckpoint,
                state.Player1.Lap,
                PlayerStatus.Disconnected);
            RaceFrame confirmed = new(new RaceState(
                state.FrameIndex,
                state.Phase,
                state.WinnerPlayerId,
                state.Player0,
                disconnectedPlayer), new RaceInputs(QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(client.ApplyConfirmed(confirmed), Is.True);
            Assert.That(client.RollbackCount, Is.EqualTo(1));
            Assert.That(client.CurrentState.Player1.Status, Is.EqualTo(PlayerStatus.Disconnected));
        }

        [Test]
        public void OneDisconnectedPlayerLeavesTheOtherPlayerAbleToFinish()
        {
            FrameSyncServer server = CreateStartedRace();
            Assert.That(server.DisconnectPlayer(10), Is.True);

            RunScriptedPlayerUntilFinished(server, 1);

            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Disconnected));
            Assert.That(server.State.Player1.Status, Is.EqualTo(PlayerStatus.Finished));
            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.Finished));
        }

        [Test]
        public void BothDisconnectedPlayersAbortTheRace()
        {
            FrameSyncServer server = CreateStartedRace();

            Assert.That(server.DisconnectPlayer(10), Is.True);
            Assert.That(server.DisconnectPlayer(11), Is.True);

            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Disconnected));
            Assert.That(server.State.Player1.Status, Is.EqualTo(PlayerStatus.Disconnected));
            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.Aborted));
        }

        [Test]
        public void FinishedPlayerCanSubmitInputAndMoveOnTheServer()
        {
            FrameSyncServer server = CreateStartedRace();
            RunScriptedPlayerUntilFinished(server, 0);
            VehicleState before = server.State.Player0.Vehicle;
            int beforeNextCheckpoint = server.State.Player0.NextCheckpoint;
            int beforeLap = server.State.Player0.Lap;
            byte beforeWinnerPlayerId = server.State.WinnerPlayerId;

            Assert.That(server.TrySubmit(new InputFrame(
                server.NextFrameIndex,
                0,
                new QuantizedInput(0, GlobalSettings.MaxPedal, 0)), 0), Is.True);
            server.AdvanceFrame();

            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Finished));
            Assert.That(server.State.Player0.Vehicle, Is.Not.EqualTo(before));
            Assert.That(server.State.Player0.NextCheckpoint, Is.EqualTo(beforeNextCheckpoint));
            Assert.That(server.State.Player0.Lap, Is.EqualTo(beforeLap));
            Assert.That(server.State.WinnerPlayerId, Is.EqualTo(beforeWinnerPlayerId));
        }

        [Test]
        public void DisconnectAfterFinishedRaceKeepsFinishedPhase()
        {
            FrameSyncServer server = CreateStartedRace();
            Assert.That(server.DisconnectPlayer(11), Is.True);
            RunScriptedPlayerUntilFinished(server, 0);
            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.Finished));

            Assert.That(server.DisconnectPlayer(10), Is.True);

            Assert.That(server.State.Phase, Is.EqualTo(RacePhase.Finished));
        }

        [Test]
        public void PhaseOnlyConfirmationMismatchTriggersRollback()
        {
            FrameSyncClient client = CreateStartedClient(0);
            client.Predict(QuantizedInput.Empty);
            RaceState racing = CreateRacingState(0);
            RaceFrame confirmed = new(new RaceState(
                racing.FrameIndex,
                RacePhase.WinnerDecided,
                racing.WinnerPlayerId,
                racing.Player0,
                racing.Player1), new RaceInputs(QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(client.ApplyConfirmed(confirmed), Is.True);
            Assert.That(client.RollbackCount, Is.EqualTo(1));
        }

        [Test]
        public void WinnerOnlyConfirmationMismatchTriggersRollback()
        {
            FrameSyncClient client = CreateStartedClient(0);
            client.Predict(QuantizedInput.Empty);
            RaceState racing = CreateRacingState(0);
            RaceFrame confirmed = new(new RaceState(
                racing.FrameIndex,
                racing.Phase,
                0,
                racing.Player0,
                racing.Player1), new RaceInputs(QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(client.ApplyConfirmed(confirmed), Is.True);
            Assert.That(client.RollbackCount, Is.EqualTo(1));
        }

        [Test]
        public void RollbackReconciliationMatchesTheServerHash()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient client = CreateStartedClient(0);
            for (int frameIndex = 0; frameIndex < 12; frameIndex++)
            {
                client.Predict(QuantizedInput.Empty);
            }

            Assert.That(server.DisconnectPlayer(11), Is.True);
            for (int frameIndex = 0; frameIndex < 12; frameIndex++)
            {
                client.ApplyConfirmed(server.AdvanceFrame());
            }

            Assert.That(client.RollbackCount, Is.GreaterThan(0));
            Assert.That(client.CurrentState.DeterministicHash(), Is.EqualTo(server.State.DeterministicHash()));
        }

        [Test]
        public void ScriptedSequenceFinishesSameLapForServerAndClients()
        {
            FrameSyncServer server = CreateStartedRace();
            FrameSyncClient first = CreateStartedClient(0);
            FrameSyncClient second = CreateStartedClient(1);
            for (int frameIndex = 0; frameIndex < 400; frameIndex++)
            {
                server.TrySubmit(new InputFrame(frameIndex, 0, ScriptedRaceInput.ForFrame(frameIndex)), 0);
                RaceFrame confirmed = server.AdvanceFrame();
                first.ApplyConfirmed(confirmed);
                second.ApplyConfirmed(confirmed);
                AssertWorldStateEqual(server.State, first.CurrentState);
                AssertWorldStateEqual(server.State, second.CurrentState);
                if (server.State.Player0.Status == PlayerStatus.Finished) break;
            }

            Assert.That(server.State.Player0.Status, Is.EqualTo(PlayerStatus.Finished));
            Assert.That(server.State.Player0.Lap, Is.EqualTo(1));
        }

        private static FrameSyncServer CreateStartedRace()
        {
            FrameSyncServer server = new();
            Assert.That(server.TryAssignPlayer(10, out _), Is.True);
            Assert.That(server.TryAssignPlayer(11, out _), Is.True);
            Assert.That(server.MarkPlayerReady(10), Is.True);
            Assert.That(server.MarkPlayerReady(11), Is.True);
            Assert.That(server.StartRace(), Is.True);
            return server;
        }

        private static FrameSyncClient CreateStartedClient(byte playerId)
        {
            FrameSyncClient client = new(playerId);
            client.StartRacePrediction();
            return client;
        }

        private static void RunScriptedPlayerUntilFinished(FrameSyncServer server, byte playerId)
        {
            for (int frameIndex = server.NextFrameIndex; frameIndex < 500; frameIndex++)
            {
                Assert.That(server.TrySubmit(new InputFrame(
                    frameIndex,
                    playerId,
                    ScriptedRaceInput.ForFrame(frameIndex)), playerId), Is.True);
                server.AdvanceFrame();
                if (server.State.GetPlayer(playerId).Status == PlayerStatus.Finished)
                {
                    return;
                }
            }

            Assert.Fail($"Player {playerId} did not finish the scripted lap.");
        }

        private static RaceFrame Frame(int frameIndex)
        {
            return new RaceFrame(
                CreateRacingState(frameIndex),
                new RaceInputs(QuantizedInput.Empty, QuantizedInput.Empty));
        }

        private static RaceState CreateRacingState(int frameIndex)
        {
            RaceState state = RaceState.CreateDefault();
            PlayerRaceState player0 = new(
                state.Player0.Vehicle,
                state.Player0.NextCheckpoint,
                state.Player0.Lap,
                PlayerStatus.Playing);
            PlayerRaceState player1 = new(
                state.Player1.Vehicle,
                state.Player1.NextCheckpoint,
                state.Player1.Lap,
                PlayerStatus.Playing);
            return new RaceState(
                frameIndex,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                player0,
                player1);
        }

        private static void AssertWorldStateEqual(RaceState expected, RaceState actual)
        {
            Assert.That(actual.DeterministicHash(), Is.EqualTo(expected.DeterministicHash()));
        }
    }
}
