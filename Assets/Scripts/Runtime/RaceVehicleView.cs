using CarRacing.Simulation;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class RaceVehicleView : MonoBehaviour
    {
        [SerializeField] private Transform vehicleModel;

        public Transform ViewTransform => vehicleModel;

        public void Render(RaceState previousState, RaceState currentState, float interpolation)
        {
            float x = Mathf.Lerp(previousState.Vehicle.PositionX, currentState.Vehicle.PositionX, interpolation) / 1_000f;
            float z = Mathf.Lerp(previousState.Vehicle.PositionZ, currentState.Vehicle.PositionZ, interpolation) / 1_000f;
            float heading = Mathf.LerpAngle(
                previousState.Vehicle.HeadingTenths,
                currentState.Vehicle.HeadingTenths,
                interpolation) / 10f;
            vehicleModel.SetPositionAndRotation(new Vector3(x, vehicleModel.position.y, z), Quaternion.Euler(0f, heading, 0f));
        }
    }
}
