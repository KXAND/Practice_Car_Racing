using CarRacing.Simulation;

namespace CarRacing.Runtime
{
    public sealed class ScriptedRaceInput : RaceInputSource
    {
        public override QuantizedInput ReadInput(int targetFrame)
        {
            return RaceDemoInputSequence.ForFrame(targetFrame);
        }
    }
}
