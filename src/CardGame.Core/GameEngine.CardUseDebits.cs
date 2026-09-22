namespace CardGame.Core;

public sealed record CardUseDebitRecordedEvent(CardUseDebitIdentity Debit) : IGameEvent;
public sealed record CardUseDebitRefundedEvent(CardUseDebitIdentity Debit) : IGameEvent;

public sealed partial class GameEngine
{
    private int _cardUseDebitPhaseInstanceId;
    private readonly Dictionary<long, CardUseDebitIdentity> _cardUseDebits = [];
    private readonly HashSet<long> _refundedCardUseDebits = [];

    private void ResetCardUseDebitPhase()
    {
        _cardUseDebitPhaseInstanceId = checked(_cardUseDebitPhaseInstanceId + 1);
        _cardUseDebits.Clear();
        _refundedCardUseDebits.Clear();
    }

    private void RecordSlashUseDebit(long cardUseFrameId, int actorSeat)
    {
        var action = _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == cardUseFrameId).Action ??
            throw new InvalidOperationException("A counted Slash has no frozen card-action identity.");
        var identity = new CardUseDebitIdentity(action.ActionId, actorSeat, SkillRuleQuery.SlashLimit,
            _turnNumber, _phase, _cardUseDebitPhaseInstanceId);
        if (!_cardUseDebits.TryAdd(action.ActionId, identity))
            throw new InvalidOperationException("A card action cannot debit its Slash quota twice.");
        _slashCountThisTurn++;
        QueueGameEvent(new CardUseDebitRecordedEvent(identity));
    }

    private bool TryRefundCardUseDebit(CardUseDebitIdentity identity)
    {
        if (!_cardUseDebits.TryGetValue(identity.CardActionId, out var recorded) || recorded != identity)
            return false;
        if (!_refundedCardUseDebits.Add(identity.CardActionId)) return false;
        if (_slashCountThisTurn <= 0)
            throw new InvalidOperationException("A recorded Slash debit cannot be refunded from an empty quota.");
        _slashCountThisTurn--;
        QueueGameEvent(new CardUseDebitRefundedEvent(identity));
        return true;
    }

    private CardUseDebitIdentity? GetCardUseDebit(long actionId) =>
        _cardUseDebits.GetValueOrDefault(actionId);

    private bool IsCardUseDebitActive(long actionId) =>
        _cardUseDebits.ContainsKey(actionId) && !_refundedCardUseDebits.Contains(actionId);

    private void RefundProgramCardUseDebit(ProgramSkillFrame frame)
    {
        var context = GetActiveProgramFrame(frame.Id).WindowContext?.CardUse ??
            throw new InvalidOperationException("Card-use refund requires a frozen card-action context.");
        if (!context.WasUsageDebited || context.DebitIdentity is not { } identity) return;
        _ = TryRefundCardUseDebit(identity);
    }
}
