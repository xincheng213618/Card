namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    /// <summary>
    /// Estimates a validated draw-phase composition from node contributions.
    /// This is a bounded heuristic, not a simulation: unknown revealed cards
    /// use a uniform suit prior, and conditions use the current visible context.
    /// No deck order or other player's private cards are inspected.
    /// normalDrawCount includes prior adjustments, before the final zero clamp.
    /// </summary>
    public (bool Activate, AiThoughtRecord Thought) ChooseDrawPhaseProgramActivation(
        GameSnapshot view,
        SkillProgramTrigger trigger,
        PlayerSkillContext conditionContext,
        int normalDrawCount,
        string skillName,
        int thoughtSequence)
    {
        if (trigger.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
            conditionContext.Seat != Seat)
            throw new InvalidOperationException("Draw-phase AI requires the acting owner's draw-phase context.");

        const double cardValue = 15d;
        var self = view.Players.Single(player => player.Seat == Seat);
        var missingHp = (double)Math.Max(0, self.MaxHp - self.Hp);
        var recoveryValue = self.Hp <= 1 ? 80d : 24d;
        var bindings = new Dictionary<string, IReadOnlyDictionary<Suit, double>>(StringComparer.Ordinal);
        var damageByKind = new Dictionary<CardKind, int>();
        SkillProgramTriggerEffect? selection = null;
        var drawAdjustment = 0;
        var benefit = 0d;
        var explicitCards = 0d;
        var expectedRecovery = 0d;
        var handTransferValue = 0d;
        var policyValue = 0d;
        var visibleSlashCount = self.Hand.Count(card => card.Kind is
            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);

        foreach (var effect in trigger.Effects)
        {
            // Execution re-evaluates each condition against real state. This
            // preview deliberately does not invent the identities of future draws.
            if (!effect.Condition.Evaluate(conditionContext)) continue;
            switch (effect.Op)
            {
                case SkillProgramTriggerEffectOp.Draw:
                    explicitCards += effect.NumberExpression == SkillProgramNumberExpression.LivingFactionCount
                        ? view.Players.Where(player => player.IsAlive && player.FactionId is not null)
                            .Select(player => player.FactionId).Distinct(StringComparer.Ordinal).Count()
                        : effect.Amount;
                    break;
                case SkillProgramTriggerEffectOp.AdjustNormalDraw:
                    drawAdjustment = checked(drawAdjustment + effect.Amount);
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardDamageModifier:
                    foreach (var kind in effect.CardKinds)
                        damageByKind[kind] = checked(damageByKind.GetValueOrDefault(kind) + effect.Amount);
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardActionProhibition:
                {
                    var matching = self.Hand.Count(card => effect.CardKinds.Contains(card.Kind));
                    policyValue -= matching * (effect.ActionTypes.Contains(CardActionType.Use) ? 22d : 0d);
                    policyValue -= matching * (effect.ActionTypes.Contains(CardActionType.Response) ? 8d : 0d);
                    break;
                }
                case SkillProgramTriggerEffectOp.GrantTurnRuleModifier:
                    if (effect.RuleQuery == SkillRuleQuery.SlashLimit &&
                        effect.RuleOperation == SkillRuleOperation.Add && visibleSlashCount >= 2)
                        policyValue += 35d * effect.Amount;
                    else if (effect.RuleQuery == SkillRuleQuery.SlashDistanceLimit &&
                             effect.RuleOperation == SkillRuleOperation.Unlimited)
                        policyValue += 5d * visibleSlashCount;
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardTargetRestriction:
                {
                    var externallyTargeted = self.Hand.Count(card => card.Kind is
                        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
                        CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
                        CardKind.Dismantlement or CardKind.Snatch or CardKind.FireAttack or
                        CardKind.PeachGarden or CardKind.FiveGrains or CardKind.BorrowedSword);
                    policyValue -= Math.Min(45d, externallyTargeted * 10d);
                    break;
                }
                case SkillProgramTriggerEffectOp.StartJudgment:
                    bindings.Add(effect.ResultBind!, new Dictionary<Suit, double>
                    {
                        [Suit.Spade] = 0.25d,
                        [Suit.Heart] = 0.25d,
                        [Suit.Club] = 0.25d,
                        [Suit.Diamond] = 0.25d
                    });
                    break;
                case SkillProgramTriggerEffectOp.GrantTurnCardConversion:
                {
                    var source = bindings[effect.SourceBind!];
                    var total = source.Values.Sum();
                    var sourceRedProbability = total <= 0d
                        ? 0.5d
                        : (source.GetValueOrDefault(Suit.Heart) +
                           source.GetValueOrDefault(Suit.Diamond)) / total;
                    foreach (var card in self.Hand.Where(card => card.Kind != CardKind.Duel))
                    {
                        var inputIsRed = card.Suit is Suit.Heart or Suit.Diamond;
                        var oppositeProbability = inputIsRed
                            ? 1d - sourceRedProbability
                            : sourceRedProbability;
                        policyValue += 32d * oppositeProbability;
                    }
                    break;
                }
                case SkillProgramTriggerEffectOp.RevealTopCards:
                {
                    var count = effect.NumberExpression == SkillProgramNumberExpression.OwnerLostHp
                        ? missingHp
                        : effect.Amount;
                    bindings.Add(effect.ResultBind!, new Dictionary<Suit, double>
                    {
                        [Suit.Spade] = count / 4d,
                        [Suit.Heart] = count / 4d,
                        [Suit.Club] = count / 4d,
                        [Suit.Diamond] = count / 4d
                    });
                    break;
                }
                case SkillProgramTriggerEffectOp.FilterBoundCards:
                    bindings.Add(effect.ResultBind!, bindings[effect.SourceBind!]
                        .Where(pair => effect.Suits.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value));
                    break;
                case SkillProgramTriggerEffectOp.MoveBoundCards:
                    if (effect.Destination == SkillProgramCardDestination.OwnerHand)
                    {
                        var source = bindings[effect.SourceBind!];
                        var except = effect.ExceptBind is { } exceptName ? bindings[exceptName] : null;
                        explicitCards += source.Sum(pair =>
                            Math.Max(0d, pair.Value - (except?.GetValueOrDefault(pair.Key) ?? 0d)));
                    }
                    break;
                case SkillProgramTriggerEffectOp.Recover:
                {
                    var amount = effect.NumberExpression == SkillProgramNumberExpression.BoundCardCount
                        ? bindings[effect.SourceBind!].Values.Sum()
                        : effect.Amount;
                    var restored = Math.Min(missingHp, amount);
                    missingHp -= restored;
                    expectedRecovery += restored;
                    break;
                }
                case SkillProgramTriggerEffectOp.SelectTargets:
                    selection = effect;
                    break;
                case SkillProgramTriggerEffectOp.TakeRandomHandCardFromSelectedTargets:
                    handTransferValue += EstimateProgramHandTransfer(view, selection ??
                        throw new InvalidOperationException("A validated transfer lost its target selection."));
                    break;
                default:
                    throw new InvalidOperationException($"Draw-phase AI does not support '{effect.Op}'.");
            }
        }

        // Aggregate bonuses by kind before valuing visible usable cards. A
        // bounded three-card estimate prevents large hands from dominating.
        var damageValue = self.Hand
            .Select(card => damageByKind.GetValueOrDefault(card.Kind))
            .Where(amount => amount > 0)
            .OrderDescending()
            .Take(3)
            .Sum(amount => 22d * amount);
        benefit += explicitCards * cardValue + expectedRecovery * recoveryValue + handTransferValue + damageValue +
                   policyValue;
        var skipScore = Math.Max(0d, normalDrawCount) * cardValue;
        var additive = trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive;
        var activateScore = benefit + (additive
            ? Math.Max(0d, (double)normalDrawCount + drawAdjustment) * cardValue
            : 0d);
        var activate = additive ? activateScore >= skipScore : activateScore > skipScore;
        var activateAction = new LegalAction(
            LegalActionKind.UseProgramSkill, null, null, $"发动【{skillName}】");
        var skipAction = new LegalAction(
            LegalActionKind.UseProgramSkill, null, null, $"跳过【{skillName}】");
        var candidates = new[]
        {
            new AiCandidateScore(activateAction, Math.Round(activateScore, 3),
                $"节点估值：直接得牌约 {explicitCards:0.##}，回复约 {expectedRecovery:0.##}，" +
                $"取牌收益 {handTransferValue:0.##}，可见伤害牌收益 {damageValue:0.##}，" +
                $"回合政策净值 {policyValue:0.##}。"),
            new AiCandidateScore(skipAction, Math.Round(skipScore, 3),
                $"保留当前普通摸牌 {Math.Max(0, normalDrawCount)} 张；不查看牌堆顺序或他人暗牌。")
        };
        return (activate, new AiThoughtRecord(
            thoughtSequence, view.TurnNumber, Seat,
            activate ? activateAction.Description : skipAction.Description,
            candidates.OrderByDescending(candidate => candidate.Score).ToArray(),
            $"组合摸牌启发式：发动 {activateScore:0.##}，跳过 {skipScore:0.##}；" +
            "未知亮牌按四种花色等概率估计，效果条件按当前可见状态预估。"));
    }

    private double EstimateProgramHandTransfer(GameSnapshot view, SkillProgramTriggerEffect selection)
    {
        var targets = view.Players
            .Where(player => player.IsAlive && player.Seat != Seat && player.HandCount > 0)
            .OrderBy(player => player.Seat)
            .ToArray();
        if (selection.TargetKind != SkillProgramTargetKind.OtherLivingWithHand ||
            targets.Length < selection.MinimumTargets)
            throw new InvalidOperationException("A validated transfer has no legal public target set.");
        var groups = new List<IReadOnlyList<PlayerSnapshot>>();
        for (var first = 0; first < targets.Length; first++)
        {
            if (selection.MinimumTargets <= 1) groups.Add([targets[first]]);
            if (selection.MaximumTargets < 2) continue;
            for (var second = first + 1; second < targets.Length; second++)
                groups.Add([targets[first], targets[second]]);
        }
        var selfRole = view.Players.Single(player => player.Seat == Seat).Role ?? Role.Renegade;
        if (selection.TargetAiOrder == SkillProgramTargetAiOrder.Stable)
        {
            var stable = groups.OrderByDescending(group => group.Count)
                .ThenBy(group => string.Join("-", group.Select(player => player.Seat)), StringComparer.Ordinal)
                .First();
            return ScoreHostileHandTargets(view, selfRole, stable);
        }
        if (selection.TargetAiOrder != SkillProgramTargetAiOrder.HostileThenHandCount)
            throw new InvalidOperationException("A validated transfer lost its target AI policy.");
        return groups.Max(group => ScoreHostileHandTargets(view, selfRole, group));
    }
}
