using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramEffect? SimaHuiPausedEffect(ProgramSkillFrame f)=>ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool CanOfferSimaHuiDamage(int owner,SkillProgramTrigger trigger,IDamageAttempt attack)
    {
        if(!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.DistributeTopCardsAfterElementalChainOrigin))return true;
        return TrySimaHuiOriginalChainMembers(attack,out _);
    }
    private bool TrySimaHuiOriginalChainMembers(IDamageAttempt attack,out int[] members)
    {
        members=[];
        if(!attack.DamageWasApplied || attack.DamageAmount<=0 || attack.IsChainPropagation || GetDamageNature(attack)==DamageNature.Normal || !IsValidPlayerSeat(attack.TargetSeat))return false;
        IReadOnlyList<int> propagated;int index;
        if(attack is ProgramAttackHandle)
        {var state=GetProgramAttackState(attack.ResolutionId);if(state.ChainedTargetSeats is null)return false;propagated=state.ChainedTargetSeats;index=state.ChainedTargetIndex;}
        else if(attack is CardAttackHandle)
        {var state=GetCardAttackState(attack.ResolutionId);propagated=state.ChainedTargetSeats;index=state.ChainedTargetIndex;}
        else return false;
        if(index!=0 || propagated.Any(s=>!IsValidPlayerSeat(s) || s==attack.TargetSeat) || propagated.Distinct().Count()!=propagated.Count)return false;
        var original=new[]{attack.TargetSeat}.Concat(propagated).ToArray();
        var damage=_resolutionStack.OfType<DamageFrame>().LastOrDefault(d=>d.ParentFrameId==attack.ResolutionId && d.TargetSeat==attack.TargetSeat && d.SourceSeat==attack.SourceSeat);
        if(damage is null || damage.Amount!=attack.DamageAmount || damage.Nature!=GetDamageNature(attack))return false;
        var events=CompleteProgramEventHistory().ToArray();var requestIndex=Array.FindLastIndex(events,e=>e is DamageRequestedEvent requested && requested.ResolutionId==damage.Id && requested.SourceSeat==attack.SourceSeat &&
            requested.TargetSeat==attack.TargetSeat && requested.Amount==damage.Amount && requested.Nature==damage.Nature && requested.SourceLess==attack.IsSourceLess);
        // Native ApplyAttackDamage publishes this exact, contiguous clear block
        // immediately before the actual DamageRequested fact. The frozen native
        // propagation list and this block also prove a solo origin (empty tail).
        if(requestIndex<original.Length)return false;
        for(var i=0;i<original.Length;i++)if(events[requestIndex-original.Length+i] is not IronChainStateChangedEvent clear || clear.ResolutionId!=attack.ResolutionId || clear.SourceSeat!=attack.SourceSeat || clear.TargetSeat!=original[i] || clear.IsChained)return false;
        members=original;return true;
    }
    private bool TrySimaHuiChainOrigin(int owner,SkillProgramTrigger trigger,ProgramSkillWindowContext context,out SimaHuiChainOriginProof proof)
    {
        proof=null!;
        if(context is not{Window:SkillProgramTriggerWindow.AfterDamageApplied,Amount:>0,DamageFrameId:{}damageId,TargetSeat:{}target} ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window || window.ParentFrameId!=damageId || window.TargetSeat!=target ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==damageId) is not{}damage)return false;
        var attack=GetDamageTriggerAttack(window);if(!TrySimaHuiOriginalChainMembers(attack,out var originalMembers) || damage.ParentFrameId!=attack.ResolutionId || damage.TargetSeat!=target || damage.Amount!=context.Amount || damage.Nature!=GetDamageNature(attack))return false;
        proof=new(window.Id,attack.ResolutionId,_turnNumber,attack.SourceSeat,target,damage.Nature,attack.IsSourceLess,originalMembers.Length);return true;
    }
    private bool CanPreventSimaHuiDamage(int owner,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(!ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.PreventDamageWithoutSealsAndArmor))return true;
        if(context.Window!=SkillProgramTriggerWindow.BeforeDamageApplied || context.TargetSeat!=owner || SimaHuiSeals.Any(m=>_players[owner].Markers.GetValueOrDefault(m)>0) || GetEquipment(_players[owner]).Any(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Armor) ||
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}window || window.TargetSeat!=owner || window.Prevented || CurrentDamageAttempt is not{}attack ||
            attack.ResolutionId!=(window.ContinuationAttackResolutionId??window.ParentFrameId) || attack.TargetSeat!=owner)return false;
        return GetDamageNature(attack)!=DamageNature.Normal || attack.EffectiveCardKind is{}kind && (IsOrdinaryTrick(kind) || kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning);
    }
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
    private bool CanContinueSimaHui(ProgramSkillFrame f)=>f.SimaHui is{}r && ValidSimaHui(f) && (r.Stage=="yeyan-paid" || CompleteProgramEventHistory().OfType<SimaHuiSealMovedEvent>().Any(e=>e.FrameId==f.Id) || r.ChainOrigin is not null);
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
    private bool IsSimaHuiYeyanSelectedPayment(ProgramSkillFrame f)=>f.SimaHui is{Stage:"yeyan-paid",OriginalCards.Count:4}r && ValidSimaHui(f) && f.InstructionIndex==2 && SimaHuiPausedEffect(f) is{Op:SkillProgramEffectOp.DiscardSelected,Amount:4} &&
        f.SelectedCardPayment is{InstructionIndex:2,Operation:SkillProgramEffectOp.DiscardSelected,MovementCommitted:true}p && p.RecipientSeat==f.OwnerSeat && p.CardIds.SequenceEqual(r.OriginalCards) &&
        r.OriginalCards.All(id=>_cardMovements.Count(m=>m.Sequence>r.MovementBefore && m.CardId==id && m.From==CardLocation.Hand(f.OwnerSeat) && m.To==CardLocation.DiscardPile && m.Reason.Value==$"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}")==1);
    private bool SimaHuiYeyanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        var r=f.SimaHui!;
        if(child is CardsMovedTriggerWindowFrame moved)return IsSimaHuiYeyanSelectedPayment(f) && f.SelectedCardPayment!.ActiveChildFrameId==moved.Id && moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id &&
            moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId && moved.Batch.Movements.Select(m=>m.CardId).SequenceEqual(r.OriginalCards) &&
            moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.MovementBefore && m.From==CardLocation.Hand(f.OwnerSeat) && m.To==CardLocation.DiscardPile && m.Reason.Value==$"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}");
        if(child is ProgramLifecycleTriggerWindowFrame skills)return skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram && f.InstructionIndex is >=1 and <=4;
        if(child is DyingFrame dying)return f.InstructionIndex==3 && SimaHuiPausedEffect(f) is{Op:SkillProgramEffectOp.LoseHpUnclamped,Amount:3} && f.SelectedCardPaymentResult is{Completed:true,InstructionIndex:2} &&
            dying.ParentFrameId==f.Id && dying.Continuation==DyingContinuationKind.ProgramSkill && dying.VictimSeat==f.OwnerSeat && dying.KillerSeat is null &&
            CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==f.Id && e.SkillId==f.SkillId && e.TargetSeat==f.OwnerSeat && e.Amount==3 && e.RemainingHp<=0)==1;
        if(child is HpChangedTriggerWindowFrame hp)return f.InstructionIndex==3 && SimaHuiPausedEffect(f) is{Op:SkillProgramEffectOp.LoseHpUnclamped,Amount:3} && hp.ResumeFrameId==f.Id && hp.Continuation==PostEventContinuation.Program &&
            hp.Change.ParentFrameId==f.Id && hp.Change.Kind==HpChangeKind.Loss && hp.Change.TargetSeat==f.OwnerSeat && hp.Change.Amount==3;
        if(f.AttackAttempt is not{}attack || SimaHuiPausedEffect(f) is not{Op:SkillProgramEffectOp.DamageParticipants,DamageNature:DamageNature.Fire} || attack.SourceSeat!=f.OwnerSeat || !f.SelectedTargetSeats.Contains(attack.TargetSeat))return false;
        if(child is DamageFrame damage)return damage.ParentFrameId==f.Id && damage.SourceSeat==attack.SourceSeat && damage.TargetSeat==attack.TargetSeat && damage.Amount==attack.DamageAmount && damage.Nature==DamageNature.Fire &&
            CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Count(e=>e.ResolutionId==damage.Id && e.SourceSeat==damage.SourceSeat && e.TargetSeat==damage.TargetSeat && e.Amount==damage.Amount && e.Nature==damage.Nature && !e.SourceLess)==1;
        return child is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId??before.ParentFrameId)==f.Id && before.SourceSeat==attack.SourceSeat && before.TargetSeat==attack.TargetSeat && before.Amount==attack.DamageAmount && before.Nature==DamageNature.Fire;
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
    private bool IsSimaHuiDying()=>ActiveDying is{}d && SimaHuiObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    private bool HasSimaHuiDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && SimaHuiObserverRoot() is not null;
    private bool HasSimaHuiBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && SimaHuiObserverRoot() is not null;
    private bool TryAdvanceSimaHuiSubtree()=>_pendingDecision is null && SimaHuiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool AllowsSimaHuiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess || f.AttackAttempt is not null || f.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=f.Id || SimaHuiObserverRoot() is null ||
            f.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.SkillsChanged))return false;
        var e=SimaHuiPausedEffect(f)!;return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    private IEnumerable<Card> SimaHuiPrivatelyViewedCards(int viewer)=>_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.OwnerSeat==viewer && f.SimaHui is{OriginalCards.Count:>0}).SelectMany(f=>f.SimaHui!.OriginalCards).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).Select(id=>_cardZones.CardsAt(CardLocation.Processing).Single(c=>c.Id==id));
}
