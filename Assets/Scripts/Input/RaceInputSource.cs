using CarRacing.Race;
using UnityEngine;

namespace CarRacing.Input
{
    public abstract class RaceInputSource : MonoBehaviour
    {
        public abstract QuantizedInput ReadInput(int targetFrameIndex);
    }
}
