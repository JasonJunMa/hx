using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    /// <summary>
    /// 华夏 Actor 持久化回写 Helper
    /// Unit (Map Scene 内存) → Actor (MongoDB 持久化)
    /// </summary>
    [EnableClass]
    public static class HuaxiaActorPersistenceHelper
    {
        /// <summary>
        /// 把 Unit 的变化同步回 MongoDB Actor
        /// 只在关键节点调用: Logout/切地图/定时批量
        /// </summary>
        public static async ETTask SaveUnitToActor(Unit unit)
        {
            HuaxiaActorComponent hac = unit.GetComponent<HuaxiaActorComponent>();
            if (hac == null || hac.ActorDbId == 0 || !hac.IsDirty)
            {
                return; // 无变化不写
            }

            Scene scene = unit.Root();
            DBManagerComponent dbManager = scene.GetComponent<DBManagerComponent>();
            if (dbManager == null)
            {
                Log.Warning($"[华夏] SaveUnitToActor: DBManagerComponent 未找到! UnitId={unit.Id}");
                return;
            }
            DBComponent db = dbManager.GetZoneDB(unit.Zone());

            // 1. 查 Actor
            List<Actor> actors = await db.Query<Actor>(a => a.Id == hac.ActorDbId);
            if (actors.Count == 0)
            {
                Log.Warning($"[华夏] SaveUnitToActor: Actor 不存在! ActorDbId={hac.ActorDbId}");
                return;
            }
            Actor actor = actors[0];

            // 2. 同步 Unit → Actor
            actor.LocationX = (int)unit.Position.x;
            actor.LocationY = (int)unit.Position.y;
            actor.MapId = (int)unit.Scene().Id;

            NumericComponent nc = unit.NumericComponent;
            if (nc != null)
            {
                actor.CurHp = nc.GetAsInt(NumericType.HP);
                actor.MaxHp = nc.GetAsInt(NumericType.MaxHP);
                actor.CurMp = nc.GetAsInt(NumericType.MP);
                actor.MaxMp = nc.GetAsInt(NumericType.MaxMP);
            }

            // 3. Save
            await db.Save(actor);
            hac.IsDirty = false;

            Log.Info($"[华夏] ✅ Actor 持久化: ActorId={actor.Id}, Name={actor.ActorName}, " +
                     $"Map={actor.MapId}({actor.LocationX},{actor.LocationY}), " +
                     $"HP={actor.CurHp}/{actor.MaxHp}, MP={actor.CurMp}/{actor.MaxMp}");

            actor.Dispose();
        }

        /// <summary>
        /// 扫整个 Map Scene，把所有 dirty 的 Player Unit 批量回写
        /// </summary>
        public static async ETTask SaveAllDirtyActors(Scene scene)
        {
            UnitComponent unitComponent = scene.GetComponent<UnitComponent>();
            if (unitComponent == null) return;

            int saved = 0;
            foreach (var entity in unitComponent.Children.Values)
            {
                if (entity is not Unit unit) continue;
                if (unit.UnitType != UnitType.Player) continue;

                HuaxiaActorComponent hac = unit.GetComponent<HuaxiaActorComponent>();
                if (hac != null && hac.IsDirty)
                {
                    await SaveUnitToActor(unit);
                    saved++;
                }
            }

            if (saved > 0)
            {
                Log.Info($"[华夏] 定时批量回写: {saved} 个 dirty Actor");
            }
        }
    }
}
