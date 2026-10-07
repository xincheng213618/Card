using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ActualHandGainProgramStage { Choosing, MovementChildren }
public sealed record ActualHandGainEntity(int CardId, long GainSequence);
public sealed record ActualHandGainProgramReceipt
{
    private IReadOnlyList<ActualHandGainEntity> _gains = Array.Empty<ActualHandGainEntity>();
    private IReadOnlyList<int> _paid = Array.Empty<int>();
    public int InstructionIndex { get; init; }
    public SkillProgramEffectOp Operation { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long OriginalParentId { get; init; }
    public int ActualTurn { get; init; }
    public int ActualTurnOwner { get; init; }
    public long OriginalBatchId { get; init; }
    public long OriginalCardUseFrameId { get; init; }
    public long OriginalActionId { get; init; }
    public SkillProgramCardCategory? Category { get; init; }
    public ActualHandGainProgramStage Stage { get; init; }
    public int RecipientSeat { get; init; } = -1;
    public IReadOnlyList<ActualHandGainEntity> Gains { get => _gains; init => _gains = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<int> PaidIds { get => _paid; init => _paid = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public long Before { get; init; }
    public long After { get; init; }
    public bool Issued { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActualHandGainProgramReceipt? ActualHandGain { get; init; }
}
public sealed record ForeignTurnHandGainsRecordedEvent(CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualTurnOwner, long BatchId, IReadOnlyList<ActualHandGainEntity> Gains) : IGameEvent
{
    private IReadOnlyList<ActualHandGainEntity> _gains = Array.AsReadOnly(Gains.ToArray());
    public IReadOnlyList<ActualHandGainEntity> Gains { get => _gains; init => _gains = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}
public sealed record ActualOwnTurnHandGainsRecordedEvent(CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualTurnOwner, long BatchId, IReadOnlyList<ActualHandGainEntity> Gains) : IGameEvent
{
    private IReadOnlyList<ActualHandGainEntity> _gains = Array.AsReadOnly(Gains.ToArray());
    public IReadOnlyList<ActualHandGainEntity> Gains { get => _gains; init => _gains = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}
public sealed record ActualHandGainDrawPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long OriginalBatchId, int ActualTurn, int ActualTurnOwner, long Before, long After, int ActualCount) : IGameEvent;
public sealed record ForeignTurnHandGainsCleanupPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualTurnOwner, IReadOnlyList<int> CardIds, long Before, long After) : IGameEvent
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cards; init => _cards = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}
public sealed record SameCategoryDeckGiftIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualTurnOwner, long CardUseFrameId, long ActionId, SkillProgramCardCategory Category,
    int RecipientSeat, int ActualCount, long Before, long After) : IGameEvent;
// This trusted fact carries an immutable accepted Action, including its frozen full target sequence.
public sealed record CompletedCategoryVirtualUseNormalizedEvent(long CardUseFrameId, int ActorSeat, CardKind EffectiveKind,
    long ProgramParentFrameId, CardConversionSource ProducerSource, string ProducerGameplayHash,
    int ProducerInstructionIndex, CardActionContext Action) : IGameEvent;
// The ordinary public summary names only the new weapon owner. This opt-in
// immutable fact proves both halves of a compound addition before counterspells.
public sealed record CompletedCategoryCompoundTargetsIssuedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    CardConversionSource Source, string GameplayHash, int InstructionIndex, IReadOnlyList<int> OriginalTargets,
    IReadOnlyList<int> Targets) : IGameEvent
{
    private IReadOnlyList<int> _original = Array.AsReadOnly(OriginalTargets.ToArray());
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Targets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}

internal interface IActualHandGainAndCategoryGiftHost
{
    SkillProgramStepOutcome ExecuteActualHandGain(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class ActualHandGainProgramDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new ActualHandGainProgramHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.GiveSameCategoryFromDeck
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw,
            effect.Op == SkillProgramEffectOp.GiveSameCategoryFromDeck ? SkillProgramEffectTarget.SelectedTarget : SkillProgramEffectTarget.Owner,
            1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.DrawAfterActualOwnHandGain ? SkillProgramTriggerWindow.CardsGained :
            Op == SkillProgramEffectOp.DiscardForeignTurnHandGains ? SkillProgramTriggerWindow.AfterTurnEnded : SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class DrawAfterActualOwnHandGainDescriptor : ActualHandGainProgramDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAfterActualOwnHandGain; }
internal sealed class DiscardForeignTurnHandGainsDescriptor : ActualHandGainProgramDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardForeignTurnHandGains; }
internal sealed class GiveSameCategoryFromDeckDescriptor : ActualHandGainProgramDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSameCategoryFromDeck; }
internal sealed class ActualHandGainProgramHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IActualHandGainAndCategoryGiftHost)h).ExecuteActualHandGain(f, e);
}
internal static class ActualHandGainAndCategoryGiftComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.DrawAfterActualOwnHandGain or
        SkillProgramEffectOp.DiscardForeignTurnHandGains or SkillProgramEffectOp.GiveSameCategoryFromDeck;
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (a.Effects.Any(e => IsOperation(e.Op))) throw new InvalidOperationException(path + ": actual gain and category gifts require their native trigger.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => IsOperation(e.Op))) return;
        if (t.Effects is not [var e] || t.Optional || t.UsageScope is not null || t.IncludeResponseUses ||
            e.Op == SkillProgramEffectOp.DrawAfterActualOwnHandGain &&
                (t.Window != SkillProgramTriggerWindow.CardsGained || t.Subject != SkillProgramTriggerSubject.Owner ||
                 t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || !t.DestinationZones.SequenceEqual([CardZoneKind.Hand])) ||
            e.Op == SkillProgramEffectOp.DiscardForeignTurnHandGains &&
                (t.Window != SkillProgramTriggerWindow.AfterTurnEnded || t.Subject != SkillProgramTriggerSubject.Owner ||
                 t.TurnOwnerScope != SkillProgramTurnOwnerScope.OtherLiving) ||
            e.Op == SkillProgramEffectOp.GiveSameCategoryFromDeck &&
                (t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor))
            throw new InvalidOperationException(path + ": actual gain and category gifts lost their exact batch, after-turn or true-use boundary.");
    }
}
