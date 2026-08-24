using NUnit.Framework;

namespace CarRacing.Simulation.Tests
{
    public sealed class FrameSyncTests
    {
        [Test]
        public void PlayerSlotsRejectReplacementConnectionsAfterTheMatchEnds()
        {
            PlayerSlotRegistry slots = new();

            Assert.That(slots.TryAssign(10, out byte firstPlayer), Is.True);
            Assert.That(slots.TryAssign(11, out byte secondPlayer), Is.True);
            slots.EndMatch();

            Assert.That(firstPlayer, Is.EqualTo(0));
            Assert.That(secondPlayer, Is.EqualTo(1));
            Assert.That(slots.TryAssign(12, out _), Is.False);
        }

        [Test]
        public void WorldHasherDistinguishesPlayerOrder()
        {
            TwoPlayerRaceState state = TwoPlayerRaceState.CreateDefault();
            TwoPlayerRaceState swapped = new(state.Player1, state.Player0);

            Assert.That(TwoPlayerRaceStateHasher.Compute(swapped), Is.Not.EqualTo(TwoPlayerRaceStateHasher.Compute(state)));
        }

        [Test]
        public void ServerRepeatsThePreviousInputWhenTheCurrentFrameIsMissing()
        {
            FrameSyncServer server = new();
            Assert.That(server.TrySubmit(new InputFrame(0, 0, new QuantizedInput(0, QuantizedInput.MaxPedal, 0)), 0), Is.True);

            ConfirmedFrame first = server.AdvanceFrame();
            ConfirmedFrame second = server.AdvanceFrame();

            Assert.That(first.Player0.Throttle, Is.EqualTo(QuantizedInput.MaxPedal));
            Assert.That(second.Player0.Throttle, Is.EqualTo(QuantizedInput.MaxPedal));
            Assert.That(second.Player1.Throttle, Is.EqualTo(0));
        }

        [Test]
        public void ServerRejectsInputsSubmittedForAnotherPlayer()
        {
            FrameSyncServer server = new();

            bool accepted = server.TrySubmit(new InputFrame(0, 1, new QuantizedInput(0, QuantizedInput.MaxPedal, 0)), 0);

            Assert.That(accepted, Is.False);
        }

