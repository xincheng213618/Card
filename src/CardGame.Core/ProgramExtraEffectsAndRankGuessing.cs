using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ExtraEffectsAndRankGuessStage { Cost, Guess, DeathTarget, DeathSkill, MovementChildren, SkillChildren, GainChildren }
public sealed record ExtraEffectsAndRankGuessReceipt
{
    private IReadOnlyList<int> _cards = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public SkillProgramEffectOp Operation { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long WindowFrameId { get; init; }
    public long? CardUseFrameId { get; init; }
    public long? ActionId { get; init; }
    public int InstructionIndex { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public ExtraEffectsAndRankGuessStage Stage { get; init; }
    public bool GuessFinished { get; init; }
    public int? TargetSeat { get; init; }
    public int FrozenOwnerMaxHp { get; init; }
    public int FrozenRecipientMaxHp { get; init; }
    public bool GainToHand { get; init; }
    public int? PaidCardId { get; init; }
    public string? GrantedSkillId { get; init; }
    public int LastRank { get; init; }
    public int GuessIndex { get; init; }
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public IReadOnlyList<int> RevealedCardIds { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record ExtraEffectsAndRankGuessOfferedEvent(long ProgramFrameId, ExtraEffectsAndRankGuessReceipt Receipt) : IGameEvent;
public sealed record ExtraEffectsAndRankGuessPaidEvent(long ProgramFrameId, ExtraEffectsAndRankGuessReceipt Receipt) : IGameEvent;
public sealed record RankGuessComparedEvent(long ProgramFrameId, int PreviousRank, int NewRank, bool GuessedGreater, bool Correct, int GuessIndex) : IGameEvent;
public sealed record RankGuessNormalDrawResetEvent(long ProgramFrameId, long WindowFrameId, int FrozenBaseDrawCount, int PreviousPlannedCount) : IGameEvent;
public sealed record ExtraCardEffectIssuedEvent(long ProgramFrameId, CardConversionSource Source, long CardUseFrameId, long ActionId, int PaidCardId) : IGameEvent;
public sealed record ExtraCardEffectStartedEvent(long CardUseFrameId, long ActionId, int TargetIndex, int TargetSeat, CardKind EffectiveKind) : IGameEvent;
public sealed record DeathRecipientSkillChosenEvent(long ProgramFrameId, CardConversionSource Source, int RecipientSeat, string SkillId) : IGameEvent;
public sealed record DeathRecipientSelectedEvent(long ProgramFrameId, CardConversionSource Source, int RecipientSeat, int OwnerMaxHp, int RecipientMaxHp) : IGameEvent;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ExtraEffectsAndRankGuessReceipt? ExtraEffectsAndRankGuess { get; init; } }
public sealed record ExtraCardEffectState
{
    public CardConversionSource Source { get; init; } = null!;
    public long ProgramFrameId { get; init; }
    public long ActionId { get; init; }
    public int PaidCardId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomaticGameplayHash { get; init; }
    public ProgramSimpleCardContinuation? Simple { get; init; }
    public ProgramTrickContinuation? Trick { get; init; }
    public bool WholeGroupRepeated { get; init; }
    private IReadOnlyList<ExtraEffectAttackSeed> _attacks = [];
    public IReadOnlyList<ExtraEffectAttackSeed> OriginalAttacks { get => _attacks; init => _attacks = Array.AsReadOnly(value.ToArray()); }
    public FangtianHalberdState? Fangtian { get; init; }
    public int? FrozenGroupDamageSourceSeat { get; init; }
    private IReadOnlyList<ExtraEffectMaterialClaim> _materialClaims = [];
    public IReadOnlyList<ExtraEffectMaterialClaim> MaterialClaims { get => _materialClaims; init => _materialClaims = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ExtraEffectAttackSeed(int TargetIndex, CardAttackState Attack);
public sealed record ExtraEffectMaterialClaim(long CardUseFrameId, long ActionId, int CardId, long ProgramFrameId,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, int EffectIndex, int RecipientSeat,
    long DamageWindowFrameId, long DamageFrameId, long MovementSequence, string MovementReason);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ExtraCardEffectState? ExtraCardEffect { get; init; } }
public sealed record ExtraForeignCardSlot(int Slot, int? PublicCardId, CardLocation? PublicLocation);
public sealed record ExtraForeignCardSelection(long ActionId, int HandCount, IReadOnlyList<ExtraForeignCardSlot> Slots)
{
    public bool AllowFinish { get; init; }
}
public sealed partial record TargetCardSelectionFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ExtraForeignCardSelection? ExtraForeignSelection { get; init; } }
internal interface IExtraEffectsAndRankGuessHost
{ SkillProgramStepOutcome BeginExtraEffectsAndRankGuess(SkillProgramEffect effect, ProgramSkillFrame frame); }
internal abstract class ExtraEffectsAndRankGuessDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new ExtraEffectsAndRankGuessHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(6));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        var grant = Op == SkillProgramEffectOp.DeathRecipientChooseSkill;
        if (grant) reader.AllowOnly("op", "target", "skillIds", "condition");
        else reader.AllowOnly("op", "target", "condition");
        var ids = grant ? reader.RequiredIdentifierArray("skillIds") : [];
        if (grant && (ids.Count != 2 || ids.Distinct().Count() != 2))
            throw new InvalidOperationException($"{reader.Path}: recipient skill choice requires two distinct skills.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(), skillIds: ids);
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.DiscardSameCategoryForExtraEffect ? SkillProgramTriggerWindow.CardUseCommitted :
            Op == SkillProgramEffectOp.GuessRanksForDrawReplacement ? SkillProgramTriggerWindow.DrawPhaseStarting : SkillProgramTriggerWindow.OwnerDied)];
}
internal sealed class DiscardSameCategoryForExtraEffectDescriptor : ExtraEffectsAndRankGuessDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSameCategoryForExtraEffect; }
internal sealed class GuessRanksForDrawReplacementDescriptor : ExtraEffectsAndRankGuessDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuessRanksForDrawReplacement;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 3, effect.Condition)));
    // A bounded unknown-deck prior; the common composition estimate separately
    // deducts the actual planned normal draw. Never inspect the unrevealed deck.
}
internal sealed class DeathRecipientChooseSkillDescriptor : ExtraEffectsAndRankGuessDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DeathRecipientChooseSkill;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.WindowContext?.Window == SkillProgramTriggerWindow.OwnerDied;
}
internal sealed class ExtraEffectsAndRankGuessHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IExtraEffectsAndRankGuessHost>().BeginExtraEffectsAndRankGuess(effect, frame);
}
internal static class ExtraEffectsAndRankGuessContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op is SkillProgramEffectOp.DiscardSameCategoryForExtraEffect or
            SkillProgramEffectOp.GuessRanksForDrawReplacement or SkillProgramEffectOp.DeathRecipientChooseSkill)) return;
        if (trigger.Effects is not [var effect] || !trigger.Optional || trigger.IncludeResponseUses ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 ||
            trigger.ChoiceGroup is not null || trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null)
            throw new InvalidOperationException($"{path}: extra effect, rank prediction and death grant each require one exact optional trigger.");
        var valid = effect.Op switch
        {
            SkillProgramEffectOp.DiscardSameCategoryForExtraEffect => trigger.Window == SkillProgramTriggerWindow.CardUseCommitted &&
                trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && trigger.UsageScope == SkillUsageScope.Phase && trigger.UsageLimit == 1,
            SkillProgramEffectOp.GuessRanksForDrawReplacement => trigger.Window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement && trigger.UsageScope is null,
            SkillProgramEffectOp.DeathRecipientChooseSkill => trigger.Window == SkillProgramTriggerWindow.OwnerDied &&
                trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.UsageScope is null,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"{path}: operation does not match its usage and actual trigger boundary.");
    }
}
