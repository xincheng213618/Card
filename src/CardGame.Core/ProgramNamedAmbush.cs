using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum NamedAmbushStage { SelectCard, SelectName, SelectDeposit, MovementChildren, Completed }
public sealed record NamedAmbushDeposit(long DepositId, CardConversionSource Source, string GameplayHash,
    int CardId, CardKind PrintedKind, CardKind RecordedKind, CardLocation Location, long SequenceBefore, long SequenceAfter);
public sealed record NamedAmbushUseIdentity(long WindowFrameId, long ParentFrameId, long ActionId,
    int ActorSeat, int ProviderSeat, CardKind EffectiveKind, bool ResponseUse);
public sealed record NamedAmbushReceipt
{
    private IReadOnlyList<PromptChoice> _choices = [];
    public int InstructionIndex { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long WindowFrameId { get; init; }
    public NamedAmbushStage Stage { get; init; }
    public bool Removing { get; init; }
    public int? SelectedCardId { get; init; }
    public NamedAmbushDeposit? Deposit { get; init; }
    public NamedAmbushUseIdentity? Use { get; init; }
    // CardActionContext defensively copies every collection in its constructor.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CardActionContext? OriginalAction { get; init; }
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record NamedAmbushOfferedEvent(long ProgramFrameId, NamedAmbushReceipt Receipt) : IGameEvent;
public sealed record NamedAmbushDepositedEvent(NamedAmbushDeposit Deposit) : IGameEvent;
public sealed record NamedAmbushRemovedEvent(long ProgramFrameId, NamedAmbushDeposit Deposit,
    NamedAmbushUseIdentity? Use, long SequenceBefore, long SequenceAfter, string Reason) : IGameEvent;
public sealed record NamedAmbushUseInvalidatedEvent(long ProgramFrameId, NamedAmbushDeposit Deposit,
    NamedAmbushUseIdentity Use, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record NamedAmbushPrivateEntrySnapshot(CardSnapshot Card, CardKind RecordedKind);
public sealed record NamedAmbushPileSnapshot(string SkillId, string PileName, int Count,
    IReadOnlyList<NamedAmbushPrivateEntrySnapshot>? Entries);
public sealed record NamedAmbushCounterspellResponse(long ActionId, int ResponderSeat, int CardId,
    bool PreviousEffectNullified, int PreviousChainDepth, NamedAmbushUseInvalidatedEvent? Invalidated = null);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NamedAmbushReceipt? NamedAmbush { get; init; } }
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NamedAmbushUseInvalidatedEvent? NamedAmbushInvalidation { get; init; }
    private IReadOnlyList<NamedAmbushUseInvalidatedEvent>? _namedAmbushResponseInvalidations;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<NamedAmbushUseInvalidatedEvent>? NamedAmbushResponseInvalidations
    { get => _namedAmbushResponseInvalidations; init => _namedAmbushResponseInvalidations = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record NullificationWindowFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NamedAmbushCounterspellResponse? NamedAmbushResponse { get; init; } }
public sealed partial record PlayerSnapshot
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<NamedAmbushPileSnapshot>? NamedAmbushPiles { get; init; } }

internal interface INamedAmbushHost
{ SkillProgramStepOutcome ExecuteNamedAmbush(SkillProgramEffect effect, ProgramSkillFrame frame); }
internal abstract class NamedAmbushDescriptorBase : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new NamedAmbushHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(8));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.RecordNamedPrivateAmbush
            ? SkillProgramTriggerWindow.TurnEnding : SkillProgramTriggerWindow.CardUseCommitted)];
}
internal sealed class RecordNamedPrivateAmbushDescriptor : NamedAmbushDescriptorBase
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordNamedPrivateAmbush; }
internal sealed class NullifyMatchingHandUseDescriptor : NamedAmbushDescriptorBase
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.NullifyMatchingHandUse; }
internal sealed class NamedAmbushHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((INamedAmbushHost)host).ExecuteNamedAmbush(effect, frame);
}
internal static class NamedAmbushContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        var special = trigger.Effects.Where(e => e.Op is SkillProgramEffectOp.RecordNamedPrivateAmbush or SkillProgramEffectOp.NullifyMatchingHandUse).ToArray();
        if (special.Length == 0) return;
        if (trigger.Effects is not [var effect] || special.Length != 1 || !trigger.Optional ||
            effect.Target != SkillProgramEffectTarget.Owner || effect.Condition.Kind != SkillProgramConditionKind.Always ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null)
            throw new InvalidOperationException($"{path}: a named ambush requires one optional owner instruction.");
        var recording = effect.Op == SkillProgramEffectOp.RecordNamedPrivateAmbush;
        if (recording ? trigger.Window != SkillProgramTriggerWindow.TurnEnding || trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.IncludeResponseUses || trigger.OwnerRelation is not null
            : trigger.Window != SkillProgramTriggerWindow.CardUseCommitted || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !trigger.IncludeResponseUses)
            throw new InvalidOperationException($"{path}: named ambush recording requires own ending, and invalidation requires the actual committed use including responses.");
    }
}
