using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouSunJianOlYipoStage { Target, Option, Drawing, DrawChildren, Discard, DiscardChildren }
public sealed record MouSunJianOlYipoReceipt(CardConversionSource Source, string GameplayHash, long ChangeId)
{
    public MouSunJianOlYipoStage Stage { get; init; }
    public int? TargetSeat { get; init; }
    public string? Branch { get; init; }
    public int X { get; init; }
    public int DrawCount { get; init; }
    public int DiscardCount { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private IReadOnlyList<int> _selected = [];
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouSunJianOlHulieEntry(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int Turn, int TurnOwner, int ActorSeat, int TargetSeat)
{ public bool Boosted { get; init; } }
public enum MouSunJianOlHulieStage { DamageChoice, CounterChoice, Issuing, NativeUse, Children }
public sealed record MouSunJianOlHulieReceipt(MouSunJianOlHulieEntry Entry, MouSunJianOlHulieStage Stage)
{
    public bool Accepted { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public bool OuterWasActive { get; init; }
}
public sealed record MouSunJianOlCounterOrigin(long FrameId, CardConversionSource Source, string GameplayHash, long OriginalUseId,
    long OriginalActionId, long UseId, long ActionId, int ActorSeat, int TargetSeat);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouSunJianOlYipoReceipt? MouSunJianOlYipo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouSunJianOlHulieReceipt? MouSunJianOlHulie { get; init; }
}
public sealed partial record CardUseFrame
{
    private IReadOnlyList<MouSunJianOlHulieEntry>? _mouSunJianOl;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MouSunJianOlHulieEntry>? MouSunJianOlHulieEntries { get => _mouSunJianOl; init => _mouSunJianOl = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouSunJianOlCounterOrigin? MouSunJianOlCounter { get; init; }
}
public sealed record MouSunJianOlHpFirstReachedEvent(int OwnerSeat, int Hp, long ChangeId) : IGameEvent;
public sealed record MouSunJianOlYipoQualifiedEvent(CardConversionSource Source, string GameplayHash, long ChangeId, long? ParentId, int HpBefore, int HpAfter) : IGameEvent;
public sealed record MouSunJianOlYipoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long ChangeId) : IGameEvent;
public sealed record MouSunJianOlYipoChosenEvent(long FrameId, int TargetSeat, string Branch, int X, int Draw, int Discard) : IGameEvent;
public sealed record MouSunJianOlYipoDrawEvent(long FrameId, int RecipientSeat, int Requested, int Actual, int Replacement) : IGameEvent;
public sealed record MouSunJianOlYipoDiscardEvent(long FrameId, int TargetSeat, long Before, long After, int Count) : IGameEvent;
public sealed record MouSunJianOlHulieQualifiedEvent(MouSunJianOlHulieEntry Entry) : IGameEvent;
public sealed record MouSunJianOlHulieUsedEvent(long FrameId, int OwnerSeat, int Turn, int TurnOwner, bool Counter, long OriginalUseId) : IGameEvent;
public sealed record MouSunJianOlUseDamageEvent(long UseId, long DamageId, int Amount) : IGameEvent;
public sealed record MouSunJianOlCounterConsideredEvent(long FrameId, int OwnerSeat, long OriginalUseId) : IGameEvent;
public sealed record MouSunJianOlCounterIssuedEvent(MouSunJianOlCounterOrigin Origin) : IGameEvent;
public sealed record MouSunJianOlCounterReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouSunJianOlCompletedEvent(long FrameId, string SkillId) : IGameEvent;

internal interface IMouSunJianOlHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouSunJianOlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouSunJianOlHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(
        Op == SkillProgramEffectOp.ResolveMouSunJianYipo ? SkillProgramTriggerWindow.AfterHealthChanged :
        Op == SkillProgramEffectOp.OfferMouSunJianHulieDamage ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.CardUseCompleted)];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => true;
}
internal sealed class ResolveMouSunJianYipoDescriptor : MouSunJianOlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouSunJianYipo;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class OfferMouSunJianHulieDamageDescriptor : MouSunJianOlDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferMouSunJianHulieDamage; }
internal sealed class ResolveMouSunJianHulieCounterDescriptor : MouSunJianOlDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouSunJianHulieCounter; }
internal sealed class MouSunJianOlHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouSunJianOlHost>().Resolve(f, op);
}
