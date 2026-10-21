namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int WushuangExtraSkillCapacity(int seat) => 2 + (HasWushuangTactic(seat, 2293) ? 1 : 0);

    private static bool IsWushuangOriginalGrant(SkillGrant grant) => grant.SourceId is
        CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource or WushuangOriginalReplacementSource;

    private IEnumerable<string> WushuangReplaceableOriginalSkills(int seat) => HasWushuangTactic(seat, 2221)
        ? _players[seat].SkillGrants.Grants.Where(g => g.IsEnabled && IsWushuangOriginalGrant(g)).Select(g => g.SkillId).Distinct(StringComparer.Ordinal)
        : [];

    private void ChangeWushuangFreeRerolls(int seat, int delta, string reason)
    {
        var progress = _wushuangPlayers[seat];
        var count = checked(progress.FreeRerolls + delta);
        if (count < 0) throw new InvalidOperationException("Wushuang free refreshes cannot be negative.");
        progress.FreeRerolls = count;
        AdvanceEventRulesAndQueueFact(new WushuangFreeRerollsChangedEvent(seat, count, reason));
    }

    private bool HasWushuangTactic(int seat, int id) => IsWushuangMode &&
        _wushuangPlayers.TryGetValue(seat, out var progress) && progress.Tactics.Contains(id);

    private bool IsWushuangHandLimitExempt(CharacterState owner, Card card) =>
        card.Kind == CardKind.Dodge && HasWushuangTactic(owner.Seat, 2002) ||
        card.Kind == CardKind.Peach && HasWushuangTactic(owner.Seat, 2003);

    private IEnumerable<RuleQueryContribution> WushuangNumericRuleContributions(CharacterState owner, SkillRuleQuery query)
    {
        if (!IsWushuangMode || !_wushuangPlayers.TryGetValue(owner.Seat, out var progress)) yield break;
        foreach (var id in progress.Tactics)
        {
            var amount = (id, query) switch
            {
                (2153, SkillRuleQuery.DrawCount) when _roundNumber >= 3 => 1,
                (2154, SkillRuleQuery.DrawCount) when _roundNumber >= 5 => 1,
                (2155, SkillRuleQuery.DrawCount) when _roundNumber >= 7 => 1,
                (2156, SkillRuleQuery.SlashLimit) when _roundNumber >= 3 => 1,
                (2157, SkillRuleQuery.SlashLimit) when _roundNumber >= 4 => 1,
                (2158, SkillRuleQuery.SlashLimit) when _roundNumber >= 7 => 1,
                (2164, SkillRuleQuery.DrawCount) when owner.Hp <= 3 => 1,
                (2165, SkillRuleQuery.DrawCount) when owner.Hp <= 5 => 1,
                (2282, SkillRuleQuery.SlashLimit) when progress.Tokens >= 3 => 1,
                (2283, SkillRuleQuery.DrawCount) when progress.Tokens >= 7 => 1,
                (2284, SkillRuleQuery.HandLimit) when progress.Tokens >= 3 => 1,
                (2226, SkillRuleQuery.SlashLimit) => 2 * Math.Max(0, WushuangExtraSkillCapacity(owner.Seat) -
                    owner.SkillGrants.Grants.Count(g => g.SourceId == WushuangGrantSource)),
                _ => 0
            };
            if (amount != 0) yield return new FiniteRuleQueryContribution($"wushuang:{owner.Seat}:{id}:{query}", SkillRuleOperation.Add, amount);
        }
    }

    private RuleQueryEvaluation ApplyWushuangEnemyQuota(CharacterState player, RuleQueryEvaluation ordinary, SkillRuleQuery query)
    {
        var tactic = query == SkillRuleQuery.DrawCount ? 2298 : 2299;
        if (!IsWushuangMode || !_players.Any(p => p.IsAlive && p.Seat != player.Seat && HasWushuangTactic(p.Seat, tactic)) ||
            ordinary.Value is not FiniteRuleQueryValue finite) return ordinary;
        var value = Math.Min(finite.Value, Math.Max(query == SkillRuleQuery.DrawCount ? 2 : 1, finite.Value - 1));
        var contribution = new FiniteRuleQueryContribution($"wushuang:{player.Seat}:enemy:{tactic}", SkillRuleOperation.Add, value - finite.Value);
        return new(new FiniteRuleQueryValue(value, Array.AsReadOnly(ordinary.Value.Contributions.Append(contribution).ToArray())),
            ordinary.BaseTerms, Array.AsReadOnly(ordinary.Stages.Append(new RuleQueryStageSnapshot("wushuang-enemy-quota",
                finite.Value, false, value, Array.AsReadOnly<RuleQueryContribution>([contribution]))).ToArray()));
    }

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 61001)]
    private int WushuangSourceDamageBonus(IDamageAttempt attack)
    {
        if (!IsWushuangMode || attack.IsSourceLess || attack.IsChainPropagation ||
            !_wushuangPlayers.TryGetValue(attack.SourceSeat, out var source)) return 0;
        var hp = _players[attack.SourceSeat].Hp;
        var bonus = (source.Tactics.Contains(2162) && hp <= 2 ? 1 : 0) +
            (source.Tactics.Contains(2163) && hp <= 3 ? 1 : 0);
        if (attack.CardUserSeat == attack.SourceSeat && attack.EffectiveCardKind is { } kind && IsSlashCard(kind))
            bonus += (source.Tactics.Contains(2150) && _roundNumber >= 3 ? 1 : 0) +
                (source.Tactics.Contains(2151) && _roundNumber >= 5 ? 1 : 0) +
                (source.Tactics.Contains(2152) && _roundNumber >= 7 ? 1 : 0);
        return bonus;
    }
}
