namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidWuAnGuoLiyong(ProgramSkillFrame f)
    {
        if (f.WuAnGuoLiyong is not { } r || f.SkillId != "ol:liyong" || f.ActivationId != "yin-obtain-and-incoming-duel" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.Source != new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            r.ActualTurn != _turnNumber || r.PhaseInstance != _cardUseDebitPhaseInstanceId || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var actor] || actor != r.InitialActorSeat || !IsValidPlayerSeat(actor) || actor == f.OwnerSeat ||
            r.UsedSuitMask is < 0 or > 15 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !Enum.IsDefined(r.Stage) ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel ||
            ProgramEventHistory<WuAnGuoLiyongYinStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ActualTurn == r.ActualTurn &&
                e.PhaseInstance == r.PhaseInstance && e.InitialActorSeat == actor && e.UsedSuitMask == r.UsedSuitMask) != 1 ||
            ProgramEventHistory<WuAnGuoLiyongGainPaidEvent>().Count(e => e.FrameId == f.Id && e.CardId == r.ObtainedCardId && e.Before == r.Before && e.After == r.After) != 1) return false;
        var movements = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray();
        if (r.ObtainedCardId is { } card)
        {
            if (movements is not [var m] || m.CardId != card || m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat) || m.Reason.Value != LiyongGainReason ||
                (ActualTurnSuitBit(GetAttackCard(card).Suit) & r.UsedSuitMask) == 0) return false;
        }
        else if (r.Before != r.After || movements.Length != 0) return false;
        var issued = ProgramEventHistory<WuAnGuoLiyongDuelIssuedEvent>().Where(e => e.Origin.ProgramFrameId == f.Id).ToArray();
        if (r.Stage == WuAnGuoLiyongStage.GainChildren) return r.Duel is null && issued.Length == 0;
        return r.Duel is { AttackStarted: false } origin && issued is [var fact] && fact.Origin == origin && origin.ProgramFrameId == f.Id && origin.InstructionIndex == f.InstructionIndex &&
            origin.Source == r.Source && origin.GameplayHash == r.GameplayHash && origin.ActualTurn == r.ActualTurn && origin.PhaseInstance == r.PhaseInstance &&
            origin.ActorSeat == r.InitialActorSeat && origin.TargetSeat == f.OwnerSeat && origin.CardUseFrameId > f.Id;
    }
    private bool MatchesWuAnGuoLiyong(ProgramSkillFrame f, WuAnGuoLiyongDuelOrigin origin) =>
        ValidWuAnGuoLiyong(f) && f.WuAnGuoLiyong is { Stage: WuAnGuoLiyongStage.DuelIssued, Duel: { } issued } && issued == (origin with { AttackStarted = false });
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27010)]
    private void AssertWuAnGuoLiyong(ProgramSkillFrame f)
    {
        if (f.WuAnGuoLiyong is not null && !ValidWuAnGuoLiyong(f))
            throw new InvalidOperationException("Liyong lost its exact paid deck entity, frozen source/actor/actual Play or native Duel origin.");
    }
}
