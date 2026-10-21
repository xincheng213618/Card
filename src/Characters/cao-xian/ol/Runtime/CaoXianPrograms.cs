using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum CaoXianStage { Pile, StoreCards, RemoveCards, RemoveChildren, OptionTarget, EffectCards, EffectChildren, AdjustCards, AdjustChildren, Drawing, DrawChildren }
public sealed record CaoXianCard(int Id, CardKind Kind, Suit Suit, CardLocation From, string Label);
public sealed record CaoXianCompletedUse(long UseId, long ActionId);
public sealed record CaoXianReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, int X, long CompletedUseId, long ActionId)
{
    public CaoXianStage Stage { get; init; }
    public PublicPersistentPileSource? Pile { get; init; }
    public int Target { get; init; } = -1;
    public int Option { get; init; }
    public int Required { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<CaoXianCard> _cards = [];
    public IReadOnlyList<CaoXianCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoXianReceipt? CaoXian { get; init; } }
public sealed record CaoXianStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, int X, long CompletedUseId, long ActionId) : IGameEvent;
public sealed record CaoXianZhifouPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int X, long CompletedUseId, long ActionId) : IGameEvent;
public sealed record CaoXianZhifouRemovedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, long CompletedUseId, long ActionId, PublicPersistentPileSource Pile, int CardId, CardKind CardKind, long Sequence) : IGameEvent;
public sealed record CaoXianMovedEvent(long FrameId, CaoXianStage Stage, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record CaoXianOptionEvent(long FrameId, int Owner, int ActualTurn, int Option, int Target) : IGameEvent;
public sealed record CaoXianAdjustedEvent(long FrameId, int Owner, int RemainingSuits, int RequiredHand) : IGameEvent;
public sealed record CaoXianDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replaced, long? AttemptId) : IGameEvent;
public sealed record CaoXianHpIssuedEvent(long FrameId, int Target) : IGameEvent;
public sealed record CaoXianCompletedEvent(long FrameId) : IGameEvent;
internal interface ICaoXianRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface ICaoXianFacts : IProgramSelectionQuery
{
    int Turn { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); string Name(int seat); int MaxHp(int seat); int HandCount(int seat); int Keep(int owner, int card); bool Friendly(int owner, int other);
    SkillProgram Program(string id); IEnumerable<T> History<T>() where T : IGameEvent; IReadOnlyList<int> Living();
    bool Parent(ProgramSkillFrame frame); CaoXianCompletedUse? Completed(ProgramSkillWindowContext context, int owner);
    IReadOnlyList<PublicPersistentPileSource> Piles(int owner, string consumerInstance); PublicPersistentPileSource? RemovedPile(ProgramSkillWindowContext context, string instance);
    IReadOnlyList<CaoXianCard> PileCards(PublicPersistentPileSource pile); IReadOnlyList<CaoXianCard> Owned(int owner, bool handOnly, bool discard);
}
internal interface ICaoXianNative : IProgramRuntimeOperations
{
    PublicPersistentPileSource EnsurePile(ProgramSkillFrame frame); void Move(ProgramSkillFrame frame); void LoseHp(ProgramSkillFrame frame); bool Drain(ProgramSkillFrame frame); void Publish(); void Finish(ProgramSkillFrame frame);
}
internal abstract class CaoXianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new CaoXianHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch { SkillProgramEffectOp.ResolveCaoXianZhifou => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)], SkillProgramEffectOp.AdjustCaoXianLingxi => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)], _ => [new RequireTriggerWindows([SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramTriggerWindow.PlayEnding])] };
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class StoreCaoXianLingxiDescriptor : CaoXianDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.StoreCaoXianLingxi; }
internal sealed class ResolveCaoXianZhifouDescriptor : CaoXianDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveCaoXianZhifou; }
internal sealed class AdjustCaoXianLingxiDescriptor : CaoXianDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustCaoXianLingxi; }
internal sealed class CaoXianHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<ICaoXianRuntime>().Resolve(f, operation);
}
