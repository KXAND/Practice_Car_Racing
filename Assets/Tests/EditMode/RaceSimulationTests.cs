using NUnit.Framework;

namespace CarRacing.Simulation.Tests
{
    public sealed class RaceSimulationTests
    {
        [Test]
        public void DefaultStateFacesTheFirstCheckpoint()
        {
            RaceState state = RaceState.CreateDefault();

            Assert.That(state.Vehicle.PositionX, Is.EqualTo(RaceState.DefaultStartX));
            Assert.That(state.Vehicle.PositionZ, Is.EqualTo(RaceState.DefaultStartZ));
            Assert.That(state.Vehicle.HeadingTenths, Is.EqualTo(900));
        }

        [Test]
        public void SustainedThrottleAcceleratesTowardsTheFirstCheckpoint()
        {
            RaceState state = RaceState.CreateDefault();
            QuantizedInput input = new(0, QuantizedInput.MaxPedal, 0);

            for (int frame = 0; frame < RaceSimulation.TickRate; frame++)
            {
                state = RaceSimulation.Step(state, input);
            }

            Assert.That(state.Frame, Is.EqualTo(RaceSimulation.TickRate));
            Assert.That(state.Vehicle.Speed, Is.GreaterThan(0));
            Assert.That(state.Vehicle.PositionX, Is.GreaterThan(RaceState.DefaultStartX));
            Assert.That(state.Vehicle.PositionZ, Is.EqualTo(RaceState.DefaultStartZ));
        }

        [Test]
        public void SteeringAtSpeedChangesTheVehicleCourse()
        {
            RaceState state = new(
                0,
                new VehicleState(0, -8_000, 6_000, 0),
                0,
                0,
                RacePhase.Racing);

            for (int frame = 0; frame < 5; frame++)
            {
                state = RaceSimulation.Step(state, new QuantizedInput(QuantizedInput.MaxSteering, 0, 0));
            }

            Assert.That(state.Vehicle.HeadingTenths, Is.GreaterThan(0));
            Assert.That(state.Vehicle.PositionX, Is.GreaterThan(0));
        }

