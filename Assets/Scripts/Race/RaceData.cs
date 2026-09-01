using System;

namespace CarRacing.Race
{
    // 被量化的输入，因为：摇杆值、死区、设备驱动和浮点计算都可能让不同端得到不同结果
    public struct QuantizedInput
    {
        public static QuantizedInput Empty => new(0, 0, 0);

        public QuantizedInput(int steering, int throttle, int brake)
        {
            Steering = Math.Clamp(steering, -GlobalSettings.MaxSteering, GlobalSettings.MaxSteering);
            Throttle = Math.Clamp(throttle, 0, GlobalSettings.MaxPedal);
            Brake = Math.Clamp(brake, 0, GlobalSettings.MaxPedal);
        }

        public int Steering;

        public int Throttle;

        public int Brake;
    }

    public struct RaceInputs
    {
        public RaceInputs(QuantizedInput player0, QuantizedInput player1)
        {
            Player0 = player0;
            Player1 = player1;
        }

        public QuantizedInput Player0;
        public QuantizedInput Player1;
    }

    public enum RacePhase : byte
    {
        WaitingForPlayers,
        Racing,
        WinnerDecided,
        Finished,
        Aborted
    }

    public enum PlayerStatus : byte
    {
        Unassigned,
        Loading,
        Ready,
        Playing,
        Finished,
        Disconnected
    }

    public readonly struct VehicleState
    {
        public VehicleState(int positionX, int positionZ, int speed, int headingTenths)
        {
            PositionX = positionX;
            PositionZ = positionZ;
            Speed = speed;
            HeadingTenths = headingTenths;
        }

        public int PositionX { get; }
        public int PositionZ { get; }
        public int Speed { get; }
        public int HeadingTenths { get; }
    }

    public readonly struct PlayerRaceState
    {
        public PlayerRaceState(VehicleState vehicle, int nextCheckpoint, int lap, PlayerStatus status)
        {
            Vehicle = vehicle;
            NextCheckpoint = nextCheckpoint;
            Lap = lap;
            Status = status;
        }

        public VehicleState Vehicle { get; }
        public int NextCheckpoint { get; }
        public int Lap { get; }
        public PlayerStatus Status { get; }

        public PlayerRaceState WithStatus(PlayerStatus status)
        {
            return new PlayerRaceState(Vehicle, NextCheckpoint, Lap, status);
        }
    }

    public readonly struct RaceState
    {
        // consts
        public const int DefaultStartX = 0;
        public const int Player0StartZ = -7_000;
        public const int Player1StartZ = -9_000;
        public const byte NoWinnerPlayerId = byte.MaxValue;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // fields
        public int FrameIndex { get; }
        public RacePhase Phase { get; }
        public byte WinnerPlayerId { get; }
        public PlayerRaceState Player0 { get; }
        public PlayerRaceState Player1 { get; }


        public RaceState(int frameIndex, RacePhase phase, byte winnerPlayerId, PlayerRaceState player0, PlayerRaceState player1)
        {
            FrameIndex = frameIndex;
            Phase = phase;
            WinnerPlayerId = winnerPlayerId;
            Player0 = player0;
            Player1 = player1;
        }

        public static RaceState CreateDefault()
        {
            return new RaceState(
                frameIndex: 0,
                RacePhase.WaitingForPlayers,
                NoWinnerPlayerId,
                CreatePlayerState(Player0StartZ),
                CreatePlayerState(Player1StartZ));
        }

        public PlayerRaceState GetPlayer(byte playerId)
        {
            return playerId == 0 ? Player0 : Player1;
        }

        public ulong DeterministicHash()
        {
            ulong hash = FnvOffsetBasis;
            hash = AppendInt32ToHash(hash, FrameIndex);
            hash = AppendInt32ToHash(hash, (int)Phase);
            hash = AppendInt32ToHash(hash, WinnerPlayerId);
            hash = AppendPlayerStateToHash(hash, Player0);
            return AppendPlayerStateToHash(hash, Player1);
        }

        private static PlayerRaceState CreatePlayerState(int startZ)
        {
            return new PlayerRaceState(
                new VehicleState(DefaultStartX, startZ, 0, 900),
                0,
                0,
                PlayerStatus.Unassigned);
        }

        private static ulong AppendPlayerStateToHash(ulong hash, PlayerRaceState state)
        {
            hash = AppendInt32ToHash(hash, state.Vehicle.PositionX);
            hash = AppendInt32ToHash(hash, state.Vehicle.PositionZ);
            hash = AppendInt32ToHash(hash, state.Vehicle.Speed);
            hash = AppendInt32ToHash(hash, state.Vehicle.HeadingTenths);
            hash = AppendInt32ToHash(hash, state.NextCheckpoint);
            hash = AppendInt32ToHash(hash, state.Lap);
            return AppendInt32ToHash(hash, (int)state.Status);
        }

        private static ulong AppendInt32ToHash(ulong hash, int value)
        {
            unchecked
            {
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= FnvPrime;
                }
            }

            return hash;
        }
    }
}
