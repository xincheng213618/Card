namespace CardGame.Core;

public enum ActualTurnKind { Normal, Extra }
/// <summary>One actual turn, its singular extra beneficiary and the saved normal seat position.</summary>
public sealed record ActualTurnProgression(ActualTurnKind Kind, int OwnerSeat, int TurnNumber,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? PendingBeneficiarySeat = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? NormalResumeSeat = null);

public sealed partial class GameEngine
{
    private void AdvanceActualTurnProgression(CharacterState previous)
    {
        var progress = _turnProgression;
        if (progress.OwnerSeat != previous.Seat || previous.Seat != _currentSeat || progress.TurnNumber != _turnNumber ||
            _phase != TurnPhase.Finished || _pendingDecision is not null || _resolutionStack.Count != 0 || (progress.Kind == ActualTurnKind.Extra) != (progress.NormalResumeSeat is not null))
            throw new InvalidOperationException("Turn advancement lost its exact actual turn or normal resume position.");
        var resume = progress.NormalResumeSeat ?? FindNextAliveSeat(previous.Seat);
        if (_winner == Winner.None && progress.PendingBeneficiarySeat is { } beneficiary && _players[beneficiary].IsAlive)
        {
            _currentSeat = beneficiary;
            _turnProgression = new(ActualTurnKind.Extra, beneficiary, _turnNumber, NormalResumeSeat: resume);
            AddLog("ExtraTurnStarted", $"{_players[beneficiary].Name} 的额外回合开始。", beneficiary);
        }
        else
        {
            _currentSeat = _players[resume].IsAlive ? resume : FindNextAliveSeat(resume);
            _turnProgression = new(ActualTurnKind.Normal, _currentSeat, _turnNumber);
        }
    }
}
