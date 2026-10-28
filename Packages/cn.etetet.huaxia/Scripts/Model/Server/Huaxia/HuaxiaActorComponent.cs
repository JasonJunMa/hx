namespace ET
{
    /// <summary>
    /// 华夏: Unit ↔ Actor 桥接组件
    /// Unit (Map Scene 内存) ↔ Actor (MongoDB 持久化) 通过这个组件关联
    /// </summary>
    [ComponentOf(typeof(Unit))]
    public class HuaxiaActorComponent : Entity, IAwake
    {
        /// <summary>
        /// MongoDB Actor._id (华夏 Actor 的持久化主键)
        /// </summary>
        public long ActorDbId { get; set; }

        /// <summary>
        /// MongoDB Account._id (方便 Logout/持久化回写)
        /// </summary>
        public long AccountId { get; set; }

        /// <summary>
        /// 脏标记: Unit 状态变化后标记，定时/Logout 时批量回写 MongoDB
        /// </summary>
        public bool IsDirty { get; set; }
    }
}
