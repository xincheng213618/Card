namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHappyPoxi(ProgramSkillFrame frame)
    {
        if(frame.HappyPoxi is not{}receipt)return false;
        if(frame.SkillId!="happy:poxi"||frame.ActivationId!="three-suit-discard"||frame.TriggerId is not null||frame.InstructionIndex!=2||frame.WindowContext is not null||
            frame.SelectedTargetSeats is not[var target]||target!=receipt.TargetSeat||!IsValidPlayerSeat(target)||target==frame.OwnerSeat||
            receipt.Source!=new CardConversionSource(frame.SkillId,GetProgramBindingId(frame),frame.OwnerSeat,frame.SkillInstanceId)||receipt.GameplayHash!=frame.GameplayHash||receipt.ActualTurn!=_turnNumber||
            !Enum.IsDefined(receipt.Stage)||receipt.OwnCount is <0 or >3||receipt.BatchId<=0||receipt.Before<0||receipt.After<=receipt.Before||receipt.After>_movementSequence||receipt.Cost.Count!=3||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e=>e.FrameId==frame.Id&&e.OwnerSeat==frame.OwnerSeat&&e.SkillId==frame.SkillId&&e.ActivationId==frame.ActivationId)!=1||
            ProgramEventHistory<HappyPoxiPaidEvent>().Count(e=>e==new HappyPoxiPaidEvent(frame.Id,receipt.Source,receipt.GameplayHash,target,receipt.OwnCount,receipt.ActualTurn,receipt.BatchId,receipt.Before,receipt.After))!=1)return false;
        var binding=GetProgramCardSet(frame,"three-suit-hands");
        if(!binding.CardIds.SequenceEqual(receipt.Cost.Select(c=>c.CardId))||!binding.SourceLocations.SequenceEqual(receipt.Cost.Select(c=>c.From))||
            binding.CardIds.Distinct().Count()!=3||receipt.Cost.Count(c=>c.From==CardLocation.Hand(frame.OwnerSeat))!=receipt.OwnCount||
            receipt.Cost.Any(c=>c.From.Zone!=CardZoneKind.Hand||c.From.OwnerSeat!=frame.OwnerSeat&&c.From.OwnerSeat!=target||c.From==c.To||c.Sequence<=receipt.Before||c.Sequence>receipt.After||
                _cardMovements.Count(actual=>actual.Sequence==c.Sequence&&actual.CardId==c.CardId&&actual.From==c.From&&actual.To==c.To&&actual.Reason.Value==HappyPoxiReason)!=1))return false;
        if(receipt.Stage==HappyPoxiStage.MaximumChildren)
            return receipt.OwnCount==0&&ProgramEventHistory<HappyPoxiBenefitIssuedEvent>().Count(e=>e==new HappyPoxiBenefitIssuedEvent(frame.Id,0,receipt.MaximumBefore,receipt.MaximumAfter))==1;
        if(receipt.Stage==HappyPoxiStage.RecoveryChildren&&receipt.OwnCount!=2||receipt.Stage is HappyPoxiStage.Drawing or HappyPoxiStage.DrawChildren&&receipt.OwnCount is not(2 or 3))return false;
        if(receipt.Stage is HappyPoxiStage.RecoveryChildren or HappyPoxiStage.Drawing or HappyPoxiStage.DrawChildren&&receipt.OwnCount==2)
            if(ProgramEventHistory<HappyPoxiBenefitIssuedEvent>().Count(e=>e.FrameId==frame.Id&&e.OwnCount==2)!=1)return false;
        return receipt.Stage!=HappyPoxiStage.DrawChildren||receipt.DrawBefore>=receipt.After&&receipt.DrawAfter>=receipt.DrawBefore&&receipt.DrawAfter<=_movementSequence&&
            ProgramEventHistory<HappyPoxiDrawReturnedEvent>().Count(e=>e==new HappyPoxiDrawReturnedEvent(frame.Id,receipt.OwnCount==2?1:3,receipt.DrawCount,receipt.DrawBefore,receipt.DrawAfter))==1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,41680)] private void AssertHappyPoxi(ProgramSkillFrame frame)
    {
        if(frame.HappyPoxi is null)return;
        if(!ValidHappyPoxi(frame)||frame.HappyPoxi.Cost is not System.Collections.IList{IsReadOnly:true})
            throw new InvalidOperationException("Happy Poxi lost its original source, three actual hand discards, paid share or native outcome cursor.");
        if(frame.PendingMovementContinuation is{}pending&&!HappyPoxiMovement(frame,GetPausedPrivateOfferEffect(frame),pending))
            throw new InvalidOperationException("Happy Poxi lost its exact paid movement or health continuation.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost,41680)] private bool HappyPoxiMovement(ProgramSkillFrame frame,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>
        effect?.Op==SkillProgramEffectOp.ResolveHappyGanNingPoxi&&pending==frame.PendingMovementContinuation&&pending.SubjectSeat==frame.OwnerSeat&&pending.BeforeCount==0&&pending.CoverageResultBind is null&&
        frame.HappyPoxi is{Stage:HappyPoxiStage.DiscardChildren or HappyPoxiStage.MaximumChildren or HappyPoxiStage.RecoveryChildren or HappyPoxiStage.DrawChildren}&&ValidHappyPoxi(frame);
}
