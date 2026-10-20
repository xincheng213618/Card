namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginMouGszBaojing(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouGongsunZan is not null || f.SkillId != MouGongsunZanComposition.Baojing || f.ActivationId != "adjust-range" || f.TriggerId is not null || f.InstructionIndex != 1 ||
            f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat) throw new InvalidOperationException("Baojing requires one actual own Play and another living target.");
        var r = new MouGongsunZanReceipt(MouGszSource(f), f.GameplayHash, 0, 0, 0, target, 0, 0)
        { Stage = MouGongsunZanStage.RangeChoice, Phase = phase, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouGongsunZan = r }); PublishMouGsz(f, MouGszChoices(f)); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IEnumerable<MouGongsunZanBaojingGrantedEvent> ActiveMouGszBaojing(int target) => ProgramEventHistory<MouGongsunZanBaojingGrantedEvent>()
        .Where(e => e.TargetSeat == target && !ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>().Any(p => p.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && p.Phase.ActorSeat == e.Source.OwnerSeat && p.Phase.Token > e.Phase.Token));
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 32700)]
    private void AddMouGszBaojingRange(CharacterState player, List<RuleQueryContribution> contributions)
    {
        foreach (var e in ActiveMouGszBaojing(player.Seat)) contributions.Add(new FiniteRuleQueryContribution($"issued:baojing:{e.FrameId}", SkillRuleOperation.Add, e.Amount));
    }
    // Final projection deliberately follows native finite/unlimited arithmetic:
    // 始终 is an absolute value, including a real zero during dying.
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeProjection, 32700)]
    private RuleQueryEvaluation ProjectMouGszJiaodiRange(CharacterState player, RuleQueryEvaluation ordinary)
    {
        if (HasRuntimeSkill(player, MouGongsunZanComposition.Jiaodi))
            return RuleQueryService.Evaluate(SkillRuleQuery.AttackRange, new(0, int.MaxValue), [new($"skill:{player.Seat}:{MouGongsunZanComposition.Jiaodi}:current-hp", Math.Max(0, player.Hp))], []);
        if (ordinary.Value is FiniteRuleQueryValue { Value: < 1 } && ActiveMouGszBaojing(player.Seat).Any(e => e.Amount < 0))
            return new(new FiniteRuleQueryValue(1, ordinary.Value.Contributions), ordinary.BaseTerms, ordinary.Stages);
        return ordinary;
    }
}
