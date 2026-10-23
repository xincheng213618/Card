namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyDingzhouReason="skill-program.happy-shen-lu-su.";
    [ProgramCapability] private sealed class HappyLuSuCapability(GameEngine engine):IHappyLuSuHost
    {
        public SkillProgramStepOutcome BeginHappyDingzhou(ProgramSkillFrame frame)=>engine.BeginHappyDingzhou(frame);
    }
    private SkillProgramStepOutcome BeginHappyDingzhou(ProgramSkillFrame supplied)
    {
        var frame=GetActiveProgramFrame(supplied.Id);
        if(frame.HappyDingzhou is not null||frame.InstructionIndex!=1||frame.SelectedTargetSeats is not[var target]||
            target==frame.OwnerSeat||!IsValidPlayerSeat(target)||!_players[target].IsAlive||!_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Happy Dingzhou requires its original once-per-Play other participant.");
        var receipt=new HappyDingzhouReceipt(ShenLsSource(frame),frame.GameplayHash,target,HappyDingzhouStage.BranchChoice)
            {Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(frame=frame with{HappyDingzhou=receipt});
        AdvanceEventRulesAndQueueFact(new HappyDingzhouStartedEvent(frame.Id,receipt.Source,receipt.GameplayHash,target));
        ContinueHappyDingzhou(frame.Id);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ContinueHappyDingzhou(long id)
    {
        while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyDingzhou:{}receipt}frame&&frame.Id==id)
        {
            AssertHappyDingzhou(frame);
            if(DrainShenLs(id))return;
            frame=GetActiveProgramFrame(id);receipt=frame.HappyDingzhou!;
            if(frame.PendingMovementContinuation is not null)
            {
                ReplaceRuntimeTop(frame=frame with{PendingMovementContinuation=null});
            }
            if(_winner!=Winner.None||!_players[frame.OwnerSeat].IsAlive||!_players[receipt.TargetSeat].IsAlive)
            {FinishHappyDingzhou(frame);return;}
            if(receipt.Stage==HappyDingzhouStage.FieldGiftChildren)
            {
                var cards=ShenLsField(receipt.TargetSeat);
                if(cards.Length==0){FinishHappyDingzhou(frame);return;}
                ReplaceRuntimeTop(frame=frame with{HappyDingzhou=receipt with{Obtained=cards}});
                MoveHappyDingzhou(frame,cards,HappyDingzhouStage.FieldGainChildren,CardLocation.Hand(frame.OwnerSeat));continue;
            }
            if(receipt.Stage is HappyDingzhouStage.FieldGainChildren or HappyDingzhouStage.HandReturnChildren)
            {FinishHappyDingzhou(frame);return;}
            if(receipt.Stage==HappyDingzhouStage.HandGainChildren)
            {
                var payable=Math.Min(receipt.Amount,ShenLsGiveable(frame.OwnerSeat).Length);
                receipt=receipt with{Stage=HappyDingzhouStage.HandReturnChoice,RequiredReturnCount=payable};
                ReplaceRuntimeTop(frame=frame with{HappyDingzhou=receipt});
                AdvanceEventRulesAndQueueFact(new HappyDingzhouReturnRequiredEvent(frame.Id,receipt.Amount,payable));
            }
            if(receipt.Stage is HappyDingzhouStage.FieldGiftChoice or HappyDingzhouStage.HandReturnChoice)
            {
                var required=receipt.Stage==HappyDingzhouStage.HandReturnChoice?receipt.RequiredReturnCount!.Value:receipt.Amount;
                if(receipt.Chosen.Count==required)
                {
                    if(required==0){FinishHappyDingzhou(frame);return;}
                    if(receipt.Chosen.Any(card=>!ShenLsGiveable(frame.OwnerSeat).Contains(card)))
                        throw new InvalidOperationException("Happy Dingzhou lost a currently payable selected physical card.");
                    MoveHappyDingzhou(frame,receipt.Chosen,receipt.Stage==HappyDingzhouStage.FieldGiftChoice?
                        HappyDingzhouStage.FieldGiftChildren:HappyDingzhouStage.HandReturnChildren,CardLocation.Hand(receipt.TargetSeat));continue;
                }
            }
            PublishHappyDingzhou(frame);return;
        }
    }
    private void MoveHappyDingzhou(ProgramSkillFrame frame,IReadOnlyList<int> cards,HappyDingzhouStage stage,CardLocation destination)
    {
        if(cards.Count==0)throw new InvalidOperationException("Happy Dingzhou cannot issue an empty paid movement.");
        var before=_movementSequence;
        ReplaceRuntimeTop(frame=frame with{HappyDingzhou=frame.HappyDingzhou! with
            {Stage=stage,Paid=false,Before=before,After=before,BatchId=0,Invoices=[]},PendingMovementContinuation=new(frame.OwnerSeat,0,null)});
        void Paid(long batch,IReadOnlyList<CardMovementRecord> moves)
        {
            var parent=GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(parent with{HappyDingzhou=parent.HappyDingzhou! with
                {Paid=true,BatchId=batch,After=moves[^1].Sequence,Invoices=moves.Select(m=>new ShenLuSuMoveInvoice(m.CardId,m.From,m.To,m.Sequence)).ToArray()}});
            AdvanceEventRulesAndQueueFact(new HappyDingzhouMovementPaidEvent(frame.Id,stage,batch,moves.Count,before,moves[^1].Sequence));
        }
        MoveProgramCardsFromMultipleSources(cards,destination,new(HappyDingzhouReason+stage),
            stage is HappyDingzhouStage.FieldGiftChildren or HappyDingzhouStage.HandReturnChildren?
                NativeGiveCompletionRecords(frame.Id,frame.OwnerSeat,destination.OwnerSeat!.Value,cards,before,Paid):Paid,
            disclosure:stage==HappyDingzhouStage.FieldGainChildren?CardMovementDisclosure.FaceUp:CardMovementDisclosure.FaceDown);
    }
    private void FinishHappyDingzhou(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame with{HappyDingzhou=null,PendingMovementContinuation=null});
        AdvanceRuntimeProgram(frame.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,41280)] private bool ResumeHappyDingzhou(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyDingzhou:not null}frame||frame.Id!=id)return false;
        ContinueHappyDingzhou(id);return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,41280)] private bool ReturnHappyDingzhouMovement(ProgramSkillFrame frame)=>
        frame.HappyDingzhou is not null&&ResumeHappyDingzhou(frame.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,41280)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,41280)] private bool KeepHappyDingzhouPaid(ProgramSkillFrame frame)=>frame.HappyDingzhou is not null;
}
