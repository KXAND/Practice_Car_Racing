using CarRacing.Simulation;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class SinglePlayerRaceController : MonoBehaviour
    {
        [SerializeField] private RaceInputSource inputSource;
        [SerializeField] private RaceVehicleView vehicleView;
        [SerializeField] private RaceStatusView statusView;

        private RaceState previousState;
        private RaceState currentState;
        private float accumulatedTime;

        private void Awake()
        {
            if (inputSource == null || vehicleView == null || statusView == null)
            {
                Debug.LogError("SinglePlayerRaceController requires input, vehicle view, and status view references.", this);
                enabled = false;
                return;
            }

            RestartRace();
        }

        private void Update()
        {
            accumulatedTime += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            float tickDuration = 1f / RaceSimulation.TickRate;

            while (accumulatedTime >= tickDuration)
            {
                previousState = currentState;
                currentState = RaceSimulation.Step(currentState, inputSource.ReadInput(currentState.Frame));
                accumulatedTime -= tickDuration;
            }

            vehicleView.Render(previousState, currentState, accumulatedTime / tickDuration);
            statusView.Render(currentState);
        }

        public void RestartRace()
        {
            currentState = RaceState.CreateDefault();
            previousState = currentState;
            accumulatedTime = 0f;
            vehicleView.Render(previousState, currentState, 0f);
            statusView.Render(currentState);
        }
    }
}
