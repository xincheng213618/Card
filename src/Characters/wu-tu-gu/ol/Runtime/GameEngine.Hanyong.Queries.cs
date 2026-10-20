namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27320)]
    private void CaptureWuTuGuHanyongUse(IGameEvent payload)
    {
        if(!HasWuTuGuCapability||payload is not CardUseDeclaredEvent declared||LifecycleCardUse(declared.ResolutionId)is not{Action:{Type:CardActionType.Use}action}use||
            use.CardId!=declared.CardId||use.SourceSeat!=declared.SourceSeat||action.ActorSeat!=use.SourceSeat||action.EffectiveKind!=use.CardKind||use.CardKind is not(CardKind.BarbarianAssault or CardKind.ArrowBarrage)||!_players[use.SourceSeat].IsAlive)return;
        foreach(var binding in GetSkillBindingShard(_players[use.SourceSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted).Where(b=>b.Trigger.Effects is[{Op:SkillProgramEffectOp.ArmHanyongNativeGroupDamage}]))
        {
            var source=new CardConversionSource(binding.SkillId,binding.Trigger.Id,use.SourceSeat,binding.SkillInstanceId);
            if(ProgramEventHistory<WuTuGuHanyongQualifiedEvent>().Any(e=>e.UseId==use.Id&&e.Source==source))continue;
            AdvanceEventRulesAndQueueFact(new WuTuGuHanyongQualifiedEvent(use.Id,action.ActionId,source,binding.Program.GameplayHash,_turnNumber,_roundNumber,_players[use.SourceSeat].Hp,action.ProviderSeat,use.CardKind));
        }
    }
    private WuTuGuHanyongQualifiedEvent? WuTuGuHanyongQualification(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardUseCommitted||context.CardUse is not{}card||context.OwnerSeat!=c.OwnerSeat||card.ActorSeat!=c.OwnerSeat||LifecycleCardUse(card.ParentCardUseFrameId)is not{Action:{}a}use||a.ActionId!=card.CardActionId)return null;
        var source=new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId);
        return ProgramEventHistory<WuTuGuHanyongQualifiedEvent>().SingleOrDefault(e=>e.UseId==use.Id&&e.ActionId==a.ActionId&&e.Source==source&&e.GameplayHash==c.GameplayHash&&e.Kind==use.CardKind&&e.ProviderSeat==a.ProviderSeat&&e.ActualTurn==_turnNumber&&e.Round>0&&e.HpAtUse<e.Round);
    }
    private bool ValidWuTuGuHanyongUse(CardUseFrame use)=>use.WuTuGuHanyong is{Grants.Count:>0}r&&use.Action is{Type:CardActionType.Use}a&&r.Grants.Select(g=>g.Source).Distinct().Count()==r.Grants.Count&&r.Grants.All(g=>
        g.UseId==use.Id&&g.ActionId==a.ActionId&&g.Kind==use.CardKind&&a.EffectiveKind==g.Kind&&g.Kind is(CardKind.BarbarianAssault or CardKind.ArrowBarrage)&&g.ProviderSeat==a.ProviderSeat&&g.Round>0&&g.HpAtUse<g.Round&&
        ShownEntityUseActorMatches(use,g.Source.OwnerSeat,g.ProviderSeat)&&ProgramEventHistory<WuTuGuHanyongArmedEvent>().Count(e=>e.Grant==g)==1&&
        ProgramEventHistory<WuTuGuHanyongQualifiedEvent>().Count(e=>e.UseId==g.UseId&&e.ActionId==g.ActionId&&e.Source==g.Source&&e.GameplayHash==g.GameplayHash&&e.ActualTurn==g.ActualTurn&&e.Round==g.Round&&e.HpAtUse==g.HpAtUse&&e.ProviderSeat==g.ProviderSeat&&e.Kind==g.Kind)==1);
    [NativeUseValidation(27320)]private void AssertWuTuGuNativeUse(CardUseFrame use){if(use.WuTuGuHanyong is not null&&!ValidWuTuGuHanyongUse(use))throw new InvalidOperationException("Hanyong lost its exact once-accepted native use, original source or frozen HP/round.");}
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 170)]
    private int WuTuGuHanyongDamageBonus(IDamageAttempt attack)
    {
        if(!HasWuTuGuCapability||attack.IsChainPropagation||LifecycleCardUse(attack.ResolutionId)is not{WuTuGuHanyong:not null}use)return 0;
        if(!ValidWuTuGuHanyongUse(use)||ActiveGroupCard is not{}group||group.ResolutionId!=use.Id||group.Effect!=GroupCardEffect.ResponseAttack||attack.EffectiveCardKind!=use.CardKind||!group.TargetSeats.Contains(attack.TargetSeat))throw new InvalidOperationException("Hanyong damage must remain on the original native group-use target cursor.");
        return use.WuTuGuHanyong!.Grants.Count;
    }
}
