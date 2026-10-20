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
public sealed record WuZhugeLiangQingshiAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, long ActionId, CardKind Name) : IGameEvent;
public sealed record WuZhugeLiangQingshiBranchEvent(long FrameId, string Branch) : IGameEvent;
public sealed record WuZhugeLiangQingshiDamageEvent(long FrameId, CardConversionSource Source, string GameplayHash, long ActionId, long UseId, int TargetSeat) : IGameEvent;
public sealed record WuZhugeLiangQingshiTargetEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record WuZhugeLiangQingshiDisabledEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record WuZhugeLiangDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual, int Replacement, long Before, long After) : IGameEvent;
public sealed record WuZhugeLiangCopyCreatedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int OriginalCardId, int CardId, CardKind Kind, Suit Suit, int Rank) : IGameEvent;
public sealed record WuZhugeLiangCopyReturnedEvent(int OwnerSeat, int CardId, long EntrySequence, long ReturnSequence, CardMoveReason Reason, int ActualTurn, int TurnOwner) : IGameEvent;
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
internal sealed class InitializeWuZhugeLiangJincuiDescriptor : WuZhugeLiangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeWuZhugeLiangJincui;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveWuZhugeLiangJincuiDescriptor : WuZhugeLiangDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhugeLiangJincui;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveWuZhugeLiangQingshiDescriptor : WuZhugeLiangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhugeLiangQingshi;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveWuZhugeLiangZhizheDescriptor : WuZhugeLiangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhugeLiangZhizhe;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuZhugeLiangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IWuZhugeLiangHost>().Resolve(f, op);
}
