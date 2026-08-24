using CarRacing.Simulation;
using UnityEngine;

namespace CarRacing.Runtime
{
    public abstract class RaceInputSource : MonoBehaviour
    {
        public abstract QuantizedInput ReadInput(int targetFrame);
    }
}
