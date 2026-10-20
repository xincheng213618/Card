namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 110)]
    private void ObserveWangYunLianjiDamage(DamageAppliedEvent damage)
    {
        if(!HasWangYunPlanCapability || damage.Amount<=0 || CurrentDamageAttempt is not{}attack || LifecycleCardUse(attack.ResolutionId) is not{WangYunNativeOrigin:{Kind:"slash",PaymentCompleted:true}r}use ||
            !ValidWangYunNativeUse(use))return;
        AdvanceEventRulesAndQueueFact(new WangYunLianjiDamageEvent(r.ProgramFrameId,r.UseId,r.ActionId,r.Source.OwnerSeat,damage.SourceSeat,damage.TargetSeat,damage.Amount));
    }
}
