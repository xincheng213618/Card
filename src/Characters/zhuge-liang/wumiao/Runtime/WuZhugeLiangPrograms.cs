using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WuZhugeLiangStage { Choice, DrawTargets, HealthChildren, Drawing, DrawChildren, CopyChildren }
public sealed record WuZhugeLiangReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int ActualTurn, int TurnOwner, long WindowId, long ActionId, long UseId)
{
    public WuZhugeLiangStage Stage { get; init; }
    public CardKind Name { get; init; }
    public string? Branch { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.Empty<int>();
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public int Cursor { get; init; }
    public int Recipient { get; init; }
    public int RequestedDraw { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int HpBefore { get; init; }
    public int HpAfter { get; init; }
    public long? HpChangeId { get; init; }
    public int SevenCount { get; init; }
    public int? CreatedCardId { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuZhugeLiangReceipt? WuZhugeLiang { get; init; } }
public sealed record WuZhugeLiangStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int ActualTurn, int TurnOwner, long WindowId, long ActionId, long UseId) : IGameEvent;
public sealed record WuZhugeLiangHpSetEvent(long FrameId, int OwnerSeat, int Before, int After, int SevenCount, long? ChangeId) : IGameEvent;
public sealed record WuZhugeLiangOrderingIssuedEvent(long FrameId, int OwnerSeat, int Count) : IGameEvent;
public sealed record WuZhugeLiangDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual, int Replacement, long Before, long After) : IGameEvent;
public sealed record WuZhugeLiangMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record WuZhugeLiangCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;

internal interface IWuZhugeLiangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class WuZhugeLiangDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.InitializeWuZhugeLiangJincui => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.ResolveWuZhugeLiangJincui => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],
        SkillProgramEffectOp.ResolveWuZhugeLiangQingshi => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireCardActionActor()],
        SkillProgramEffectOp.ResolveWuZhugeLiangZhizhe => [new CaptureActivationCards("wu-zhuge-liang-copy-input"), new ReadSingleCardSet("wu-zhuge-liang-copy-input"), new RetainOwnedCardSet("wu-zhuge-liang-copy-input")],
        _ => throw new InvalidOperationException("Unknown Wu Zhuge Liang resource contract.")
    };
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(8));
    public override ISkillProgramEffectHandler Handler => new WuZhugeLiangHandler(Op);
}
internal sealed class WuZhugeLiangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IWuZhugeLiangHost>().Resolve(f, op);
}
