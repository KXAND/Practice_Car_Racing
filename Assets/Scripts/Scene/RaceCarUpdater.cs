using CarRacing.Race;
using UnityEngine;

namespace CarRacing.Scene
{
    /// <summary>
    /// 更新汽车和相机的 transform
    /// </summary>
    public sealed class RaceCarUpdater : MonoBehaviour
    {
        [SerializeField] private Transform player0Transform;
        [SerializeField] private Transform player1Transform;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Vector3 cameraLocalOffset = new(0f, 6f, -5f);
        [SerializeField] private float cameraPositionSmoothTime = 0.18f;
        [SerializeField] private float cameraRotationSharpness = 10f;

        private Transform cameraTarget;
        private Vector3 cameraPositionVelocity;

        public void UpdateState(RaceState state, byte localPlayerId)
        {
            Transform localPlayerTransform = localPlayerId == 0 ? player0Transform : player1Transform;
            if (cameraTarget != localPlayerTransform)
            {
                cameraTarget = localPlayerTransform;

                cameraTransform.position = cameraTarget.TransformPoint(cameraLocalOffset);
                Vector3 lookTarget = cameraTarget.position + cameraTarget.forward * GlobalSettings.CameraLookForwardDistance;
                cameraTransform.rotation = Quaternion.LookRotation(lookTarget - cameraTransform.position, Vector3.up);
            }

            ApplyVehicleState(player0Transform, state.Player0.Vehicle);
            ApplyVehicleState(player1Transform, state.Player1.Vehicle);
        }

        // 平滑位移 camera
        void LateUpdate()
        {
            if (cameraTarget == null)
            {
                return;
            }

            Vector3 targetPosition = cameraTarget.TransformPoint(cameraLocalOffset);
            cameraTransform.position = Vector3.SmoothDamp(
                cameraTransform.position,
                targetPosition,
                ref cameraPositionVelocity,
                cameraPositionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            Vector3 lookTarget = cameraTarget.position + cameraTarget.forward * GlobalSettings.CameraLookForwardDistance;
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - cameraTransform.position, Vector3.up);
            float rotationT = 1f - Mathf.Exp(-cameraRotationSharpness * Time.unscaledDeltaTime);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation, rotationT);
        }

        #region Private Func
        private static void ApplyVehicleState(Transform vehicleTransform, VehicleState state)
        {
            // convert state to coordinates
            float x = state.PositionX / GlobalSettings.PositionUnitsPerUnityUnit;
            float z = state.PositionZ / GlobalSettings.PositionUnitsPerUnityUnit;
            float heading = state.HeadingTenths / GlobalSettings.HeadingTenthsPerDegree;

            vehicleTransform.SetPositionAndRotation(
                new Vector3(x, vehicleTransform.position.y, z),
                Quaternion.Euler(0f, heading, 0f));
        }
    }
    #endregion
}
