using CarRacing.Simulation;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class TwoPlayerRaceView : MonoBehaviour
    {
        [SerializeField] private RaceVehicleView player0View;
        [SerializeField] private RaceVehicleView player1View;
        [SerializeField] private RaceStatusView statusView;
        [SerializeField] private RaceCameraFollow raceCamera;

        private TwoPlayerRaceState previousState;
        private bool initialized;

        public void Render(TwoPlayerRaceState state, byte localPlayerId, FrameSyncDebugInfo debugInfo)
        {
            if (!initialized)
            {
                previousState = state;
                initialized = true;
                raceCamera.SetTarget(localPlayerId == 0 ? player0View.ViewTransform : player1View.ViewTransform);
            }

            player0View.Render(previousState.Player0, state.Player0, 1f);
            player1View.Render(previousState.Player1, state.Player1, 1f);
            statusView.Render(localPlayerId == 0 ? state.Player0 : state.Player1, debugInfo);
            previousState = state;
        }

        public void SetMatchEnded()
        {
            statusView.SetMatchEnded();
        }
    }
}
