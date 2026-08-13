using UnityEngine;

namespace Demo.MyActiveRagdoll.script.Player
{
    /// <summary>
    /// 固定观察角度、跟随稳定角色根节点的第三人称相机。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class MyActiveRagdollCamera : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("稳定的角色根节点或胶囊体，不要填写骨骼节点。")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private Camera targetCamera;

        [Header("Orbit")]
        [SerializeField] private float targetHeight = 1.35f;
        [SerializeField] private float distance = 4f;
        [SerializeField] private float minimumDistance = 1.5f;
        [SerializeField] private float maximumDistance = 7f;
        [Tooltip("相对于角色朝向的水平观察角度。0 表示正后方。")]
        [SerializeField] private float yawOffset;
        [SerializeField] private float pitch = 15f;
        [SerializeField] private float focusSharpness = 18f;
        [SerializeField] private float positionSharpness = 14f;

        [Header("Collision")]
        [SerializeField] private LayerMask obstructionLayers = ~0;
        [SerializeField, Min(0f)] private float collisionRadius = 0.15f;

        private readonly RaycastHit[] _collisionHits = new RaycastHit[8];
        private Vector3 _smoothedFocus;
        private bool _hasFocus;

        private void Awake()
        {
            followTarget = followTarget != null ? followTarget : transform;
            ResolveCamera();
            if (targetCamera != null)
            {
                targetCamera.transform.rotation = GetOrbitRotation();
            }
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (targetCamera == null || followTarget == null)
            {
                return;
            }

            var targetFocus = followTarget.position + Vector3.up * targetHeight;
            if (!_hasFocus)
            {
                _smoothedFocus = targetFocus;
                _hasFocus = true;
            }

            var focusBlend = 1f - Mathf.Exp(-focusSharpness * Time.deltaTime);
            _smoothedFocus = Vector3.Lerp(_smoothedFocus, targetFocus, focusBlend);

            var orbitRotation = GetOrbitRotation();
            var direction = orbitRotation * Vector3.back;
            var correctedDistance = GetCollisionCorrectedDistance(_smoothedFocus, direction);
            var desiredPosition = _smoothedFocus + direction * correctedDistance;
            var positionBlend = 1f - Mathf.Exp(-positionSharpness * Time.deltaTime);

            targetCamera.transform.position = Vector3.Lerp(
                targetCamera.transform.position,
                desiredPosition,
                positionBlend);
            targetCamera.transform.rotation = orbitRotation;
        }

        private Quaternion GetOrbitRotation()
        {
            return Quaternion.Euler(
                pitch,
                followTarget != null ? followTarget.eulerAngles.y + yawOffset : yawOffset,
                0f);
        }

        private float GetCollisionCorrectedDistance(Vector3 focusPoint, Vector3 direction)
        {
            var requestedDistance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            var hitCount = Physics.SphereCastNonAlloc(
                focusPoint,
                collisionRadius,
                direction,
                _collisionHits,
                requestedDistance,
                obstructionLayers,
                QueryTriggerInteraction.Ignore);

            var nearestDistance = requestedDistance;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = _collisionHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(followTarget))
                {
                    continue;
                }

                nearestDistance = Mathf.Min(nearestDistance, hit.distance);
            }

            return Mathf.Max(0.2f, nearestDistance - collisionRadius);
        }

        private void ResolveCamera()
        {
            if (targetCamera != null)
            {
                return;
            }

            targetCamera = Camera.main;
            if (targetCamera != null)
            {
                return;
            }

            var cameraObject = new GameObject("My Active Ragdoll Camera");
            cameraObject.tag = "MainCamera";
            targetCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumDistance = Mathf.Max(0.2f, minimumDistance);
            maximumDistance = Mathf.Max(minimumDistance, maximumDistance);
            distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            focusSharpness = Mathf.Max(0f, focusSharpness);
            positionSharpness = Mathf.Max(0f, positionSharpness);
        }
#endif
    }
}
