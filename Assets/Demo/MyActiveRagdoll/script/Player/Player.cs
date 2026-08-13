using UnityEngine;

namespace Demo.MyActiveRagdoll.script.Player
{
    /// <summary>
    /// 接入玩家独有属性。
    /// </summary>
    [RequireComponent(typeof(SingleArmController))]
    public class Player : Entity<Player>
    {
    }
}
