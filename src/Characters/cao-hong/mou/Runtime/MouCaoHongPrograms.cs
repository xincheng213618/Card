using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouCaoHongStage { ExchangeTarget, ExchangeTakeChildren, ReturnCards, ReturnChildren, ObtainCard, ObtainChildren, DiscardCards, DiscardChildren }
public sealed record MouCaoHongCard(int Id, CardKind Kind, CardLocation From, string Label);
public sealed record MouCaoHongUse(long WindowId, long UseId, long ActionId, IReadOnlyList<int> Targets);
public sealed record MouCaoHongReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int Turn, int TurnOwner, int PhaseId, long ParentId, long ActionId, long UseId, long DebtId)
{
    public MouCaoHongStage Stage { get; init; }
    public int Target { get; init; } = -1;
    public int Cursor { get; init; }
    public int Requested { get; init; }
    public int Actual { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private IReadOnlyList<int> _targets = [], _selected = [];
    private IReadOnlyList<MouCaoHongCard> _cards = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouCaoHongCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouCaoHongReceipt? MouCaoHong { get; init; }
}
public sealed record MouCaoHongStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int Turn, int TurnOwner, int PhaseId, long ParentId, long ActionId, long UseId, long DebtId) : IGameEvent;
public sealed record MouCaoHongDistanceIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int TurnOwner, long ActionId, int Target) : IGameEvent;
public sealed record MouCaoHongExchangeIssuedEvent(long FrameId, int Owner, int Turn, int Target) : IGameEvent;
public sealed record MouCaoHongXianjuIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int TurnOwner, int PhaseId) : IGameEvent;
public sealed record MouCaoHongDebtConsumedEvent(long DebtId, long FrameId, int Required) : IGameEvent;
public sealed record MouCaoHongMovementPaidEvent(long FrameId, int Cursor, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouCaoHongHandChosenEvent(long FrameId, int Holder, int CardId, uint Before, uint After, int Count) : IGameEvent;
public sealed record MouCaoHongSegmentCompletedEvent(long FrameId, MouCaoHongStage Stage, int Target, int Requested, int Actual) : IGameEvent;
public sealed record MouCaoHongCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouCaoHongRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouCaoHongFacts : IProgramSelectionQuery
{
    int Turn { get; } int TurnOwner { get; } int PhaseId { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); bool ActualPlay(int owner); string Name(int seat); int Keep(int owner, int card);
    SkillProgram? Program(string skill); IEnumerable<T> History<T>() where T : IGameEvent;
    MouCaoHongUse? Use(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context); bool Parent(ProgramSkillFrame frame);
    bool AllDistanceOne(int owner); IReadOnlyList<int> InRange(int owner); int OutsideCount(int owner); IReadOnlyList<int> ExchangeTargets(int owner);
    IReadOnlyList<MouCaoHongCard> Hand(int owner); IReadOnlyList<MouCaoHongCard> Cards(int actor, int holder, bool discard);
    MouCaoHongXianjuIssuedEvent? Debt(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
}
internal interface IMouCaoHongNative : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame frame); void Publish(); bool Drain(ProgramSkillFrame frame); void Move(ProgramSkillFrame frame);
    MouCaoHongCard RandomHand(ProgramSkillFrame frame, IReadOnlyList<MouCaoHongCard> cards);
}
internal abstract class MouCaoHongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouCaoHongHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase,
        Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveMouCaoHongYingjia ? [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)] :
        Op == SkillProgramEffectOp.SettleMouCaoHongXianju ? [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)] : [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouCaoHongYingjiaDescriptor : MouCaoHongDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouCaoHongYingjia; }
internal sealed class ResolveMouCaoHongXianjuDescriptor : MouCaoHongDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouCaoHongXianju; }
internal sealed class SettleMouCaoHongXianjuDescriptor : MouCaoHongDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleMouCaoHongXianju; }
internal sealed class MouCaoHongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouCaoHongRuntime>().Resolve(f, op);
}
