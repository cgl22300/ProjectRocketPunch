using UnityEngine;

namespace Demo.MyActiveRagdoll.script.Player
{
    public readonly struct ArmImpactSample
    {
        public ArmImpactSample(
            GameObject source,
            GameObject target,
            Vector3 point,
            Vector3 direction,
            float closingSpeed,
            float impulse,
            float kineticEnergy,
            float damage)
        {
            Source = source;
            Target = target;
            Point = point;
            Direction = direction;
            ClosingSpeed = closingSpeed;
            Impulse = impulse;
            KineticEnergy = kineticEnergy;
            Damage = damage;
        }

        public GameObject Source { get; }
        public GameObject Target { get; }
        public Vector3 Point { get; }
        public Vector3 Direction { get; }
        public float ClosingSpeed { get; }
        public float Impulse { get; }
        public float KineticEnergy { get; }
        public float Damage { get; }
    }

    public interface IArmImpactReceiver
    {
        void ReceiveArmImpact(ArmImpactSample impact);
    }

    /// <summary>
    /// 把运行时物理肢体收到的攻击转发给所属角色。
    /// </summary>
    internal sealed class ArmImpactReceiverRelay : MonoBehaviour, IArmImpactReceiver
    {
        private IArmImpactReceiver _receiver;

        public void Configure(IArmImpactReceiver receiver)
        {
            _receiver = receiver;
        }

        public void ReceiveArmImpact(ArmImpactSample impact)
        {
            _receiver?.ReceiveArmImpact(impact);
        }
    }

    /// <summary>
    /// 根据手掌接触点的相对速度、有效质量与碰撞冲量生成伤害样本。
    /// </summary>
    public sealed class PhysicsArmImpact : MonoBehaviour
    {
        [Min(0f)] [SerializeField] private float minimumClosingSpeed = 1.5f;
        [Min(0f)] [SerializeField] private float minimumEnergy = 1f;
        [Min(0f)] [SerializeField] private float energyToDamage = 0.35f;
        [Min(0f)] [SerializeField] private float impulseToDamage = 0.5f;
        [Min(0f)] [SerializeField] private float repeatHitDelay = 0.12f;
        [SerializeField] private bool logImpacts = true;

        private SingleArmController _controller;
        private Rigidbody _hand;
        private float _lastHitTime = float.NegativeInfinity;

        public void Configure(SingleArmController controller, Rigidbody hand)
        {
            _controller = controller;
            _hand = hand;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_hand == null
                || _controller == null
                || !_controller.IsArmControlActive
                || Time.time - _lastHitTime < repeatHitDelay
                || collision.contactCount == 0)
            {
                return;
            }

            var contact = collision.GetContact(0);
            var otherBody = collision.rigidbody;
            var handVelocity = _hand.GetPointVelocity(contact.point);
            var otherVelocity = otherBody != null
                ? otherBody.GetPointVelocity(contact.point)
                : Vector3.zero;
            var relativeVelocity = handVelocity - otherVelocity;
            var closingSpeed = Mathf.Max(0f, Vector3.Dot(relativeVelocity, -contact.normal));
            if (closingSpeed < minimumClosingSpeed)
            {
                return;
            }

            var otherMass = otherBody != null ? otherBody.mass : _hand.mass;
            var effectiveMass = (_hand.mass * otherMass) / Mathf.Max(0.001f, _hand.mass + otherMass);
            var energy = 0.5f * effectiveMass * closingSpeed * closingSpeed;
            if (energy < minimumEnergy)
            {
                return;
            }

            var impulse = collision.impulse.magnitude;
            var damage = Mathf.Max(0f, energy - minimumEnergy) * energyToDamage
                         + impulse * impulseToDamage;
            var direction = relativeVelocity.sqrMagnitude > 0.0001f
                ? relativeVelocity.normalized
                : -contact.normal;
            var sample = new ArmImpactSample(
                _controller.gameObject,
                collision.gameObject,
                contact.point,
                direction,
                closingSpeed,
                impulse,
                energy,
                damage);

            _lastHitTime = Time.time;
            NotifyTarget(collision.transform, sample);

            if (logImpacts)
            {
                Debug.Log(
                    $"Arm impact: speed={closingSpeed:F2} m/s, impulse={impulse:F2} Ns, " +
                    $"energy={energy:F2} J, damage={damage:F2}",
                    this);
            }
        }

        private static void NotifyTarget(Transform target, ArmImpactSample sample)
        {
            var behaviours = target.GetComponentsInParent<MonoBehaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IArmImpactReceiver receiver)
                {
                    receiver.ReceiveArmImpact(sample);
                }
            }
        }
    }
}
