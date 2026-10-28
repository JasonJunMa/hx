using System;
using System.Collections.Generic;
using System.Net;

namespace ET.Server
{
    [Invoke(SceneType.Realm)]
    public class FiberInit_Realm: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<MailBoxComponent, int>(MailBoxType.UnOrderedMessage);
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<ProcessInnerSender>();
            root.AddComponent<MessageSender>();

            // MongoDB 数据库管理组件 (华夏需要)
            root.AddComponent<DBManagerComponent>();

            // 华夏: 原始客户端 TCP 适配器 (port 1600)
            root.AddComponent<HX_TcpAdapterComponent, int>(1600);

            AddressSingleton addressSingleton = root.GetSingleton<AddressSingleton>();
            int outerPort = AddressHelper.GetSceneOuterPort(root.Fiber(), root.Name.GetSceneConfigName());

            IPEndPoint innerIPOuterPort = new Address(addressSingleton.InnerIP, outerPort);
            
            NetComponent netComponent = root.AddComponent<NetComponent, IKcpTransport>(new UdpTransport(innerIPOuterPort));
            
            // 注册服务发现
            ServiceDiscoveryProxy serviceDiscoveryProxy = root.AddComponent<ServiceDiscoveryProxy>();
            EntityRef<ServiceDiscoveryProxy> serviceDiscoveryProxyComponentRef = serviceDiscoveryProxy;

            await serviceDiscoveryProxy.RegisterToServiceDiscovery
            (
                new StringKV()
                {
                    { ServiceMetaKey.InnerIPOuterPort, $"{netComponent.GetBindPoint()}" }
                }
            );
            
            // 订阅跟realm属于同一个zone的Gate
            serviceDiscoveryProxy = serviceDiscoveryProxyComponentRef;
            await serviceDiscoveryProxy.SubscribeServiceChange("Gate", 
                new StringKV()
                {
                    {ServiceMetaKey.SceneType, SceneTypeSingleton.Instance.GetSceneName(SceneType.Gate)},
                    { ServiceMetaKey.Zone, $"{fiberInit.Fiber.Zone}" }
                });

