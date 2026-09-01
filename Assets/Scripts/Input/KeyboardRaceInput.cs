using CarRacing.Race;
using UnityEngine.InputSystem;

namespace CarRacing.Input
{
    public sealed class KeyboardRaceInput : RaceInputSource
    {
        public override QuantizedInput ReadInput(int targetFrameIndex)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return new QuantizedInput(0, 0, 0);
            }

            int steering = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) -
                           (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
            int throttle = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? GlobalSettings.MaxPedal : 0;
            int brake = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? GlobalSettings.MaxPedal : 0;
           
            return new QuantizedInput(steering * GlobalSettings.MaxSteering, throttle, brake);
        }
    }
}
