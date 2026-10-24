namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ShiZhHuiCharge(int owner) =>
        _players[owner].Markers.GetValueOrDefault(PlayerMarkerKind.ShiZhHuiCharge);
    private int ShiZhHuiOverflowCount(int seat) => !HasShiZhHuiCapability ? 0 :
        ProgramEventHistory<ShiZhHuiXiezhiOverflowEvent>().Count(entry => entry.OwnerSeat == seat);

    // Charge is a public player marker; the raw overflow above the capacity is a
    // separate ledger fact because the marker itself stays clamped.
    private int ChangeShiZhHuiCharge(long frameId, int owner, int amount, string reason, out int excess)
    {
        var before = ShiZhHuiCharge(owner);
        var raw = checked(before + amount);
        excess = Math.Max(0, raw - ShiZhHuiComposition.ChargeCapacity);
        var after = Math.Clamp(raw, 0, ShiZhHuiComposition.ChargeCapacity);
        if (before != after)
        {
            var player = _players[owner];
            var key = (PlayerMarkerKind.ShiZhHuiCharge, owner);
            if (after == 0)
            { player.Markers.Remove(PlayerMarkerKind.ShiZhHuiCharge); player.MarkerSourceCounts.Remove(key); }
            else
            {
                player.Markers[PlayerMarkerKind.ShiZhHuiCharge] = after;
                player.MarkerSourceCounts[key] = after;
            }
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frameId, owner,
                PlayerMarkerKind.ShiZhHuiCharge, after - before, after, owner, reason));
        }
        return after;
    }

    // Observes completed engine facts only: slash uses per turn, deaths per round
    // and the owner's own turn start that expires a running sizi effect.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 67700)]
    private void ObserveShiZhHuiTurnFacts(IGameEvent payload)
    {
        if (!HasShiZhHuiCapability || _winner != Winner.None) return;
        switch (payload)
        {
            case CardUsedEvent { CardKind: var kind, SourceSeat: var actor } when
                IsSlashCard(kind) && _turnNumber > 0 && IsValidPlayerSeat(actor):
                AdvanceEventRulesAndQueueFact(new ShiZhHuiSlashUsedEvent(_turnNumber, _currentSeat, actor));
                break;
            case PlayerDiedEvent { VictimSeat: var victim }:
                AdvanceEventRulesAndQueueFact(new ShiZhHuiDeathRecordedEvent(_roundNumber, victim));
                break;
            case TurnStartedEvent { ActorSeat: var seat } when _players[seat].IsAlive &&
                ShiZhHuiActiveSizi is { } activation && activation.Source.OwnerSeat == seat:
                AdvanceEventRulesAndQueueFact(new ShiZhHuiSiziExpiredEvent(activation.Ordinal, "owner-turn-start"));
                break;
        }
    }

    private int ShiZhHuiKechangLevelForResponses(int sourceSeat) => HasShiZhHuiKechang(sourceSeat) &&
        _players[sourceSeat].IsAlive ? ShiZhHuiKechangLevel(sourceSeat) : 0;

    private bool ShiZhHuiKeChangCannotRespond(int sourceSeat, int responderSeat, CardKind incomingKind,
        IReadOnlyList<int> targetSeats) =>
        IsSlashCard(incomingKind) && sourceSeat != responderSeat && targetSeats.Contains(responderSeat) &&
        ShiZhHuiKechangLevelForResponses(sourceSeat) >= 2;

    private void ApplyShiZhHuiFactionChange(ProgramSkillFrame frame, string faction)
    {
        if (!TracksNativeEffectiveFactionChanges || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            !AvailableEffectiveFactionIds().Contains(faction, StringComparer.Ordinal))
            throw new InvalidOperationException("Yunan requires its living native program owner and supported faction.");
        SynchronizeNativeEffectiveFactions();
        var player = _players[frame.OwnerSeat];
        var previous = GetEffectiveFactionId(player);
        player.NativeEffectiveFactionOverrideId = faction;
        player.NativeEffectiveFactionWasDeclared = true;
        _nativeFactionObservations[player.Seat] = new(faction, CaptureNativeFactionAvatar(player));
        if (string.Equals(previous, faction, StringComparison.Ordinal)) return;
        _nativeFactionRevision++;
        if (_setupComplete && _turnNumber > 0)
            AdvanceEventRulesAndQueueFact(new NativeEffectiveFactionChangedEvent(++_resolutionSequence,
                _turnNumber, _turnProgression.OwnerSeat, player.Seat, previous, faction, frame.Id,
                new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), "yunan"));
        AddLog("FactionChanged", $"{player.Name} 将势力变更为{GetFactionName(faction)}。", player.Seat);
    }
}
