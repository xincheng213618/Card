using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum PrivateWhirlStage { SelectDeposit, SelectRemove, DepositChildren, RemovalChildren, FirstDrawChildren, ExtraDrawChildren, ExpiryChildren }
public sealed record PrivateWhirlDeposit(long DepositId, CardConversionSource Source, string GameplayHash, int CardId,
    CardLocation Location, int CreatedPlayInstance, long SequenceBefore, long SequenceAfter);
public sealed record PrivateWhirlDepositedEvent(PrivateWhirlDeposit Deposit) : IGameEvent;
public sealed record PrivateWhirlRemovedEvent(long ProgramFrameId, PrivateWhirlDeposit Deposit, long? ActionId, long Before, long After, string Reason) : IGameEvent;
public sealed record PrivateWhirlSegmentIssuedEvent(long ProgramFrameId, CardConversionSource Source, PrivateWhirlStage Stage, long? ActionId, long Before, long After, int Count, int RemainingCount, bool? NotUniqueMaximum) : IGameEvent;
public sealed record PrivateWhirlReceipt
{
    private IReadOnlyList<int> _selected = [];
    private IReadOnlyList<PrivateWhirlDeposit> _deposits = [];
    public int InstructionIndex { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long WindowId { get; init; }
    public PrivateWhirlStage Stage { get; init; }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PrivateWhirlDeposit> Deposits { get => _deposits; init => _deposits = Array.AsReadOnly(value.ToArray()); }
    public CardActionContext? OriginalAction { get; init; }
    public long? UseId { get; init; }
    public int CreatedPlayInstance { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawCount { get; init; }
    public int RemainingCount { get; init; }
    public bool? NotUniqueMaximum { get; init; }
}
public sealed record PrivateWhirlPileSnapshot
{
    public string SkillId { get; init; } = "";
    public string PileName { get; init; } = "旋";
    public int Count { get; init; }
    private IReadOnlyList<CardSnapshot>? _cards;
    public IReadOnlyList<CardSnapshot>? Cards { get => _cards; init => _cards = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record PlayerSnapshot
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<PrivateWhirlPileSnapshot>? PrivateWhirlPiles { get; init; } }
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PrivateWhirlReceipt? PrivateWhirl { get; init; } }
internal interface IPrivateWhirlHost { SkillProgramStepOutcome BeginPrivateWhirl(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal abstract class PrivateWhirlDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new PrivateWhirlHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(6));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(Op == SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding ? SkillProgramTriggerWindow.DiscardPhaseStarting : Op == SkillProgramEffectOp.ClearPrivateWhirlAtNextPlayEnding ? SkillProgramTriggerWindow.PlayEnding : SkillProgramTriggerWindow.CardUseCommitted)];
}
internal sealed class DepositPrivateWhirlUntilNextPlayEndingDescriptor : PrivateWhirlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding; }
internal sealed class ConsumePrivateWhirlForActualUseDescriptor : PrivateWhirlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ConsumePrivateWhirlForActualUse; }
internal sealed class ClearPrivateWhirlAtNextPlayEndingDescriptor : PrivateWhirlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClearPrivateWhirlAtNextPlayEnding; }
internal sealed class PrivateWhirlHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<IPrivateWhirlHost>().BeginPrivateWhirl(frame, effect);
}
internal static class PrivateWhirlContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op is SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding or SkillProgramEffectOp.ConsumePrivateWhirlForActualUse or SkillProgramEffectOp.ClearPrivateWhirlAtNextPlayEnding)) return;
        if (trigger.Effects is not [var e] || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.IncludeResponseUses || trigger.UsageScope is not null || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 ||
            (e.Op == SkillProgramEffectOp.ConsumePrivateWhirlForActualUse ? trigger.Window != SkillProgramTriggerWindow.CardUseCommitted || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || trigger.Optional : trigger.Subject != SkillProgramTriggerSubject.Owner || (e.Op == SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding ? trigger.Window != SkillProgramTriggerWindow.DiscardPhaseStarting || !trigger.Optional : trigger.Window != SkillProgramTriggerWindow.PlayEnding || trigger.Optional)))
            throw new InvalidOperationException($"{path}: private Whirl requires its exact discard, committed actual use or next own Play ending.");
    }
}
