using System.Collections.Generic;

namespace ET.Server
{
    /// <summary>
    /// 华夏: Gate 层选角色 Handler
    /// 客户端 Gate 登录成功 → 发 HX_C2G_SelectActor(ActorId) → 校验归属 → 返回出生点
    /// </summary>
    [MessageSessionHandler(SceneType.Gate)]
    public class HX_C2G_SelectActorHandler : MessageSessionHandler<HX_C2G_SelectActor, HX_G2C_SelectActor>
    {
        protected override async ETTask Run(Session session, HX_C2G_SelectActor request, HX_G2C_SelectActor response)
        {
            Scene root = session.Root();
            Player player = session.GetComponent<SessionPlayerComponent>().Player;

            if (player == null)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "未登录 Gate!";
                return;
            }

            DBManagerComponent dbManager = root.GetComponent<DBManagerComponent>();
            if (dbManager == null)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "DBManagerComponent 未初始化!";
                return;
            }

            DBComponent db = dbManager.GetZoneDB(session.Fiber().Zone);

            // 1. 查 Actor + 校验归属
            List<Actor> actors = await db.Query<Actor>(a => a.Id == request.ActorId && a.UserId == player.AccountId);
            if (actors.Count == 0)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = $"角色不存在或不属于当前账号! ActorId={request.ActorId}, AccountId={player.AccountId}";
                Log.Warning($"[华夏] ❌ SelectActor 校验失败: ActorId={request.ActorId}, AccountId={player.AccountId}");
                return;
            }

            Actor actor = actors[0];

            // 2. 写入 Player.ActorId (后续 EnterMap 用)
            player.ActorId = actor.Id;
            response.MapId = actor.MapId;
            response.LocationX = actor.LocationX;
            response.LocationY = actor.LocationY;
            response.Message = "OK";

            Log.Info($"[华夏] ✅ SelectActor 成功: PlayerId={player.Id}, AccountId={player.AccountId}, ActorId={actor.Id}, Name={actor.ActorName}, Map={actor.MapId}({actor.LocationX},{actor.LocationY})");

            actor.Dispose();
            await ETTask.CompletedTask;
        }
    }
}
