using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class RaceCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset = new(0f, 12f, -16f);
        [SerializeField] private float positionSmoothTime = 0.18f;
        [SerializeField] private float rotationSharpness = 10f;

        private Vector3 positionVelocity;

        public void SetTarget(Transform value)
        {
            target = value;
            if (target != null)
            {
                SnapToTarget();
            }
        }

        private void Awake()
        {
            if (target == null)
            {
                Debug.LogError("RaceCameraFollow requires a target Transform.", this);
                enabled = false;
                return;
            }

            SnapToTarget();
        }

        private void LateUpdate()
        {
            Vector3 targetPosition = target.TransformPoint(localOffset);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref positionVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            Vector3 lookTarget = target.position + target.forward * 8f;
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
            float rotationT = 1f - Mathf.Exp(-rotationSharpness * Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationT);
        }

        private void SnapToTarget()
        {
            transform.position = target.TransformPoint(localOffset);
            Vector3 lookTarget = target.position + target.forward * 8f;
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
        }
    }
}
