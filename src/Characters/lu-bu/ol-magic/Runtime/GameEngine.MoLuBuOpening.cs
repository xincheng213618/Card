namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginMoLuBuOpening()
    {
        if (!HasMoLuBu || !_setupComplete || _winner != Winner.None || _turnProgression.Kind != ActualTurnKind.Normal || _roundNumber != 0 ||
            _resolutionStack.Count != 0 || _pendingDecision is not null || _turnProgression.MoLuBuOpening is not null) return false;
        var request = ProgramEventHistory<MoLuBuInitializedEvent>().FirstOrDefault(e => _players[e.Source.OwnerSeat].IsAlive &&
            !ProgramEventHistory<MoLuBuOpeningStartedEvent>().Any(s => s.IssuerFrameId == e.FrameId));
        if (request is null) return false;
        var normal = _currentSeat; _currentSeat = request.Source.OwnerSeat;
        var receipt = new MoLuBuOpeningReceipt(request.FrameId, request.Source, request.GameplayHash, normal, _turnNumber + 1);
        _turnProgression = new(ActualTurnKind.Extra, _currentSeat, _turnNumber, NormalResumeSeat: normal) { MoLuBuOpening = receipt };
        AdvanceEventRulesAndQueueFact(new MoLuBuOpeningStartedEvent(request.FrameId, request.Source, receipt.ActualTurn, normal));
        BeginTurnAfterBeforeTurnPrelude(_players[_currentSeat]); return true;
    }
    private bool TryStartMoLuBuOpeningPlay(CharacterState owner)
    {
        if (_turnProgression.MoLuBuOpening is not { PlayStarted: false } r) return false;
        if (_turnProgression.Kind != ActualTurnKind.Extra || _turnProgression.NormalResumeSeat != r.NormalSeat || owner.Seat != r.Source.OwnerSeat || _currentSeat != owner.Seat || r.ActualTurn != _turnNumber)
            throw new InvalidOperationException("Duoqi lost its exact opening actual-turn owner and first-seat return.");
        _turnProgression = _turnProgression with { MoLuBuOpening = r with { PlayStarted = true } };
        EnterPlayPhase(owner); return true;
    }
    private bool IsMoLuBuOpeningPlay(int owner) => _turnProgression.MoLuBuOpening is { PlayStarted: true } r &&
        r.Source.OwnerSeat == owner && r.ActualTurn == _turnNumber && _turnProgression.Kind == ActualTurnKind.Extra && _turnProgression.NormalResumeSeat == r.NormalSeat;
    private bool TryFinishMoLuBuOpeningPlay()
    {
        if (!IsMoLuBuOpeningPlay(_currentSeat)) return false;
        var r = _turnProgression.MoLuBuOpening!; _turnProgression = _turnProgression with { MoLuBuOpening = null };
        ClearActualDiscardRecoveryPhase();
        AdvanceEventRulesAndQueueFact(new MoLuBuOpeningCompletedEvent(r.IssuerFrameId, _currentSeat, _turnNumber, r.NormalSeat));
        FinalizeEndTurn(_players[_currentSeat]); return true;
    }
    private bool TryEndMoLuBuOpeningEarly()
    { if (!IsMoLuBuOpeningPlay(_currentSeat)) return false; CompletePlayPhaseAfterProgramWindow(); return true; }
    private bool IsMoLuBuOpeningDelayedForbidden(int actor, CardKind kind) => IsMoLuBuOpeningPlay(actor) && IsDelayedCard(kind);

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 10)]
    private bool MoLuBuOpeningCardUseForbidden(ProgramCardUseQuery query) =>
        query.ActionType == CardActionType.Use && IsMoLuBuOpeningDelayedForbidden(query.ActorSeat, query.Kind);
}
