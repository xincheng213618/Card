using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouXuShengStage { Benefit, Drawing, DrawChildren, Selecting, Paying, DiscardChildren, Claiming, ClaimChildren }
public sealed record MouXuShengCard(int CardId, int Holder, bool? IsRed);
public sealed record MouXuShengView(int Viewer, int Holder)
{
    private IReadOnlyList<MouXuShengCard> _cards = [];
    public IReadOnlyList<MouXuShengCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouXuShengSelection(int Chooser, MouXuShengCard Card);
public sealed record MouXuShengPaid(int Chooser, MouXuShengCard Card, long Sequence);
public sealed record MouXuShengUse(long WindowId, CardActionContext Action, long UseId);
public sealed record MouXuShengReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int ActualTurn, int TurnOwner, long WindowId, long ActionId, long UseId, long DamageId, int OtherSeat, int X)
{
    public MouXuShengStage Stage { get; init; }
    public bool Accepted { get; init; }
    public int Cursor { get; init; }
    public int PayCursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public MouXuShengSelection? Paying { get; init; }
    private IReadOnlyList<MouXuShengView> _views = [];
    private IReadOnlyList<MouXuShengSelection> _selected = [];
    private IReadOnlyList<MouXuShengPaid> _paid = [];
    public IReadOnlyList<MouXuShengView> Views { get => _views; init => _views = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouXuShengSelection> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouXuShengPaid> Paid { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouXuShengBoost(long FrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int X);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXuShengReceipt? MouXuSheng { get; init; }
}
public sealed partial record CardUseFrame
{
    private IReadOnlyList<MouXuShengBoost>? _mouXuShengBoosts;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<MouXuShengBoost>? MouXuShengBoosts
    { get => _mouXuShengBoosts; init => _mouXuShengBoosts = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouXuShengStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int ActualTurn, int TurnOwner, long WindowId, long ActionId, long UseId, long DamageId, int OtherSeat, int X) : IGameEvent;
public sealed record MouXuShengBenefitUsedEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, long ActionId, bool Damage, int X) : IGameEvent;
public sealed record MouXuShengBoostIssuedEvent(MouXuShengBoost Boost) : IGameEvent;
public sealed record MouXuShengDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouXuShengDiscardPaidEvent(long FrameId, int Chooser, int Holder, int CardId, bool? IsRed, long Sequence) : IGameEvent;
public sealed record MouXuShengClaimedEvent(long FrameId, int CardId, long Sequence) : IGameEvent;
public sealed record MouXuShengCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouXuShengRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MouXuShengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MouXuShengHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(
        Op == SkillProgramEffectOp.ResolveMouXuShengQinqiang ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeLogicalDiscard);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouXuShengQinqiangDescriptor : MouXuShengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXuShengQinqiang; }
internal sealed class ResolveMouXuShengYizhenDescriptor : MouXuShengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXuShengYizhen; }
internal sealed class MouXuShengHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouXuShengRuntime>().Resolve(frame, operation);
}
