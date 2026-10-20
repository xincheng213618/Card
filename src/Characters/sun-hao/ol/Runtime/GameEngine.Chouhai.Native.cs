namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasSunHaoChouhaiCapability => _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.EmptyHandDamageTakenBonus);
    private SunHaoChouhaiReceipt? SunHaoDamageState(long id) => _resolutionStack.SingleOrDefault(f=>f.Id==id) switch
    { CardUseFrame f=>f.SunHaoChouhai, ProgramSkillFrame f=>f.SunHaoChouhai, JudgmentFrame f=>f.SunHaoChouhai, _=>null };
    private void SetSunHaoDamageState(long id, SunHaoChouhaiReceipt? r)
    {
        var f = _resolutionStack.Single(x=>x.Id==id);
        ReplaceRuntimeFrame(id,f switch { CardUseFrame c=>c with {SunHaoChouhai=r}, ProgramSkillFrame p=>p with {SunHaoChouhai=r}, JudgmentFrame j=>j with {SunHaoChouhai=r},
            _=>throw new InvalidOperationException("Chouhai requires the actual damage attempt's owning frame.") });
    }
    private int SunHaoDamageCursor(IDamageAttempt a) => !a.IsChainPropagation ? -1 : a is ProgramAttackHandle ? GetProgramAttackState(a.ResolutionId).ChainedTargetIndex : GetCardAttackState(a.ResolutionId).ChainedTargetIndex;
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 180)]
    private int SunHaoChouhaiDamageBonus(IDamageAttempt attack)
    {
        if (!HasSunHaoChouhaiCapability) return 0;
        var cursor=SunHaoDamageCursor(attack);
        if (SunHaoDamageState(attack.ResolutionId) is { } old && old.TargetSeat==attack.TargetSeat && old.Cursor==cursor) return old.Bonus;
        var target=_players[attack.TargetSeat];
        var bonus=target.IsAlive && GetHand(target).Count==0 && HasCardPolicy(target,SkillProgramCardPolicyKind.EmptyHandDamageTakenBonus) ? 1 : 0;
        if(bonus==0){if(SunHaoDamageState(attack.ResolutionId) is not null)SetSunHaoDamageState(attack.ResolutionId,null);return 0;}
        var r=new SunHaoChouhaiReceipt(attack.ResolutionId,attack.TargetSeat,cursor,attack.DamageAmount,bonus,InvoiceId:++_resolutionSequence);
        SetSunHaoDamageState(attack.ResolutionId,r);
        AdvanceEventRulesAndQueueFact(new SunHaoChouhaiFrozenEvent(r.AttackId,r.TargetSeat,r.Cursor,r.InheritedAmount,r.Bonus,r.InvoiceId));
        return bonus;
    }
    private void SetSunHaoChainAmount(IDamageAttempt attack,int amount)
    {
        if (!SameAttackOwner(attack,CurrentDamageAttempt) || !attack.IsChainPropagation || amount<0) throw new InvalidOperationException("Chouhai can only restore its exact native chained recipient.");
        if(attack is ProgramAttackHandle) UpdateProgramAttackState(attack.ResolutionId,s=>s with {DamageAmount=amount});
        else UpdateCardAttackState(attack.ResolutionId,s=>s! with {DamageAmount=amount});
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 30)]
    private void PrepareSunHaoChainAdvance(IDamageAttempt attack)
    {
        if (!HasSunHaoChouhaiCapability || SunHaoDamageState(attack.ResolutionId) is not { } r) return;
        var seats=attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats : GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        var next=attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
        if (seats is null || !seats.Skip(next).Any(s=>_players[s].IsAlive)) return;
        if (attack.IsChainPropagation && r.Cursor==SunHaoDamageCursor(attack) && (r.TransferredTarget??r.TargetSeat)==attack.TargetSeat)
        {
            SetSunHaoChainAmount(attack,r.InheritedAmount);
            AdvanceEventRulesAndQueueFact(new SunHaoChouhaiRebasedEvent(r.AttackId,r.TargetSeat,r.Cursor,r.InheritedAmount));
        }
        SetSunHaoDamageState(attack.ResolutionId,null);
    }
    [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 40)]
    private void ApplySunHaoChainedDamageBonus(IDamageAttempt attack)
    {
        if(HasSunHaoChouhaiCapability && attack.IsChainPropagation && attack.DamageAmountFinalized)
            SetSunHaoChainAmount(attack,checked(attack.DamageAmount+SunHaoChouhaiDamageBonus(attack)));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish,27332)]
    private void AssertSunHaoDamageReceipts()
    {
        if(!HasSunHaoChouhaiCapability)return;
        foreach(var frame in _resolutionStack)
        {
            var r=SunHaoDamageState(frame.Id);if(r is null)continue;
            if(!HasSunHaoChouhaiCapability||r.AttackId!=frame.Id||!IsValidPlayerSeat(r.TargetSeat)||r.Cursor< -1||r.InheritedAmount<0||r.Bonus!=1||r.InvoiceId<=0||
                ProgramEventHistory<SunHaoChouhaiFrozenEvent>().Count(e=>e==new SunHaoChouhaiFrozenEvent(r.AttackId,r.TargetSeat,r.Cursor,r.InheritedAmount,r.Bonus,r.InvoiceId))!=1||
                (r.TransferredTarget is{} target ? !IsValidPlayerSeat(target)||r.TransferWindowId is not{} window||
                    ProgramEventHistory<SunHaoChouhaiTransferredEvent>().Count(e=>e.AttackId==r.AttackId&&e.OriginalSeat==r.TargetSeat&&e.TargetSeat==target&&e.Cursor==r.Cursor&&e.WindowId==window)!=1 : r.TransferWindowId is not null))
                throw new InvalidOperationException("Chouhai lost its actual native recipient's once-issued modifier invoice.");
        }
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27332)]
    private void ObserveSunHaoDamage(IGameEvent payload)
    {
        if(!HasSunHaoChouhaiCapability)return;
        if(payload is ExtraCardEffectStartedEvent extra && LifecycleCardUse(extra.CardUseFrameId) is {SunHaoChouhai:not null,Action:{} repeatedAction} repeated && repeatedAction.ActionId==extra.ActionId)
            SetSunHaoDamageState(repeated.Id,null);
        if(payload is ProgramDamageTransferredEvent e && CurrentDamageAttempt is {} attack && SunHaoDamageState(attack.ResolutionId) is {TransferredTarget:null} r &&
            _resolutionStack.Count>=2 && _resolutionStack[^1] is ProgramSkillFrame f && _resolutionStack[^2] is BeforeDamageProgramWindowFrame w &&
            GetPausedPrivateOfferEffect(f)?.Op==SkillProgramEffectOp.RedirectCurrentDamage && f.WindowContext?.ParentFrameId==w.Id &&
            e.ResolutionId==attack.ResolutionId && (w.ContinuationAttackResolutionId??w.ParentFrameId)==attack.ResolutionId && r.TargetSeat==w.TargetSeat &&
            w.RedirectedTargetSeat==attack.TargetSeat && e.TargetSeat==attack.TargetSeat && e.SourceSeat==attack.SourceSeat && e.DamageAmount==w.Amount &&
            e.SkillId==f.SkillId && e.OwnerSeat==f.OwnerSeat && attack.DamageRedirected)
        {
            SetSunHaoDamageState(attack.ResolutionId,r with {TransferredTarget=attack.TargetSeat,TransferWindowId=w.Id});
            AdvanceEventRulesAndQueueFact(new SunHaoChouhaiTransferredEvent(r.AttackId,r.TargetSeat,attack.TargetSeat,r.Cursor,w.Id));
        }
    }
}
