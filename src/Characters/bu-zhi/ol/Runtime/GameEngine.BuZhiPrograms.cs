using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string BuZhiDrawReason="program.bu-zhi.draw",BuZhiMoveReason="program.bu-zhi.equipment";
    private bool BuZhiMovementQualifies(CardMovementBatchContext batch,int owner)
    {
        var hand=CardLocation.Hand(owner);
        // A source-side processing trip is still a real loss. Returning the same
        // used card to its original hand is not a new acquisition.
        return batch.NativeDrawInvocation is{}draw && draw.LastBatchId==batch.Id && draw.ParentFrameId==batch.ParentFrameId &&
            draw.ActualTurnNumber==batch.TurnNumber && draw.OwnerSeat==owner && draw.ActualCount>=2 && ValidNativeDrawInvocation(draw) ||
            batch.NativeBuZhiDiscardInvocation is{}discard && discard.LastBatchId==batch.Id && discard.ParentFrameId==batch.ParentFrameId &&
            discard.ActualTurnNumber==batch.TurnNumber && discard.Source.OwnerSeat==owner && discard.ActualCount>=2 && ValidNativeBuZhiDiscardInvocation(discard) ||
            batch.Movements.Count(m=>m.From.OwnerSeat==owner && m.From!=m.To)>=2 ||
            batch.Movements.Count(m=>m.To==hand && ActualHandGainOriginalSource(m)!=hand)>=2;
    }
    private bool IsBuZhiActivation(SkillProgramActivation a)=>
        ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.ResolveRebelCountEquipmentChoice);
    private int? BuZhiPhaseLimit(CharacterState owner,SkillProgramActivation a)=>IsBuZhiActivation(a)?_players.Count(p=>p.IsAlive && p.Role==Role.Rebel):a.UsesPerPhase;
    private bool BuZhiActivationTargetAllowed(CharacterState target,SkillProgramActivation a)=>!IsBuZhiActivation(a) || target.IsAlive && GetEquipment(target).Count>0;
    private bool BuZhiPaidEquipmentEntitlement(ProgramSkillFrame f)=>f.BuZhi is{BatchId:null,Target:{}target} &&
        IsValidPlayerSeat(target) && _players[target].IsAlive && ValidBuZhi(f) &&
        CompleteProgramEventHistory().OfType<BuZhiMovementPaidEvent>().Count(e=>e.FrameId==f.Id && e.Target==target && e.ReturnStage=="branch" && e.CardIds.Count==0)==1;
    private bool BuZhiCurrent(ProgramSkillFrame f)=>_winner==Winner.None && _players[f.OwnerSeat].IsAlive &&
        (HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) && EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p=>p.Id==f.SkillId && p.GameplayHash==f.GameplayHash) ||
         BuZhiPaidEquipmentEntitlement(f));
    private SkillProgramEffect? BuZhiPausedEffect(ProgramSkillFrame f)=>f.InstructionIndex<1?null:ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool BuZhiInitialParentMatches(ProgramSkillFrame f,bool gainLoss)
    {
        if(f.InstructionIndex!=1 || f.SelectedCardIds.Count!=0 || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
        if(!gainLoss)return f.TriggerId is null && f.WindowContext is null && f.SelectedTargetSeats.Count==1 &&
            ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Activation is{}a && IsBuZhiActivation(a);
        var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        return f.TriggerId is not null && f.ActivationId==f.TriggerId && f.SelectedTargetSeats.Count==0 &&
            f.WindowContext is{Window:SkillProgramTriggerWindow.CardsMoved,MovementBatch:{}batch} c && c.OwnerSeat==f.OwnerSeat &&
            index>0 && _resolutionStack[index-1] is CardsMovedTriggerWindowFrame parent && parent.Id==c.ParentFrameId && parent.Batch.Id==batch.Id &&
            parent.CandidateIndex>=0 && parent.CandidateIndex<parent.Candidates.Count && MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]) &&
            BuZhiMovementQualifies(parent.Batch,f.OwnerSeat);
    }
    private SkillProgramStepOutcome ResolveBuZhi(ProgramSkillFrame supplied,bool gainLoss)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.BuZhi is not null || !BuZhiInitialParentMatches(f,gainLoss))throw new InvalidOperationException("Native gain/loss or equipment choice lost its original program binding.");
        var target=gainLoss?(int?)null:f.SelectedTargetSeats.Single();
        if(target is{}s && (!_players[s].IsAlive || GetEquipment(_players[s]).Count==0))throw new InvalidOperationException("Equipment choice lost its initial equipped recipient.");
        var r=new BuZhiReceipt{Source=new(f.SkillId,f.ActivationId!,f.OwnerSeat,f.SkillInstanceId),GameplayHash=f.GameplayHash,ParentFrameId=f.WindowContext?.ParentFrameId??0,
            BatchId=f.WindowContext?.MovementBatch?.Id,Turn=_turnNumber,InstructionIndex=f.InstructionIndex,Target=target,Stage=gainLoss?"beneficiary":"draw"};
        ReplaceRuntimeTop(f=f with{BuZhi=r});AdvanceEventRulesAndQueueFact(new BuZhiStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.BatchId,r.Turn,r.Target));
        if(gainLoss){PublishBuZhi(f);return SkillProgramStepOutcome.AwaitChoice;}
        PayBuZhiDraw(f,target!.Value,"branch");return SkillProgramStepOutcome.AwaitChild;
    }
    private static Dictionary<string,string> BuZhiParameters(ProgramSkillFrame f,string branch)=>new(){["program-action"]="bu-zhi",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch};
    private int BuZhiChooser(ProgramSkillFrame f)=>f.BuZhi!.Stage=="branch"?f.BuZhi.Target!.Value:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> BuZhiChoices(ProgramSkillFrame f)
    {
        var r=f.BuZhi!;var choices=new List<PromptChoice>();
        void Add(string branch,string id,string description,IReadOnlyList<int> cards,IReadOnlyList<int> targets)=>choices.Add(new(new($"bu-zhi.{f.Id}.{id}"),description,cards,targets,BuZhiParameters(f,branch)));
        if(r.Stage=="beneficiary")
        {
            foreach(var p in _players.Where(p=>p.IsAlive && p.Seat!=f.OwnerSeat))Add("benefit",p.Seat.ToString(CultureInfo.InvariantCulture),$"令 {p.Name} 摸一张牌",[],[p.Seat]);
            Add("decline","decline","不发动弘德",[],[]);
        }
        else if(r.Target is{}target && _players[target].IsAlive)
        {
            if(r.Stage=="branch")
            {
                if(GetEquipment(_players[target]).Count>0)Add("discard","discard","令其弃置你装备区里一张牌",[],[target]);
                Add("obtain","obtain","获得你装备区里的所有牌并受到1点伤害",[],[target]);
            }
            else if(r.Stage=="discard")
                foreach(var c in BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,target,[CardZoneKind.Equipment],OwnedCardMoveIntent.Discard))
                {var args=c.Parameters.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);args["program-action"]="bu-zhi";args["branch"]="pay-discard";choices.Add(new(c.Id,c.Description,c.Cards,c.Targets,args));}
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishBuZhi(ProgramSkillFrame f)
    {
        if(!BuZhiCurrent(f) || f.BuZhi!.Target is{}t && !_players[t].IsAlive){FinishBuZhi(f);return;}
        var choices=BuZhiChoices(f);if(choices.Count==0){FinishBuZhi(f);return;}
        var chooser=BuZhiChooser(f);var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,f.BuZhi!.Stage=="beneficiary"?"弘德：选择一名其他角色摸牌，或不发动。":"定叛：选择后续装备效果。",
            choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=chooser,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("bu-zhi")]
    private void ResolveBuZhiChoice(PromptChoice selected)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{BuZhi:not null}f || !BuZhiCurrent(f) || _pendingDecision?.PlayerSeat!=BuZhiChooser(f) ||
            !AssistedChoicesEqual([selected],BuZhiChoices(f).Where(c=>c.Id==selected.Id).ToArray()))throw new InvalidOperationException("Bu Zhi choice lost its actual chooser and exact current candidates.");
        AssertBuZhi(f);ClearPendingDecision();var r=f.BuZhi!;var branch=selected.Parameters["branch"];
        AdvanceEventRulesAndQueueFact(new BuZhiBranchSelectedEvent(f.Id,BuZhiChooser(f),branch));
        if(branch=="decline"){FinishBuZhi(f);return;}
        if(branch=="benefit"){ReplaceRuntimeTop(f=f with{BuZhi=r with{Target=selected.Targets.Single()}});PayBuZhiDraw(f,selected.Targets.Single(),"complete");return;}
        if(branch=="discard"){ReplaceRuntimeTop(f=f with{BuZhi=r with{Stage="discard"}});PublishBuZhi(f);return;}
        if(branch=="obtain")
        {
            var ids=GetEquipment(_players[r.Target!.Value]).Select(c=>c.Id).ToArray();
            if(ids.Length==0){IssueBuZhiDamage(f);return;}
            PayBuZhiEquipment(f,ids,CardLocation.Hand(r.Target.Value),"damage");return;
        }
        if(branch=="pay-discard")
        {
            if(!Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"),true,out var zone) || zone!=CardZoneKind.Equipment ||
                !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Equipment discard lost its opaque public slot.");
            var cards=_cardZones.CardsAt(CardLocation.Equipment(r.Target!.Value));if(slot<0 || slot>=cards.Count)throw new InvalidOperationException("Equipment slot is no longer present.");
            PayBuZhiEquipment(f,[cards[slot].Id],CardLocation.DiscardPile,"complete");return;
        }
        throw new InvalidOperationException("Unknown Bu Zhi choice.");
    }
    private void PayBuZhiDraw(ProgramSkillFrame f,int target,string next)
    {
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{BuZhi=f.BuZhi! with{Stage="movement",ReturnStage=next,Before=before,After=before,Actual=0,PaidIds=[]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        DrawProgramCards(f.Id,target,1,null,null,SkillProgramCardSetVisibility.Private,new(BuZhiDrawReason));f=GetActiveProgramFrame(f.Id);
        var actual=_cardMovements.Count(m=>m.Sequence>before && m.Sequence<=_movementSequence && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(target) && m.Reason.Value==BuZhiDrawReason);
        ReplaceRuntimeTop(f=f with{BuZhi=f.BuZhi! with{After=_movementSequence,Actual=actual}});
        AdvanceEventRulesAndQueueFact(new BuZhiMovementPaidEvent(f.Id,next,target,before,_movementSequence,actual,[]));
        if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);
    }
    private void PayBuZhiEquipment(ProgramSkillFrame f,IReadOnlyList<int> ids,CardLocation destination,string next)
    {
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{BuZhi=f.BuZhi! with{Stage="movement",ReturnStage=next,Before=before,After=before,Actual=0,PaidIds=ids},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var logicalDiscard=destination==CardLocation.DiscardPile?CaptureNativeLogicalDiscardInvocation(f.OwnerSeat,f.OwnerSeat,ids,new(BuZhiMoveReason),f.Id):null;
        MoveProgramCardsFromMultipleSources(ids,destination,new(BuZhiMoveReason),(_,records)=>
        {
            var active=GetActiveProgramFrame(f.Id);var actual=records.Count(m=>m.To==destination || destination==CardLocation.DiscardPile && m.To==CardLocation.OutsideGame);
            ReplaceRuntimeTop(active with{BuZhi=active.BuZhi! with{After=_movementSequence,Actual=actual}});
            AdvanceEventRulesAndQueueFact(new BuZhiMovementPaidEvent(f.Id,next,active.BuZhi!.Target!.Value,before,_movementSequence,actual,ids));
        });
        CompleteNativeLogicalDiscardInvocation(logicalDiscard);
        if(!TryBeginCardsMovedProgramWindow(f.Id))ReturnRuntimeProgramMovement(f.Id);
    }
    private void IssueBuZhiDamage(ProgramSkillFrame f)
    {
        if(!BuZhiCurrent(f) || !_players[f.BuZhi!.Target!.Value].IsAlive){FinishBuZhi(f);return;}
        ReplaceRuntimeTop(f=f with{BuZhi=f.BuZhi! with{Stage="damage-issued",DamageIssued=true}});
        AdvanceEventRulesAndQueueFact(new BuZhiDamageIssuedEvent(f.Id,f.OwnerSeat,f.BuZhi!.Target!.Value,1));BeginProgramSkillDamage(f,f.BuZhi.Target.Value,1);
    }
    private bool IsBuZhiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>e?.Op is SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit or SkillProgramEffectOp.ResolveRebelCountEquipmentChoice &&
        f.BuZhi is{Stage:"movement"}r && r.InstructionIndex==f.InstructionIndex && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement,22300)]
    private bool ReturnBuZhiMovement(ProgramSkillFrame f)
    {
        if(f.BuZhi is not{}r)return false;
        if(f.PendingMovementContinuation is not{}p || !IsBuZhiMovement(f,BuZhiPausedEffect(f),p))throw new InvalidOperationException("Bu Zhi consumed a foreign movement continuation.");
        AssertBuZhi(f);if(GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f))return true;
        ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,BuZhi=r with{Stage=r.ReturnStage}});
        if(!BuZhiCurrent(f) || r.ReturnStage=="complete" || r.Target is{}target && !_players[target].IsAlive)FinishBuZhi(f);
        else if(r.ReturnStage=="damage")IssueBuZhiDamage(f);else PublishBuZhi(f);return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,22300)]
    private bool ResumeBuZhi(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{BuZhi:not null}f || f.Id!=id)return false;
        AssertBuZhi(f);if(GainGiftHasFinalGameEnd())return true;
        if(f.PendingMovementContinuation is not null){if(!TryDrainFireTargetMovement(f))ReturnBuZhiMovement(f);return true;}
        if(f.BuZhi!.Stage=="damage-issued"){if(f.AttackAttempt is not null)return false;FinishBuZhi(f);return true;}
        if(!BuZhiCurrent(f)){ClearPendingDecision();FinishBuZhi(f);return true;}
        if(_pendingDecision is null)PublishBuZhi(f);return true;
    }
    private void FinishBuZhi(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{BuZhi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    private bool ValidBuZhi(ProgramSkillFrame f)
    {
        if(f.BuZhi is not{}r || r.Source!=new CardConversionSource(f.SkillId,f.ActivationId!,f.OwnerSeat,f.SkillInstanceId) || r.GameplayHash!=f.GameplayHash ||
            r.Turn!=_turnNumber || r.InstructionIndex!=f.InstructionIndex || r.ParentFrameId!=(f.WindowContext?.ParentFrameId??0) || r.BatchId!=f.WindowContext?.MovementBatch?.Id ||
            !BuZhiInitialParentMatches(f,r.BatchId is not null) || r.Target is{}target && !IsValidPlayerSeat(target) || r.Before>r.After ||
            r.Stage is not("beneficiary" or "draw" or "branch" or "discard" or "movement" or "damage" or "damage-issued" or "complete"))return false;
        var events=CompleteProgramEventHistory().ToArray();
        if(events.OfType<BuZhiStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not [var start] || start.Source!=r.Source || start.GameplayHash!=r.GameplayHash ||
            start.ParentFrameId!=r.ParentFrameId || start.BatchId!=r.BatchId || start.Turn!=r.Turn)return false;
        var paid=events.OfType<BuZhiMovementPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(paid.Length>2 || paid.Count(e=>e.CardIds.Count==0)>1 || paid.Any(e=>e.Before>e.After || !IsValidPlayerSeat(e.Target) || e.Actual<0 ||
            e.CardIds.Count==0 && _cardMovements.Count(m=>m.Sequence>e.Before && m.Sequence<=e.After && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(e.Target) && m.Reason.Value==BuZhiDrawReason)!=e.Actual ||
            e.CardIds.Count>0 && _cardMovements.Count(m=>m.Sequence>e.Before && m.Sequence<=e.After && e.CardIds.Contains(m.CardId) && m.From==CardLocation.Equipment(e.Target) && m.Reason.Value==BuZhiMoveReason)!=e.CardIds.Count))return false;
        if(r.Stage=="movement" && paid.Count(e=>e.Before==r.Before && e.After==r.After && e.Actual==r.Actual && e.ReturnStage==r.ReturnStage && e.CardIds.SequenceEqual(r.PaidIds))!=1)return false;
        var damage=events.OfType<BuZhiDamageIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        return r.DamageIssued?damage is[var d] && d.SourceSeat==f.OwnerSeat && d.TargetSeat==r.Target && d.Amount==1:damage.Length==0;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 130)]
    private void AssertBuZhi(ProgramSkillFrame f)
    {
        if(f.BuZhi is null)return;if(!ValidBuZhi(f))throw new InvalidOperationException("Bu Zhi lost its exact native batch, source or once-paid movement/damage receipts.");
        if(ReferenceEquals(f,_resolutionStack.LastOrDefault()) && _pendingDecision is{}p && f.BuZhi.Stage is "beneficiary" or "branch" or "discard" &&
            (p.PlayerSeat!=BuZhiChooser(f) || !p.IsPrivate || !AssistedChoicesEqual(p.Choices,BuZhiChoices(f))))throw new InvalidOperationException("Bu Zhi published a foreign chooser or changed candidates.");
    }
    [ProgramAiChoice(
        SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit,
        SkillProgramEffectOp.ResolveRebelCountEquipmentChoice)]
    private PromptChoice SelectAiBuZhi(PendingDecision p,ProgramSkillFrame f)
    {
        if(f.BuZhi!.Stage=="branch")return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="discard")??p.Choices[0];
        if(f.BuZhi.Stage=="discard")return p.Choices[0];
        var snapshot=CreateSnapshot(f.OwnerSeat);var hint=new SkillProgramAiHint(
            OwnerDraw:0,OwnerRecovery:0,OwnerHpLoss:0,TargetDraw:1,TargetRecovery:0,TargetHpLoss:0,GivesSelected:false,DiscardsSelected:false);
        return p.Choices.Where(c=>c.Targets.Count==1).OrderByDescending(c=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(snapshot,c.Targets[0],hint)).FirstOrDefault()??p.Choices[^1];
    }
    [ProgramCapability]
    private sealed class BuZhiProgramCapability(GameEngine engine) : IBuZhiProgramHost
    {public SkillProgramStepOutcome ResolveBuZhi(ProgramSkillFrame f,bool gainLoss)=>engine.ResolveBuZhi(f,gainLoss);}
}
