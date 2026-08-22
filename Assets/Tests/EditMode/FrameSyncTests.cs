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
        public void ServerAndBothClientsFinishTheSameLapWithTheSameWorldState()
        {
            FrameSyncServer server = new();
            FrameSyncClient firstClient = new(0);
            FrameSyncClient secondClient = new(1);

            for (int frame = 0; frame < 400; frame++)
            {
                QuantizedInput playerZeroInput = LapInputAt(frame);
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

        private static QuantizedInput LapInputAt(int frame)
        {
            bool turnLeft =
                (frame >= 76 && frame < 110) ||
                (frame >= 123 && frame < 160) ||
                (frame >= 209 && frame < 248) ||
                (frame >= 263 && frame < 288);
            return new QuantizedInput(
                turnLeft ? -QuantizedInput.MaxSteering : 0,
                QuantizedInput.MaxPedal,
                0);
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
