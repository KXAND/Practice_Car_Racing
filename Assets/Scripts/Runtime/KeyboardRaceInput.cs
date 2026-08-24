using CarRacing.Simulation;
using UnityEngine.InputSystem;

namespace CarRacing.Runtime
{
    public sealed class KeyboardRaceInput : RaceInputSource
    {
        public override QuantizedInput ReadInput(int targetFrame)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return new QuantizedInput(0, 0, 0);
            }

            int steering = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) -
                           (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            int throttle = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? QuantizedInput.MaxPedal : 0;
            int brake = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? QuantizedInput.MaxPedal : 0;
            return new QuantizedInput(steering * QuantizedInput.MaxSteering, throttle, brake);
        }
    }
}
