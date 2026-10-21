using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouLuSuAnniversaryStage { Opening, Show, Recipient, Gift, GiftChildren, Drawing, DrawChildren, Request, DraftCards, DraftTargets, Issuing, NativeUse, UseChildren, PlayedAccepted, PlayedCompleted, PlayedChildren, MissingHp, HpChildren, Recover, RecoveryChildren, PlayedCostChildren }
public sealed record MouLuSuAnniversaryPlayedPlan(CardKind Kind, CardConversionSource? Source, bool Zhangba)
{
    private readonly IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLuSuAnniversaryReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn, int Other, int X, bool Yin)
{
    public MouLuSuAnniversaryStage Stage { get; init; }
    public long Before { get; init; } public long After { get; init; }
    public int Index { get; init; } public int Cursor { get; init; }
    public long UseId { get; init; } public long ActionId { get; init; }
    public bool Returned { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public LegalAction? SelectedAction { get; init; }
    public PendingDecision? OriginalDecision { get; init; }
    public MouLuSuAnniversaryPlayedPlan? Played { get; init; }
    public CardActionContext? Response { get; init; }
    private readonly IReadOnlyList<int> _cards = [], _targets = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLuSuAnniversaryUseOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Index, int Actor, long UseId, long ActionId);
public sealed record MouLuSuAnniversaryPlayedReturn(long FrameId, long ActionId, SkillProgramTriggerWindow Window);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuSuAnniversaryReceipt? MouLuSuAnniversary { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuSuAnniversaryUseOrigin? MouLuSuAnniversary { get; init; } }
public sealed partial record ProgramCardTriggerWindowFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuSuAnniversaryPlayedReturn? MouLuSuAnniversaryPlayed { get; init; } }
public sealed record MouLuSuAnniversaryStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn, int Other, int X, bool Yin) : IGameEvent;
public sealed record MouLuSuAnniversaryDrawTicketEvent(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLuSuAnniversaryDrawConsumedEvent(CardConversionSource Source, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLuSuAnniversaryAcceptedEvent(long FrameId, int Owner, int Other, int Turn, bool Yin, int X) : IGameEvent;
public sealed record MouLuSuAnniversaryShownEvent(long FrameId, int CardId) : IGameEvent;
public sealed record MouLuSuAnniversaryGiftEvent(long FrameId, int Recipient, int CardId, long Sequence) : IGameEvent;
public sealed record MouLuSuAnniversaryUseIssuedEvent(MouLuSuAnniversaryUseOrigin Origin) : IGameEvent;
public sealed record MouLuSuAnniversaryUseReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MouLuSuAnniversaryPlayedEvent(long FrameId, int Index, long ActionId, bool Completed) : IGameEvent;
public sealed record MouLuSuAnniversaryHpIssuedEvent(long FrameId, int Cursor, int Target, bool Recovery) : IGameEvent;
public sealed record MouLuSuAnniversaryDamageRewardIssuedEvent(long FrameId, long DamageFrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouLuSuAnniversaryCompletedEvent(long FrameId) : IGameEvent;
public sealed record MouLuSuAnniversaryTransferInvoiceEvent(long EntrySequence, string Producer, int Owner, int Recipient) : IGameEvent;
internal interface IMouLuSuAnniversaryRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp operation); }
internal interface IMouLuSuAnniversaryFacts : IProgramSelectionQuery
{
    int Turn { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); bool Friendly(int owner, int other); string Name(int seat); int MaxHp(int seat); int HandCount(int seat);
    IReadOnlyList<int> Living(); IReadOnlyList<CardSnapshot> Hand(int seat); bool InHand(int seat, int card); SkillProgram Program(string id); IEnumerable<T> History<T>() where T : IGameEvent;
    bool Yin(int owner, string instance); bool Parent(ProgramSkillFrame f); ActualDiscardRecoveryPhaseKey? Phase();
    int? Peer(ProgramTriggerCandidate c, ProgramSkillWindowContext w); (int Actor, int Amount, long UseId)? Damage(ProgramTriggerCandidate c, ProgramSkillWindowContext w);
    IReadOnlyList<LegalAction> Uses(ProgramSkillFrame f); IReadOnlyList<MouLuSuAnniversaryPlayedPlan> Plays(ProgramSkillFrame f);
}
internal interface IMouLuSuAnniversaryNative : IProgramRuntimeOperations
{
    void PublishState(); void Finish(ProgramSkillFrame f); bool Drain(ProgramSkillFrame f); void Yin(ProgramSkillFrame f, bool yin);
    void Reveal(ProgramSkillFrame f); void Gift(ProgramSkillFrame f, int card); void Use(ProgramSkillFrame f); void Play(ProgramSkillFrame f); void CompletePlayed(ProgramSkillFrame f);
    void LoseHp(ProgramSkillFrame f, int target); void Recover(ProgramSkillFrame f, int target);
}
internal abstract class MouLuSuAnniversaryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouLuSuAnniversaryHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class InitializeMouLuSuMengmouDescriptor : MouLuSuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouLuSuMengmou; }
internal sealed class ScheduleMouLuSuMingshiDescriptor : MouLuSuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleMouLuSuMingshi; }
internal sealed class ResolveMouLuSuMingshiDescriptor : MouLuSuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuSuMingshi; }
internal sealed class ResolveMouLuSuMengmouDescriptor : MouLuSuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuSuMengmou; }
internal sealed class RecoverMouLuSuMengmouDamageDescriptor : MouLuSuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverMouLuSuMengmouDamage; }
internal sealed class MouLuSuAnniversaryHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouLuSuAnniversaryRuntime>().Resolve(f, operation);
}
