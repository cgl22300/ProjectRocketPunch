using UnityEngine;

namespace Demo.MyActiveRagdoll.script.Player
{
    /// <summary>
    /// 用三段刚体追赶期望骨骼姿态，并将物理结果写回显示骨骼。
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SingleArmPhysicsDriver : MonoBehaviour, IArmImpactReceiver
    {
        [Header("References")]
        private SingleArmController controller;
        private SingleArmPoseSolver poseSolver;
        [Tooltip("可选。填写角色胶囊刚体后，拳击反作用会传递给身体。")]
        [SerializeField] private Rigidbody bodyAnchor;

        [Header("Mass")]
        [Min(0.01f)] [SerializeField] private float upperArmMass = 2f;
        [Min(0.01f)] [SerializeField] private float lowerArmMass = 1.4f;
        [Min(0.01f)] [SerializeField] private float handMass = 0.8f;
        [Min(0.005f)] [SerializeField] private float limbRadius = 0.065f;
        [Min(0.005f)] [SerializeField] private float handRadius = 0.09f;

        [Header("Muscle")]
        [Min(0f)] [SerializeField] private float spring = 900f;
        [Min(0f)] [SerializeField] private float damper = 80f;
        [Min(0f)] [SerializeField] private float maximumForce = 2500f;
        [Range(0f, 1f)] [SerializeField] private float muscleWeight = 1f;
        [Min(0f)] [SerializeField] private float impactRecoverySpeed = 2f;
        [Range(0.1f, 1f)] [SerializeField] private float movingMuscleMultiplier = 0.82f;
        [Range(1f, 2f)] [SerializeField] private float impactMuscleMultiplier = 1.3f;

        [Header("Joint Limits")]
        [Range(1f, 177f)] [SerializeField] private float shoulderLimit = 120f;
        [Range(1f, 177f)] [SerializeField] private float elbowLimit = 145f;
        [Range(1f, 177f)] [SerializeField] private float wristLimit = 70f;

        private Transform _upperTarget;
        private Transform _lowerTarget;
        private Transform _handTarget;
        private GameObject _physicsRoot;
        private Rigidbody _generatedAnchor;
        private Rigidbody _upperBody;
        private Rigidbody _lowerBody;
        private Rigidbody _handBody;
        private ConfigurableJoint _upperJoint;
        private ConfigurableJoint _lowerJoint;
        private ConfigurableJoint _handJoint;
        private Quaternion _upperBindRotation;
        private Quaternion _lowerBindRotation;
        private Quaternion _handBindRotation;
        private Quaternion _desiredUpperRotation;
        private Quaternion _desiredLowerRotation;
        private Quaternion _desiredHandRotation;
        private Vector3 _desiredShoulderPosition;
        private Quaternion _desiredShoulderFrame;
        private float _impactMuscleWeight = 1f;
        private bool _isReady;

        public float EffectiveMuscleWeight => muscleWeight * _impactMuscleWeight;

        private void Awake()
        {
            controller = controller != null ? controller : GetComponent<SingleArmController>();
            poseSolver = poseSolver != null ? poseSolver : GetComponent<SingleArmPoseSolver>();
            if (controller == null)
            {
                enabled = false;
                return;
            }

            if (poseSolver == null)
            {
                enabled = false;
                return;
            }

            _upperTarget = controller.UpperArm;
            _lowerTarget = controller.LowerArm;
            _handTarget = controller.Hand;
            if (_upperTarget == null || _lowerTarget == null || _handTarget == null)
            {
                enabled = false;
                return;
            }

        }

        private void FixedUpdate()
        {
            if (!_isReady)
            {
                return;
            }

            _impactMuscleWeight = Mathf.MoveTowards(
                _impactMuscleWeight,
                1f,
                impactRecoverySpeed * Time.fixedDeltaTime);

            if (_generatedAnchor != null)
            {
                _generatedAnchor.MovePosition(_desiredShoulderPosition);
                _generatedAnchor.MoveRotation(_desiredShoulderFrame);
            }
            else if (bodyAnchor != null && _upperJoint != null)
            {
                _upperJoint.connectedAnchor = bodyAnchor.transform.InverseTransformPoint(_desiredShoulderPosition);
            }

            var motionBlend = controller.PunchSpeed01;
            var impactBlend = Mathf.SmoothStep(0f, 1f, controller.PunchExtension01)
                              * motionBlend;
            var phaseMultiplier = Mathf.Lerp(1f, movingMuscleMultiplier, motionBlend);
            phaseMultiplier = Mathf.Lerp(phaseMultiplier, impactMuscleMultiplier, impactBlend);
            var weight = EffectiveMuscleWeight * phaseMultiplier;
            DriveJoint(_upperJoint, _desiredUpperRotation, _upperBindRotation, weight);
            DriveJoint(_lowerJoint, _desiredLowerRotation, _lowerBindRotation, weight);
            DriveJoint(_handJoint, _desiredHandRotation, _handBindRotation, weight);
        }

        private void LateUpdate()
        {
            if (!_isReady)
            {
                if (controller != null
                    && controller.HasReferencePose
                    && poseSolver.HasSolvedPose)
                {
                    InitializeFromReferencePose();
                }
                return;
            }

            if (poseSolver.HasSolvedPose)
            {
                _desiredUpperRotation = poseSolver.DesiredUpperLocalRotation;
                _desiredLowerRotation = poseSolver.DesiredLowerLocalRotation;
                _desiredHandRotation = poseSolver.DesiredHandLocalRotation;
                _desiredShoulderPosition = poseSolver.DesiredShoulderPosition;
                _desiredShoulderFrame = poseSolver.DesiredShoulderFrame;
            }

            _upperTarget.SetPositionAndRotation(_upperBody.position, _upperBody.rotation);
            _lowerTarget.SetPositionAndRotation(_lowerBody.position, _lowerBody.rotation);
            _handTarget.SetPositionAndRotation(_handBody.position, _handBody.rotation);
        }

        private void InitializeFromReferencePose()
        {
            _upperBindRotation = _upperTarget.localRotation;
            _lowerBindRotation = _lowerTarget.localRotation;
            _handBindRotation = _handTarget.localRotation;
            _desiredUpperRotation = poseSolver.DesiredUpperLocalRotation;
            _desiredLowerRotation = poseSolver.DesiredLowerLocalRotation;
            _desiredHandRotation = poseSolver.DesiredHandLocalRotation;
            _desiredShoulderPosition = poseSolver.DesiredShoulderPosition;
            _desiredShoulderFrame = poseSolver.DesiredShoulderFrame;

            BuildPhysicalArm();
            _isReady = true;
        }

        private void OnDestroy()
        {
            if (_physicsRoot != null)
            {
                Destroy(_physicsRoot);
            }
        }

        public void ReceiveArmImpact(ArmImpactSample impact)
        {
            var severity = Mathf.InverseLerp(2f, 18f, impact.Impulse);
            _impactMuscleWeight = Mathf.Min(_impactMuscleWeight, Mathf.Lerp(0.8f, 0.25f, severity));
        }

        private void BuildPhysicalArm()
        {
            _physicsRoot = new GameObject($"{name}_PhysicalArm");
            _physicsRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var anchor = bodyAnchor;
            if (anchor == null)
            {
                var anchorObject = new GameObject("ShoulderAnchor");
                anchorObject.transform.SetParent(_physicsRoot.transform, false);
                anchorObject.transform.SetPositionAndRotation(_desiredShoulderPosition, _desiredShoulderFrame);
                _generatedAnchor = anchorObject.AddComponent<Rigidbody>();
                _generatedAnchor.isKinematic = true;
                _generatedAnchor.interpolation = RigidbodyInterpolation.Interpolate;
                anchor = _generatedAnchor;
            }

            _upperBody = CreateLimbBody("PhysicalUpperArm", _upperTarget, _lowerTarget, upperArmMass);
            _lowerBody = CreateLimbBody("PhysicalLowerArm", _lowerTarget, _handTarget, lowerArmMass);
            _handBody = CreateHandBody("PhysicalHand", _handTarget, handMass);

            _upperJoint = CreateJoint(_upperBody, anchor, _upperTarget.position, shoulderLimit);
            _lowerJoint = CreateJoint(_lowerBody, _upperBody, _lowerTarget.position, elbowLimit);
            _handJoint = CreateJoint(_handBody, _lowerBody, _handTarget.position, wristLimit);

            IgnoreInternalAndOwnerCollisions();
            AttachImpactReceiver(_upperBody);
            AttachImpactReceiver(_lowerBody);
            AttachImpactReceiver(_handBody);
            var impact = _handBody.gameObject.AddComponent<PhysicsArmImpact>();
            impact.Configure(controller, _handBody);
        }

        private void AttachImpactReceiver(Rigidbody body)
        {
            body.gameObject.AddComponent<ArmImpactReceiverRelay>().Configure(this);
        }

        private Rigidbody CreateLimbBody(string objectName, Transform bone, Transform child, float mass)
        {
            var bodyObject = new GameObject(objectName);
            bodyObject.transform.SetParent(_physicsRoot.transform, true);
            bodyObject.transform.SetPositionAndRotation(bone.position, bone.rotation);

            var body = ConfigureBody(bodyObject.AddComponent<Rigidbody>(), mass);
            var localEnd = bodyObject.transform.InverseTransformPoint(child.position);
            var collider = bodyObject.AddComponent<CapsuleCollider>();
            collider.direction = DominantAxis(localEnd);
            collider.center = localEnd * 0.5f;
            collider.radius = Mathf.Min(limbRadius, localEnd.magnitude * 0.45f);
            collider.height = Mathf.Max(collider.radius * 2f, localEnd.magnitude);
            return body;
        }

        private Rigidbody CreateHandBody(string objectName, Transform bone, float mass)
        {
            var bodyObject = new GameObject(objectName);
            bodyObject.transform.SetParent(_physicsRoot.transform, true);
            bodyObject.transform.SetPositionAndRotation(bone.position, bone.rotation);
            var body = ConfigureBody(bodyObject.AddComponent<Rigidbody>(), mass);
            bodyObject.AddComponent<SphereCollider>().radius = handRadius;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return body;
        }

        private static Rigidbody ConfigureBody(Rigidbody body, float mass)
        {
            body.mass = mass;
            body.drag = 0.05f;
            body.angularDrag = 0.15f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            return body;
        }

        private static ConfigurableJoint CreateJoint(
            Rigidbody body,
            Rigidbody connectedBody,
            Vector3 worldAnchor,
            float angularLimit)
        {
            var joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = connectedBody;
            joint.configuredInWorldSpace = false;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = body.transform.InverseTransformPoint(worldAnchor);
            joint.connectedAnchor = connectedBody.transform.InverseTransformPoint(worldAnchor);
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -angularLimit };
            joint.highAngularXLimit = new SoftJointLimit { limit = angularLimit };
            joint.angularYLimit = new SoftJointLimit { limit = angularLimit };
            joint.angularZLimit = new SoftJointLimit { limit = angularLimit };
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.03f;
            joint.projectionAngle = 8f;
            joint.enablePreprocessing = true;
            return joint;
        }

