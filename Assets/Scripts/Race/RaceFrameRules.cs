namespace CarRacing.Race
{
    public static class RaceFrameRules
    {
        public const int OuterMinimumX = -18_000;
        public const int OuterMaximumX = 18_000;
        public const int OuterMinimumZ = -12_000;
        public const int OuterMaximumZ = 12_000;

        private const int MaximumSpeed = 10_000;
        private const int MaximumReverseSpeed = MaximumSpeed * 30 / 100;
        private const int AccelerationPerSecond = 3_000;
        private const int BrakingPerSecond = 6_000;
        private const int DragPerTick = 20;
        private const int MaximumTurnTenthsPerTick = 30;
        private const int VehicleRadius = 700;
        private const int InnerMinimumX = -9_000 - VehicleRadius;
        private const int InnerMaximumX = 9_000 + VehicleRadius;
        private const int InnerMinimumZ = -4_000 - VehicleRadius;
        private const int InnerMaximumZ = 4_000 + VehicleRadius;
        private const int DirectionScale = 1_000_000;

        #region Public API

        public static RaceState AdvanceOneFrame(RaceState current, RaceInputs inputs)
        {
            if (current.Phase == RacePhase.WaitingForPlayers || current.Phase == RacePhase.Aborted)
            {
                return current;
            }

            PlayerRaceState player0 = AdvancePlayer(current.Player0, inputs.Player0);
            PlayerRaceState player1 = AdvancePlayer(current.Player1, inputs.Player1);
            byte winnerPlayerId = DetermineWinnerPlayerId(current, player0, player1);
            RacePhase phase = DeterminePhase(current.Phase, player0, player1, winnerPlayerId);

            return new RaceState(current.FrameIndex + 1, phase, winnerPlayerId, player0, player1);
        }

        #endregion

        #region Frame Advancement

        private static PlayerRaceState AdvancePlayer(PlayerRaceState state, QuantizedInput input)
        {
            if (state.Status != PlayerStatus.Playing && state.Status != PlayerStatus.Finished)
            {
                return state;
            }

            VehicleState vehicle = AdvanceVehicle(state.Vehicle, input);
            if (state.Status == PlayerStatus.Finished)
            {
                return new PlayerRaceState(vehicle, state.NextCheckpoint, state.Lap, PlayerStatus.Finished);
            }

            AdvanceCheckpoint(vehicle, state.NextCheckpoint, state.Lap, out int nextCheckpoint, out int lap, out bool finished);
            PlayerStatus status = finished ? PlayerStatus.Finished : PlayerStatus.Playing;
            return new PlayerRaceState(vehicle, nextCheckpoint, lap, status);
        }

        #endregion

        #region Vehicle Movement

        private static VehicleState AdvanceVehicle(VehicleState vehicle, QuantizedInput input)
        {
            int speed = UpdateSpeed(vehicle.Speed, input);

            int headingTenths = NormalizeHeading(
                vehicle.HeadingTenths + input.Steering * MaximumTurnTenthsPerTick * speed /
                (GlobalSettings.MaxSteering * MaximumSpeed));

            GetForwardDirection(headingTenths, out int forwardX, out int forwardZ);
            int positionX = vehicle.PositionX + (int)((long)speed * forwardX / DirectionScale / GlobalSettings.TickRate);
            int positionZ = vehicle.PositionZ + (int)((long)speed * forwardZ / DirectionScale / GlobalSettings.TickRate);
            ConstrainToTrack(ref positionX, ref positionZ, ref speed);

            return new VehicleState(positionX, positionZ, speed, headingTenths);
        }

        private static int UpdateSpeed(int speed, QuantizedInput input)
        {
            if (input.Throttle > 0 && input.Brake == 0)
            {
                int amount = input.Throttle * AccelerationPerSecond /
                    (GlobalSettings.MaxPedal * GlobalSettings.TickRate);
                if (speed < 0)
                {
                    speed += input.Throttle * BrakingPerSecond /
                        (GlobalSettings.MaxPedal * GlobalSettings.TickRate);
                    if (speed > 0)
                    {
                        speed = 0;
                    }
                }
                else
                {
                    speed += amount;
                }
            }
            else if (input.Brake > 0 && input.Throttle == 0)
            {
                if (speed > 0)
                {
                    speed -= input.Brake * BrakingPerSecond /
                        (GlobalSettings.MaxPedal * GlobalSettings.TickRate);
                    if (speed < 0)
                    {
                        speed = 0;
                    }
                }
                else
                {
                    speed -= input.Brake * AccelerationPerSecond /
                        (GlobalSettings.MaxPedal * GlobalSettings.TickRate);
                }
            }
            if (speed > 0)
            {
                speed -= DragPerTick;
                if (speed < 0)
                {
                    speed = 0;
                }
            }
            else if (speed < 0)
            {
                speed += DragPerTick;
                if (speed > 0)
                {
                    speed = 0;
                }
            }

            if (speed > MaximumSpeed)
            {
                return MaximumSpeed;
            }

            return speed < -MaximumReverseSpeed ? -MaximumReverseSpeed : speed;
        }

        private static void GetForwardDirection(int headingTenths, out int forwardX, out int forwardZ)
        {
            int quadrant = headingTenths / 900;
            int offset = headingTenths % 900;
            int sine = SineFirstQuadrant(offset);
            int cosine = SineFirstQuadrant(900 - offset);

            switch (quadrant)
            {
                case 0:
                    forwardX = sine;
                    forwardZ = cosine;
                    return;
                case 1:
                    forwardX = cosine;
                    forwardZ = -sine;
                    return;
                case 2:
                    forwardX = -sine;
                    forwardZ = -cosine;
                    return;
                default:
                    forwardX = -cosine;
                    forwardZ = sine;
                    return;
            }
        }

        private static int SineFirstQuadrant(int tenths)
        {
            return (int)((long)tenths * (1_800 - tenths) * DirectionScale / 810_000);
        }

        private static int NormalizeHeading(int headingTenths)
        {
            int normalized = headingTenths % 3_600;
            return normalized < 0 ? normalized + 3_600 : normalized;
        }

        #endregion

        #region Lap Progress

        private static void AdvanceCheckpoint(
            VehicleState vehicle,
            int nextCheckpoint,
            int lap,
            out int updatedCheckpoint,
            out int updatedLap,
            out bool finished)
        {
            updatedCheckpoint = nextCheckpoint;
            updatedLap = lap;
            finished = false;

            if (!IsInsideExpectedCheckpoint(vehicle, nextCheckpoint))
            {
                return;
            }

            if (nextCheckpoint < 3)
            {
                updatedCheckpoint++;
                return;
            }
            else
            {
                updatedCheckpoint = 0;
                updatedLap++;
                finished = true;
                return;
            }
        }

        private static bool IsInsideExpectedCheckpoint(VehicleState vehicle, int checkpoint)
        {
            return checkpoint switch
            {
                0 => vehicle.PositionX >= 12_000 && Abs(vehicle.PositionZ) <= 2_000,
                1 => Abs(vehicle.PositionX) <= 3_000 && vehicle.PositionZ >= 7_500,
                2 => vehicle.PositionX <= -12_000 && Abs(vehicle.PositionZ) <= 2_000,
                3 => Abs(vehicle.PositionX) <= 3_000 && vehicle.PositionZ <= -7_500,
                _ => false
            };
        }

        #endregion

        #region Race Result

        private static byte DetermineWinnerPlayerId(RaceState current, PlayerRaceState player0, PlayerRaceState player1)
        {
            if (current.WinnerPlayerId != RaceState.NoWinnerPlayerId)
            {
                return current.WinnerPlayerId;
            }

            if (player0.Status == PlayerStatus.Finished)
            {
                return 0;
            }

            if (player1.Status == PlayerStatus.Finished)
            {
                return 1;
            }

            return RaceState.NoWinnerPlayerId;
        }

        private static RacePhase DeterminePhase(RacePhase currentPhase, PlayerRaceState player0, PlayerRaceState player1, byte winnerPlayerId)
        {
            if (currentPhase == RacePhase.Finished || currentPhase == RacePhase.Aborted)
            {
                return currentPhase;
            }

            if (player0.Status == PlayerStatus.Disconnected && player1.Status == PlayerStatus.Disconnected)
            {
                return RacePhase.Aborted;
            }

            bool player0Complete =
                player0.Status == PlayerStatus.Finished ||
                player0.Status == PlayerStatus.Disconnected;

            bool player1Complete =
                player1.Status == PlayerStatus.Finished ||
                player1.Status == PlayerStatus.Disconnected;

            if (player0Complete && player1Complete)// 已排除都断线的情况
            {
                return RacePhase.Finished;
            }

            return winnerPlayerId == RaceState.NoWinnerPlayerId ? RacePhase.Racing : RacePhase.WinnerDecided;
        }

        #endregion

        #region Track Constraints

        private static void ConstrainToTrack(ref int positionX, ref int positionZ, ref int speed)
        {
            int minimumX = OuterMinimumX + VehicleRadius;
            int maximumX = OuterMaximumX - VehicleRadius;
            int minimumZ = OuterMinimumZ + VehicleRadius;
            int maximumZ = OuterMaximumZ - VehicleRadius;

            bool collided = false;
            if (positionX < minimumX)
            {
                positionX = minimumX;
                collided = true;
            }
            else if (positionX > maximumX)
            {
                positionX = maximumX;
                collided = true;
            }

            if (positionZ < minimumZ)
            {
                positionZ = minimumZ;
                collided = true;
            }
            else if (positionZ > maximumZ)
            {
                positionZ = maximumZ;
                collided = true;
            }

            if (positionX > InnerMinimumX && positionX < InnerMaximumX &&
                positionZ > InnerMinimumZ && positionZ < InnerMaximumZ)
            {
                int distanceToLeft = positionX - InnerMinimumX;
                int distanceToRight = InnerMaximumX - positionX;
                int distanceToBottom = positionZ - InnerMinimumZ;
                int distanceToTop = InnerMaximumZ - positionZ;

                int smallestDistance = distanceToLeft;
                if (distanceToRight < smallestDistance)
                {
                    smallestDistance = distanceToRight;
                }

                if (distanceToBottom < smallestDistance)
                {
                    smallestDistance = distanceToBottom;
                }

                if (distanceToTop < smallestDistance)
                {
                    smallestDistance = distanceToTop;
                }

                if (smallestDistance == distanceToLeft)
                {
                    positionX = InnerMinimumX;
                }
                else if (smallestDistance == distanceToRight)
                {
                    positionX = InnerMaximumX;
                }
                else if (smallestDistance == distanceToBottom)
                {
                    positionZ = InnerMinimumZ;
                }
                else
                {
                    positionZ = InnerMaximumZ;
                }

                collided = true;
            }

            if (collided)
            {
                speed = 0;
            }
        }

        #endregion

        private static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }
    }
}
