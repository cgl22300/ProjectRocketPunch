using UnityEngine;

namespace Demo.MyActiveRagdoll.script.Player
{
    /// <summary>
    /// 只负责把玩家输入转换为手部目标，不直接修改骨骼。
    /// 鼠标左右控制弧线扫动，鼠标上下控制收拳/伸拳，滚轮控制出拳高度。
    /// </summary>
    [DefaultExecutionOrder(25)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SingleArmPoseSolver), typeof(SingleArmPhysicsDriver))]
    public sealed class SingleArmController : MonoBehaviour
    {
        private enum ArmSide
        {
            Left,
            Right
        }

        [Header("Arm Chain")]
        [SerializeField] private ArmSide controlledArm = ArmSide.Right;
        [SerializeField] private Transform upperArm;
        [SerializeField] private Transform lowerArm;
        [SerializeField] private Transform hand;

        [Header("Control Space")]
        [SerializeField] private InputManager input;
        [Tooltip("IK 弯曲方向的退化参考，通常填写胸口；操控方向仍由玩家根节点决定。")]
        [SerializeField] private Transform targetSpace;
        [Tooltip("操控方向参考，填写玩家预制体根节点；留空时使用本组件所在节点。")]
        [SerializeField] private Transform controlRoot;

        [Header("Animation Assist Prototype")]
        [SerializeField] private Animator animator;
        [SerializeField] private string triggerAction = "Attack";
        [Tooltip("可选的动画轨迹目标；留空时使用 Animator 当帧手骨位置。")]
        [SerializeField] private Transform animationTarget;
        [Range(0f, 1f)] [SerializeField] private float animationAssistWeight;
        [Min(0f)] [SerializeField] private float assistWeightSharpness = 12f;

        [Header("Punch Surface")]
        [Tooltip("鼠标横向：玩家根节点左右。允许负值反转。")]
        [SerializeField] private float lateralSensitivity = 0.0025f;
        [Tooltip("鼠标纵向：收拳/向前出拳。允许负值反转。")]
        [SerializeField] private float reachSensitivity = 0.0025f;
        [Tooltip("滚轮：拳面高度。允许负值反转。")]
        [SerializeField] private float wheelHeightSensitivity = 0.001f;
        [Tooltip("相对总臂长的左右范围。")]
        [SerializeField] private Vector2 lateralLimits = new Vector2(-0.55f, 0.55f);
        [Tooltip("相对总臂长的前向范围。最小值必须大于零，禁止目标进入身体后方。")]
        [SerializeField] private Vector2 reachLimits = new Vector2(0.18f, 0.92f);
        [Tooltip("相对总臂长的高度范围。")]
        [SerializeField] private Vector2 heightLimits = new Vector2(-0.55f, 0.35f);
        [SerializeField] private float defaultLateral = 0.2f;
        [SerializeField] private float defaultReach = 0.28f;
        [SerializeField] private float defaultHeight = -0.15f;
        [Min(0f)] [SerializeField] private float targetSharpness = 18f;
        [Min(0f)] [SerializeField] private float recoverySharpness = 26f;

        [Header("OmniMan Body Assist")]
        [Tooltip("基于 OmniMan 的 Spine/Spine1/Spine2/Shoulder 骨骼添加出拳联动。")]
        [SerializeField] private bool enableBodyAssist = true;
        [Range(0f, 25f)] [SerializeField] private float spineTwistDegrees = 7f;
        [Range(0f, 35f)] [SerializeField] private float chestTwistDegrees = 12f;
        [Range(0f, 25f)] [SerializeField] private float upperChestTwistDegrees = 8f;
        [Range(0f, 15f)] [SerializeField] private float forwardLeanDegrees = 5f;
        [Range(0f, 0.25f)] [SerializeField] private float shoulderProtraction = 0.08f;
        [Min(0f)] [SerializeField] private float bodyAssistSharpness = 10f;

        private InputManager _input;
        private float _upperLength;
        private float _lowerLength;
        private float _armLength;
        private float _desiredPunchLateral;
        private float _desiredPunchReach;
        private float _desiredPunchHeight;
        private float _punchLateral;
        private float _punchReach;
        private float _punchHeight;
        private float _previousPunchReach;
        private float _punchSpeed01;
        private float _currentAssistWeight;
        private Vector3 _poseTargetPosition;
        private Quaternion _poseTargetRotation;
        private Quaternion _handRotationOffset;
        private Vector3 _poseElbowHintPosition;
        private Transform _spine;
        private Transform _chest;
        private Transform _upperChest;
        private Transform _shoulder;
        private float _bodyAssistWeight;
        private bool _hasReferencePose;
        private bool _isReady;

        public Transform UpperArm => upperArm;
        public Transform LowerArm => lowerArm;
        public Transform Hand => hand;
        public Transform TargetSpace => targetSpace;
        public Vector3 PoseTargetPosition => _poseTargetPosition;
        public Quaternion PoseTargetRotation => _poseTargetRotation;
        public Vector3 PoseElbowHintPosition => _poseElbowHintPosition;
        public bool HasReferencePose => _hasReferencePose;
        public float PunchExtension01 => Mathf.InverseLerp(
            reachLimits.x * _armLength,
            reachLimits.y * _armLength,
            _punchReach);
        public float PunchSpeed01 => _punchSpeed01;
        public bool IsArmControlActive => _isReady
                                          && _hasReferencePose
                                          && _input.IsPrimaryArmControlHeld();

        public float AnimationAssistWeight
        {
            get => animationAssistWeight;
            set => animationAssistWeight = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            TryAutoBindHumanoidArm();
            _input = input != null ? input : GetComponentInParent<InputManager>();
            if (_input == null || !ValidateChain())
            {
                Debug.LogError($"{nameof(SingleArmController)} is missing input or arm bones.", this);
                enabled = false;
                return;
            }

            targetSpace = targetSpace != null ? targetSpace : transform;
            _upperLength = Vector3.Distance(upperArm.position, lowerArm.position);
            _lowerLength = Vector3.Distance(lowerArm.position, hand.position);
            _armLength = _upperLength + _lowerLength;
            if (_upperLength < 0.001f || _lowerLength < 0.001f)
            {
                Debug.LogError($"{nameof(SingleArmController)} found a zero-length arm segment.", this);
                enabled = false;
                return;
            }

            _poseTargetPosition = hand.position;
            _poseTargetRotation = hand.rotation;
            controlRoot = controlRoot != null ? controlRoot : transform;
            _isReady = true;
        }

        private void Update()
        {
            if (!_isReady || !_hasReferencePose)
            {
                return;
            }

            if (_input.WasAttackPressed() && animator != null && !string.IsNullOrEmpty(triggerAction))
            {
                animator.SetTrigger(triggerAction);
            }

            if (_input.IsPrimaryArmControlHeld())
            {
                UpdatePunchTarget(_input.GetPointerDelta(), _input.GetPointerDepthDelta());
            }
            else
            {
                ResetToRestTarget();
            }

            var sharpness = _input.IsPrimaryArmControlHeld() ? targetSharpness : recoverySharpness;
            var blend = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
            _punchLateral = Mathf.Lerp(_punchLateral, _desiredPunchLateral, blend);
            _punchReach = Mathf.Lerp(_punchReach, _desiredPunchReach, blend);
            _punchHeight = Mathf.Lerp(_punchHeight, _desiredPunchHeight, blend);

            var reachSpeed = Mathf.Abs(_punchReach - _previousPunchReach)
                             / Mathf.Max(0.0001f, Time.deltaTime * _armLength);
            _punchSpeed01 = Mathf.MoveTowards(
                _punchSpeed01,
                Mathf.Clamp01(reachSpeed / 4f),
                8f * Time.deltaTime);
            _previousPunchReach = _punchReach;
        }

        private void LateUpdate()
        {
            if (!_isReady)
            {
                return;
            }

            // Animator 在 Awake/Update 之后才会写入首帧骨骼。
            // 在第一个 LateUpdate 采集，避免把模型导入 T-Pose 当成休息拳位。
            if (!_hasReferencePose)
            {
                CaptureReferencePose();
                return;
            }

            ApplyOmniManBodyAssist();

            // 肩膀已经完成本帧送肩和躯干联动，此时再构造拳头目标。
            // 因此前向限制永远以“当前肩膀”而不是上一帧肩膀为基准。
            var manualTarget = BuildConstrainedPunchTarget();
            var hasAnimationSource = animationTarget != null || (animator != null && animator.isActiveAndEnabled);
            var animatedTarget = animationTarget != null ? animationTarget.position : hand.position;
            var assistBlend = 1f - Mathf.Exp(-assistWeightSharpness * Time.deltaTime);
            _currentAssistWeight = Mathf.Lerp(
                _currentAssistWeight,
                hasAnimationSource ? animationAssistWeight : 0f,
                assistBlend);
            _poseTargetPosition = Vector3.Lerp(manualTarget, animatedTarget, _currentAssistWeight);
            var manualRotation = GetHandRotationForDirection(
                _poseTargetPosition - lowerArm.position);
            var animatedRotation = animationTarget != null ? animationTarget.rotation : hand.rotation;
            _poseTargetRotation = Quaternion.Slerp(
                manualRotation,
                animatedRotation,
                _currentAssistWeight);
            _poseElbowHintPosition = ResolveElbowHintPosition();

        }

        private void CaptureReferencePose()
        {
            _poseTargetPosition = hand.position;
            _poseTargetRotation = hand.rotation;
            var referenceFrame = GetHandDirectionFrame(hand.position - lowerArm.position);
            _handRotationOffset = Quaternion.Inverse(referenceFrame) * hand.rotation;
            ResetPunchSurfaceParameters();
            _poseElbowHintPosition = ResolveElbowHintPosition();
            _hasReferencePose = true;

        }

        private Vector3 ResolveElbowHintPosition()
        {
            GetControlBasis(out var right, out var up, out var forward);
            var side = controlledArm == ArmSide.Right ? 1f : -1f;
            var armLength = _upperLength + _lowerLength;

            // 稳定的出拳肘平面：向身体外侧、略向下并保持在身体前方。
            // 前向分量必须为正，否则 IK 会选择背后的弯肘解。
            var hintDirection = right * (0.75f * side)
                                - up * 0.45f
                                + forward * 0.35f;
            return upperArm.position + hintDirection.normalized * armLength;
        }

        private Quaternion GetHandRotationForDirection(Vector3 direction)
        {
            return GetHandDirectionFrame(direction) * _handRotationOffset;
        }

        private Quaternion GetHandDirectionFrame(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = controlRoot.forward;
            }
            direction.Normalize();

            var frameUp = Vector3.ProjectOnPlane(controlRoot.up, direction).normalized;
            if (frameUp.sqrMagnitude < 0.000001f)
            {
                frameUp = Vector3.ProjectOnPlane(controlRoot.forward, direction).normalized;
            }
            if (frameUp.sqrMagnitude < 0.000001f)
            {
                frameUp = Vector3.up;
            }
            return Quaternion.LookRotation(direction, frameUp);
        }

        private void UpdatePunchTarget(Vector2 pointerDelta, float wheelDelta)
        {
            _desiredPunchLateral = Mathf.Clamp(
                _desiredPunchLateral + pointerDelta.x * lateralSensitivity,
                lateralLimits.x * _armLength,
                lateralLimits.y * _armLength);
            _desiredPunchReach = Mathf.Clamp(
                _desiredPunchReach + pointerDelta.y * reachSensitivity,
                Mathf.Max(0.01f, reachLimits.x * _armLength),
                Mathf.Max(0.02f, reachLimits.y * _armLength));
            _desiredPunchHeight = Mathf.Clamp(
                _desiredPunchHeight + wheelDelta * wheelHeightSensitivity,
                heightLimits.x * _armLength,
                heightLimits.y * _armLength);
        }

        private Vector3 BuildConstrainedPunchTarget()
        {
            GetControlBasis(out var right, out var up, out var forward);
            var offset = right * _punchLateral
                         + up * _punchHeight
                         + forward * Mathf.Max(0.01f, _punchReach);
            return ClampToReach(upperArm.position + offset, forward);
        }

        private Vector3 ClampToReach(Vector3 worldTarget, Vector3 fallbackDirection)
        {
            var root = upperArm.position;
            var offset = worldTarget - root;
            var distance = offset.magnitude;
            var minimum = Mathf.Abs(_upperLength - _lowerLength) + 0.001f;
            var maximum = _upperLength + _lowerLength - 0.001f;
            var direction = distance > 0.000001f ? offset / distance : fallbackDirection;
            return root + direction * Mathf.Clamp(distance, minimum, maximum);
        }

        private void ResetToRestTarget()
        {
            ResetPunchSurfaceParameters();
        }

        private void ResetPunchSurfaceParameters()
        {
            var side = controlledArm == ArmSide.Right ? 1f : -1f;
            _desiredPunchLateral = defaultLateral * side * _armLength;
            _desiredPunchReach = Mathf.Max(reachLimits.x, defaultReach) * _armLength;
            _desiredPunchHeight = defaultHeight * _armLength;

            if (!_hasReferencePose)
            {
                _punchLateral = _desiredPunchLateral;
                _punchReach = _desiredPunchReach;
                _punchHeight = _desiredPunchHeight;
                _previousPunchReach = _punchReach;
            }
        }

        private void ApplyOmniManBodyAssist()
        {
            if (!enableBodyAssist || _spine == null || _chest == null || _upperChest == null)
            {
                return;
            }

            var isControlling = _input.IsPrimaryArmControlHeld();
            var normalizedReach = Mathf.InverseLerp(
                reachLimits.x * _armLength,
                reachLimits.y * _armLength,
                _punchReach);
            var targetWeight = isControlling ? normalizedReach : 0f;
            var blend = 1f - Mathf.Exp(-bodyAssistSharpness * Time.deltaTime);
            _bodyAssistWeight = Mathf.Lerp(_bodyAssistWeight, targetWeight, blend);

            if (_bodyAssistWeight < 0.0001f)
            {
                return;
            }

            GetControlBasis(out var right, out var up, out var forward);
            var side = controlledArm == ArmSide.Right ? 1f : -1f;
            // 右拳需要让右肩向前，因此绕玩家根节点 Up 使用负 yaw；左拳相反。
            var twistSign = -side;

            AddWorldRotation(_spine, up, twistSign * spineTwistDegrees * _bodyAssistWeight);
            AddWorldRotation(_chest, up, twistSign * chestTwistDegrees * _bodyAssistWeight);
            AddWorldRotation(
                _upperChest,
                up,
                twistSign * upperChestTwistDegrees * _bodyAssistWeight);
            AddWorldRotation(
                _upperChest,
                right,
                forwardLeanDegrees * _bodyAssistWeight);

            if (_shoulder != null)
            {
                _shoulder.position += forward
                                      * (shoulderProtraction * _armLength * _bodyAssistWeight);
            }
        }

        private static void AddWorldRotation(Transform bone, Vector3 axis, float degrees)
        {
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        private void GetControlBasis(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            var reference = controlRoot != null ? controlRoot : transform;
            up = reference.up.normalized;
            if (up.sqrMagnitude < 0.000001f)
            {
                up = Vector3.up;
            }

            forward = Vector3.ProjectOnPlane(reference.forward, up).normalized;
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            }
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.forward;
            }

            // Cross 的顺序保证 forward=世界前方时 right=世界右方。
            right = Vector3.Cross(up, forward).normalized;
        }

        private bool ValidateChain()
        {
            return upperArm != null && lowerArm != null && hand != null;
        }

        private void TryAutoBindHumanoidArm()
        {
            animator = animator != null ? animator : GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = GetComponentInParent<Animator>();
            }
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            var isLeft = controlledArm == ArmSide.Left;
            upperArm = upperArm != null ? upperArm : animator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            lowerArm = lowerArm != null ? lowerArm : animator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            hand = hand != null ? hand : animator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _shoulder = animator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
            targetSpace = targetSpace != null
                ? targetSpace
                : animator.GetBoneTransform(HumanBodyBones.Chest)
                  ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        }
    }
}
