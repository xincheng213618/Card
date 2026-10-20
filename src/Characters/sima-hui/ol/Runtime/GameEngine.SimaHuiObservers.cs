using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{

    private SkillProgramEffect? SimaHuiPausedEffect(ProgramSkillFrame f)=>ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool ExactSimaHuiParent(ProgramSkillFrame f)
    {
        if(f.TriggerId is null)return f.WindowContext is null && f.ParentFrameId is null;
        if(f.WindowContext is not{}c)return false;
        if(_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId) is{}l)return l.CandidateIndex>=0 && l.CandidateIndex<l.Candidates.Count && MountObserverCandidateMatches(f,l.Candidates[l.CandidateIndex]) &&
            l.Window==c.Window && c.BeforeTurnPreludeProof==l.BeforeTurnPreludeProof && c.BeforeTurnPreludeProof is{}proof && ValidBeiMiHuBeforeTurnPrelude(proof);
        if(_resolutionStack.OfType<ProgramKillTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId) is{}k)return k.CandidateIndex>=0 && k.CandidateIndex<k.Candidates.Count && MountObserverCandidateMatches(f,k.Candidates[k.CandidateIndex]) && k.VictimSeat==c.TargetSeat;
        if(_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId) is{}d)return d.CandidateIndex>=0 && d.CandidateIndex<d.Candidates.Count && MountObserverCandidateMatches(f,d.Candidates[d.CandidateIndex].ToProgramCandidate()) && d.ParentFrameId==c.DamageFrameId;
        return false;
    }
    private bool ValidSimaHui(ProgramSkillFrame f)
    {
        if(f.SimaHui is not{}r || r.Source!=SimaHuiSource(f) || r.GameplayHash!=f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash ||
            r.ParentFrameId!=(f.WindowContext?.ParentFrameId??0) || r.ActualTurn!=(f.WindowContext?.BeforeTurnPreludeProof?.UpcomingTurnNumber??_turnNumber) || !ExactSimaHuiParent(f))return false;
        var history=CompleteProgramEventHistory().ToArray();
        if(r.Stage=="yeyan-paid")return r.InstructionIndex==1 && f.InstructionIndex>=1 && r.OriginalCards.Count is 0 or 4 && r.OriginalCards.SequenceEqual(f.SelectedCardIds) && history.OfType<SimaHuiYeyanSealsPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var paid] && paid.Source==r.Source && paid.GameplayHash==r.GameplayHash && paid.ActualTurn==r.ActualTurn && paid.GrantSourceIds.Count>=2 && paid.GrantSourceIds.Distinct().Count()==paid.GrantSourceIds.Count && paid.MaterialCardIds.SequenceEqual(r.OriginalCards);
        if(f.InstructionIndex!=r.InstructionIndex || f.InstructionIndex!=1 || r.Cursor<0 || r.Cursor>r.OriginalCards.Count || r.MovementBefore>r.MovementAfter)return false;
        if(r.Stage is "initial-dragon" or "initial-phoenix" or "transfer-kind" or "transfer-target" or "seal-children")return history.OfType<SimaHuiChoiceStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var choice] && choice.Source==r.Source && choice.GameplayHash==r.GameplayHash && choice.ParentFrameId==r.ParentFrameId && choice.ActualTurn==r.ActualTurn && r.Candidates.Distinct().Count()==r.Candidates.Count && r.Candidates.All(IsValidPlayerSeat) &&
            (r.Stage!="seal-children" || history.OfType<SimaHuiSealMovedEvent>().Any(e=>e.FrameId==f.Id && e.Holder==r.ChosenHolder && e.ActualTurn==r.ActualTurn));
        if(r.ChainOrigin is not{}origin || history.OfType<SimaHuiTopViewedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var viewed] || viewed.Source!=r.Source || viewed.GameplayHash!=r.GameplayHash || viewed.Origin!=origin ||
            !viewed.CardIds.SequenceEqual(r.OriginalCards) || r.OriginalCards.Count>origin.LivingChainedCount+1 || r.OriginalCards.Distinct().Count()!=r.OriginalCards.Count ||
            !_cardMovements.Where(m=>m.Sequence>viewed.Before && m.Sequence<=viewed.After && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing && m.Reason.Value==SimaHuiViewReason).Select(m=>m.CardId).SequenceEqual(r.OriginalCards))return false;
        if(r.Stage=="gift-children")return r.Cursor<r.OriginalCards.Count && r.ChosenHolder is{}recipient && history.OfType<SimaHuiCardDistributedEvent>().Where(e=>e.FrameId==f.Id && e.Cursor==r.Cursor).ToArray() is[var gift] &&
            gift.Source==r.Source && gift.GameplayHash==r.GameplayHash && gift.CardId==r.OriginalCards[r.Cursor] && gift.Recipient==recipient && gift.Before==r.MovementBefore && gift.After==r.MovementAfter &&
            _cardMovements.Count(m=>m.Sequence>gift.Before && m.Sequence<=gift.After && m.CardId==gift.CardId && m.From==CardLocation.Processing && m.To==CardLocation.Hand(recipient) && m.Reason.Value==SimaHuiGiftReason)==1;
        if(r.Stage=="return-children")return history.OfType<SimaHuiUnassignedReturnedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var returned] && returned.Source==r.Source && returned.GameplayHash==r.GameplayHash && returned.Before==r.MovementBefore && returned.After==r.MovementAfter &&
            returned.CardIds.All(r.OriginalCards.Contains) && _cardMovements.Where(m=>m.Sequence>returned.Before && m.Sequence<=returned.After && m.From==CardLocation.Processing && m.To==CardLocation.DrawPile && m.Reason.Value=="program.sima-hui.chenghao.return").Select(m=>m.CardId).SequenceEqual(returned.CardIds);
        return r.Stage is "view-children" or "distribute";
    }
    private void AssertSimaHui(ProgramSkillFrame f){if(f.SimaHui is not null && !ValidSimaHui(f))throw new InvalidOperationException("Sima Hui lost its exact native source, frozen cards or completed payment invoice.");}

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 220)]
    private void AssertRegisteredSimaHui(ProgramSkillFrame frame)
    {
        if (frame.SimaHui is not null) AssertSimaHui(frame);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 180)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 180)]
    private bool CanContinueSimaHui(ProgramSkillFrame f)=>f.SimaHui is{}r && ValidSimaHui(f) && (r.Stage=="yeyan-paid" || CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().Any(e=>e.FrameId==f.Id) || r.ChainOrigin is not null);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 180)]
    private bool IsSimaHuiMovement(ProgramSkillFrame f,SkillProgramEffect?effect,ProgramMovementContinuation pending)=>f.SimaHui is{Stage:"seal-children" or "view-children" or "gift-children" or "return-children"} && effect is not null && IsSimaHuiOp(effect.Op) && pending.SubjectSeat==f.OwnerSeat && pending.CoverageResultBind is null && ValidSimaHui(f);
    [ProgramContinuation(ProgramContinuationKind.Movement,23500)]
    private bool ReturnSimaHuiMovement(ProgramSkillFrame f)
    {
        if(f.SimaHui is null)return false;
        if(f.SimaHui.Stage=="yeyan-paid")
        {
            if(f.SelectedCardPayment is null || f.SelectedCardPaymentResult is not null)return false;
            if(!IsSimaHuiYeyanSelectedPayment(f) || f.SelectedCardPayment.ActiveChildFrameId is not null || f.PendingMovementContinuation is not null)throw new InvalidOperationException("Yeyan cannot return a foreign or incomplete native four-card payment.");
            var payment=f.SelectedCardPayment;ReplaceRuntimeTop(f with{SelectedCardPaymentResult=new(payment.InstructionIndex,payment.Operation,payment.LastCompletedChildFrameId,true)});AdvanceRuntimeProgram(f.Id);return true;
        }
        var pending=f.PendingMovementContinuation??throw new InvalidOperationException("Missing Sima Hui movement invoice.");if(!IsSimaHuiMovement(f,SimaHuiPausedEffect(f),pending))throw new InvalidOperationException("Sima Hui received a foreign movement return.");return ResumeSimaHui(f.Id);
    }
    private bool SimaHuiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.SimaHui is not{}r || !ValidSimaHui(f))return false;
        if(r.Stage=="yeyan-paid")return SimaHuiYeyanFirstChild(f,child);
        if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id && moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.MovementBefore && m.Sequence<=r.MovementAfter);
        if(child is ProgramLifecycleTriggerWindowFrame skills)return r.Stage=="seal-children" && skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        return false;
    }
    private ProgramSkillFrame? SimaHuiObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.SimaHuiFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 110)]
    private bool IsSimaHuiDying()=>ActiveDying is{}d && SimaHuiObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 140)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 250)]
    private bool HasSimaHuiDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && SimaHuiObserverRoot() is not null;
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 340)]
    private bool HasSimaHuiBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && SimaHuiObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 250)]
    private bool TryAdvanceSimaHuiSubtree()=>_pendingDecision is null && SimaHuiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 480)]
    private bool AllowsSimaHuiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess || f.AttackAttempt is not null || f.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=f.Id || SimaHuiObserverRoot() is null ||
            f.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.SkillsChanged))return false;
        var e=SimaHuiPausedEffect(f)!;return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    private IEnumerable<Card> SimaHuiPrivatelyViewedCards(int viewer)=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer && f.SimaHui is{OriginalCards.Count:>0}).SelectMany(f=>f.SimaHui!.OriginalCards).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).Select(id=>_cardZones.CardsAt(CardLocation.Processing).Single(c=>c.Id==id));
}
