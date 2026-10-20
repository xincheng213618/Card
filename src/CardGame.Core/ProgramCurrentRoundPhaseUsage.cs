using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SkillProgramPhaseUsageExpression { CurrentRound }

public sealed partial class SkillProgramActivation
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramPhaseUsageExpression? UsesPerPhaseExpression { get; internal init; }
}

[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 30, SkillProgramEffectOp.UseSelectedActorDuel)]
internal sealed class CurrentRoundDuelPhaseLimit : IProgramSelectionPhaseLimitProvider
{
    public int? GetLimit(ProgramSelectionContext context, IProgramSelectionQueries queries) =>
        context.Activation.UsesPerPhaseExpression == SkillProgramPhaseUsageExpression.CurrentRound
            ? Math.Max(0, queries.Get<IProgramSelectionPhaseLimitState>().RoundNumber)
            : context.Activation.UsesPerPhase;
}
