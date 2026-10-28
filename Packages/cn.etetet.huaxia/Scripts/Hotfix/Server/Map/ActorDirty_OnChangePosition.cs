namespace ET.Server
{
    /// <summary>
    /// 华夏: Unit 位置变化 → Actor.LocationX/Y 回写 (只标记 dirty)
    /// </summary>
    [EnableClass]
    public class ActorDirty_OnChangePosition : AEvent<Scene, ChangePosition>
    {
        protected override ETTask Run(Scene scene, ChangePosition a)
        {
            Unit unit = a.Unit;
            if (unit == null || unit.IsDisposed) return ETTask.CompletedTask;

            HuaxiaActorComponent hac = unit.GetComponent<HuaxiaActorComponent>();
            if (hac != null)
            {
                hac.IsDirty = true;
            }
            return ETTask.CompletedTask;
        }
    }
}
