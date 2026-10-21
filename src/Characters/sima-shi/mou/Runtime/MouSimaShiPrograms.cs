using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouSimaShiStage { Choosing, Moving, MovementChildren, Damage, Shuffle }
public sealed record MouSimaShiCard(int Id, CardKind Kind, CardLocation From);
public sealed record MouSimaShiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn)
{
    public MouSimaShiStage Stage { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public long DebtId { get; init; }
    public int Target { get; init; } = -1;
    public int Index { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<MouSimaShiCard> _cards = [];
    public IReadOnlyList<MouSimaShiCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouSimaShiReceipt? MouSimaShi { get; init; } }
public sealed record MouSimaShiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn) : IGameEvent;
public sealed record MouSimaShiMarkedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int CardId, int Rank, ulong RandomBefore, ulong RandomAfter) : IGameEvent;
public sealed record MouSimaShiInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MouSimaShiMaterialEvent(long ActionId, int Actor, long ParentId, int CardId, CardKind Kind, CardLocation From, long ProcessingEntry, long DirectDiscard) : IGameEvent;
public sealed record MouSimaShiDiscardEvent(int Turn, int CardId, CardKind Kind, long Sequence, long ParentId, long ProcessingEntry, bool NativeFinish) : IGameEvent;
public sealed record MouSimaShiTargetEvent(long FrameId, int Owner, int Turn, int Target, long ActionId) : IGameEvent;
public sealed record MouSimaShiClaimIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouSimaShiMovedEvent(long FrameId, SkillProgramEffectOp Operation, long DebtId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouSimaShiRemovedEvent(long DebtId, int CardId, long Sequence) : IGameEvent;
public sealed record MouSimaShiPhaseSettledEvent(long DebtId, long FrameId) : IGameEvent;
public sealed record MouSimaShiDeathSettledEvent(long DebtId, long FrameId) : IGameEvent;
public sealed record MouSimaShiShuffledEvent(long FrameId, int DeckCount, ulong RandomBefore, ulong RandomAfter) : IGameEvent;
public sealed record MouSimaShiCompletedEvent(long FrameId) : IGameEvent;
internal sealed record MouSimaShiUse(long Id, long ActionId, int Actor, IReadOnlyList<int> Targets);
internal sealed record MouSimaShiDeckCard(int Id, int Rank);
internal interface IMouSimaShiRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp operation); }
internal interface IMouSimaShiFacts : IProgramSelectionQuery
{
    int Turn { get; } long Sequence { get; }
    bool Alive(int seat); bool Human(int seat); string Name(int seat); bool Friendly(int owner, int other); int HandCount(int seat);
    bool Parent(ProgramSkillFrame f); SkillProgram Program(string id); ActualDiscardRecoveryPhaseKey? Phase();
    IReadOnlyList<MouSimaShiDeckCard> Deck(); IReadOnlyList<MouSimaShiCard> Entities(IEnumerable<int> ids); bool Quarantined(MouSimaShiRemovedEvent removed);
    IEnumerable<T> History<T>() where T : IGameEvent;
    MouSimaShiUse? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w);
    MouSimaShiClaimIssuedEvent? Due(ProgramTriggerCandidate c, ProgramSkillWindowContext w);
}
internal interface IMouSimaShiNative : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame f); void Publish(); bool Drain(ProgramSkillFrame f);
    (int Id, ulong Before, ulong After) Random(IReadOnlyList<MouSimaShiDeckCard> cards);
    void Move(ProgramSkillFrame f, MouSimaShiCard card, CardLocation destination);
    void Damage(ProgramSkillFrame f); void Shuffle(ProgramSkillFrame f);
}
internal abstract class MouSimaShiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouSimaShiHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeTrueUseProcessingEntry, Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeTrueUseProcessingEntry);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouSimaShiSanshiDescriptor : MouSimaShiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouSimaShiSanshi; }
internal sealed class RecoverMouSimaShiSanshiDescriptor : MouSimaShiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverMouSimaShiSanshi; }
internal sealed class ResolveMouSimaShiZhenraoDescriptor : MouSimaShiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouSimaShiZhenrao; }
internal sealed class ResolveMouSimaShiChenlveDescriptor : MouSimaShiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouSimaShiChenlve; }
internal sealed class RemoveMouSimaShiChenlveDescriptor : MouSimaShiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RemoveMouSimaShiChenlve; }
internal sealed class ReturnMouSimaShiChenlveDescriptor : MouSimaShiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReturnMouSimaShiChenlve;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.Frame.SkillId == MouSimaShiRuntime.Chenlve && c.Frame.TriggerId == "death-return-shuffle" && c.Frame.WindowContext is { Window: SkillProgramTriggerWindow.OwnerDied } || base.AllowsDeadTarget(c);
}
internal sealed class MouSimaShiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouSimaShiRuntime>().Resolve(f, operation);
}
