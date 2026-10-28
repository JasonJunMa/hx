using MemoryPack;
using System.Collections.Generic;

namespace ET
{
    /// <summary>
    /// 华夏: Gate 层选角色请求 (LoginGate 后调用)
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_C2G_SelectActor)]
    [ResponseType(nameof(HX_G2C_SelectActor))]
    public partial class HX_C2G_SelectActor : MessageObject, ISessionRequest
    {
        public static HX_C2G_SelectActor Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_C2G_SelectActor>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long ActorId { get; set; }

        public override void Dispose()
        {
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 华夏: Gate 层选角色响应
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_G2C_SelectActor)]
    public partial class HX_G2C_SelectActor : MessageObject, ISessionResponse
    {
        public static HX_G2C_SelectActor Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_G2C_SelectActor>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        /// <summary>
        /// 选中角色的初始出生地图 (默认 1000)
        /// </summary>
        [MemoryPackOrder(3)]
        public int MapId { get; set; }

        [MemoryPackOrder(4)]
        public int LocationX { get; set; }

        [MemoryPackOrder(5)]
        public int LocationY { get; set; }

        public override void Dispose()
        {
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 华夏 Opcode 区段: 11500-11520
    /// </summary>
    public static partial class Opcode
    {
        public const ushort HX_C2G_SelectActor = 11507;
        public const ushort HX_G2C_SelectActor = 11508;
    }
}
