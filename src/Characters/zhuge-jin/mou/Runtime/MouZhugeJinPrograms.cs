using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouZhugeJinStage { Target, SecretNumber, Give, GiveChildren, Draw, Drawing, DrawChildren, Cost, CostChildren, LossChildren, GrantChildren }
public sealed record MouZhugeJinCard(int Id, CardLocation From, string Label);
public sealed record MouZhugeJinUse(long WindowId, long UseId, CardActionContext Action, bool Red);
public sealed record MouZhugeJinReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int ActualTurn, int TurnOwner, int Round, long WindowId, long UseId, long ActionId)
{
    public MouZhugeJinStage Stage { get; init; }
    public int Target { get; init; } = -1;
    public int SecretNumber { get; init; }
    public bool Red { get; init; }
    public bool Committed { get; init; }
    public int Given { get; init; }
    public int DrawCursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int HpBefore { get; init; }
    public MouZhugeJinCard? Paying { get; init; }
    public bool CostPaid { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhugeJinReceipt? MouZhugeJin { get; init; }
}
public sealed record MouZhugeJinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int ActualTurn, int TurnOwner, int Round, long WindowId, long UseId, long ActionId) : IGameEvent;
public sealed record MouZhugeJinCooldownIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Round, int DueRound) : IGameEvent;
public sealed record MouZhugeJinGivenEvent(long FrameId, int Giver, int Recipient, int CardId, long Sequence) : IGameEvent;
public sealed record MouZhugeJinBudgetIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Target, int Count) : IGameEvent;
public sealed record MouZhugeJinBudgetHitEvent(long FrameId, long DamageId, int Target) : IGameEvent;
public sealed record MouZhugeJinDrawReturnedEvent(long FrameId, int Recipient, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouZhugeJinCostPaidEvent(long FrameId, int Executor, int Holder, int CardId, long Sequence) : IGameEvent;
public sealed record MouZhugeJinHandChosenEvent(long FrameId, int CardId, uint Before, uint After, int Count) : IGameEvent;
public sealed record MouZhugeJinZijinGrantedEvent(long FrameId, int Recipient) : IGameEvent;
public sealed record MouZhugeJinCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouZhugeJinRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouZhugeJinFacts : IProgramSelectionQuery
{
    int Turn { get; } int TurnOwner { get; } int Round { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); bool Friendly(int owner, int other); int Hp(int seat); string Name(int seat);
    SkillProgram? Program(string skill); IEnumerable<T> History<T>() where T : IGameEvent;
    IReadOnlyList<int> Targets(int owner); IReadOnlyList<MouZhugeJinCard> Hand(int seat); IReadOnlyList<MouZhugeJinCard> Costs(int chooser, int holder);
    MouZhugeJinUse? Use(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context, SkillProgramEffectOp operation);
    bool Parent(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal interface IMouZhugeJinNative : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame frame); void Publish(); bool Drain(ProgramSkillFrame frame);
    void Give(ProgramSkillFrame frame); void Discard(ProgramSkillFrame frame); void Lose(ProgramSkillFrame frame); void Nullify(ProgramSkillFrame frame); void Grant(ProgramSkillFrame frame);
    MouZhugeJinCard RandomHand(ProgramSkillFrame frame, IReadOnlyList<MouZhugeJinCard> cards);
}
internal abstract class MouZhugeJinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouZhugeJinHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveMouZhugeJinTaozhou ? [] :
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveMouZhugeJinHoude ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects : SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeLogicalDiscard,
        Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouZhugeJinTaozhouDescriptor : MouZhugeJinDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhugeJinTaozhou; }
internal sealed class ResolveMouZhugeJinHoudeDescriptor : MouZhugeJinDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhugeJinHoude; }
internal sealed class ResolveMouZhugeJinZijinDescriptor : MouZhugeJinDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhugeJinZijin; }
internal sealed class MouZhugeJinHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouZhugeJinRuntime>().Resolve(f, op);
}
