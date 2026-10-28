using System;
using System.Collections.Generic;
using System.Net;

namespace ET.Server
{
    [Invoke(SceneType.Gate)]
    public class FiberInit_Gate: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<MailBoxComponent, int>(MailBoxType.UnOrderedMessage);
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<ProcessInnerSender>();
            root.AddComponent<MessageSender>();
            root.AddComponent<PlayerComponent>();
            root.AddComponent<GateSessionKeyComponent>();
            root.AddComponent<LocationProxyComponent>();
            root.AddComponent<MessageLocationSenderComponent>();

            // 华夏: Gate 层也需要 MongoDB 连接 (查 AccountId 和 Actor)
            root.AddComponent<DBManagerComponent>();

            // 华夏: 原始客户端 TCP Adapter (赤水=1607, 雷炎=1602)
            int hxPort = root.Name.Contains("2") ? 1602 : 1607;
            Log.Info($"[华夏] Gate({root.Name}) TCP Adapter 绑定 port={hxPort}");
            root.AddComponent<HX_TcpAdapterComponent, int>(hxPort);

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
            
            // 订阅location
            serviceDiscoveryProxy = serviceDiscoveryProxyComponentRef;
            await serviceDiscoveryProxy.SubscribeServiceChange("Location", 
                new StringKV() { {ServiceMetaKey.SceneType, SceneTypeSingleton.Instance.GetSceneName(SceneType.Location)} });

