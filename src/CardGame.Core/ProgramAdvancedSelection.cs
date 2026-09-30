namespace CardGame.Core;

/// <summary>Private, replayable draft for sampling and selecting content or deck cards.</summary>
public sealed record ProgramAdvancedSelection(
    SkillProgramEffectOp Operation,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> Selected,
    int RequiredRankSum = 0);
