namespace CardGame.Core;

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnHandCategoryRestriction> _handCategoryRestrictions = [];
    internal IReadOnlyList<TurnHandCategoryRestriction> HandCategoryRestrictions => Array.AsReadOnly(_handCategoryRestrictions.ToArray());
    internal TurnHandCategoryRestriction GrantHandCategoryRestriction(int turn, int seat, long programId,
        long damageWindowId, CardUseEffectSource source, int affected, SkillProgramCardCategory category)
    {
        var prior = _handCategoryRestrictions.SingleOrDefault(p => p.ProducerProgramFrameId == programId);
        if (prior is not null)
        {
            if (prior.TurnNumber != turn || prior.TurnSeat != seat || prior.Source != source || prior.AffectedSeat != affected ||
                prior.Category != category || prior.DamageWindowFrameId != damageWindowId)
                throw new InvalidOperationException("A hand-category declaration cannot change its committed source/category.");
            return prior;
        }
        if (turn < 1 || affected < 0 || !Enum.IsDefined(category)) throw new InvalidOperationException("Invalid current-turn hand category restriction.");
        var policy = new TurnHandCategoryRestriction(++_grantSequence, turn, seat, programId, damageWindowId, source, affected, category);
        _handCategoryRestrictions.Add(policy); return policy;
    }
    internal bool IsHandCategoryRestricted(int turn, int turnSeat, int affected, SkillProgramCardCategory category) =>
        _handCategoryRestrictions.Any(p => p.TurnNumber == turn && p.TurnSeat == turnSeat && p.AffectedSeat == affected && p.Category == category);
    private IEnumerable<long> ExpiringHandCategoryRestrictions(int turn, int seat) => _handCategoryRestrictions
        .Where(p => p.TurnNumber == turn && p.TurnSeat == seat).Select(p => p.GrantSequence);
    private void ExpireHandCategoryRestrictions(IReadOnlySet<long> ids) => _handCategoryRestrictions.RemoveAll(p => ids.Contains(p.GrantSequence));
    private bool HandCategoryRestrictionsAreInvalid() => _handCategoryRestrictions.Any(p => p.TurnNumber < 1 ||
        p.TurnSeat < 0 || p.AffectedSeat < 0 || p.ProducerProgramFrameId < 1 || p.DamageWindowFrameId < 1 || !Enum.IsDefined(p.Category));
}
