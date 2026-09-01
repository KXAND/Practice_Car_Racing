using CarRacing.Race;

namespace CarRacing.Input
{
    public sealed class ScriptedRaceInput : RaceInputSource
    {
        public override QuantizedInput ReadInput(int targetFrameIndex)
        {
            return ForFrame(targetFrameIndex);
        }

        public static QuantizedInput ForFrame(int frameIndex)
        {
            bool turnLeft = IsTurnFrame(frameIndex);// 目前的赛道只需要左转
            return new QuantizedInput(
                turnLeft ? -GlobalSettings.MaxSteering : 0,
                GlobalSettings.MaxPedal,
                0);
        }

        private static bool IsTurnFrame(int frameIndex)
        {
            return (frameIndex >= 76 && frameIndex < 110) ||
                   (frameIndex >= 123 && frameIndex < 160) ||
                   (frameIndex >= 209 && frameIndex < 248) ||
                   (frameIndex >= 263 && frameIndex < 288);
        }
    }
}
