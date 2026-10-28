using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    /// <summary>
    /// 华夏: 删除角色
    /// MSSQL 对应: DELETE FROM tActorInfo WHERE iActorID=@actorId AND iUserID=@accountId
    /// </summary>
    [MessageSessionHandler(SceneType.Realm)]
    public class HX_C2R_DeleteActorHandler : MessageSessionHandler<HX_C2R_DeleteActor, HX_R2C_DeleteActor>
    {
        protected override async ETTask Run(Session session, HX_C2R_DeleteActor request, HX_R2C_DeleteActor response)
        {
            Scene root = session.Root();
            DBManagerComponent dbManager = root.GetComponent<DBManagerComponent>();
            if (dbManager == null)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "DBManager 未初始化";
                return;
            }

            DBComponent db = dbManager.GetZoneDB(session.Fiber().Zone);

            // 1. 验证角色归属
            List<Actor> actors = await db.Query<Actor>(a => a.Id == request.ActorId && a.UserId == request.AccountId);
            Actor actor = actors.FirstOrDefault();
            if (actor == null)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "角色不存在或不属于该账号";
                return;
            }

            // 2. 删除
            long deleted = await db.Remove<Actor>(request.ActorId);
            actor.Dispose();

            if (deleted > 0)
            {
                Log.Info($"[华夏] 删除角色成功: ActorId={request.ActorId}, AccountId={request.AccountId}");
            }
            else
            {
                Log.Warning($"[华夏] 删除角色返回 0 条: ActorId={request.ActorId}");
            }
        }
    }
}