        private void DriveJoint(
            ConfigurableJoint joint,
            Quaternion targetLocalRotation,
            Quaternion bindLocalRotation,
            float weight)
        {
            if (joint == null)
            {
                return;
            }

            var drive = joint.slerpDrive;
            drive.positionSpring = spring * weight;
            drive.positionDamper = damper * Mathf.Sqrt(weight);
            drive.maximumForce = maximumForce * weight;
            joint.slerpDrive = drive;

            // ConfigurableJoint 的局部坐标定义不是 LookRotation(axis, secondaryAxis)。
            // axis 是 joint-space 的 X/right；secondaryAxis 只用于确定 XY 平面。
            // 旧写法对默认 X/Y 轴额外引入了 90 度旋转，导致手臂向上、向后翻转。
            var right = joint.axis.normalized;
            var forward = Vector3.Cross(right, joint.secondaryAxis).normalized;
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.forward;
            }
            var up = Vector3.Cross(forward, right).normalized;
            var jointSpace = Quaternion.LookRotation(forward, up);
            joint.targetRotation = Quaternion.Inverse(jointSpace)
                                   * (Quaternion.Inverse(targetLocalRotation) * bindLocalRotation)
                                   * jointSpace;
        }

        private void IgnoreInternalAndOwnerCollisions()
        {
            var physical = _physicsRoot.GetComponentsInChildren<Collider>();
            for (var i = 0; i < physical.Length; i++)
            {
                for (var j = i + 1; j < physical.Length; j++)
                {
                    Physics.IgnoreCollision(physical[i], physical[j], true);
                }
            }

            var ownerColliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < physical.Length; i++)
            {
                for (var j = 0; j < ownerColliders.Length; j++)
                {
                    if (ownerColliders[j] != physical[i])
                    {
                        Physics.IgnoreCollision(physical[i], ownerColliders[j], true);
                    }
                }
            }
        }

        private static int DominantAxis(Vector3 value)
        {
            var absolute = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            {
                return 0;
            }
            return absolute.y >= absolute.z ? 1 : 2;
        }
    }
}
