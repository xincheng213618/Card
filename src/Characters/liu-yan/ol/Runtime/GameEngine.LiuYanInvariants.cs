namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidLiuYan(ProgramSkillFrame f)
    {
        if(f.LiuYan is not{}r||!HasLiuYanCapability||r.Source!=LiuYanSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=1||f.InstructionIndex!=1||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||
           f.PendingMovementContinuation is not null||!Enum.IsDefined(r.Stage)||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Targets.Count==0||r.Targets.Distinct().Count()!=r.Targets.Count||
           ProgramEventHistory<LiuYanStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var start]||start!=new LiuYanStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.Operation,r.ParentWindowId,r.OriginalUseId,r.ActionId,string.Join(',',r.Targets)))return false;
        if(r.Operation==SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets)
        {
            if(r.Stage!=LiuYanStage.TusheDrawChildren||f.SkillId!="ol:tushe"||f.TriggerId!="actual-targets"||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||r.Use is not null||r.PaymentSequence is not null||r.RecoveryIssued||f.WindowContext is not{}context||context.ParentFrameId!=r.ParentWindowId||context.CardUse?.ParentCardUseFrameId!=r.OriginalUseId||context.CardUse.CardActionId!=r.ActionId||
               _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentWindowId)is not{}parent||GetCardActionWindow(parent)!=SkillProgramTriggerWindow.CardUseTargetsFinalized||
               parent.CandidateIndex>=parent.Candidates.Count||!MountObserverCandidateMatches(f,ToSharedCandidate(parent.Candidates[parent.CandidateIndex]))||
               LifecycleCardUse(r.OriginalUseId)is not{Action:{Type:CardActionType.Use}a}||a.ActionId!=r.ActionId||a.ActorSeat!=f.OwnerSeat||CardCatalog.Get(a.EffectiveKind).CategoryName=="装备牌")return false;
            var draws=ProgramEventHistory<LiuYanTusheDrawnEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            return draws is[var d]&&d.OwnerSeat==f.OwnerSeat&&d.Requested==r.Targets.Count&&d.Count==r.DrawCount&&d.Before==r.Before&&d.After==r.After&&r.DrawCount>=0&&r.DrawCount<=r.Targets.Count&&
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==LiuYanDrawReason)==r.DrawCount;
        }
        if(r.Operation!=SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover||f.SkillId!="ol:limu"||f.TriggerId is not null||f.ActivationId!="self-indulgence"||f.WindowContext is not null||r.Use is not{}o||r.Stage==LiuYanStage.TusheDrawChildren||
           f.SelectedCardIds is not[var card]||card!=o.CardId||f.SelectedTargetSeats.Count!=0||o.ProgramFrameId!=f.Id||o.Source!=r.Source||o.GameplayHash!=r.GameplayHash||o.ActorSeat!=f.OwnerSeat||o.ActualTurn!=r.ActualTurn||o.PhaseInstance!=_cardUseDebitPhaseInstanceId||o.Suit!=Suit.Diamond||
           o.From.OwnerSeat!=f.OwnerSeat||o.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)||r.OriginalUseId!=o.CardUseFrameId||r.ActionId!=o.ActionId||!r.Targets.SequenceEqual([f.OwnerSeat])||
           ProgramEventHistory<LiuYanLimuIssuedEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray()is not[var issue]||issue.Origin!=o)return false;
        var pays=ProgramEventHistory<LiuYanLimuPaidEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray();
        if(r.PaymentSequence is{}sequence)
        {if(pays is not[var pay]||pay.Origin!=o||pay.Sequence!=sequence||sequence<=r.Before||sequence>r.After||!_cardMovements.Any(m=>m.Sequence==sequence&&m.CardId==o.CardId&&m.CardKind==o.PrintedKind&&m.From==o.From&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use))return false;}
        else if(pays.Length!=0||r.Stage!=LiuYanStage.LimuNative||_cardZones.GetLocation(o.CardId)!=o.From)return false;
        var returned=ProgramEventHistory<LiuYanLimuReturnedEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray();
        if(r.Stage==LiuYanStage.LimuNative?returned.Length!=0:returned is not[var ret]||ret.Origin!=o)return false;
        var recovery=ProgramEventHistory<LiuYanLimuRecoveryIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        return r.RecoveryIssued?r.Stage==LiuYanStage.LimuRecoveryChildren&&recovery is[var rec]&&rec.OwnerSeat==f.OwnerSeat&&rec.Amount==1:recovery.Length==0;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27230)]private void AssertLiuYan(ProgramSkillFrame f)
    {if(f.LiuYan is not null&&!ValidLiuYan(f))throw new InvalidOperationException("Liu Yan lost his actual target count, exact original physical payment, source generation or once-only native recovery return.");}
}
