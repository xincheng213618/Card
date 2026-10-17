namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool AHuiNanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.AHuiNanJueman is not{}r||!ValidAHuiNan(f))return false;
        if(child is CardUseFrame use)return r.Stage==AHuiNanJuemanStage.NativeUse&&use.AHuiNanJueman is{}o&&MatchesAHuiNanJueman(f,o)&&IsAHuiNanJuemanUse(use.Id);
        if(child is CardsMovedTriggerWindowFrame moved)return r.Stage==AHuiNanJuemanStage.DrawChildren&&moved.ResumeProgramFrameId==f.Id&&moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&(m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==AHuiNanDrawReason||m.Reason==CardMoveReasons.Reshuffle));
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? AHuiNanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.AHuiNanFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasAHuiNanDamageObserver(long id)=>AHuiNanObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasAHuiNanBeforeDamageObserver(long id)=>HasAHuiNanDamageObserver(id);
    private bool IsAHuiNanDying()=>ActiveDying is{}d&&AHuiNanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsAHuiNanNestedDamage(ProgramSkillFrame f,int targetSeat,int amount,ProgramParticipantReference? sourceReference,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||AHuiNanObserverRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==sourceReference&&e.DamageNature==nature&&targetSeat==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27200)]private bool AdvanceAHuiNanChildren()=>_pendingDecision is null&&AHuiNanObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveJuemanFirstBasicTurnEnd)]private PromptChoice AiAHuiNan(PendingDecision p,ProgramSkillFrame f)
    {
        var friendly=f.AHuiNanJueman!.History[2].Kind is CardKind.Peach or CardKind.Alcohol;var hint=friendly?new SkillProgramAiHint(0,0,0,1,0,0,false,false):new SkillProgramAiHint(1,0,0,0,0,0,false,false);var view=CreateSnapshot(f.OwnerSeat);
        return p.Choices.OrderByDescending(c=>c.Targets.Sum(t=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(view,t,hint))).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    }
}
