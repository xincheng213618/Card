namespace CardGame.Core;

public sealed partial class GameEngine
{

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 100)]
    private int ZhangYiKangruiDamageBonus(IDamageAttempt attack)
    {
        if (!HasZhangYiCapability || attack.IsSourceLess) return 0;
        var grants = ProgramEventHistory<ZhangYiKangruiArmedEvent>().Where(g => g.ActualTurnNumber == _turnNumber && g.RecipientSeat == attack.SourceSeat && g.DamageBonus &&
            !ProgramEventHistory<ZhangYiKangruiDamageConsumedEvent>().Any(e => e.GrantFrameId == g.FrameId)).ToArray();
        foreach (var grant in grants) AdvanceEventRulesAndQueueFact(new ZhangYiKangruiDamageConsumedEvent(grant.FrameId, attack.ResolutionId, attack.SourceSeat,
            attack.TargetSeat, _turnNumber, attack.IsChainPropagation, attack.IsChainPropagation ? ZhangYiChainCursor(attack) : 0));
        return grants.Length;
    }
    private int ZhangYiChainCursor(IDamageAttempt attack) => attack is ProgramAttackHandle
        ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
    private void SetZhangYiChainAmount(IDamageAttempt attack, int amount)
    {
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || !attack.IsChainPropagation || amount < 0)
            throw new InvalidOperationException("Kangrui can only restore its exact native chained damage instance.");
        if (attack is ProgramAttackHandle) UpdateProgramAttackState(attack.ResolutionId, s => s with { DamageAmount = amount });
        else UpdateCardAttackState(attack.ResolutionId, s => s! with { DamageAmount = amount });
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 60)]
    private void PrepareZhangYiChainAdvance(IDamageAttempt attack)
    {
        if (!HasZhangYiCapability || !attack.IsChainPropagation || attack.IsSourceLess) return;
        var cursor = ZhangYiChainCursor(attack);
        var remaining = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats : GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        if (remaining is null || !remaining.Skip(cursor).Any(seat => _players[seat].IsAlive)) return;
        var issued = ProgramEventHistory<ZhangYiChainInstanceIssuedEvent>().SingleOrDefault(e => e.AttackFrameId == attack.ResolutionId && e.ChainCursor == cursor);
        if (issued is null || !ProgramEventHistory<ZhangYiKangruiDamageConsumedEvent>().Any(e => e.AttackFrameId == attack.ResolutionId && e.ChainPropagation && e.ChainCursor == cursor)) return;
        if (ProgramEventHistory<ZhangYiChainInstanceRebasedEvent>().Any(e => e.AttackFrameId == attack.ResolutionId && e.ChainCursor == cursor)) return;
        SetZhangYiChainAmount(attack, issued.InheritedAmount);
        AdvanceEventRulesAndQueueFact(new ZhangYiChainInstanceRebasedEvent(attack.ResolutionId, cursor, attack.TargetSeat, issued.InheritedAmount));
    }
    [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 10)]
    private void ApplyZhangYiChainedDamageBonus(IDamageAttempt attack)
    {
        if (!HasZhangYiCapability || !attack.IsChainPropagation || attack.IsSourceLess ||
            !ProgramEventHistory<ZhangYiKangruiArmedEvent>().Any(g => g.ActualTurnNumber == _turnNumber && g.RecipientSeat == attack.SourceSeat && g.DamageBonus &&
                !ProgramEventHistory<ZhangYiKangruiDamageConsumedEvent>().Any(e => e.GrantFrameId == g.FrameId))) return;
        var cursor = ZhangYiChainCursor(attack);
        if (ProgramEventHistory<ZhangYiChainInstanceIssuedEvent>().Any(e => e.AttackFrameId == attack.ResolutionId && e.ChainCursor == cursor))
            throw new InvalidOperationException("A chained Kangrui instance cannot issue its increment twice.");
        AdvanceEventRulesAndQueueFact(new ZhangYiChainInstanceIssuedEvent(attack.ResolutionId, cursor, attack.TargetSeat, attack.DamageAmount));
        // Formal armor recomputes this recipient in the ordinary finalizer.
        // Legacy already-finalized propagation gets only the newly armed bonus.
        if (attack.DamageAmountFinalized) SetZhangYiChainAmount(attack, checked(attack.DamageAmount + ZhangYiKangruiDamageBonus(attack)));
    }
    private RuleQueryEvaluation ApplyZhangYiKangruiHandLimit(CharacterState player, RuleQueryEvaluation ordinary)
    {
        if (!HasZhangYiCapability || !ProgramEventHistory<ZhangYiKangruiHandLimitSetEvent>().Any(e => e.ActualTurnNumber == _turnNumber && e.RecipientSeat == player.Seat)) return ordinary;
        var set = new FiniteRuleQueryContribution($"turn:{_turnNumber}:zhang-yi:kangrui:{player.Seat}", SkillRuleOperation.Set, 0, 200);
        var frozen = Array.AsReadOnly(ordinary.Value.Contributions.Append(set).OrderBy(c => c.SourceId, StringComparer.Ordinal).ToArray());
        var stage = new RuleQueryStageSnapshot("kangrui-hand-limit-zero", ordinary.Value is FiniteRuleQueryValue finite ? finite.Value : 0, false, 0, Array.AsReadOnly<RuleQueryContribution>([set]));
        return new(new FiniteRuleQueryValue(0, frozen), ordinary.BaseTerms, Array.AsReadOnly(ordinary.Stages.Append(stage).ToArray()));
    }
}
