namespace CardGame.Core;

public enum ProgramProvenanceAlcoholStage { FaceCost, WineUse }
public sealed record ProgramProvenanceAlcoholReceipt(int InstructionIndex, ProgramProvenanceAlcoholStage Stage, long? ChildFrameId=null);
public sealed partial class GameEngine
{
    private bool CanStartProvenanceAlcohol(CharacterState owner)=>!owner.IsFaceDown && VirtualBasicOptions(owner.Seat).Contains((CardKind.Alcohol,owner.Seat));
    private SkillProgramStepOutcome BeginProvenanceVirtualAlcohol(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.ProvenanceAlcohol is not null || f.VirtualBasicDraft is not null) throw new InvalidOperationException("A virtual Alcohol instruction cannot pay twice.");
        if(!CanStartProvenanceAlcohol(_players[f.OwnerSeat]) || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))
        { CancelProgramBindingAndCleanup(f,"当前不能翻背使用酒，未支付成本。"); return SkillProgramStepOutcome.AwaitChild; }
        ReplaceRuntimeTop(f with { ProvenanceAlcohol=new(f.InstructionIndex,ProgramProvenanceAlcoholStage.FaceCost) });
        SetProgramTargetFaceState(f.Id,f.OwnerSeat,f.OwnerSeat,true);
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeProvenanceAlcohol(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=id || f.ProvenanceAlcohol is not { } receipt) return false;
        AssertProvenanceAlcohol(f);
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id)) return true;
        if(receipt.Stage==ProgramProvenanceAlcoholStage.WineUse)
        {
            if(f.VirtualBasicDraft is not null) throw new InvalidOperationException("The virtual Wine child returned without consuming its exact typed return.");
            var completed=CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e=>e.ResolutionId==receipt.ChildFrameId && e.CardId==0 && e.CardKind==CardKind.Alcohol);
            if(!completed) throw new InvalidOperationException("A virtual Wine return has no actual zero-entity completion.");
            var finished=f with { ProvenanceAlcohol=null };
            ReplaceRuntimeTop(finished); FinishProgramSkill(finished,completed:_players[f.OwnerSeat].IsAlive); return true;
        }
        var actor=_players[f.OwnerSeat];
        if(!VirtualBasicOptions(actor.Seat).Contains((CardKind.Alcohol,actor.Seat)))
        {
            var cancelled=f with { ProvenanceAlcohol=null }; ReplaceRuntimeTop(cancelled); FinishProgramSkill(cancelled,completed:false); return true;
        }
        var useId=++_resolutionSequence;
        ReplaceRuntimeTop(f with { ProvenanceAlcohol=receipt with { Stage=ProgramProvenanceAlcoholStage.WineUse,ChildFrameId=useId },VirtualBasicDraft=new(f.InstructionIndex,useId) });
        var action=CaptureFactionAction(new CardActionContext(++_cardActionSequence,null,CardActionType.Use,actor.Seat,actor.Seat,null,null,null,CardKind.Alcohol,[actor.Seat],[],[],effectiveSuit:Suit.None,effectiveRank:0));
        PushRuntimeFrame(new CardUseFrame(useId,actor.Seat,0,CardKind.Alcohol,[actor.Seat],PhysicalCardIds:[])
        { Action=action,VirtualBasicReturn=new(f.Id,f.InstructionIndex),FirstOwnPlayUseDistanceUnlimited=HasFirstActualPlayUseDistance(actor) });
        if(TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(useId,0,CardKind.Alcohol,actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(useId,[actor.Seat]));
        RecordYingboCardUse(useId,actor.Seat,CardKind.Alcohol); RecordProgramUsedBasicCard(actor.Seat,CardKind.Alcohol); RecordActualPlayPhaseUse(action);
        BeginSimpleCardUse(useId,new(0,SimpleCardUseEffect.Alcohol)); return true;
    }
    private bool IsExactProvenanceVirtualBasicDraft(ProgramSkillFrame f,ProgramVirtualBasicDraft draft)=>f.ProvenanceAlcohol is { Stage:ProgramProvenanceAlcoholStage.WineUse } receipt && receipt.InstructionIndex==f.InstructionIndex && receipt.ChildFrameId==draft.ChildFrameId && draft.InstructionIndex==receipt.InstructionIndex;
    private void AssertProvenanceAlcohol(ProgramSkillFrame f)
    {
        if(f.ProvenanceAlcohol is not { } receipt) return;
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions;
        if(receipt.InstructionIndex!=f.InstructionIndex || plan.Count!=1 || receipt.InstructionIndex!=1 || plan[0].Op!=SkillProgramEffectOp.UseVirtualAlcohol || f.TriggerId is not null || f.SelectedCardIds.Count!=0 || f.SelectedTargetSeats.Count!=0 ||
            receipt.Stage==ProgramProvenanceAlcoholStage.FaceCost && (receipt.ChildFrameId is not null || f.VirtualBasicDraft is not null) ||
            receipt.Stage==ProgramProvenanceAlcoholStage.WineUse && receipt.ChildFrameId is null)
            throw new InvalidOperationException("A paid Wine instruction lost its exact zero-card activation receipt.");
    }
}
