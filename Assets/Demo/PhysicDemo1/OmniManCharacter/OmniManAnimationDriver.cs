using UnityEngine;

namespace NetworkExperience.Demo.PhysicDemo1
{
    /// <summary>
    /// Centralizes the parameter names used by the Omni Man Animator Controller.
    /// It can also be called directly by UI buttons, Timeline signals or gameplay code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class OmniManAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int HitId = Animator.StringToHash("Hit");

        [SerializeField] private Animator animator;

        public Animator Animator => animator;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        public void SetLocomotion(float normalizedSpeed, bool grounded)
        {
            if (animator == null)
                return;

            animator.SetFloat(SpeedId, Mathf.Clamp01(normalizedSpeed), 0.1f, Time.deltaTime);
            animator.SetBool(GroundedId, grounded);
        }

        public void Attack()
        {
            if (animator == null)
                return;

            animator.ResetTrigger(HitId);
            animator.SetTrigger(AttackId);
        }

        public void PlayHit()
        {
            if (animator == null)
                return;

            animator.ResetTrigger(AttackId);
            animator.SetTrigger(HitId);
        }

        public void ReturnToLocomotion()
        {
            if (animator == null)
                return;

            animator.ResetTrigger(AttackId);
            animator.ResetTrigger(HitId);
            animator.CrossFade("Locomotion", 0.1f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }
#endif
    }
}