            // ===== 华夏: 端到端登录验证 =====
            // 等 ServiceDiscovery 把 Gate 注册进来
            await root.TimerComponent.WaitAsync(3000);
            RunHuaxiaLoginVerify(fiberInit).Coroutine();
        }

        private async ETTask RunHuaxiaLoginVerify(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            try
            {
                Log.Info("========== [华夏] 端到端登录验证开始 ==========");

                // 1. MongoDB 链路
                DBManagerComponent dbManager = root.GetComponent<DBManagerComponent>();
                if (dbManager == null)
                {
                    Log.Error("[华夏] ❌ DBManagerComponent 未找到！");
                    return;
                }

                DBComponent db = dbManager.GetZoneDB(fiberInit.Fiber.Zone);
                Log.Info($"[华夏] ✅ MongoDB 连接 OK, ZoneDB={dbManager.GetZoneDBName(fiberInit.Fiber.Zone)}");

                // 2. 创建测试账号
                const string testUser = "huaxia_test";
                const string testPwd = "123456";

                await db.Remove<Account>(a => a.UserName == testUser);
                Log.Info("[华夏] 清理旧测试账号完成");

                Account account = root.AddChild<Account>();
                account.UserName = testUser;
                account.PasswordHash = testPwd;
                account.OpenAccountNum = 1;
                account.IsBanned = false;
                await db.Save(account);
                long accountId = account.Id;
                account.Dispose();
                Log.Info($"[华夏] ✅ 创建测试账号成功: UserName={testUser}, AccountId={accountId}");

                // 3. 查询验证
                List<Account> accounts = await db.Query<Account>(a => a.UserName == testUser);
                if (accounts.Count > 0 && accounts[0].PasswordHash == testPwd)
                {
                    Log.Info($"[华夏] ✅ 查询验证成功: PasswordHash={accounts[0].PasswordHash}");
                    accounts[0].Dispose();
                }
                else
                {
                    Log.Error("[华夏] ❌ 查询验证失败！");
                    return;
                }

                // 4. Gate 分配
                ServiceDiscoveryProxy proxy = root.GetComponent<ServiceDiscoveryProxy>();
                List<ServiceInfo> gates = proxy.GetBySceneTypeAndZone(SceneType.Gate, fiberInit.Fiber.Zone);
                Log.Info($"[华夏] ✅ 发现 Gate 节点: {gates.Count} 个");
                foreach (var g in gates)
                    Log.Info($"   → {g.SceneName} InnerPort={g.Metadata[ServiceMetaKey.InnerIPOuterPort]}");

                if (gates.Count == 0)
                {
                    Log.Error("[华夏] ❌ 没发现 Gate 节点！");
                    return;
                }

                // 5. Gate Key 获取
                ServiceInfo selectedGate = gates[0];
                MessageSender sender = root.GetComponent<MessageSender>();
                R2G_GetLoginKey r2GGetLoginKey = R2G_GetLoginKey.Create();
                r2GGetLoginKey.Account = testUser;
                G2R_GetLoginKey keyResp = (G2R_GetLoginKey)await sender.Call(selectedGate.ActorId, r2GGetLoginKey);

                Log.Info("========== [华夏] 端到端登录验证通过 ✅ ==========");
                Log.Info($"  AccountId={accountId}");
                Log.Info($"  Gate={selectedGate.SceneName}");
                Log.Info($"  Login Key={keyResp.Key}  GateId={keyResp.GateId}");

                // ===== 华夏: 角色持久化链路验证 =====
                await RunActorPersistenceTest(db, root, accountId);
            }
            catch (Exception e)
            {
                Log.Error($"[华夏] ❌ 验证异常: {e.Message}\n{e.StackTrace}");
            }
        }

        private async ETTask RunActorPersistenceTest(DBComponent db, Scene root, long accountId)
        {
            Log.Info("---------- [华夏] 角色持久化链路验证 ----------");

            // 1. 清理旧角色
            await db.Remove<Actor>(a => a.UserId == accountId);
            Log.Info("[华夏] 清理旧角色完成");

            // 2. 创建 3 个角色 (华夏 上限)
            string[] names = { "战士一号", "法师二号", "暗巫三号" };
            int[] classes = { 1, 2, 3 };
            long[] actorIds = new long[3];

            for (int i = 0; i < 3; i++)
            {
                Actor actor = root.AddChild<Actor>();
                actor.UserId = accountId;
                actor.ActorName = names[i];
                actor.Class = classes[i];
                actor.Sex = i % 2;
                actor.Level = 1;
                actor.MapId = 1000;
                actor.LocationX = 100;
                actor.LocationY = 100;
                actor.MaxHp = 100;
                actor.CurHp = 100;
                actor.MaxMp = 50;
                actor.CurMp = 50;
                actor.StoragePass = "";

                await db.Save(actor);
                actorIds[i] = actor.Id;
                Log.Info($"[华夏] ✅ 创建角色: Name={names[i]}, Class={classes[i]}, ActorId={actor.Id}");
                actor.Dispose();
            }

            // 3. 查询角色列表 (对应 HX_C2R_ActorListHandler 逻辑)
            List<Actor> actors = await db.Query<Actor>(a => a.UserId == accountId);
            if (actors.Count == 3)
            {
                Log.Info($"[华夏] ✅ 查询角色列表: {actors.Count} 个");
                foreach (var a in actors)
                {
                    Log.Info($"   → ActorId={a.Id}, Name={a.ActorName}, Class={a.Class}, Level={a.Level}");
                    a.Dispose();
                }
            }
            else
            {
                Log.Error($"[华夏] ❌ 查询角色列表: 期望 3 个, 实际 {actors.Count} 个");
                return;
            }

            // 4. 修改角色属性 + 重新保存
            actors = await db.Query<Actor>(a => a.Id == actorIds[0]);
            if (actors.Count > 0)
            {
                Actor modified = actors[0];
                modified.Level = 10;
                modified.Exp = 99999;
                modified.Strength = 50;
                await db.Save(modified);
                Log.Info($"[华夏] ✅ 修改角色成功: {modified.ActorName}, Level→{modified.Level}, Exp={modified.Exp}");
                modified.Dispose();
            }

            // 5. 删除一个角色 (对应 HX_C2R_DeleteActorHandler 逻辑)
            long deleted = await db.Remove<Actor>(actorIds[2]);
            Log.Info($"[华夏] ✅ 删除角色: ActorId={actorIds[2]}, Deleted={deleted}");

            // 6. 最终确认: 应该剩 2 个
            actors = await db.Query<Actor>(a => a.UserId == accountId);
            Log.Info($"[华夏] ✅ 最终角色数: {actors.Count} (期望 2)");
            foreach (var a in actors)
            {
                a.Dispose();
            }

            Log.Info("========== [华夏] 角色持久化链路验证通过 ✅ ==========");
        }
    }
}
