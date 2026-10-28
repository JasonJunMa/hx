using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    /// <summary>
    /// 华夏: 创建角色
    /// MSSQL 对应: INSERT INTO tActorInfo (iActorID, iUserID, szActorName, iClass, ...)
    /// 规则: 每账号最多 3 角色, 名字不能重复
    /// </summary>
    [MessageSessionHandler(SceneType.Realm)]
    public class HX_C2R_CreateActorHandler : MessageSessionHandler<HX_C2R_CreateActor, HX_R2C_CreateActor>
    {
        protected override async ETTask Run(Session session, HX_C2R_CreateActor request, HX_R2C_CreateActor response)
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

            // 1. 验证账号
            List<Account> accounts = await db.Query<Account>(a => a.Id == request.AccountId);
            Account account = accounts.FirstOrDefault();
            if (account == null)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "账号不存在";
                return;
            }

            // 2. 检查角色数量限制 (华夏 最多 3 个)
            List<Actor> existing = await db.Query<Actor>(a => a.UserId == request.AccountId);
            if (existing.Count >= 3)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "角色数量已达上限(3个)";
                account.Dispose();
                return;
            }

            // 3. 检查名字重复
            List<Actor> nameDup = await db.Query<Actor>(a => a.ActorName == request.ActorName);
            if (nameDup.Count > 0)
            {
                response.Error = ErrorCode.ERR_ConnectGateKeyError;
                response.Message = "角色名已被使用";
                account.Dispose();
                return;
            }

            // 4. 创建角色 (AddChild 自动分配 Id)
            Actor actor = root.AddChild<Actor>();
            actor.UserId = request.AccountId;
            actor.ActorName = request.ActorName;
            actor.Class = request.Class;
            actor.Sex = request.Sex;
            actor.Level = 1;
            actor.MapId = 1000;      // 默认出生地图赤水城 (需要对照原表)
            actor.LocationX = 100;
            actor.LocationY = 100;
            actor.MaxHp = 100;      // 初始值, 后续从配置表读
            actor.CurHp = 100;
            actor.MaxMp = 50;
            actor.CurMp = 50;
            actor.StoragePass = "";

            await db.Save(actor);
            Log.Info($"[华夏] 创建角色成功: AccountId={request.AccountId}, ActorId={actor.Id}, Name={request.ActorName}, Class={request.Class}");

            // 5. 返回
            HX_ActorInfo info = HX_ActorInfo.Create();
            info.ActorId = actor.Id;
            info.ActorName = actor.ActorName;
            info.Class = actor.Class;
            info.Level = actor.Level;
            info.Sex = actor.Sex;
            info.MapId = actor.MapId;
            info.LocationX = actor.LocationX;
            info.LocationY = actor.LocationY;
            response.Actor = info;

            actor.Dispose();
            account.Dispose();
        }
    }
}
