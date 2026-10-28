using MemoryPack;
using System.Collections.Generic;

namespace ET
{
    /// <summary>
    /// 华夏 角色信息（内嵌消息，用于列表返回）
    /// 对应 MSSQL tActorInfo 精简字段
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_ActorInfo)]
    public partial class HX_ActorInfo : MessageObject
    {
        public static HX_ActorInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_ActorInfo>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public long ActorId { get; set; }

        [MemoryPackOrder(1)]
        public string ActorName { get; set; }

        [MemoryPackOrder(2)]
        public int Class { get; set; }       // 1=战士 2=法师 3=暗巫 4=幻师

        [MemoryPackOrder(3)]
        public int Level { get; set; }

        [MemoryPackOrder(4)]
        public int Sex { get; set; }         // 0=女 1=男

        [MemoryPackOrder(5)]
        public int MapId { get; set; }

        [MemoryPackOrder(6)]
        public int LocationX { get; set; }

        [MemoryPackOrder(7)]
        public int LocationY { get; set; }

        public override void Dispose()
        {
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 请求角色列表（登录成功后调用）
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_C2R_ActorList)]
    [ResponseType(nameof(HX_R2C_ActorList))]
    public partial class HX_C2R_ActorList : MessageObject, ISessionRequest
    {
        public static HX_C2R_ActorList Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_C2R_ActorList>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        /// <summary>
        /// 账号 Id (由 C2R_Login 返回后客户端暂存)
        /// </summary>
        [MemoryPackOrder(1)]
        public long AccountId { get; set; }

        public override void Dispose()
        {
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 返回角色列表
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_R2C_ActorList)]
    public partial class HX_R2C_ActorList : MessageObject, ISessionResponse
    {
        public static HX_R2C_ActorList Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_R2C_ActorList>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        /// <summary>
        /// 角色列表（华夏 最多 3 个角色）
        /// </summary>
        [MemoryPackOrder(3)]
        public List<HX_ActorInfo> Actors { get; set; } = new();

        public override void Dispose()
        {
            foreach (var actor in Actors)
            {
                actor?.Dispose();
            }
            Actors?.Clear();
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 创建角色请求
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_C2R_CreateActor)]
    [ResponseType(nameof(HX_R2C_CreateActor))]
    public partial class HX_C2R_CreateActor : MessageObject, ISessionRequest
    {
        public static HX_C2R_CreateActor Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_C2R_CreateActor>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long AccountId { get; set; }

        [MemoryPackOrder(2)]
        public string ActorName { get; set; }

        [MemoryPackOrder(3)]
        public int Class { get; set; }

        [MemoryPackOrder(4)]
        public int Sex { get; set; }

        public override void Dispose()
        {
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 创建角色响应
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_R2C_CreateActor)]
    public partial class HX_R2C_CreateActor : MessageObject, ISessionResponse
    {
        public static HX_R2C_CreateActor Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_R2C_CreateActor>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public HX_ActorInfo Actor { get; set; }

        public override void Dispose()
        {
            Actor?.Dispose();
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 删除角色请求
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_C2R_DeleteActor)]
    [ResponseType(nameof(HX_R2C_DeleteActor))]
    public partial class HX_C2R_DeleteActor : MessageObject, ISessionRequest
    {
        public static HX_C2R_DeleteActor Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_C2R_DeleteActor>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long AccountId { get; set; }

        [MemoryPackOrder(2)]
        public long ActorId { get; set; }

        public override void Dispose()
        {
            ObjectPool.Recycle(this);
        }
    }

    /// <summary>
    /// 删除角色响应
    /// </summary>
    [MemoryPackable]
    [Message(Opcode.HX_R2C_DeleteActor)]
    public partial class HX_R2C_DeleteActor : MessageObject, ISessionResponse
    {
        public static HX_R2C_DeleteActor Create(bool isFromPool = false)
        {
            return ObjectPool.Fetch<HX_R2C_DeleteActor>(isFromPool);
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

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
        public const ushort HX_ActorInfo = 11500;
        public const ushort HX_C2R_ActorList = 11501;
        public const ushort HX_R2C_ActorList = 11502;
        public const ushort HX_C2R_CreateActor = 11503;
        public const ushort HX_R2C_CreateActor = 11504;
        public const ushort HX_C2R_DeleteActor = 11505;
        public const ushort HX_R2C_DeleteActor = 11506;
    }
}
