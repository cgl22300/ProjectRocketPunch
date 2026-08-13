using UnityEngine;
using UnityEngine.InputSystem;

namespace Demo.MyActiveRagdoll.script
{
    public class InputManager : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actionAsset;

        private InputAction movement;
        private InputAction pointerDelta;
        private InputAction pointerScroll;
        private InputAction attack;
        private InputAction armControl;

        private void Awake()
        {
            if (actionAsset == null)
            {
                Debug.LogWarning(
                    $"{nameof(InputManager)} on {name} has no InputActionAsset. Arm mouse input is still available.",
                    this);
                return;
            }

            CacheAction();
        }

        private void OnEnable()
        {
            if (actionAsset != null)
            {
                actionAsset.Enable();
            }
        }

        private void OnDisable()
        {
            if (actionAsset != null)
            {
                actionAsset.Disable();
            }
        }

        protected void CacheAction()
        {
            movement = actionAsset.FindAction("Gameplay/Movement");
            pointerDelta = actionAsset.FindAction("Gameplay/MouseXY");
            pointerScroll = actionAsset.FindAction("Gameplay/MouseScroll");
            attack = actionAsset.FindAction("Gameplay/Attack");
            armControl = actionAsset.FindAction("Gameplay/ArmControl");
        }

        public virtual Vector3 GetMovementDirection()
        {
            if (movement == null)
            {
                return Vector3.zero;
            }

            return GetAxisWithCrossDeadZone(movement.ReadValue<Vector2>());
        }

        public virtual Vector3 GetAxisWithCrossDeadZone(Vector2 axis)
        {
            var deadzone = InputSystem.settings.defaultDeadzoneMin;
            axis.x = Mathf.Abs(axis.x) > deadzone ? RemapToDeadzone(axis.x, deadzone) : 0f;
            axis.y = Mathf.Abs(axis.y) > deadzone ? RemapToDeadzone(axis.y, deadzone) : 0f;
            return new Vector3(axis.x, 0f, axis.y);
        }

        protected float RemapToDeadzone(float value, float deadzone) =>
            (value - Mathf.Sign(value) * deadzone) / (1f - deadzone);

        public virtual Vector2 GetPointerDelta()
        {
            return pointerDelta?.ReadValue<Vector2>() ?? Vector2.zero;
        }

        public virtual float GetPointerDepthDelta()
        {
            return pointerScroll?.ReadValue<Vector2>().y ?? 0f;
        }

        public virtual bool IsPrimaryArmControlHeld()
        {
            return armControl?.IsPressed() ?? false;
        }

        public virtual bool WasAttackPressed()
        {
            return attack?.WasPressedThisFrame() ?? false;
        }
    }
}
