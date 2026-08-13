using UnityEngine;

namespace Demo.MyActiveRagdoll.script
{
    /// <summary>
    /// 人形实体的基类，包括基本属性和实现ActiveRagdoll的系列组件
    /// </summary>
    public abstract class EntityBase : MonoBehaviour
    {
    }

    /// <summary>
    /// 引入泛型参数
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class Entity<T> : EntityBase where T : Entity<T>
    {
    }
}