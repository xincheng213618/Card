using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouWenChouOlStage { Targets, Issuing, NativeUse, Children, Reward, Drawing, Discard, DiscardChildren }
public sealed record MouWenChouOlReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, MouWenChouOlStage Stage)
{
    public int ActualTurn { get; init; }
    public int TargetSeat { get; init; } = -1;
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int Amount { get; init; }
    public int Discarded { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool NativeReturned { get; init; }
    public bool AttackReturned { get; init; }
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> NativeTargets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<long> _batches = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatchIds { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed record MouWenChouLunzhanOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int ActualTurn, int MaterialCount, int CardId, Suit Suit, int Rank, bool? IsRed);
public sealed record MouWenChouJuejueEntry(CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int ActualTurn, int TargetSeat, int Amount);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouWenChouOlReceipt? MouWenChouOl { get; init; } }
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouWenChouLunzhanOrigin? MouWenChouLunzhan { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouWenChouJuejueEntry? MouWenChouJuejue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? MouWenChouAllHand { get; init; }
}
public sealed record MouWenChouStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long UseId) : IGameEvent;
public sealed record MouWenChouLunzhanIssuedEvent(MouWenChouLunzhanOrigin Origin) : IGameEvent;
public sealed record MouWenChouLunzhanReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouWenChouTargetDeclaredEvent(int ActualTurn, int ActorSeat, long ActionId, int TargetSeat) : IGameEvent;
public sealed record MouWenChouAllHandPaidEvent(long UseId, long ActionId, int OwnerSeat, int Count) : IGameEvent;
public sealed record MouWenChouJuejueQualifiedEvent(MouWenChouJuejueEntry Entry) : IGameEvent;
public sealed record MouWenChouUseDamageEvent(long UseId, long DamageId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record MouWenChouRewardAcceptedEvent(long FrameId, long UseId, int OwnerSeat, int ActualTurn, int TargetSeat, int Amount) : IGameEvent;
public sealed record MouWenChouDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement) : IGameEvent;
public sealed record MouWenChouDiscardPaidEvent(long FrameId, int OwnerSeat, int CardId, long Before, long After) : IGameEvent;
public sealed record MouWenChouCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;
internal interface IMouWenChouOlHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MouWenChouOlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        if (Op != SkillProgramEffectOp.UseMouWenChouLunzhan) return ParseOwnerAlways(reader);
        reader.AllowOnly("op", "target", "condition", "amount");
        var amount = reader.RequiredInt("amount");
        if (amount is < 0 or > 5) throw new InvalidOperationException(reader.Path + ": Lunzhan permits zero through five materials.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), amount, reader.Condition()); RequireAlways(effect, reader.Path); return effect;
    }
    public override ISkillProgramEffectHandler Handler => new MouWenChouOlHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger | ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeLogicalDiscard,
        Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.UseMouWenChouLunzhan => effect.Amount == 0 ? [new ReadSelectedTarget()] : [new ReadSelectedTarget(), new ConsumeSelectedCards(effect.Amount)],
        SkillProgramEffectOp.RewardMouWenChouLunzhan => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]
    };
}
internal sealed class UseMouWenChouLunzhanDescriptor : MouWenChouOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseMouWenChouLunzhan; }
internal sealed class RewardMouWenChouLunzhanDescriptor : MouWenChouOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardMouWenChouLunzhan; }
internal sealed class ResolveMouWenChouJuejueDescriptor : MouWenChouOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouWenChouJuejue; }
internal sealed class MouWenChouOlHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouWenChouOlHost>().Resolve(frame, operation);
}

public sealed record MouWenChouNativeBatchSignedEvent(long FrameId, long UseId, long BatchId, long? ParentBatchId, int Count) : IGameEvent;
