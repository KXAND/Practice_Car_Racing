using CarRacing.Input;
using CarRacing.Network.FrameSync;
using CarRacing.Race;
using NUnit.Framework;

namespace CarRacing.Tests
{
    public sealed class RaceFrameRulesTests
    {
        [Test]
        public void DefaultStateFacesTheFirstCheckpoint()
        {
            RaceState state = RaceState.CreateDefault();

            Assert.That(state.Phase, Is.EqualTo(RacePhase.WaitingForPlayers));
            Assert.That(state.Player0.Status, Is.EqualTo(PlayerStatus.Unassigned));
            Assert.That(state.Player0.Vehicle.PositionX, Is.EqualTo(RaceState.DefaultStartX));
            Assert.That(state.Player0.Vehicle.PositionZ, Is.EqualTo(RaceState.Player0StartZ));
            Assert.That(state.Player0.Vehicle.HeadingTenths, Is.EqualTo(900));
        }

        [Test]
        public void DeterministicHashChangesWhenOneLifecycleFieldChanges()
        {
            RaceState racing = CreateRacingState();
            RaceState phaseChanged = new(
                racing.FrameIndex,
                RacePhase.WinnerDecided,
                racing.WinnerPlayerId,
                racing.Player0,
                racing.Player1);
            RaceState winnerChanged = new(
                racing.FrameIndex,
                racing.Phase,
                0,
                racing.Player0,
                racing.Player1);
            RaceState player0StatusChanged = new(
                racing.FrameIndex,
                racing.Phase,
                racing.WinnerPlayerId,
                racing.Player0.WithStatus(PlayerStatus.Finished),
                racing.Player1);
            RaceState player1StatusChanged = new(
                racing.FrameIndex,
                racing.Phase,
                racing.WinnerPlayerId,
                racing.Player0,
                racing.Player1.WithStatus(PlayerStatus.Disconnected));

            Assert.That(phaseChanged.DeterministicHash(), Is.Not.EqualTo(racing.DeterministicHash()));
            Assert.That(winnerChanged.DeterministicHash(), Is.Not.EqualTo(racing.DeterministicHash()));
            Assert.That(player0StatusChanged.DeterministicHash(), Is.Not.EqualTo(racing.DeterministicHash()));
            Assert.That(player1StatusChanged.DeterministicHash(), Is.Not.EqualTo(racing.DeterministicHash()));
        }

