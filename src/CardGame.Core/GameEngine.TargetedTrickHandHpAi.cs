namespace CardGame.Core;

public sealed partial class GameEngine
{
    private double UnequalHandHpPublicChoiceScore(int owner, int target, bool draw)
    {
        var snapshot = CreateSnapshot(owner); var publicTarget = snapshot.Players[target];
        if (!publicTarget.IsAlive || publicTarget.HandCount == publicTarget.Hp || !draw && publicTarget.HandCount == 0) return double.NegativeInfinity;
        // Cancellation of DrawTwo already has public alliance/card-effect pricing.
        // One card gets half that value; equalizing also gives the owner one card.
        var cancelDraw = _aiBrains[owner].ScoreCardEffectIntervention(snapshot, CardKind.DrawTwo, target) / 2d;
        var adjustment = draw ? -cancelDraw : cancelDraw;
        var equals = publicTarget.HandCount + (draw ? 1 : -1) == publicTarget.Hp;
        return adjustment + (equals ? 8d : 0d);
    }
    private double TargetedTrickPublicChoiceScore(int owner, CardUseFrame use, PromptChoice choice)
    {
        if (choice.Targets.Count == 0) return 0d;
        var branch = choice.Parameters["branch"]; var target = choice.Targets[0];
        var score = _aiBrains[owner].ScoreCardEffectIntervention(CreateSnapshot(owner), use.CardKind, target);
        return branch == "ineffective" ? score : -score;
    }
    private double GetTargetedTrickHandHpPublicScore(int owner, IReadOnlyList<SkillProgramEffect> effects, ProgramSkillWindowContext? context)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ResolveUnequalHandHpEnding))
            return CreateSnapshot(owner).Players.Where(p => p.IsAlive && p.HandCount != p.Hp)
                .SelectMany(p => new[] { UnequalHandHpPublicChoiceScore(owner, p.Seat, true), UnequalHandHpPublicChoiceScore(owner, p.Seat, false) })
                .Append(0d).Max();
        return context is not null && TryGetTargetedOrdinaryTrickUse(context, out var use, out _)
            ? TargetedTrickChoices(0, use).Select(c => TargetedTrickPublicChoiceScore(owner, use, c)).DefaultIfEmpty(0d).Max() : 0d;
    }
}
