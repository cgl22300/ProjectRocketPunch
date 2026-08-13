using UnityEngine;
using UnityEngine.InputSystem;

namespace NetworkExperience.Demo.PhysicDemo1
{
    /// <summary>
    /// Lightweight third-person movement using CharacterController and the Input System.
    /// No InputActionAsset or Cinemachine dependency is required.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(OmniManAnimationDriver))]
    public sealed class OmniManThirdPersonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera movementCamera;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 3.5f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float acceleration = 18f;
        [SerializeField, Min(0.01f)] private float rotationSmoothTime = 0.08f;

        [Header("Air")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -25f;

        private CharacterController _characterController;
        private OmniManAnimationDriver _animationDriver;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private float _rotationVelocity;

        public bool IsGrounded => _characterController != null && _characterController.isGrounded;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _animationDriver = GetComponent<OmniManAnimationDriver>();
            ResolveCamera();
        }

        private void Update()
        {
            ResolveCamera();

            Vector2 moveInput = ReadMoveInput();
            bool sprint = IsSprintPressed();
            bool grounded = _characterController.isGrounded;

            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (grounded && WasJumpPressed())
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            Vector3 desiredDirection = GetCameraRelativeDirection(moveInput);
            float targetSpeed = moveInput.sqrMagnitude > 0.001f
                ? moveSpeed * (sprint ? sprintMultiplier : 1f)
                : 0f;
            Vector3 desiredVelocity = desiredDirection * targetSpeed;

            _planarVelocity = Vector3.MoveTowards(
                _planarVelocity,
                desiredVelocity,
                acceleration * Time.deltaTime);

            if (desiredDirection.sqrMagnitude > 0.001f)
                RotateTowards(desiredDirection);

            _verticalVelocity += gravity * Time.deltaTime;
            Vector3 velocity = _planarVelocity + Vector3.up * _verticalVelocity;
            _characterController.Move(velocity * Time.deltaTime);

            float maximumSpeed = Mathf.Max(0.01f, moveSpeed * sprintMultiplier);
            float normalizedSpeed = Mathf.Clamp01(_planarVelocity.magnitude / maximumSpeed);
            _animationDriver.SetLocomotion(normalizedSpeed, _characterController.isGrounded);

            if (WasAttackPressed())
                _animationDriver.Attack();
            if (WasHitTestPressed())
                _animationDriver.PlayHit();
        }

        public void PlayHit()
        {
            _animationDriver?.PlayHit();
        }

        private void ResolveCamera()
        {
            if (movementCamera == null)
                movementCamera = Camera.main;
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            Transform cameraTransform = movementCamera != null
                ? movementCamera.transform
                : transform;

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            Vector3 direction = forward * input.y + right * input.x;
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        private void RotateTowards(Vector3 direction)
        {
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float smoothedAngle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                targetAngle,
                ref _rotationVelocity,
                rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
        }

        private static Vector2 ReadMoveInput()
        {
            Vector2 keyboardInput = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                keyboardInput.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                keyboardInput.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
            }

            Vector2 gamepadInput =
                Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
            Vector2 input = gamepadInput.sqrMagnitude > keyboardInput.sqrMagnitude
                ? gamepadInput
                : keyboardInput;
            return Vector2.ClampMagnitude(input, 1f);
        }

        private static bool IsSprintPressed()
        {
            return (Keyboard.current != null &&
                    (Keyboard.current.leftShiftKey.isPressed ||
                     Keyboard.current.rightShiftKey.isPressed)) ||
                   (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
        }

        private static bool WasJumpPressed()
        {
            return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                   (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        }

        private static bool WasAttackPressed()
        {
            return (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                   (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
        }

        private static bool WasHitTestPressed()
        {
            return (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) ||
                   (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame);
        }
    }
}
