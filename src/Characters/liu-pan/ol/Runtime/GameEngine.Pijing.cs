namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string PijingGiftReason="skill-program.liu-pan.pijing.random-gift",PijingDrawReason="skill-program.liu-pan.pijing.next-use-draw";
    [ProgramCapability]private sealed class LiuPanCapability(GameEngine e):ILiuPanHost {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginPijing(f,op);}
    private SkillProgramStepOutcome BeginPijing(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var use=RequirePijingUse(f,out var window);var next=op==SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice;var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0);
        if(f.Pijing is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context||(!next?!PijingInitialEligible(candidate,context):!IsIssuedPijingCandidate(candidate,context)))throw new InvalidOperationException("Pijing cannot replace or issue an ineligible original target/lease resolution.");
        var group=next?PijingIssuedGroup(candidate,context,out _,out _):null;var x=next?1:Math.Max(1,_players[f.OwnerSeat].MaxHp-_players[f.OwnerSeat].Hp);var actor=group?.HolderSeat??use.SourceSeat;
        var r=new PijingReceipt(PijingSource(f),f.GameplayHash,use.Id,use.Action!.ActionId,window.Id,_turnNumber,actor,use.Action.ProviderSeat,x,next,group?.GroupId??0,next?PijingStage.Reciprocal:PijingStage.Toggles,use.TargetSeats,[],group?.Grants??[]);
        ReplaceRuntimeTop(f=f with{Pijing=r});AdvanceEventRulesAndQueueFact(new PijingStartedEvent(f.Id,r.Source,r.GameplayHash,r.UseId,r.ActionId,r.WindowId,r.ActualTurn,r.ActorSeat,r.ProviderSeat,r.X,r.Reciprocal,r.GroupId,r.Original.Count));for(var i=0;i<r.Original.Count;i++)AdvanceEventRulesAndQueueFact(new PijingTargetSlotEvent(f.Id,false,i,r.Original[i]));
        if(next)foreach(var id in r.Grants)AdvanceEventRulesAndQueueFact(new PijingGrantConsumedEvent(f.Id,r.UseId,r.GroupId,id));PublishPijing(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<Card> PijingGiftCards(int donor,int recipient)=>Array.AsReadOnly(GetHand(_players[donor]).Concat(GetEquipment(donor)).Where(c=>!c.IsGeneralWeapon&&!IsForeignEquipmentDiscardPrevented(recipient,c,_cardZones.GetLocation(c.Id),OwnedCardMoveIntent.Transfer)).OrderBy(c=>_cardZones.GetLocation(c.Id).Zone).ThenBy(c=>c.Id).ToArray());
    private void IssuePijingGift(ProgramSkillFrame f)
    {
        var r=f.Pijing!;var donor=r.Toggles[r.Index].Seat;var cards=_players[donor].IsAlive&&_players[f.OwnerSeat].IsAlive?PijingGiftCards(donor,f.OwnerSeat):[];
        if(cards.Count==0){AdvanceEventRulesAndQueueFact(new PijingGiftSkippedEvent(f.Id,r.Index,donor));CompletePijingGift(f);return;}
        var card=cards[_random.Next(cards.Count)];var from=_cardZones.GetLocation(card.Id);var selected=new PijingGiftSelectedEvent(f.Id,r.Index,donor,card.Id,card.Kind,from);var before=_movementSequence;
        ReplaceRuntimeTop(f=f with{Pijing=r with{Stage=PijingStage.GiftChildren,Gift=selected,Before=before,After=before,Count=1},PendingMovementContinuation=new(donor,0,null)});AdvanceEventRulesAndQueueFact(selected);
        MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), new(PijingGiftReason), NativeGiveCompletionRecords(f.Id,donor,f.OwnerSeat,[card.Id],_movementSequence,(batch,moves)=>
        {if(moves is not[var m]||m.CardId!=card.Id||m.From!=from||m.To!=CardLocation.Hand(f.OwnerSeat))throw new InvalidOperationException("Pijing lost its one actual random transferable HE entity.");AdvanceEventRulesAndQueueFact(new PijingGiftPaidEvent(f.Id,r.Index,donor,card.Id,from,f.OwnerSeat,batch,m.Sequence,before,m.Sequence));}));
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Pijing=f.Pijing! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new PijingMovementPaidEvent(f.Id,PijingStage.GiftChildren,r.Index,donor,before,_movementSequence,1));ContinuePijing(f.Id);
    }
    private void CompletePijingGift(ProgramSkillFrame f)
    {
        var r=f.Pijing!;var grantId=++_resolutionSequence;var donor=r.Toggles[r.Index].Seat;AdvanceEventRulesAndQueueFact(new PijingGrantIssuedEvent(grantId,r.Source,r.GameplayHash,donor,r.UseId,r.ActionId,r.ActualTurn));AdvanceEventRulesAndQueueFact(new PijingGiftCompletedEvent(f.Id,r.Index,grantId));ReplaceRuntimeTop(f=f with{Pijing=r with{Index=r.Index+1,Stage=PijingStage.Gifts,Gift=null,Before=0,After=0,Count=0},PendingMovementContinuation=null});ContinuePijing(f.Id);
    }
    private void DrawPijingReciprocal(ProgramSkillFrame f)
    {
        var r=f.Pijing!;var before=_movementSequence;ReplaceRuntimeTop(f=f with{Pijing=r with{Stage=PijingStage.DrawChildren,Before=before,After=before,Count=0},PendingMovementContinuation=new(r.ActorSeat,0,null)});var count=_players[r.ActorSeat].IsAlive?DrawCards(_players[r.ActorSeat],1,true,new(PijingDrawReason)).Count:0;f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Pijing=f.Pijing! with{After=_movementSequence,Count=count}});AdvanceEventRulesAndQueueFact(new PijingMovementPaidEvent(f.Id,PijingStage.DrawChildren,0,r.ActorSeat,before,_movementSequence,count));ContinuePijing(f.Id);
    }
    private bool DrainPijing(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinuePijing(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Pijing:{}r}f||f.Id!=id)return;AssertPijing(f);
        if(r.Stage is PijingStage.Toggles or PijingStage.Reciprocal){if(_pendingDecision is null)PublishPijing(f);return;}
        if(r.Stage==PijingStage.Gifts){if(r.Index>=r.Toggles.Count){FinishPijing(f);return;}IssuePijingGift(f);return;}
        if(DrainPijing(f))return;f=GetActiveProgramFrame(id);if(r.Stage==PijingStage.GiftChildren)CompletePijingGift(f);else FinishPijing(f);
    }
    private void FinishPijing(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new PijingCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{Pijing=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27410)]private bool ResumePijing(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Pijing:not null}f||f.Id!=id)return false;ContinuePijing(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27410)]private bool ReturnPijingMovement(ProgramSkillFrame f){if(f.Pijing is null)return false;ContinuePijing(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27410)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27410)]private bool ContinuePaidPijing(ProgramSkillFrame f){if(f.Pijing is null)return false;AssertPijing(f);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27411)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27411)]private bool ContinueIssuedPijing(ProgramSkillFrame f)=>f.Pijing is null&&f.InstructionIndex is 0 or 1&&f.TriggerId=="next-sole-use"&&f.WindowContext is{}c&&IsIssuedPijingCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),c);
    [ProgramAiChoice(SkillProgramEffectOp.ResolvePijingTargetTogglesAndRandomGifts,SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice)]
    private PromptChoice ChoosePijing(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.Pijing!;if(r.Reciprocal)return p.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="draw");var use=RequirePijingUse(f,out _);var helpful=use.CardKind is CardKind.DrawTwo or CardKind.PeachGarden or CardKind.FiveGrains;
        return p.Choices.FirstOrDefault(c=>c.Targets.Count>0&&(c.Parameters.GetValueOrDefault("branch")=="remove"?!helpful&&AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets[0]]):helpful==AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets[0]])))??p.Choices.Last();
    }
}
