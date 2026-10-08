namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseSupportRecoveryTargets(
        GameSnapshot view, IReadOnlyList<PromptChoice> choices, int thoughtSequence)
    {
        if (choices.Count == 0 || choices.Any(c => c.Targets.Distinct().Count() != c.Targets.Count ||
                c.Targets.Any(s => !view.Players.Any(p => p.Seat == s && p.IsAlive))))
            throw new InvalidOperationException("AI received an invalid recovery target-set prompt.");
        var selfRole = view.Players.Single(p => p.Seat == Seat).Role ?? Role.Renegade;
        var scored = choices.Select(choice =>
        {
            var score = choice.Targets.Sum(seat =>
            {
                var target = view.Players.Single(p => p.Seat == seat);
                return target.Hp >= target.MaxHp ? 0d : GetTacticalSupport(view, selfRole, target) * 24d +
                    (seat == Seat ? 5d : 0d);
            });
            return (Choice: choice, Candidate: new AiCandidateScore(new LegalAction(
                LegalActionKind.UseProgramSkill, null, choice.Targets.Count == 0 ? null : choice.Targets[0],
                choice.Description, TargetSeats: choice.Targets), Math.Round(score, 3),
                "按公开体力和关系选择回复目标；空集合没有收益，不读取暗牌。"));
        }).ToArray();
        var selected = scored.OrderByDescending(s => s.Candidate.Score)
            .ThenBy(s => s.Choice.Targets.Count).ThenBy(s => s.Choice.Id.Value, StringComparer.Ordinal).First();
        return (selected.Choice.Id, new(thoughtSequence, view.TurnNumber, Seat, selected.Choice.Description,
            scored.Select(s => s.Candidate).OrderByDescending(c => c.Score).ToArray(), "从真实发布的零至多目标组合中选择回复目标。"));
    }
}
