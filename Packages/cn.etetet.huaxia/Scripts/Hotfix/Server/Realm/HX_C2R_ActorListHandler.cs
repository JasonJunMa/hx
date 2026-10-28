using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    /// <summary>
    /// 华夏: 查询角色列表
    /// MSSQL 对应: SELECT * FROM tActorInfo WHERE iUserID=@accountId
    /// </summary>
    [MessageSessionHandler(SceneType.Realm)]
    public class HX_C2R_ActorListHandler : MessageSessionHandler<HX_C2R_ActorList, HX_R2C_ActorList>
    {
        protected override async ETTask Run(Session session, HX_C2R_ActorList request, HX_R2C_ActorList response)
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

            // 1. 验证账号存在
            List<Account> accounts = await db.Query<Account>(a => a.Id == request.AccountId);
            Account account = accounts.FirstOrDefault();
            if (account == null)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "账号不存在";
                return;
            }

            // 2. 查询角色列表
            List<Actor> actors = await db.Query<Actor>(a => a.UserId == request.AccountId);
            Log.Info($"[华夏] 查询角色列表: AccountId={request.AccountId}, 找到 {actors.Count} 个角色");

            foreach (Actor actor in actors)
            {
                HX_ActorInfo info = HX_ActorInfo.Create();
                info.ActorId = actor.Id;
                info.ActorName = actor.ActorName;
                info.Class = actor.Class;
                info.Level = actor.Level;
                info.Sex = actor.Sex;
                info.MapId = actor.MapId;
                info.LocationX = actor.LocationX;
                info.LocationY = actor.LocationY;
                response.Actors.Add(info);
                actor.Dispose();
            }

            account.Dispose();
            await ETTask.CompletedTask;
        }
    }
}
