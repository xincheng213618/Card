namespace CardGame.Core;

public sealed record TurnTargetCardQuotaAllowance(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, int TargetSeat, long DamageWindowId);
public sealed record TurnTargetCardQuotaAllowanceGrantedEvent(TurnTargetCardQuotaAllowance Allowance) : IGameEvent;

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnTargetCardQuotaAllowance> _targetCardQuotaAllowances = [];
    internal IReadOnlyList<TurnTargetCardQuotaAllowance> TargetCardQuotaAllowances =>
        Array.AsReadOnly(_targetCardQuotaAllowances.ToArray());
    internal TurnTargetCardQuotaAllowance GrantTargetCardQuotaAllowance(int turn, int turnSeat, long parent,
        int effect, CardUseEffectSource source, int targetSeat, long damageWindowId)
    {
        if (turn < 1 || turnSeat < 0 || parent < 1 || effect < 0 || source.OwnerSeat < 0 || targetSeat < 0 || damageWindowId < 1 ||
            string.IsNullOrWhiteSpace(source.SkillId) || string.IsNullOrWhiteSpace(source.BindingId) || string.IsNullOrWhiteSpace(source.SkillInstanceId))
            throw new InvalidOperationException("A target card quota allowance requires an exact original source and actual turn.");
        var old = _targetCardQuotaAllowances.SingleOrDefault(g => g.ParentFrameId == parent && g.EffectIndex == effect);
        if (old is not null)
        {
            if (old.TurnNumber != turn || old.TurnSeat != turnSeat || old.Source != source ||
                old.TargetSeat != targetSeat || old.DamageWindowId != damageWindowId)
                throw new InvalidOperationException("A target quota issuance changed its original meaning.");
            return old;
        }
        var issued = new TurnTargetCardQuotaAllowance(++_grantSequence, turn, turnSeat, parent, effect, source, targetSeat, damageWindowId);
        _targetCardQuotaAllowances.Add(issued); return issued;
    }
    internal bool HasTargetCardQuotaAllowance(int turn, int turnSeat, int actor, int target) =>
        _targetCardQuotaAllowances.Any(g => g.TurnNumber == turn && g.TurnSeat == turnSeat &&
            g.Source.OwnerSeat == actor && g.TargetSeat == target);
    internal IEnumerable<long> ExpiringTargetCardQuotaAllowances(int turn, int turnSeat) =>
        _targetCardQuotaAllowances.Where(g => g.TurnNumber == turn && g.TurnSeat == turnSeat).Select(g => g.GrantSequence);
    internal void ExpireTargetCardQuotaAllowances(IReadOnlySet<long> ids) =>
        _targetCardQuotaAllowances.RemoveAll(g => ids.Contains(g.GrantSequence));
}

public sealed partial class GameEngine
{
    private bool HasTargetCardQuotaAllowance(int actor, int target) =>
        _turnCardUseEffects.HasTargetCardQuotaAllowance(_turnNumber, _currentSeat, actor, target);
}
