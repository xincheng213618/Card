using System.Text.Json.Serialization;
namespace CardGame.Core;

// Trusted movement-ledger metadata, not a public per-loss marker/event or pending sidecar.
public sealed record ActualPlayPhysicalSlashLossStamp(int ActualTurnNumber, int ActualTurnOwnerSeat,
    int PhaseActorSeat, int PhaseInstanceId, long? ProducerFrameId);
public sealed partial record CardMovementRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActualPlayPhysicalSlashLossStamp? ActualPlaySlashLoss { get; init; }
}
public sealed partial class GameEngine
{
    private bool TracksActualPlaySlashLoss => _contentRegistry.ProgramDependencies.UsesTriggerValue(SkillProgramTriggerValueKind.CurrentActualPlayPhysicalSlashLossCount);
    private ActualPlayPhysicalSlashLossStamp? CaptureActualPlaySlashLoss(Card card, CardLocation from, CardLocation to)
    {
        if (!TracksActualPlaySlashLoss || _turnNumber < 1 || _phase != TurnPhase.Play || _cardUseDebitPhaseInstanceId < 1 ||
            !IsSlashCard(card.Kind) || from.OwnerSeat is not { } owner || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) ||
            to.OwnerSeat == owner) return null;
        var actor = _programPhaseSchedule is { Phase: TurnPhase.Play } schedule ? schedule.Frame.OwnerSeat : _currentSeat;
        return actor == owner ? new(_turnNumber, _turnProgression.OwnerSeat, actor, _cardUseDebitPhaseInstanceId, _resolutionStack.LastOrDefault()?.Id) : null;
    }
    private int CurrentActualPlaySlashLossCount(int owner) => !TracksActualPlaySlashLoss || _phase != TurnPhase.Play || owner != _currentSeat ? 0 :
        _cardMovements.Count(m => m.ActualPlaySlashLoss is { } stamp && stamp.ActualTurnNumber == _turnNumber && stamp.ActualTurnOwnerSeat == _turnProgression.OwnerSeat &&
            stamp.PhaseActorSeat == owner && stamp.PhaseInstanceId == _cardUseDebitPhaseInstanceId && m.TurnNumber == stamp.ActualTurnNumber &&
            m.From.OwnerSeat == owner && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment &&
            m.To.OwnerSeat != owner && IsSlashCard(m.CardKind));
}