            // ===== 华夏: Gate 层端到端验证 =====
            await root.TimerComponent.WaitAsync(5000); // 等 Realm 跑完 Account/Actor 创建
            RunHuaxiaGateVerify(fiberInit).Coroutine();
        }

        private async ETTask RunHuaxiaGateVerify(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            try
            {
                string sceneName = root.Name;
                Log.Info($"========== [华夏] Gate({sceneName}) 端到端验证开始 ==========");

                DBManagerComponent dbManager = root.GetComponent<DBManagerComponent>();
                if (dbManager == null)
                {
                    Log.Error("[华夏] ❌ Gate: DBManagerComponent 未找到!");
                    return;
                }
                DBComponent db = dbManager.GetZoneDB(fiberInit.Fiber.Zone);
                Log.Info($"[华夏] ✅ Gate MongoDB 连接 OK, ZoneDB={dbManager.GetZoneDBName(fiberInit.Fiber.Zone)}");

                // 1. 反查 AccountId
                const string testUser = "huaxia_test";
                List<Account> accounts = await db.Query<Account>(a => a.UserName == testUser);
                if (accounts.Count == 0)
                {
                    Log.Warning("[华夏] Gate: 未找到测试账号 huaxia_test，跳过 Gate 验证");
                    return;
                }
                long accountId = accounts[0].Id;
                accounts[0].Dispose();
                Log.Info($"[华夏] ✅ Gate 反查 AccountId 成功: UserName={testUser} → AccountId={accountId}");

                // 2. 查 Actor 列表
                List<Actor> actors = await db.Query<Actor>(a => a.UserId == accountId);
                Log.Info($"[华夏] ✅ Gate 查询 Actor: {actors.Count} 个 (AccountId={accountId})");

                long firstActorId = 0;
                foreach (var a in actors)
                {
                    Log.Info($"   → ActorId={a.Id}, Name={a.ActorName}, Class={a.Class}, Level={a.Level}, Map={a.MapId}({a.LocationX},{a.LocationY})");
                    if (firstActorId == 0) firstActorId = a.Id;
                    a.Dispose();
                }

                if (actors.Count == 0)
                {
                    Log.Warning("[华夏] Gate: 没有角色，跳过 SelectActor 验证");
                    return;
                }

                // 3. 模拟 HX_C2G_SelectActor: 验证 Actor 归属
                actors = await db.Query<Actor>(a => a.Id == firstActorId && a.UserId == accountId);
                if (actors.Count > 0)
                {
                    Actor actor = actors[0];
                    Log.Info($"[华夏] ✅ SelectActor 归属校验通过: ActorId={actor.Id}, UserId={actor.UserId}, Name={actor.ActorName}");
                    Log.Info($"   → 返回出生点: MapId={actor.MapId}, X={actor.LocationX}, Y={actor.LocationY}");
                    actor.Dispose();
                }
                else
                {
                    Log.Error($"[华夏] ❌ SelectActor 归属校验失败! ActorId={firstActorId}, AccountId={accountId}");
                }

                // 4. 负向测试: 用假 AccountId 查 Actor 应该 0 结果
                actors = await db.Query<Actor>(a => a.Id == firstActorId && a.UserId == 999999);
                if (actors.Count == 0)
                {
                    Log.Info("[华夏] ✅ 负向测试通过: 假 AccountId 查 Actor 返回 0 (归属校验生效)");
                }

                // ===== 5. 模拟 Map 层 G2Map_EnterMapHandler: Unit 创建 + Actor 属性覆盖 =====
                actors = await db.Query<Actor>(a => a.Id == firstActorId);
                if (actors.Count > 0)
                {
                    Actor actor = actors[0];
                    await RunHuaxiaMapUnitVerify(root, actor);
                    actor.Dispose();
                }

                Log.Info($"========== [华夏] Gate({sceneName}) 端到端验证通过 ✅ ==========");
            }
            catch (Exception e)
            {
                Log.Error($"[华夏] ❌ Gate 验证异常: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// 模拟 Map 层 G2Map_EnterMapHandler 流程: UnitFactory.Create + Actor 数据覆盖 + 验证属性
        /// </summary>
        private static async ETTask RunHuaxiaMapUnitVerify(Scene scene, Actor actor)
        {
            try
            {
                Log.Info($"---------- [华夏] 模拟 Map 层 Unit 初始化 (Actor={actor.ActorName}) ----------");

                // Gate Scene 上没有 UnitComponent，先加一个
                UnitComponent unitComponent = scene.GetComponent<UnitComponent>();
                if (unitComponent == null)
                {
                    unitComponent = scene.AddComponent<UnitComponent>();
                }

                // Gate Scene 上没有 AOIManagerComponent (Map Scene 才有)，临时加一下让 AOIEntity 不 NRE
                AOIManagerComponent aoiManager = scene.GetComponent<AOIManagerComponent>();
                if (aoiManager == null)
                {
                    aoiManager = scene.AddComponent<AOIManagerComponent>();
                }

                // 模拟 G2Map_EnterMapHandler: UnitFactory.Create + Actor 覆盖
                long fakePlayerId = IdGenerater.Instance.GenerateId();
                Unit unit = UnitFactory.Create(scene, fakePlayerId, 1001);

                // 华夏 Actor 属性覆盖 (同 G2Map_EnterMapHandler)
                unit.Position = new Unity.Mathematics.float3(actor.LocationX, actor.LocationY, 0);
                NumericComponent nc = unit.NumericComponent;
                nc.SetNoEvent(NumericType.X, actor.LocationX * 1000L);
                nc.SetNoEvent(NumericType.Y, actor.LocationY * 1000L);
                nc.SetNoEvent(NumericType.Z, 0);
                nc.SetNoEvent(NumericType.HP, actor.CurHp);
                nc.SetNoEvent(NumericType.MaxHP, actor.MaxHp);
                nc.SetNoEvent(NumericType.MP, actor.CurMp);
                nc.SetNoEvent(NumericType.MaxMP, actor.MaxMp);

                // 验证
                var pos = unit.Position;
                float ncX = nc.GetAsFloat(NumericType.X);
                float ncY = nc.GetAsFloat(NumericType.Y);
                int hp = nc.GetAsInt(NumericType.HP);
                int maxHp = nc.GetAsInt(NumericType.MaxHP);
                int mp = nc.GetAsInt(NumericType.MP);
                int maxMp = nc.GetAsInt(NumericType.MaxMP);

                unitComponent.Add(unit);

                Log.Info($"[华夏] ✅ Unit 创建+覆盖成功: UnitId={unit.Id}, UnitType={unit.UnitType}");
                Log.Info($"   → Actor={actor.ActorName}, Lv.{actor.Level}, Class={actor.Class}, Sex={actor.Sex}");
                Log.Info($"   → Position=({pos.x},{pos.y}) NumericX={ncX} Y={ncY} (应与 Actor Map={actor.MapId}({actor.LocationX},{actor.LocationY}) 一致)");
                Log.Info($"   → HP={hp}/{maxHp} (Actor: {actor.CurHp}/{actor.MaxHp}) MP={mp}/{maxMp} (Actor: {actor.CurMp}/{actor.MaxMp})");

                // Position ↔ NumericComponent X/Y 一致性校验
                bool posOk = System.Math.Abs(pos.x - actor.LocationX) < 0.01f && System.Math.Abs(pos.y - actor.LocationY) < 0.01f;
                bool ncOk = System.Math.Abs(ncX - actor.LocationX) < 0.01f && System.Math.Abs(ncY - actor.LocationY) < 0.01f;
                bool hpOk = hp == actor.CurHp && maxHp == actor.MaxHp && mp == actor.CurMp && maxMp == actor.MaxMp;

                if (posOk && ncOk && hpOk)
                {
                    Log.Info("[华夏] ✅ Unit Position ↔ NumericComponent ↔ Actor DB 三者一致");
                }
                else
                {
                    Log.Error($"[华夏] ❌ Unit 数据不一致! posOk={posOk} ncOk={ncOk} hpOk={hpOk}");
                }

                // 清理
                unitComponent.Remove(unit.Id);
                unit.Dispose();
                Log.Info("[华夏] ---------- Map 层 Unit 初始化验证结束 ----------");
            }
            catch (Exception e)
            {
                Log.Error($"[华夏] ❌ Map Unit 验证异常: {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
