namespace CardGame.Core;
internal static class XuanGongZhuContract
{
    private static bool HasOperation(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ArmQimeiPair or SkillProgramEffectOp.SettleQimeiPairEquality or
        SkillProgramEffectOp.OfferZhuijiActualPlayChoice or SkillProgramEffectOp.SettleZhuijiActualPlayDebt or SkillProgramEffectOp.AfterActualIdentityAppearanceRecover;
    internal static void ValidateActivation(string path,SkillProgramActivation a)
    {
        if(a.Effects.Any(HasOperation))throw new InvalidOperationException(path+": identity appearance, preparation pair and actual phase debt require their native trigger windows.");
    }
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(HasOperation))return;
        if(t.Subject!=SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || t.UsageScope is not null || t.UsageLimit is not null || t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own ||
            t.Condition.Kind!=SkillProgramTriggerConditionKind.Always)throw new InvalidOperationException(path+": Xuan Gong Zhu requires exact owning native entries, without borrowed quotas or predicates.");
        var valid=t.Window switch
        {
            SkillProgramTriggerWindow.IdentityGeneralAppeared=>t.Optional && t.Effects is
                [{Op:SkillProgramEffectOp.SelectTarget,Target:SkillProgramEffectTarget.Owner,TargetKind:SkillProgramTargetKind.AnyWounded},
                 {Op:SkillProgramEffectOp.AfterActualIdentityAppearanceRecover,Target:SkillProgramEffectTarget.Owner},
                 {Op:SkillProgramEffectOp.Recover,Target:SkillProgramEffectTarget.SelectedTarget,Amount:1}],
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow=>t.Optional && t.Effects is
                [{Op:SkillProgramEffectOp.SelectTarget,Target:SkillProgramEffectTarget.Owner,TargetKind:SkillProgramTargetKind.OtherLiving},
                 {Op:SkillProgramEffectOp.ArmQimeiPair,Target:SkillProgramEffectTarget.Owner}],
            SkillProgramTriggerWindow.CardsMoved=>!t.Optional && t.SourceZones.SequenceEqual([CardZoneKind.Hand]) && t.MovementOccurrence==SkillProgramMovementOccurrence.PerOwnerBatch &&
                t.Effects is [{Op:SkillProgramEffectOp.SettleQimeiPairEquality,Target:SkillProgramEffectTarget.Owner}],
            SkillProgramTriggerWindow.AfterHealthChanged=>!t.Optional && t.HpChangeOccurrence==SkillProgramHpChangeOccurrence.PerEvent &&
                t.Effects is [{Op:SkillProgramEffectOp.SettleQimeiPairEquality,Target:SkillProgramEffectTarget.Owner}],
            SkillProgramTriggerWindow.PlayPhaseStarting=>t.Optional && t.Effects is
                [{Op:SkillProgramEffectOp.ChooseOption,Target:SkillProgramEffectTarget.Owner,ResultBind:"zhuiji-choice"}choice,
                 {Op:SkillProgramEffectOp.OfferZhuijiActualPlayChoice,Target:SkillProgramEffectTarget.Owner},
                 {Op:SkillProgramEffectOp.Recover,Target:SkillProgramEffectTarget.Owner,Amount:1}recover,
                 {Op:SkillProgramEffectOp.Draw,Target:SkillProgramEffectTarget.Owner,Amount:2}draw] &&
                 choice.Options.Select(o=>o.Id).SequenceEqual(["recover","draw"]) && Choice(recover.Condition,"recover") && Choice(draw.Condition,"draw"),
            SkillProgramTriggerWindow.PlayEnding=>!t.Optional && t.Effects is [{Op:SkillProgramEffectOp.SettleZhuijiActualPlayDebt,Target:SkillProgramEffectTarget.Owner}],
            _=>false
        };
        if(!valid)throw new InvalidOperationException(path+": Xuan Gong Zhu requires its complete appearance, pair mutation or same-actual-Play debt composition.");
    }
    private static bool Choice(SkillProgramCondition c,string id)=>c.Kind==SkillProgramConditionKind.ChoiceIs && c.SourceBind=="zhuiji-choice" && c.OptionId==id;
}
