namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyPoxiReason="skill-program.happy:poxi.discard",HappyPoxiDrawReason="skill-program.happy:poxi.draw";
    [ProgramCapability] private sealed class HappyPoxiCapability(GameEngine engine):IHappyPoxiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,string bind)=>engine.BeginHappyPoxi(frame,bind); }
    private SkillProgramStepOutcome BeginHappyPoxi(ProgramSkillFrame supplied,string bind)
    {
        var frame=GetActiveProgramFrame(supplied.Id);var cards=GetProgramCardSet(frame,bind);
        if(cards.CardIds.Count==0)return SkillProgramStepOutcome.Continue;
        if(frame.HappyPoxi is not null||frame.InstructionIndex!=2||frame.SelectedTargetSeats is not[var target]||
            cards.CardIds.Count!=3||cards.CardIds.Distinct().Count()!=3||cards.SourceLocations.Count!=3||
            cards.SourceLocations.Any(from=>from.Zone!=CardZoneKind.Hand||from.OwnerSeat!=frame.OwnerSeat&&from.OwnerSeat!=target)||
            cards.CardIds.Where((id,index)=>_cardZones.GetLocation(id)!=cards.SourceLocations[index]).Any()||
            cards.CardIds.Select(id=>GetAttackCard(id).Suit).Distinct().Count()!=3)
            throw new InvalidOperationException("Happy Poxi lost its three different-suit original hand entities and fixed other participant.");
        if(cards.CardIds.Select((id,index)=>(Card:GetAttackCard(id),From:cards.SourceLocations[index])).Any(item=>
            IsSelfHandCategoryDiscardForbidden(frame.OwnerSeat,item.Card,item.From,OwnedCardMoveIntent.Discard)||
            IsForeignEquipmentDiscardPrevented(frame.OwnerSeat,item.Card,item.From,OwnedCardMoveIntent.Discard)))return SkillProgramStepOutcome.Continue;
        var receipt=new HappyPoxiReceipt(new(frame.SkillId,GetProgramBindingId(frame),frame.OwnerSeat,frame.SkillInstanceId),frame.GameplayHash,target,
            cards.SourceLocations.Count(from=>from.OwnerSeat==frame.OwnerSeat),_turnNumber,HappyPoxiStage.DiscardChildren){Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(frame=frame with{HappyPoxi=receipt,PendingMovementContinuation=new(frame.OwnerSeat,0,null)});
        MoveProgramLogicalDiscard(frame,frame.OwnerSeat,cards.CardIds,new(HappyPoxiReason),(batch,moves)=>
        {
            var current=GetActiveProgramFrame(frame.Id);
            var paid=receipt with{BatchId=batch,After=moves[^1].Sequence,Cost=moves.Select(m=>new ShenLuSuMoveInvoice(m.CardId,m.From,m.To,m.Sequence)).ToArray()};
            ReplaceRuntimeTop(current with{HappyPoxi=paid});
            AdvanceEventRulesAndQueueFact(new HappyPoxiPaidEvent(frame.Id,paid.Source,paid.GameplayHash,target,paid.OwnCount,paid.ActualTurn,batch,paid.Before,paid.After));
        });
        ContinueHappyPoxi(frame.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinueHappyPoxi(long id)
    {
        while(_pendingDecision is null&&_resolutionStack.LastOrDefault() is ProgramSkillFrame{HappyPoxi:{}receipt}frame&&frame.Id==id)
        {
            AssertHappyPoxi(frame);
            if(receipt.Stage==HappyPoxiStage.Drawing)return;
            if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||
                TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(id)||
                TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id))return;
            frame=GetActiveProgramFrame(id);receipt=frame.HappyPoxi!;
            if(frame.PendingMovementContinuation is not null)ReplaceRuntimeTop(frame=frame with{PendingMovementContinuation=null});
            if(_winner!=Winner.None||!_players[frame.OwnerSeat].IsAlive||receipt.Stage is HappyPoxiStage.MaximumChildren or HappyPoxiStage.DrawChildren)
            {FinishHappyPoxi(frame);return;}
            if(receipt.Stage==HappyPoxiStage.RecoveryChildren||receipt.Stage==HappyPoxiStage.DiscardChildren&&receipt.OwnCount==3)
            {
                ReplaceRuntimeTop(frame=frame with{HappyPoxi=receipt with{Stage=HappyPoxiStage.Drawing}});
                BeginOperationProgramDraw(frame,frame.OwnerSeat,receipt.OwnCount==2?1:3,new(HappyPoxiDrawReason));return;
            }
            if(receipt.Stage!=HappyPoxiStage.DiscardChildren)throw new InvalidOperationException("Happy Poxi cannot repeat a paid outcome stage.");
            var owner=_players[frame.OwnerSeat];
            if(receipt.OwnCount==1){FinishHappyPoxi(frame);return;}
            if(receipt.OwnCount==0)
            {
                var before=owner.MaxHp;var hp=owner.Hp;var after=HasWushuangTactic(owner.Seat,2020)?7:Math.Max(0,before-1);
                ReplaceRuntimeTop(frame=frame with{HappyPoxi=receipt with{Stage=HappyPoxiStage.MaximumChildren,MaximumBefore=before,MaximumAfter=after},PendingMovementContinuation=new(frame.OwnerSeat,0,null)});
                owner.MaxHp=after;owner.Hp=Math.Min(hp,after);
                AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat,after-before,after,frame.SkillId){HpBeforeClamp=hp});
                AdvanceEventRulesAndQueueFact(new HappyPoxiBenefitIssuedEvent(frame.Id,receipt.OwnCount,before,after));
                if(after==0&&owner.IsAlive)BeginPlayerDeath(frame.Id,owner,null,null,null,causingProgramSkillFrameId:frame.Id);
                continue;
            }
            ReplaceRuntimeTop(frame=frame with{HappyPoxi=receipt with{Stage=HappyPoxiStage.RecoveryChildren},PendingMovementContinuation=new(frame.OwnerSeat,0,null)});
            AdvanceEventRulesAndQueueFact(new HappyPoxiBenefitIssuedEvent(frame.Id,receipt.OwnCount,owner.MaxHp,owner.MaxHp));
            new ProgramSkillHost(this).Recover(frame.Id,frame.OwnerSeat,frame.OwnerSeat,1,null,null);
        }
    }
    private void FinishHappyPoxi(ProgramSkillFrame frame)
    {ReplaceRuntimeTop(frame with{HappyPoxi=null,PendingMovementContinuation=null});AdvanceRuntimeProgram(frame.Id);}
    [ProgramContinuation(ProgramContinuationKind.Resume,41680)] private bool ResumeHappyPoxi(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyPoxi:not null}frame||frame.Id!=id)return false;
        ContinueHappyPoxi(id);return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,41680)] private bool ReturnHappyPoxiMovement(ProgramSkillFrame frame)=>frame.HappyPoxi is not null&&ResumeHappyPoxi(frame.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveHappyGanNingPoxi)] private void ReturnHappyPoxiDraw(ProgramSkillFrame supplied,NativeDrawOutcome outcome)
    {
        var frame=GetActiveProgramFrame(supplied.Id);var receipt=frame.HappyPoxi??throw new InvalidOperationException("Happy Poxi lost its native reward draw parent.");
        if(receipt.Stage!=HappyPoxiStage.Drawing||outcome.RecipientSeat!=frame.OwnerSeat||outcome.RequestedCount!=(receipt.OwnCount==2?1:3)||
            outcome.SequenceAfter!=_movementSequence||ProgramEventHistory<HappyPoxiDrawReturnedEvent>().Any(e=>e.FrameId==frame.Id))
            throw new InvalidOperationException("Happy Poxi cannot change or repeat its already-issued native draw reward.");
        ReplaceRuntimeTop(frame with{HappyPoxi=receipt with{Stage=HappyPoxiStage.DrawChildren,DrawBefore=outcome.SequenceBefore,DrawAfter=outcome.SequenceAfter,DrawCount=outcome.ActualDrawIds.Count},PendingMovementContinuation=new(frame.OwnerSeat,0,null)});
        AdvanceEventRulesAndQueueFact(new HappyPoxiDrawReturnedEvent(frame.Id,outcome.RequestedCount,outcome.ActualDrawIds.Count,outcome.SequenceBefore,outcome.SequenceAfter));ContinueHappyPoxi(frame.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,41680)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,41680)] private bool KeepHappyPoxiPaid(ProgramSkillFrame frame)=>frame.HappyPoxi is not null;
}
