using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MobileShiHuanJieStage { Target, ExchangeChildren, GrantChildren, DiscardChildren, ShuffleChildren, Drawing, DrawChildren, Complete }
public sealed record MobileShiHuanJieMaterial(int CardId, CardKind Kind, CardLocation From, CardLocation To);
public sealed record MobileShiHuanJieReceipt(CardConversionSource Source, string GameplayHash, int ActualTurn,
    int ActualTurnOwner, long ParentId, SkillProgramEffectOp Operation, MobileShiHuanJieStage Stage)
{
    public int Target { get; init; } = -1;
    public int Cursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? BatchId { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    private IReadOnlyList<MobileShiHuanJieMaterial> _materials = [];
    public IReadOnlyList<MobileShiHuanJieMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _recipients = [];
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _shuffleBefore = [];
    public IReadOnlyList<int> ShuffleBefore { get => _shuffleBefore; init => _shuffleBefore = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _shuffleAfter = [];
    public IReadOnlyList<int> ShuffleAfter { get => _shuffleAfter; init => _shuffleAfter = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiHuanJieReceipt? MobileShiHuanJie { get; init; }
}
public sealed record MobileShiHuanJieStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualTurnOwner, long ParentId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record MobileShiHuanJieMaterialPaidEvent(long FrameId, MobileShiHuanJieStage Stage, MobileShiHuanJieMaterial Material, long Sequence) : IGameEvent;
public sealed record MobileShiHuanJieMovementPaidEvent(long FrameId, MobileShiHuanJieStage Stage, int Cursor, long? BatchId,
    long Before, long After, int Count) : IGameEvent;
public sealed record MobileShiHuanJieTargetChosenEvent(long FrameId, int TargetSeat, int OwnerHandCount, int TargetHandCount) : IGameEvent;
public sealed record MobileShiHuanJieShuffledEvent(long FrameId, uint RandomBefore, uint RandomAfter, int DeckCount) : IGameEvent;
public sealed record MobileShiHuanJieDrawCompletedEvent(long FrameId, int Cursor, int RecipientSeat, int ActualCount, bool Replaced) : IGameEvent;
public sealed record MobileShiHuanJieCompletedEvent(long FrameId, SkillProgramEffectOp Operation, int TargetSeat) : IGameEvent;

internal interface IMobileShiHuanJieHost
{ SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MobileShiHuanJieDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MobileShiHuanJieHandler(operation);
    public override ProgramOperationInteraction Interaction => operation == SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeDrawInvocation,
        Triggered: ProgramTrackedFact.NativeDrawInvocation);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => operation == SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou
        ? [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)] : [];
}
internal sealed class ResolveMobileShiHuanJieGongmouDescriptor() : MobileShiHuanJieDescriptor(SkillProgramEffectOp.ResolveMobileShiHuanJieGongmou);
internal sealed class ResolveMobileShiHuanJieZhengshuoDescriptor() : MobileShiHuanJieDescriptor(SkillProgramEffectOp.ResolveMobileShiHuanJieZhengshuo);
internal sealed class MobileShiHuanJieHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiHuanJieHost>().Begin(frame, operation);
}
