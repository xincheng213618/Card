using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouCaoShuangAnniversaryStage { Option, Target, Cards, MoveChildren, Drawing, DrawChildren, NextRepeat, Maximum, Recover, RecoveryChildren, Grant, GrantChildren, Fudou, HpChildren }
public sealed record MouCaoShuangAnniversaryCard(int Id, CardKind Kind, CardLocation From, string Label);
public sealed record MouCaoShuangAnniversaryReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    long ParentId, int Turn, int PhaseId, int Remaining, int X, long ActionId = 0, long UseId = 0, int Other = -1, bool Red = false)
{
    public MouCaoShuangAnniversaryStage Stage { get; init; }
    public int Option { get; init; }
    public int Payer { get; init; } = -1;
    public int Required { get; init; }
    public int Repeat { get; init; }
    public int Cursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<MouCaoShuangAnniversaryCard> _cards = [];
    public IReadOnlyList<MouCaoShuangAnniversaryCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouCaoShuangAnniversaryReceipt? MouCaoShuangAnniversary { get; init; }
}
public sealed record MouCaoShuangAnniversaryStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn, int PhaseId, int Remaining, int X, long ActionId, long UseId, int Other, bool Red) : IGameEvent;
public sealed record MouCaoShuangAnniversaryOptionEvent(long FrameId, int Owner, int Turn, int PhaseId, int Option, int X) : IGameEvent;
public sealed record MouCaoShuangAnniversaryDeletedEvent(long FrameId, int Owner, int Turn, int PhaseId, int Option, int BeforeMask, int AfterMask, uint RandomBefore, uint RandomAfter) : IGameEvent;
public sealed record MouCaoShuangAnniversaryAwakenedEvent(long FrameId, int Owner, int Remaining) : IGameEvent;
public sealed record MouCaoShuangAnniversaryMaterialEvent(long FrameId, int Repeat, int Payer, int CardId, CardLocation From, long Sequence, bool Recast) : IGameEvent;
public sealed record MouCaoShuangAnniversaryItemCompletedEvent(long FrameId, int Repeat, int Option, int X, int Payer, int Actual) : IGameEvent;
public sealed record MouCaoShuangAnniversaryDrawReturnedEvent(long FrameId, int Repeat, int Target, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouCaoShuangAnniversaryHpIssuedEvent(long FrameId, int Cursor, int Target) : IGameEvent;
public sealed record MouCaoShuangAnniversaryGrantedEvent(long FrameId, int Owner) : IGameEvent;
public sealed record MouCaoShuangAnniversaryCompletedEvent(long FrameId) : IGameEvent;
public sealed record MouCaoShuangAnniversaryUse(long WindowId, CardActionContext Action, long UseId, int Other, bool? Red);
internal interface IMouCaoShuangAnniversaryRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouCaoShuangAnniversaryFacts : IProgramSelectionQuery
{
    int Turn { get; } int CurrentSeat { get; } int PhaseId { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); string Name(int seat); int Keep(int seat, int card); bool Friendly(int owner, int other); IReadOnlyList<int> Living();
    SkillProgram Program(string id); IEnumerable<T> History<T>() where T : IGameEvent;
    bool Parent(ProgramSkillFrame frame); bool ActualPlay(int owner); MouCaoShuangAnniversaryUse? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w);
    IReadOnlyList<MouCaoShuangAnniversaryCard> Payable(int payer, bool recast); bool Hurt(int source, int victim);
}
internal interface IMouCaoShuangAnniversaryNative : IProgramRuntimeOperations
{
    void PublishState(); void Finish(ProgramSkillFrame frame); bool Drain(ProgramSkillFrame frame);
    void Move(ProgramSkillFrame frame); void CompleteRecast(ProgramSkillFrame frame, int actual); void Delete(ProgramSkillFrame frame);
    void Maximum(ProgramSkillFrame frame); void Recover(ProgramSkillFrame frame); void Grant(ProgramSkillFrame frame); void LoseHp(ProgramSkillFrame frame, int target);
}
internal abstract class MouCaoShuangAnniversaryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouCaoShuangAnniversaryHandler(Op);
    public override ProgramOperationInteraction Interaction => Op is SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan or SkillProgramEffectOp.ResolveMouCaoShuangFanshi ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouCaoShuangJianzhuanDescriptor : MouCaoShuangAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouCaoShuangJianzhuan; }
internal sealed class DeleteMouCaoShuangJianzhuanOptionDescriptor : MouCaoShuangAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DeleteMouCaoShuangJianzhuanOption; }
internal sealed class ResolveMouCaoShuangFanshiDescriptor : MouCaoShuangAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouCaoShuangFanshi; }
internal sealed class ResolveMouCaoShuangFudouDescriptor : MouCaoShuangAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouCaoShuangFudou; }
internal sealed class MouCaoShuangAnniversaryHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouCaoShuangAnniversaryRuntime>().Resolve(f, operation);
}
