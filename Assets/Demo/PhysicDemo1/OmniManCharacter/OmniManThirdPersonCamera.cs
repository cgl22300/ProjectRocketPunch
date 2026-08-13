using UnityEngine;
using UnityEngine.InputSystem;

namespace NetworkExperience.Demo.PhysicDemo1
{
    /// <summary>
    /// Simple orbiting third-person camera. It uses the scene Main Camera when present,
    /// or creates one automatically when the playable prefab is dropped into an empty scene.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class OmniManThirdPersonCamera : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private Camera targetCamera;

        [Header("Orbit")]
        [SerializeField] private float targetHeight = 1.45f;
        [SerializeField] private float distance = 4f;
        [SerializeField] private float minimumDistance = 1.5f;
        [SerializeField] private float maximumDistance = 7f;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float gamepadSensitivity = 120f;
        [SerializeField] private Vector2 pitchLimits = new Vector2(-10f, 65f);
        [SerializeField] private float positionSmoothTime = 0.05f;

        [Header("Collision")]
        [SerializeField] private LayerMask obstructionLayers = ~0;
        [SerializeField, Min(0f)] private float collisionRadius = 0.15f;

        private float _yaw;
        private float _pitch = 15f;
        private Vector3 _positionVelocity;
        private readonly RaycastHit[] _collisionHits = new RaycastHit[8];

        private void Awake()
        {
            if (followTarget == null)
                followTarget = transform;

            ResolveCamera();
            _yaw = followTarget.eulerAngles.y;
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (targetCamera == null || followTarget == null)
                return;

            ReadOrbitInput();
            ReadZoomInput();

            Vector3 focusPoint = followTarget.position + Vector3.up * targetHeight;
            Quaternion orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 direction = orbitRotation * Vector3.back;
            float correctedDistance = GetCollisionCorrectedDistance(focusPoint, direction);
            Vector3 desiredPosition = focusPoint + direction * correctedDistance;

            targetCamera.transform.position = Vector3.SmoothDamp(
                targetCamera.transform.position,
                desiredPosition,
                ref _positionVelocity,
                positionSmoothTime);
            targetCamera.transform.rotation = orbitRotation;
        }

        private void ResolveCamera()
        {
            if (targetCamera != null)
                return;

            targetCamera = Camera.main;
            if (targetCamera != null)
                return;

            var cameraObject = new GameObject("Third Person Camera");
            cameraObject.tag = "MainCamera";
            targetCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        private void ReadOrbitInput()
        {
            Vector2 orbitDelta = Vector2.zero;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
                orbitDelta += mouse.delta.ReadValue() * mouseSensitivity;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
                orbitDelta += gamepad.rightStick.ReadValue() * gamepadSensitivity * Time.deltaTime;

            _yaw += orbitDelta.x;
            _pitch = Mathf.Clamp(_pitch - orbitDelta.y, pitchLimits.x, pitchLimits.y);
        }

        private void ReadZoomInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - scroll * 0.005f, minimumDistance, maximumDistance);
        }

        private float GetCollisionCorrectedDistance(Vector3 focusPoint, Vector3 direction)
        {
            float requestedDistance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            int hitCount = Physics.SphereCastNonAlloc(
                focusPoint,
                collisionRadius,
                direction,
                _collisionHits,
                requestedDistance,
                obstructionLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = requestedDistance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _collisionHits[i];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(followTarget))
                {
                    continue;
                }

                nearestDistance = Mathf.Min(nearestDistance, hit.distance);
            }

            return Mathf.Max(0.2f, nearestDistance - collisionRadius);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumDistance = Mathf.Max(0.2f, minimumDistance);
            maximumDistance = Mathf.Max(minimumDistance, maximumDistance);
            distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
        }
#endif
    }
}