        [Test]
        public void SustainedThrottleAcceleratesTowardsTheFirstCheckpoint()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(RaceState.DefaultStartX, RaceState.Player0StartZ, 0, 900),
                0,
                0,
                PlayerStatus.Playing));
            QuantizedInput input = new(0, GlobalSettings.MaxPedal, 0);

            for (int frameIndex = 0; frameIndex < GlobalSettings.TickRate; frameIndex++)
            {
                state = Advance(state, input);
            }

            Assert.That(state.FrameIndex, Is.EqualTo(GlobalSettings.TickRate));
            Assert.That(state.Player0.Vehicle.Speed, Is.GreaterThan(0));
            Assert.That(state.Player0.Vehicle.PositionX, Is.GreaterThan(RaceState.DefaultStartX));
            Assert.That(state.Player0.Vehicle.PositionZ, Is.EqualTo(RaceState.Player0StartZ));
        }

        [Test]
        public void SteeringAtSpeedChangesTheVehicleCourse()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(0, -8_000, 6_000, 0),
                0,
                0,
                PlayerStatus.Playing));

            for (int frameIndex = 0; frameIndex < 5; frameIndex++)
            {
                state = Advance(state, new QuantizedInput(GlobalSettings.MaxSteering, 0, 0));
            }

            Assert.That(state.Player0.Vehicle.HeadingTenths, Is.GreaterThan(0));
            Assert.That(state.Player0.Vehicle.PositionX, Is.GreaterThan(0));
        }

        [Test]
        public void BrakingReachesAStoppedStateBeforeReverse()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(0, -8_000, 6_000, 0),
                0,
                0,
                PlayerStatus.Playing));

            bool reachedStoppedState = false;
            for (int frameIndex = 0; frameIndex < GlobalSettings.TickRate * 2; frameIndex++)
            {
                state = Advance(state, new QuantizedInput(0, 0, GlobalSettings.MaxPedal));
                if (state.Player0.Vehicle.Speed == 0)
                {
                    reachedStoppedState = true;
                    break;
                }
            }

            Assert.That(reachedStoppedState, Is.True);
        }

        [Test]
        public void HoldingBrakeFromRestReversesAtThirtyPercentOfForwardMaximumSpeed()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(RaceState.DefaultStartX, RaceState.Player0StartZ, 0, 900),
                0,
                0,
                PlayerStatus.Playing));
            QuantizedInput brake = new(0, 0, GlobalSettings.MaxPedal);

            for (int frameIndex = 0; frameIndex < 120; frameIndex++)
            {
                state = Advance(state, brake);
            }

            Assert.That(state.Player0.Vehicle.Speed, Is.LessThan(0));
            Assert.That(state.Player0.Vehicle.Speed, Is.GreaterThanOrEqualTo(-3_000));
            Assert.That(state.Player0.Vehicle.PositionX, Is.LessThan(RaceState.DefaultStartX));
        }

        [Test]
        public void ForwardInputStopsAReversingVehicleBeforeAcceleratingForward()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(0, -8_000, -100, 900),
                0,
                0,
                PlayerStatus.Playing));
            QuantizedInput throttle = new(0, GlobalSettings.MaxPedal, 0);

            state = Advance(state, throttle);
            Assert.That(state.Player0.Vehicle.Speed, Is.EqualTo(0));

            state = Advance(state, throttle);
            Assert.That(state.Player0.Vehicle.Speed, Is.GreaterThan(0));
        }

        [Test]
        public void ReverseSteeringTurnsInTheOppositeDirection()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(0, -8_000, -6_000, 900),
                0,
                0,
                PlayerStatus.Playing));

            state = Advance(state, new QuantizedInput(GlobalSettings.MaxSteering, 0, 0));

            Assert.That(state.Player0.Vehicle.HeadingTenths, Is.LessThan(900));
        }

        [Test]
        public void ReversingAfterAnOuterBoundaryCollisionMovesTheVehicleBackIntoTheTrack()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(RaceFrameRules.OuterMaximumX - 700, 0, 0, 900),
                0,
                0,
                PlayerStatus.Playing));

            state = Advance(state, new QuantizedInput(0, 0, GlobalSettings.MaxPedal));

            Assert.That(state.Player0.Vehicle.Speed, Is.LessThan(0));
            Assert.That(state.Player0.Vehicle.PositionX, Is.LessThan(RaceFrameRules.OuterMaximumX - 700));
        }

        [Test]
        public void OuterTrackBoundaryStopsTheVehicle()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(17_000, 0, 10_000, 900),
                0,
                0,
                PlayerStatus.Playing));

            state = Advance(state, new QuantizedInput(0, 0, 0));

            Assert.That(state.Player0.Vehicle.PositionX, Is.LessThanOrEqualTo(RaceFrameRules.OuterMaximumX - 700));
            Assert.That(state.Player0.Vehicle.Speed, Is.EqualTo(0));
        }

        [Test]
        public void InnerTrackBoundaryStopsTheVehicle()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(0, -4_800, 5_000, 0),
                0,
                0,
                PlayerStatus.Playing));

            state = Advance(state, new QuantizedInput(0, 0, 0));

            Assert.That(state.Player0.Vehicle.PositionZ, Is.EqualTo(-4_700));
            Assert.That(state.Player0.Vehicle.Speed, Is.EqualTo(0));
        }

        [Test]
        public void OrderedCheckpointsFinishOneLap()
        {
            RaceState state = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(RaceState.DefaultStartX, RaceState.Player0StartZ, 0, 900),
                0,
                0,
                PlayerStatus.Playing));

            state = StepAt(state, new VehicleState(13_000, 0, 0, 0));
            Assert.That(state.Player0.NextCheckpoint, Is.EqualTo(1));

            state = StepAt(state, new VehicleState(0, 8_000, 0, 0));
            Assert.That(state.Player0.NextCheckpoint, Is.EqualTo(2));

            state = StepAt(state, new VehicleState(-13_000, 0, 0, 0));
            Assert.That(state.Player0.NextCheckpoint, Is.EqualTo(3));

            state = StepAt(state, new VehicleState(0, -8_000, 0, 0));
            Assert.That(state.Player0.Lap, Is.EqualTo(1));
            Assert.That(state.Player0.Status, Is.EqualTo(PlayerStatus.Finished));
        }

        [Test]
        public void FirstFinisherWinsWhileTheOtherPlayerCanContinue()
        {
            RaceState defaultState = RaceState.CreateDefault();
            PlayerRaceState player0 = new(
                new VehicleState(RaceState.DefaultStartX, RaceState.Player0StartZ, 0, 900),
                0,
                0,
                PlayerStatus.Playing);
            PlayerRaceState player1 = new(
                defaultState.Player1.Vehicle,
                defaultState.Player1.NextCheckpoint,
                defaultState.Player1.Lap,
                PlayerStatus.Playing);
            RaceState state = new(
                0,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                player0,
                player1);

            state = StepAt(state, new VehicleState(13_000, 0, 0, 0));
            state = StepAt(state, new VehicleState(0, 8_000, 0, 0));
            state = StepAt(state, new VehicleState(-13_000, 0, 0, 0));
            state = StepAt(state, new VehicleState(0, -8_000, 0, 0));

            Assert.That(state.WinnerPlayerId, Is.EqualTo(0));
            Assert.That(state.Phase, Is.EqualTo(RacePhase.WinnerDecided));
            Assert.That(state.Player1.Status, Is.EqualTo(PlayerStatus.Playing));

            PlayerRaceState finishedPlayer1 = new(
                state.Player1.Vehicle,
                state.Player1.NextCheckpoint,
                1,
                PlayerStatus.Finished);
            state = new RaceState(
                state.FrameIndex,
                state.Phase,
                state.WinnerPlayerId,
                state.Player0,
                finishedPlayer1);
            state = RaceFrameRules.AdvanceOneFrame(state, new RaceInputs(QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(state.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(state.WinnerPlayerId, Is.EqualTo(0));
        }

        [Test]
        public void SimultaneousFinishUsesTheLowerPlayerIdAsWinner()
        {
            PlayerRaceState player0 = new(
                new VehicleState(0, -8_000, 0, 0),
                3,
                0,
                PlayerStatus.Playing);
            PlayerRaceState player1 = new(
                new VehicleState(0, -8_000, 0, 0),
                3,
                0,
                PlayerStatus.Playing);
            RaceState state = new(
                0,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                player0,
                player1);

            state = RaceFrameRules.AdvanceOneFrame(state, new RaceInputs(QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(state.WinnerPlayerId, Is.EqualTo(0));
            Assert.That(state.Phase, Is.EqualTo(RacePhase.Finished));
        }

        [Test]
        public void SameInputSequenceProducesTheSameHashEveryFrame()
        {
            QuantizedInput[] inputs =
            {
                new QuantizedInput(0, GlobalSettings.MaxPedal, 0),
                new QuantizedInput(80, GlobalSettings.MaxPedal, 0),
                new QuantizedInput(-50, GlobalSettings.MaxPedal, 0),
                new QuantizedInput(0, 0, GlobalSettings.MaxPedal)
            };
            RaceState firstRun = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(RaceState.DefaultStartX, RaceState.Player0StartZ, 0, 900),
                0,
                0,
                PlayerStatus.Playing));
            RaceState secondRun = firstRun;

            for (int frameIndex = 0; frameIndex < 120; frameIndex++)
            {
                QuantizedInput input = inputs[frameIndex % inputs.Length];
                firstRun = Advance(firstRun, input);
                secondRun = Advance(secondRun, input);

                Assert.That(firstRun.DeterministicHash(), Is.EqualTo(secondRun.DeterministicHash()));
            }
        }

        [Test]
        public void FixedInputSequenceCompletesOneDeterministicLap()
        {
            RaceState firstRun = CreatePlayingRace(new PlayerRaceState(
                new VehicleState(RaceState.DefaultStartX, RaceState.Player0StartZ, 0, 900),
                0,
                0,
                PlayerStatus.Playing));
            RaceState secondRun = firstRun;

            for (int frameIndex = 0; frameIndex < 340; frameIndex++)
            {
                QuantizedInput input = ScriptedRaceInput.ForFrame(frameIndex);
                firstRun = Advance(firstRun, input);
                secondRun = Advance(secondRun, input);

                Assert.That(firstRun.DeterministicHash(), Is.EqualTo(secondRun.DeterministicHash()));
                if (firstRun.Player0.Status == PlayerStatus.Finished)
                {
                    break;
                }
            }

            Assert.That(firstRun.Player0.Status, Is.EqualTo(PlayerStatus.Finished));
            Assert.That(firstRun.Player0.Lap, Is.EqualTo(1));
            Assert.That(firstRun.FrameIndex, Is.EqualTo(286));
        }

        [Test]
        public void ScriptedPlayerSequenceCompletesOneLapFromPlayerOneSpawn()
        {
            RaceState defaultState = RaceState.CreateDefault();
            PlayerRaceState player1 = new(
                defaultState.Player1.Vehicle,
                defaultState.Player1.NextCheckpoint,
                defaultState.Player1.Lap,
                PlayerStatus.Playing);
            RaceState state = new(
                0,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                defaultState.Player0,
                player1);

            for (int frameIndex = 0; frameIndex < 500; frameIndex++)
            {
                QuantizedInput input = frameIndex < GlobalSettings.InputDelayFrames
                    ? QuantizedInput.Empty
                    : ScriptedRaceInput.ForFrame(frameIndex);
                state = RaceFrameRules.AdvanceOneFrame(state, new RaceInputs(QuantizedInput.Empty, input));
                if (state.Player1.Status == PlayerStatus.Finished)
                {
                    break;
                }
            }

            Assert.That(state.Player1.Status, Is.EqualTo(PlayerStatus.Finished));
            Assert.That(state.Player1.Lap, Is.EqualTo(1));
        }

        [Test]
        public void FinishedRaceKeepsSimulatingVehicleMovement()
        {
            RaceState defaultState = RaceState.CreateDefault();
            PlayerRaceState player0 = new(
                new VehicleState(0, -8_000, 0, 900),
                0,
                1,
                PlayerStatus.Finished);
            RaceState state = new(
                0,
                RacePhase.Finished,
                0,
                player0,
                defaultState.Player1);

            state = Advance(state, new QuantizedInput(0, GlobalSettings.MaxPedal, 0));

            Assert.That(state.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(state.Player0.Lap, Is.EqualTo(1));
            Assert.That(state.Player0.Vehicle.PositionX, Is.GreaterThan(0));
        }

        private static RaceState StepAt(RaceState state, VehicleState vehicle)
        {
            PlayerRaceState player0 = new(
                vehicle,
                state.Player0.NextCheckpoint,
                state.Player0.Lap,
                state.Player0.Status);
            RaceState atCheckpoint = new(
                state.FrameIndex,
                state.Phase,
                state.WinnerPlayerId,
                player0,
                state.Player1);
            return Advance(atCheckpoint, new QuantizedInput(0, 0, 0));
        }

        private static RaceState Advance(RaceState state, QuantizedInput input)
        {
            return RaceFrameRules.AdvanceOneFrame(state, new RaceInputs(input, QuantizedInput.Empty));
        }

        private static RaceState CreatePlayingRace(PlayerRaceState player0)
        {
            RaceState defaultState = RaceState.CreateDefault();
            return new RaceState(
                0,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                player0,
                defaultState.Player1);
        }

        private static RaceState CreateRacingState()
        {
            RaceState defaultState = RaceState.CreateDefault();
            return new RaceState(
                0,
                RacePhase.Racing,
                RaceState.NoWinnerPlayerId,
                defaultState.Player0.WithStatus(PlayerStatus.Playing),
                defaultState.Player1.WithStatus(PlayerStatus.Playing));
        }

    }
}
