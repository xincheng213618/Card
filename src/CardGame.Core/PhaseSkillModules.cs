using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>Engine-owned boundaries; skill names never define the continuation.</summary>
public enum PhaseSkillWindow { PlayEnding, TurnEnding }

/// <summary>A frozen query result, without engine references or hidden opponent data.</summary>
public sealed record PhaseSkillContext(
    int OwnerSeat, int ActorSeat, int TurnNumber, TurnPhase Phase,
    int Hp, int MaxHp, int HandCount, int CardsUsedThisTurn, bool IsClassicIdentityMode);

/// <summary>
/// A trusted, stateless content module. It describes a bounded activation; only
/// the host may execute its effects. Revision is part of the content fingerprint.
/// </summary>
public interface IPhaseSkillModule
{
    string SkillId { get; }
    int Revision { get; }
    PhaseSkillWindow Window { get; }
    SkillActivationPlan? CreatePlan(PhaseSkillContext context);
    IGameEvent? CreateResolvedEvent(PhaseSkillContext context, SkillActivationResult result) => null;
}

/// <summary>Semantic UI metadata; UI consumers do not dispatch on a skill name.</summary>
public sealed record SkillPromptPresentation(string SkillId, string Name, string Title, string Instructions);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$effect")]
[JsonDerivedType(typeof(DrawSkillCards), "draw")]
public abstract record SkillModuleEffect;

public sealed record DrawSkillCards(int Count, CardMoveReason Reason, bool LogDraw = false) : SkillModuleEffect;

/// <summary>
/// This first module contract supports a finite optional activation and draw
/// effects at a clean phase boundary. Nested damage/selection is not implied.
/// LegacyDecisionKind and choice data only preserve existing client projections.
/// </summary>
public sealed record SkillActivationPlan(
    SkillPromptPresentation Presentation,
    string Prompt,
    PromptChoice Activate,
    PromptChoice Skip,
    IReadOnlyList<SkillModuleEffect> Effects,
    bool AiPrefersActivation = true,
    DecisionKind LegacyDecisionKind = DecisionKind.SkillModule)
{
    internal SkillActivationPlan Freeze(string skillId)
    {
        if (Presentation.SkillId != skillId || string.IsNullOrWhiteSpace(Presentation.Name) ||
            string.IsNullOrWhiteSpace(Prompt) || Activate.Id == Skip.Id ||
            Activate.Cards.Count != 0 || Skip.Cards.Count != 0 ||
            Activate.Targets.Count != 0 || Skip.Targets.Count != 0 ||
            Activate.ContentIds.Count != 0 || Skip.ContentIds.Count != 0 ||
            Effects.Count is < 1 or > 16 ||
            Effects.Any(effect => effect is not DrawSkillCards { Count: > 0 and <= 100 }))
            throw new InvalidOperationException($"Invalid phase skill activation for '{skillId}'.");

        static PromptChoice FreezeChoice(PromptChoice choice) => choice with
        {
            Cards = Array.Empty<int>(),
            Targets = Array.Empty<int>(),
            ContentIds = Array.Empty<string>(),
            Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(choice.Parameters))
        };
        return this with
        {
            Activate = FreezeChoice(Activate), Skip = FreezeChoice(Skip),
            Effects = Array.AsReadOnly(Effects.ToArray())
        };
    }
}

public sealed record SkillActivationResult(bool Used, IReadOnlyList<int> DrawnCardIds);

/// <summary>Data-only activation state; never retains a module, callback or UI object.</summary>
public sealed record PhaseSkillFrame(
    long Id, string SkillId, string BindingId, string SkillInstanceId,
    PhaseSkillWindow Window, PhaseSkillContext Context, SkillActivationPlan Plan,
    int InstructionIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse)
    : ResolutionFrame(Id, ResolutionFrameKind.PhaseSkill, Step);

public sealed record SkillModuleResolvedEvent(long FrameId, string SkillId, int OwnerSeat, bool Used) : IGameEvent;
