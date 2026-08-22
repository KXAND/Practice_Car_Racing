namespace CarRacing.Simulation
{
    public static class RaceSimulation
    {
        public const int TickRate = 30;
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

        public static RaceState Step(RaceState state, QuantizedInput input)
        {
            int speed = UpdateSpeed(state.Vehicle.Speed, input);

            int headingTenths = NormalizeHeading(
                state.Vehicle.HeadingTenths + input.Steering * MaximumTurnTenthsPerTick * speed /
                (QuantizedInput.MaxSteering * MaximumSpeed));

            GetForwardDirection(headingTenths, out int forwardX, out int forwardZ);
            int positionX = state.Vehicle.PositionX + (int)((long)speed * forwardX / DirectionScale / TickRate);
            int positionZ = state.Vehicle.PositionZ + (int)((long)speed * forwardZ / DirectionScale / TickRate);
            ConstrainToTrack(ref positionX, ref positionZ, ref speed);

            VehicleState vehicle = new(positionX, positionZ, speed, headingTenths);
            if (state.Phase == RacePhase.Finished)
            {
                return new RaceState(
                    state.Frame + 1,
                    vehicle,
                    state.NextCheckpoint,
                    state.Lap,
                    RacePhase.Finished);
            }

            AdvanceCheckpoint(vehicle, state.NextCheckpoint, state.Lap, out int nextCheckpoint, out int lap, out RacePhase phase);
            return new RaceState(state.Frame + 1, vehicle, nextCheckpoint, lap, phase);
        }

        private static void AdvanceCheckpoint(
            VehicleState vehicle,
            int nextCheckpoint,
            int lap,
            out int updatedCheckpoint,
            out int updatedLap,
            out RacePhase phase)
        {
            updatedCheckpoint = nextCheckpoint;
            updatedLap = lap;
            phase = RacePhase.Racing;

            if (!IsInsideExpectedCheckpoint(vehicle, nextCheckpoint))
            {
                return;
            }

            if (nextCheckpoint < 3)
            {
                updatedCheckpoint++;
                return;
            }

            updatedCheckpoint = 0;
            updatedLap++;
            phase = RacePhase.Finished;
        }

        private static int UpdateSpeed(int speed, QuantizedInput input)
        {
            if (input.Throttle > 0 && input.Brake == 0)
            {
                int amount = input.Throttle * AccelerationPerSecond /
                    (QuantizedInput.MaxPedal * TickRate);
                if (speed < 0)
                {
                    speed += input.Throttle * BrakingPerSecond /
                        (QuantizedInput.MaxPedal * TickRate);
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
                        (QuantizedInput.MaxPedal * TickRate);
                    if (speed < 0)
                    {
                        speed = 0;
                    }
                }
                else
                {
                    speed -= input.Brake * AccelerationPerSecond /
                        (QuantizedInput.MaxPedal * TickRate);
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

        private static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }
    }
}
