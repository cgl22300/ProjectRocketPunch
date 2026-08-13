using UnityEngine;

namespace Demo.MyActiveRagdoll.script.Player
{
    /// <summary>
    /// 临时的期望姿态生成器。它只修改目标骨架，之后可直接替换为 Animation Rigging TwoBoneIK。
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class SingleArmPoseSolver : MonoBehaviour
    {
        private SingleArmController controller;
        [Range(0f, 1f)] [SerializeField] private float weight = 1f;

        private Transform _upperArm;
        private Transform _lowerArm;
        private Transform _hand;
        private Transform _targetSpace;
        private Vector3 _fallbackHintLocalDirection;
        private float _upperLength;
        private float _lowerLength;
        private bool _isReady;

        public Quaternion DesiredUpperLocalRotation { get; private set; }
        public Quaternion DesiredLowerLocalRotation { get; private set; }
        public Quaternion DesiredHandLocalRotation { get; private set; }
        public Vector3 DesiredShoulderPosition { get; private set; }
        public Quaternion DesiredShoulderFrame { get; private set; }
        public bool HasSolvedPose { get; private set; }

        private void Awake()
        {
            controller = controller != null ? controller : GetComponent<SingleArmController>();
            if (controller == null)
            {
                enabled = false;
                return;
            }

            _upperArm = controller.UpperArm;
            _lowerArm = controller.LowerArm;
            _hand = controller.Hand;
            _targetSpace = controller.TargetSpace;
            if (_upperArm == null || _lowerArm == null || _hand == null || _targetSpace == null)
            {
                enabled = false;
                return;
            }

            _upperLength = Vector3.Distance(_upperArm.position, _lowerArm.position);
            _lowerLength = Vector3.Distance(_lowerArm.position, _hand.position);
            var armDirection = (_hand.position - _upperArm.position).normalized;
            var bendDirection = Vector3.ProjectOnPlane(
                _lowerArm.position - _upperArm.position,
                armDirection);
            if (bendDirection.sqrMagnitude < 0.000001f)
            {
                bendDirection = _targetSpace.up;
            }
            _fallbackHintLocalDirection = _targetSpace.InverseTransformDirection(bendDirection.normalized);
            _isReady = true;
        }

        private void LateUpdate()
        {
            HasSolvedPose = false;
            if (!_isReady || !controller.HasReferencePose || weight <= 0f)
            {
                return;
            }

            // IK 只临时修改显示骨架，用于取得期望旋转；随后立即恢复动画姿态。
            // 物理层读取缓冲值，不再读取被自己上一帧改写过的骨骼。
            var upperPosition = _upperArm.localPosition;
            var lowerPosition = _lowerArm.localPosition;
            var handPosition = _hand.localPosition;
            var upperRotation = _upperArm.localRotation;
            var lowerRotation = _lowerArm.localRotation;
            var handRotation = _hand.localRotation;

            DesiredShoulderPosition = _upperArm.position;
            DesiredShoulderFrame = _upperArm.parent != null
                ? _upperArm.parent.rotation
                : transform.rotation;

            Solve(controller.PoseTargetPosition);
            DesiredUpperLocalRotation = _upperArm.localRotation;
            DesiredLowerLocalRotation = _lowerArm.localRotation;
            DesiredHandLocalRotation = _hand.localRotation;
            HasSolvedPose = true;

            _upperArm.localPosition = upperPosition;
            _upperArm.localRotation = upperRotation;
            _lowerArm.localPosition = lowerPosition;
            _lowerArm.localRotation = lowerRotation;
            _hand.localPosition = handPosition;
            _hand.localRotation = handRotation;
        }

        private void Solve(Vector3 targetPosition)
        {
            var rootPosition = _upperArm.position;
            var toTarget = targetPosition - rootPosition;
            var distance = Mathf.Clamp(toTarget.magnitude, 0.001f, _upperLength + _lowerLength - 0.001f);
            var targetDirection = toTarget.normalized;

            var hintDirection = controller.PoseElbowHintPosition - rootPosition;
            if (hintDirection.sqrMagnitude < 0.000001f)
            {
                hintDirection = _targetSpace.TransformDirection(_fallbackHintLocalDirection);
            }
            var bendDirection = Vector3.ProjectOnPlane(hintDirection, targetDirection).normalized;
            if (bendDirection.sqrMagnitude < 0.000001f)
            {
                bendDirection = Vector3.Cross(targetDirection, _targetSpace.right).normalized;
            }

            var along = (_upperLength * _upperLength - _lowerLength * _lowerLength + distance * distance)
                        / (2f * distance);
            var away = Mathf.Sqrt(Mathf.Max(0f, _upperLength * _upperLength - along * along));
            var desiredElbow = rootPosition + targetDirection * along + bendDirection * away;

            var upperStart = _upperArm.rotation;
            var upperSolved = Quaternion.FromToRotation(
                _lowerArm.position - rootPosition,
                desiredElbow - rootPosition) * upperStart;
            _upperArm.rotation = Quaternion.Slerp(upperStart, upperSolved, weight);

            var lowerStart = _lowerArm.rotation;
            var lowerSolved = Quaternion.FromToRotation(
                _hand.position - _lowerArm.position,
                targetPosition - _lowerArm.position) * lowerStart;
            _lowerArm.rotation = Quaternion.Slerp(lowerStart, lowerSolved, weight);

            // Two-bone 的位置解算不会自动保持末端旋转；拳头朝向必须单独约束。
            _hand.rotation = Quaternion.Slerp(
                _hand.rotation,
                controller.PoseTargetRotation,
                weight);
        }
    }
}
