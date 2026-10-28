namespace ET.Server
{
    /// <summary>
    /// 华夏: Unit 数值变化 → Actor 属性回写 (只标记 dirty)
    /// 只关心 HP/MaxHP/MP/MaxMP 这几个影响 Actor 字段的
    /// </summary>
    [EnableClass]
    public class ActorDirty_OnNumbericChange : AEvent<Scene, NumbericChange>
    {
        protected override ETTask Run(Scene scene, NumbericChange a)
        {
            // 只过滤会影响 Actor 字段的 NumericType
            switch (a.NumericType)
            {
                case NumericType.HP:
                case NumericType.MaxHP:
                case NumericType.MP:
                case NumericType.MaxMP:
                {
                    Unit unit = a.Unit;
                    if (unit == null || unit.IsDisposed) return ETTask.CompletedTask;

                    HuaxiaActorComponent hac = unit.GetComponent<HuaxiaActorComponent>();
                    if (hac != null)
                    {
                        hac.IsDirty = true;
                    }
                    break;
                }
            }
            return ETTask.CompletedTask;
        }
    }
}
