namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsQinLangDiscardMovement(QinLangXianyingReceipt r,CardMovementRecord m)=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&
        (r.Selected.Any(c=>c.CardId==m.CardId&&c.Kind==m.CardKind&&c.From==m.From)&&m.To==(m.CardKind==CardKind.GeneralWeapon&&m.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.DiscardPile)&&m.Reason.Value==QinLangDiscardReason||
         r.Auxiliaries.Any(c=>c.CardId==m.CardId&&c.Kind==m.CardKind&&c.From==m.From)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard);
    private bool IsQinLangRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.QinLangXianying is{Stage:QinLangXianyingStage.DiscardChildren}r&&ValidQinLang(f)&&
        r.Selected.Any(c=>c.Kind==CardKind.SilverLion&&c.From==CardLocation.Equipment(f.OwnerSeat))&&recovery.ParentFrameId==f.Id&&recovery.Return==new RecoveryReplacementReturn(PostEventContinuation.Program,f.Id)&&
        recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==QinLangDiscardReason&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==f.OwnerSeat&&recovery.Attempt.Amount==1;
    private bool QinLangFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.QinLangXianying is not{}r||!ValidQinLang(f))return false;
        if(child is CardUseFrame use)return r.Stage==QinLangXianyingStage.NativeUse&&use.QinLangXianying is{}o&&MatchesQinLangXianying(f,o)&&IsQinLangXianyingUse(use.Id);
        if(child is RecoveryReplacementFrame recovery)return IsQinLangRecoveryParent(f,recovery);
        if(child is CardsMovedTriggerWindowFrame moved)
        {
            if(moved.ResumeProgramFrameId!=f.Id||moved.Batch.ParentFrameId!=f.Id||moved.Batch.OriginOwnerSeat!=f.OwnerSeat||moved.Batch.OriginSkillId!=f.SkillId||moved.Batch.OriginSkillInstanceId!=f.SkillInstanceId||moved.Batch.Movements.Count==0)return false;
            if(r.Stage==QinLangXianyingStage.DrawChildren)return moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&
                (m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==QinLangDrawReason||m.Reason==CardMoveReasons.Reshuffle));
            if(r.Stage==QinLangXianyingStage.DiscardChildren)return moved.Batch.Movements.All(m=>IsQinLangDiscardMovement(r,m))&&
                (moved.Batch.Id==r.PaidBatchId||moved.Batch.ParentBatchId==r.PaidBatchId&&moved.Batch.Movements.All(m=>r.Auxiliaries.Any(c=>c.CardId==m.CardId)));
            return false;
        }
        if(child is HpChangedTriggerWindowFrame hp)return r.Stage==QinLangXianyingStage.DiscardChildren&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&
            hp.Change.ParentFrameId==f.Id&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==f.OwnerSeat&&hp.Change.TargetSeat==f.OwnerSeat&&hp.Change.Amount==1&&r.Selected.Any(c=>c.Kind==CardKind.SilverLion&&c.From==CardLocation.Equipment(f.OwnerSeat));
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? QinLangObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.QinLangFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,
        static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasQinLangDamageObserver(long id)=>QinLangObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasQinLangBeforeDamageObserver(long id)=>HasQinLangDamageObserver(id);
    private bool IsQinLangDying()=>ActiveDying is{}dying&&QinLangObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsQinLangNestedDamage(ProgramSkillFrame f,int targetSeat,int amount,ProgramParticipantReference? sourceReference,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||QinLangObserverRoot()is not{}root||root.Id==f.Id)return false;
        var effect=GetPausedPrivateOfferEffect(f);return effect is{Op:SkillProgramEffectOp.Damage}&&effect.Amount==amount&&effect.ActorReference==sourceReference&&effect.DamageNature==nature&&
            targetSeat==(effect.TargetReference is{}participant?ResolveProgramParticipant(f,participant):ResolveProgramEffectTarget(f,effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27170)]private bool AdvanceQinLangChildren()=>_pendingDecision is null&&QinLangObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveXianyingDrawDiscard,SkillProgramEffectOp.ResolveXianyingTurnEndUses)]
    private PromptChoice AiQinLang(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.QinLangXianying!;
        if(r.Stage==QinLangXianyingStage.EndChoice)
        {
            var view=CreateSnapshot(f.OwnerSeat);var benefit=new SkillProgramAiHint(0,0,0,1,0,0,false,false);var harm=new SkillProgramAiHint(1,0,0,0,0,0,false,false);
            var scored=p.Choices.Where(c=>c.Parameters.GetValueOrDefault("branch")=="use").Select(c=>
            {
                var grant=r.Grants.Single(g=>g.FrameId==long.Parse(c.Parameters["grant-id"],System.Globalization.CultureInfo.InvariantCulture));
                var friendly=grant.Name is CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains;
                var score=c.Targets.Count==0?1:c.Targets.Sum(t=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(view,t,friendly?benefit:harm));return(Choice:c,Score:score);
            }).OrderByDescending(x=>x.Score).ThenBy(x=>x.Choice.Id.Value,StringComparer.Ordinal).FirstOrDefault();
            return scored.Choice is not null&&scored.Score>=0?scored.Choice:p.Choices.Last();
        }
        var group=r.Cards.GroupBy(c=>c.EffectiveName??QinLangName(c.Kind)).Where(g=>QinLangImitable(g.Key)).SelectMany(g=>Enumerable.Range(1,g.Count()).Where(n=>!QinLangCountUsed(f.OwnerSeat,r.RoundNumber,n)).Select(n=>g.OrderBy(c=>GetKeepValue(GetAdvancedCard(c.CardId),_players[f.OwnerSeat])).Take(n).ToArray()))
            .OrderBy(g=>g.Sum(c=>GetKeepValue(GetAdvancedCard(c.CardId),_players[f.OwnerSeat]))-3).ThenBy(g=>g.Length).FirstOrDefault();
        if(group is null)
        {
            var count=Enumerable.Range(0,r.Cards.Count+1).First(n=>!QinLangCountUsed(f.OwnerSeat,r.RoundNumber,n));group=r.Cards.OrderBy(c=>GetKeepValue(GetAdvancedCard(c.CardId),_players[f.OwnerSeat])).Take(count).ToArray();
        }
        var remove=r.Selected.FirstOrDefault(c=>!group.Contains(c));var add=group.FirstOrDefault(c=>!r.Selected.Contains(c));
        return remove is not null?p.Choices.Single(c=>c.Cards.SequenceEqual([remove.CardId])):add is not null?p.Choices.Single(c=>c.Cards.SequenceEqual([add.CardId])):p.Choices.Single(c=>c.Parameters["branch"]=="pay");
    }
}
