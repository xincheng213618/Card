namespace CardGame.Core;

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnJudgedRankSlashPolicy> _judgedRankSlashPolicies = [];
    internal IReadOnlyList<TurnJudgedRankSlashPolicy> JudgedRankSlashPolicies => _judgedRankSlashPolicies;
    internal TurnJudgedRankSlashPolicy GrantJudgedRankSlashPolicy(int turn, int turnSeat, long parent,
        int effect, CardUseEffectSource source, string hash, long judgment, int card, int rank)
    {
        var existing = _judgedRankSlashPolicies.SingleOrDefault(g => g.ParentFrameId == parent && g.EffectIndex == effect);
        if (existing is not null)
        {
            if (existing != new TurnJudgedRankSlashPolicy(existing.GrantSequence, turn, turnSeat, parent, effect, source, hash, judgment, card, rank))
                throw new InvalidOperationException("One judged-rank grant key cannot change its finalized result.");
            return existing;
        }
        if (turn < 1 || turnSeat < 0 || parent <= 0 || effect != 1 || judgment <= 0 || card <= 0 || rank is < 1 or > 13)
            throw new InvalidOperationException("A judged-rank grant requires its exact actual turn and final physical judgment.");
        var granted = new TurnJudgedRankSlashPolicy(++_grantSequence, turn, turnSeat, parent, effect, source, hash, judgment, card, rank);
        _judgedRankSlashPolicies.Add(granted); return granted;
    }
    private IEnumerable<long> ExpiringJudgedRankSlashPolicies(int turn, int turnSeat) => _judgedRankSlashPolicies
        .Where(g => g.TurnNumber == turn && g.TurnSeat == turnSeat).Select(g => g.GrantSequence);
    private void ExpireJudgedRankSlashPolicies(IReadOnlySet<long> expired) =>
        _judgedRankSlashPolicies.RemoveAll(g => expired.Contains(g.GrantSequence));
    private bool JudgedRankSlashPoliciesAreInvalid() => _judgedRankSlashPolicies.Any(g => g.TurnNumber < 1 || g.TurnSeat < 0 ||
        g.ParentFrameId <= 0 || g.EffectIndex != 1 || g.JudgmentFrameId <= 0 || g.JudgmentCardId <= 0 || g.Rank is < 1 or > 13 ||
        g.Source.OwnerSeat < 0 || string.IsNullOrWhiteSpace(g.Source.SkillInstanceId) || string.IsNullOrWhiteSpace(g.GameplayHash));
}
