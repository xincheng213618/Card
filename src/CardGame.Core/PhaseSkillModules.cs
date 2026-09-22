using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>Engine-owned boundaries; skill names never define the continuation.</summary>
public enum PhaseSkillWindow { PlayEnding, TurnEnding, PlayStarting }

/// <summary>A frozen query result, without engine references or hidden opponent data.</summary>
public sealed record PhaseSkillContext(
    int OwnerSeat, int ActorSeat, int TurnNumber, TurnPhase Phase,
    int Hp, int MaxHp, int HandCount, int CardsUsedThisTurn, bool IsClassicIdentityMode,
    int PindianOpponentCount = 0);

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
[JsonDerivedType(typeof(BeginSkillPindian), "pindian")]
[JsonDerivedType(typeof(GrantNextCardTargetAdjustment), "grant-next-card-target-adjustment")]
[JsonDerivedType(typeof(ForbidCardUseUntilTurnEnd), "forbid-card-use-until-turn-end")]
public abstract record SkillModuleEffect;

public sealed record DrawSkillCards(int Count, CardMoveReason Reason, bool LogDraw = false) : SkillModuleEffect;

/// <summary>
/// Bounded activation at a phase boundary. A Pindian child may pause execution;
/// arbitrary nested damage/selection is not implied.
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
            Effects.Count(effect => effect is BeginSkillPindian) > 1)
            throw new InvalidOperationException($"Invalid phase skill activation for '{skillId}'.");

        var hasPriorPindian = false;
        foreach (var effect in Effects)
        {
            var valid = effect switch
            {
                DrawSkillCards { Count: > 0 and <= 100 } => true,
                BeginSkillPindian => true,
                GrantNextCardTargetAdjustment adjustment =>
                    CardUseCategoryCatalog.IsValid(adjustment.Categories) &&
                    adjustment.MinimumTargets >= 1 &&
                    (adjustment.AllowAdd || adjustment.AllowRemove) &&
                    Enum.IsDefined(adjustment.When) &&
                    (adjustment.When == SkillEffectCondition.Always || hasPriorPindian),
                ForbidCardUseUntilTurnEnd prohibition =>
                    CardUseCategoryCatalog.IsValid(prohibition.Categories) &&
                    Enum.IsDefined(prohibition.When) &&
                    (prohibition.When == SkillEffectCondition.Always || hasPriorPindian),
                _ => false
            };
            if (!valid)
                throw new InvalidOperationException($"Invalid phase skill activation for '{skillId}'.");
            hasPriorPindian |= effect is BeginSkillPindian;
        }

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

public sealed record SkillActivationResult(bool Used, IReadOnlyList<int> DrawnCardIds, PindianResult? Pindian = null);

/// <summary>Data-only activation state; never retains a module, callback or UI object.</summary>
public sealed record PhaseSkillFrame(
    long Id, string SkillId, string BindingId, string SkillInstanceId,
    PhaseSkillWindow Window, PhaseSkillContext Context, SkillActivationPlan Plan,
    int InstructionIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.AwaitingResponse,
    IReadOnlyList<int>? DrawnCardIds = null, PindianResult? Pindian = null)
    : ResolutionFrame(Id, ResolutionFrameKind.PhaseSkill, Step);

public sealed record SkillModuleResolvedEvent(long FrameId, string SkillId, int OwnerSeat, bool Used) : IGameEvent;