        [Test]
        public void ClientOnlyAdvancesWhenConfirmedFramesAreContiguous()
        {
            FrameSyncClient client = new(0);
            client.Receive(new ConfirmedFrame(1, QuantizedInput.Empty, QuantizedInput.Empty));
            Assert.That(client.TryAdvance(), Is.False);

            client.Receive(new ConfirmedFrame(0, QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(client.TryAdvance(), Is.True);
            Assert.That(client.TryAdvance(), Is.True);
            Assert.That(client.NextConfirmedFrame, Is.EqualTo(2));
        }

        [Test]
        public void ServerAndClientProduceTheSameWorldStateForConfirmedFrames()
        {
            FrameSyncServer server = new();
            FrameSyncClient client = new(0);

            for (int frame = 0; frame < 120; frame++)
            {
                QuantizedInput input = frame < 90
                    ? new QuantizedInput(0, QuantizedInput.MaxPedal, 0)
                    : QuantizedInput.Empty;
                server.TrySubmit(new InputFrame(frame, 0, input), 0);
                ConfirmedFrame confirmed = server.AdvanceFrame();
                client.Receive(confirmed);
                Assert.That(client.TryAdvance(), Is.True);
                AssertWorldStateEqual(server.State, client.State);
            }
        }

        [Test]
        public void ClientTargetsInputsTwoFramesAhead()
        {
            FrameSyncClient client = new(1);

            InputFrame input = client.CreateLocalInput(new QuantizedInput(10, 20, 30), 10);

            Assert.That(input.Frame, Is.EqualTo(12));
            Assert.That(input.PlayerId, Is.EqualTo(1));
        }

        [Test]
        public void ClientStartsConfirmingAtTheServerSpecifiedFrame()
        {
            FrameSyncClient client = new(1, 10);

            client.Receive(new ConfirmedFrame(9, QuantizedInput.Empty, QuantizedInput.Empty));
            client.Receive(new ConfirmedFrame(10, QuantizedInput.Empty, QuantizedInput.Empty));

            Assert.That(client.TryAdvance(), Is.True);
            Assert.That(client.NextConfirmedFrame, Is.EqualTo(11));
        }

        [Test]
        public void ClientRollsBackMispredictedRemoteInputToTheConfirmedWorldState()
        {
            FrameSyncServer server = new();
            FrameSyncClient client = new(0);

            for (int frame = 0; frame < 12; frame++)
            {
                client.SampleLocalInput(QuantizedInput.Empty);
                client.PredictNextFrame();
                if (frame == 2)
                {
                    Assert.That(server.TrySubmit(new InputFrame(frame, 1, new QuantizedInput(0, QuantizedInput.MaxPedal, 0)), 1), Is.True);
                }
            }

            for (int frame = 0; frame < 12; frame++)
            {
                client.Receive(server.AdvanceFrame());
                while (client.TryAdvance())
                {
                }
            }

            Assert.That(client.RollbackCount, Is.EqualTo(1));
            Assert.That(client.LastRollbackLength, Is.GreaterThan(0));
            AssertWorldStateEqual(server.State, client.State);
        }

        [Test]
        public void UpstreamConditionerDelaysInputsAndDropsEveryConfiguredNthInput()
        {
            UpstreamInputConditioner conditioner = new(delayTicks: 1, dropEveryNthInput: 3);
            InputFrame first = new(2, 0, QuantizedInput.Empty);
            InputFrame second = new(3, 0, new QuantizedInput(0, QuantizedInput.MaxPedal, 0));
            InputFrame dropped = new(4, 0, new QuantizedInput(QuantizedInput.MaxSteering, 0, 0));

            conditioner.Enqueue(first, 0);
            conditioner.Enqueue(second, 1);
            conditioner.Enqueue(dropped, 2);

            Assert.That(conditioner.TryDequeueDue(0, out _), Is.False);
            Assert.That(conditioner.TryDequeueDue(1, out InputFrame deliveredFirst), Is.True);
            Assert.That(deliveredFirst.Frame, Is.EqualTo(first.Frame));
            Assert.That(conditioner.TryDequeueDue(2, out InputFrame deliveredSecond), Is.True);
            Assert.That(deliveredSecond.Frame, Is.EqualTo(second.Frame));
            Assert.That(conditioner.TryDequeueDue(3, out _), Is.False);
        }

        [Test]
        public void InvalidUpstreamDelayFallsBackToImmediateDelivery()
        {
            UpstreamInputConditioner conditioner = new(delayTicks: 2, dropEveryNthInput: 0);
            InputFrame input = new(2, 0, QuantizedInput.Empty);

            conditioner.Enqueue(input, 10);

            Assert.That(conditioner.TryDequeueDue(10, out InputFrame delivered), Is.True);
            Assert.That(delivered.Frame, Is.EqualTo(input.Frame));
        }

        [Test]
        public void WeakUpstreamConditionsTriggerRollbackAndRecoverConfirmedHashes()
        {
            FrameSyncServer server = new();
            FrameSyncClient client = new(0);
            UpstreamInputConditioner conditioner = new(delayTicks: 1, dropEveryNthInput: 3);
            int hashComparisons = 0;

            for (int tick = 0; tick < 90; tick++)
            {
                QuantizedInput sampledInput = tick % 2 == 0
                    ? new QuantizedInput(QuantizedInput.MaxSteering, QuantizedInput.MaxPedal, 0)
                    : new QuantizedInput(0, QuantizedInput.MaxPedal, 0);
                InputFrame input = client.SampleLocalInput(sampledInput);
                conditioner.Enqueue(input, tick);
                client.PredictNextFrame();
                while (conditioner.TryDequeueDue(tick, out InputFrame dueInput))
                {
                    server.TrySubmit(dueInput, dueInput.PlayerId);
                }

                ConfirmedFrame confirmed = server.AdvanceFrame();
                client.Receive(confirmed);
                while (client.TryAdvance())
                {
                }

                if ((confirmed.Frame + 1) % RaceSimulation.TickRate == 0)
                {
                    client.ReceiveServerHash(confirmed.Frame, TwoPlayerRaceStateHasher.Compute(server.State));
                    Assert.That(client.HashMismatch, Is.False);
                    hashComparisons++;
                }
            }

            Assert.That(client.RollbackCount, Is.GreaterThan(0));
            Assert.That(server.State.Player0.Vehicle.PositionZ, Is.Not.EqualTo(-7_000));
            Assert.That(hashComparisons, Is.GreaterThan(1));
            AssertWorldStateEqual(server.State, client.ConfirmedState);
        }

        [Test]
        public void ClientComparesAHashReceivedBeforeItsConfirmedFrame()
        {
            FrameSyncServer server = new();
            FrameSyncClient client = new(0);

            ConfirmedFrame confirmed = server.AdvanceFrame();
            ulong expectedHash = TwoPlayerRaceStateHasher.Compute(server.State);
            client.ReceiveServerHash(confirmed.Frame, expectedHash);
            client.Receive(confirmed);

            Assert.That(client.TryAdvance(), Is.True);
            Assert.That(client.ConfirmedHash, Is.EqualTo(expectedHash));
            Assert.That(client.HashMismatch, Is.False);
        }

        [Test]
        public void CorrectHashClearsTheDesyncReportedByAnEarlierIncorrectHash()
        {
            FrameSyncServer server = new();
            FrameSyncClient client = new(0);

            ConfirmedFrame confirmed = server.AdvanceFrame();
            client.Receive(confirmed);
            Assert.That(client.TryAdvance(), Is.True);

            ulong correctHash = TwoPlayerRaceStateHasher.Compute(server.State);
            client.ReceiveServerHash(confirmed.Frame, correctHash ^ 1UL);
            Assert.That(client.HashMismatch, Is.True);

            client.ReceiveServerHash(confirmed.Frame, correctHash);
            Assert.That(client.HashMismatch, Is.False);
            Assert.That(client.ConfirmedHash, Is.EqualTo(correctHash));
        }

        [Test]
        public void ServerAndBothClientsFinishTheSameLapWithTheSameWorldState()
        {
            FrameSyncServer server = new();
            FrameSyncClient firstClient = new(0);
            FrameSyncClient secondClient = new(1);

            for (int frame = 0; frame < 400; frame++)
            {
                QuantizedInput playerZeroInput = RaceDemoInputSequence.ForFrame(frame);
                Assert.That(server.TrySubmit(new InputFrame(frame, 0, playerZeroInput), 0), Is.True);

                ConfirmedFrame confirmed = server.AdvanceFrame();
                firstClient.Receive(confirmed);
                secondClient.Receive(confirmed);
                Assert.That(firstClient.TryAdvance(), Is.True);
                Assert.That(secondClient.TryAdvance(), Is.True);
                AssertWorldStateEqual(server.State, firstClient.State);
                AssertWorldStateEqual(server.State, secondClient.State);

                if (server.State.Player0.Phase == RacePhase.Finished)
                {
                    break;
                }
            }

            Assert.That(server.State.Player0.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(server.State.Player0.Lap, Is.EqualTo(1));
            Assert.That(server.State.Player1.Vehicle.Speed, Is.EqualTo(0));
        }

        private static void AssertWorldStateEqual(TwoPlayerRaceState expected, TwoPlayerRaceState actual)
        {
            AssertRaceStateEqual(expected.Player0, actual.Player0);
            AssertRaceStateEqual(expected.Player1, actual.Player1);
        }

        private static void AssertRaceStateEqual(RaceState expected, RaceState actual)
        {
            Assert.That(actual.Frame, Is.EqualTo(expected.Frame));
            Assert.That(actual.Vehicle.PositionX, Is.EqualTo(expected.Vehicle.PositionX));
            Assert.That(actual.Vehicle.PositionZ, Is.EqualTo(expected.Vehicle.PositionZ));
            Assert.That(actual.Vehicle.Speed, Is.EqualTo(expected.Vehicle.Speed));
            Assert.That(actual.Vehicle.HeadingTenths, Is.EqualTo(expected.Vehicle.HeadingTenths));
            Assert.That(actual.NextCheckpoint, Is.EqualTo(expected.NextCheckpoint));
            Assert.That(actual.Lap, Is.EqualTo(expected.Lap));
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
        }
    }
}