        [Test]
        public void BrakingReachesAStoppedStateBeforeReverse()
        {
            RaceState state = new(
                0,
                new VehicleState(0, -8_000, 6_000, 0),
                0,
                0,
                RacePhase.Racing);

            bool reachedStoppedState = false;
            for (int frame = 0; frame < RaceSimulation.TickRate * 2; frame++)
            {
                state = RaceSimulation.Step(state, new QuantizedInput(0, 0, QuantizedInput.MaxPedal));
                if (state.Vehicle.Speed == 0)
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
            RaceState state = RaceState.CreateDefault();
            QuantizedInput brake = new(0, 0, QuantizedInput.MaxPedal);

            for (int frame = 0; frame < 120; frame++)
            {
                state = RaceSimulation.Step(state, brake);
            }

            Assert.That(state.Vehicle.Speed, Is.LessThan(0));
            Assert.That(state.Vehicle.Speed, Is.GreaterThanOrEqualTo(-3_000));
            Assert.That(state.Vehicle.PositionX, Is.LessThan(RaceState.DefaultStartX));
        }

        [Test]
        public void ForwardInputStopsAReversingVehicleBeforeAcceleratingForward()
        {
            RaceState state = new(
                0,
                new VehicleState(0, -8_000, -100, 900),
                0,
                0,
                RacePhase.Racing);
            QuantizedInput throttle = new(0, QuantizedInput.MaxPedal, 0);

            state = RaceSimulation.Step(state, throttle);
            Assert.That(state.Vehicle.Speed, Is.EqualTo(0));

            state = RaceSimulation.Step(state, throttle);
            Assert.That(state.Vehicle.Speed, Is.GreaterThan(0));
        }

        [Test]
        public void ReverseSteeringTurnsInTheOppositeDirection()
        {
            RaceState state = new(
                0,
                new VehicleState(0, -8_000, -6_000, 900),
                0,
                0,
                RacePhase.Racing);

            state = RaceSimulation.Step(state, new QuantizedInput(QuantizedInput.MaxSteering, 0, 0));

            Assert.That(state.Vehicle.HeadingTenths, Is.LessThan(900));
        }

        [Test]
        public void ReversingAfterAnOuterBoundaryCollisionMovesTheVehicleBackIntoTheTrack()
        {
            RaceState state = new(
                0,
                new VehicleState(RaceSimulation.OuterMaximumX - 700, 0, 0, 900),
                0,
                0,
                RacePhase.Racing);

            state = RaceSimulation.Step(state, new QuantizedInput(0, 0, QuantizedInput.MaxPedal));

            Assert.That(state.Vehicle.Speed, Is.LessThan(0));
            Assert.That(state.Vehicle.PositionX, Is.LessThan(RaceSimulation.OuterMaximumX - 700));
        }

        [Test]
        public void OuterTrackBoundaryStopsTheVehicle()
        {
            RaceState state = new(
                0,
                new VehicleState(17_000, 0, 10_000, 900),
                0,
                0,
                RacePhase.Racing);

            state = RaceSimulation.Step(state, new QuantizedInput(0, 0, 0));

            Assert.That(state.Vehicle.PositionX, Is.LessThanOrEqualTo(RaceSimulation.OuterMaximumX - 700));
            Assert.That(state.Vehicle.Speed, Is.EqualTo(0));
        }

        [Test]
        public void InnerTrackBoundaryStopsTheVehicle()
        {
            RaceState state = new(
                0,
                new VehicleState(0, -4_800, 5_000, 0),
                0,
                0,
                RacePhase.Racing);

            state = RaceSimulation.Step(state, new QuantizedInput(0, 0, 0));

            Assert.That(state.Vehicle.PositionZ, Is.EqualTo(-4_700));
            Assert.That(state.Vehicle.Speed, Is.EqualTo(0));
        }

        [Test]
        public void OrderedCheckpointsFinishOneLap()
        {
            RaceState state = RaceState.CreateDefault();

            state = StepAt(state, new VehicleState(13_000, 0, 0, 0));
            Assert.That(state.NextCheckpoint, Is.EqualTo(1));

            state = StepAt(state, new VehicleState(0, 8_000, 0, 0));
            Assert.That(state.NextCheckpoint, Is.EqualTo(2));

            state = StepAt(state, new VehicleState(-13_000, 0, 0, 0));
            Assert.That(state.NextCheckpoint, Is.EqualTo(3));

            state = StepAt(state, new VehicleState(0, -8_000, 0, 0));
            Assert.That(state.Lap, Is.EqualTo(1));
            Assert.That(state.Phase, Is.EqualTo(RacePhase.Finished));
        }

        [Test]
        public void SameInputSequenceProducesTheSameHashEveryFrame()
        {
            QuantizedInput[] inputs =
            {
                new QuantizedInput(0, QuantizedInput.MaxPedal, 0),
                new QuantizedInput(80, QuantizedInput.MaxPedal, 0),
                new QuantizedInput(-50, QuantizedInput.MaxPedal, 0),
                new QuantizedInput(0, 0, QuantizedInput.MaxPedal)
            };
            RaceState firstRun = RaceState.CreateDefault();
            RaceState secondRun = RaceState.CreateDefault();

            for (int frame = 0; frame < 120; frame++)
            {
                QuantizedInput input = inputs[frame % inputs.Length];
                firstRun = RaceSimulation.Step(firstRun, input);
                secondRun = RaceSimulation.Step(secondRun, input);

                Assert.That(RaceStateHasher.Compute(firstRun), Is.EqualTo(RaceStateHasher.Compute(secondRun)));
            }
        }

        [Test]
        public void FixedInputSequenceCompletesOneDeterministicLap()
        {
            RaceState firstRun = RaceState.CreateDefault();
            RaceState secondRun = RaceState.CreateDefault();

            for (int frame = 0; frame < 340; frame++)
            {
                QuantizedInput input = RaceDemoInputSequence.ForFrame(frame);
                firstRun = RaceSimulation.Step(firstRun, input);
                secondRun = RaceSimulation.Step(secondRun, input);

                Assert.That(RaceStateHasher.Compute(firstRun), Is.EqualTo(RaceStateHasher.Compute(secondRun)));
                if (firstRun.Phase == RacePhase.Finished)
                {
                    break;
                }
            }

            Assert.That(firstRun.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(firstRun.Lap, Is.EqualTo(1));
            Assert.That(firstRun.Frame, Is.EqualTo(286));
        }

        [Test]
        public void ScriptedPlayerSequenceCompletesOneLapFromPlayerOneSpawn()
        {
            RaceState state = TwoPlayerRaceState.CreateDefault().Player1;

            for (int frame = 0; frame < 500; frame++)
            {
                QuantizedInput input = frame < FrameSyncClient.InputDelayFrames
                    ? QuantizedInput.Empty
                    : RaceDemoInputSequence.ForFrame(frame);
                state = RaceSimulation.Step(state, input);
                if (state.Phase == RacePhase.Finished)
                {
                    break;
                }
            }

            Assert.That(state.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(state.Lap, Is.EqualTo(1));
        }

        [Test]
        public void FinishedRaceKeepsSimulatingVehicleMovement()
        {
            RaceState state = new(
                0,
                new VehicleState(0, -8_000, 0, 900),
                0,
                1,
                RacePhase.Finished);

            state = RaceSimulation.Step(state, new QuantizedInput(0, QuantizedInput.MaxPedal, 0));

            Assert.That(state.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(state.Lap, Is.EqualTo(1));
            Assert.That(state.Vehicle.PositionX, Is.GreaterThan(0));
        }

        private static RaceState StepAt(RaceState state, VehicleState vehicle)
        {
            RaceState atCheckpoint = new(
                state.Frame,
                vehicle,
                state.NextCheckpoint,
                state.Lap,
                state.Phase);
            return RaceSimulation.Step(atCheckpoint, new QuantizedInput(0, 0, 0));
        }

    }
}
