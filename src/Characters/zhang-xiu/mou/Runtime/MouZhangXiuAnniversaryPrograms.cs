using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouZhangXiuAnniversaryStage { Branch, GiveCard, DiscardCard, GiveChildren, DiscardChildren, ObtainChildren, DistributionCard, DistributionTarget, DistributionChildren, Drawing, DrawChildren, SlashIssuing, NativeSlash, SlashChildren }
public sealed record MouZhangXiuAnniversaryCard(int Id, CardKind Kind, CardLocation From, string Label);
public sealed record MouZhangXiuAnniversaryReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn)
{
    public MouZhangXiuAnniversaryStage Stage { get; init; }
    public int Target { get; init; } = -1;
    public int Recipient { get; init; } = -1;
    public int Selected { get; init; }
    public int Count { get; init; } = 1;
    public int Index { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<MouZhangXiuAnniversaryCard> _cards = [];
    public IReadOnlyList<MouZhangXiuAnniversaryCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _gained = [];
    public IReadOnlyList<int> Gained { get => _gained; init => _gained = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _given = [];
    public IReadOnlyList<int> Given { get => _given; init => _given = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouZhangXiuAnniversarySlashOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Index, int Target, long UseId, long ActionId);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhangXiuAnniversaryReceipt? MouZhangXiuAnniversary { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhangXiuAnniversarySlashOrigin? MouZhangXiuAnniversarySlash { get; init; } }
public sealed record MouZhangXiuAnniversaryStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Turn) : IGameEvent;
public sealed record MouZhangXiuAnniversaryMovedEvent(long FrameId, MouZhangXiuAnniversaryStage Stage, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouZhangXiuAnniversaryHandChosenEvent(long FrameId, int Target, int CardId, ulong RandomBefore, ulong RandomAfter) : IGameEvent;
public sealed record MouZhangXiuAnniversaryDiscardEvent(int Turn, int CardId, long Sequence) : IGameEvent;
public sealed record MouZhangXiuAnniversaryDamageEvent(int Turn, long ResolutionId, long DamageFrameId, int CardId, CardKind EffectiveKind, int Target, int Amount, bool Delayed) : IGameEvent;
public sealed record MouZhangXiuAnniversarySlashIssuedEvent(MouZhangXiuAnniversarySlashOrigin Origin) : IGameEvent;
public sealed record MouZhangXiuAnniversarySlashReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MouZhangXiuAnniversaryDrawReturnedEvent(long FrameId, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouZhangXiuAnniversaryCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouZhangXiuAnniversaryRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal interface IMouZhangXiuAnniversaryFacts : IProgramSelectionQuery
{
    int Turn { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); bool Friendly(int owner, int other); string Name(int seat); int Keep(int owner, int card);
    SkillProgram Program(string id); bool Parent(ProgramSkillFrame f); bool Largest(int seat); int PlayActor(ProgramSkillWindowContext w);
    IReadOnlyList<int> Others(int owner); IReadOnlyList<MouZhangXiuAnniversaryCard> Cards(int actor, int holder, bool discard); IReadOnlyList<MouZhangXiuAnniversaryCard> Entities(IEnumerable<int> ids);
    IEnumerable<T> History<T>() where T : IGameEvent;
}
internal interface IMouZhangXiuAnniversaryNative : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame f); void PublishState(); bool Drain(ProgramSkillFrame f); void Move(ProgramSkillFrame f);
    MouZhangXiuAnniversaryCard RandomHand(ProgramSkillFrame f, IReadOnlyList<MouZhangXiuAnniversaryCard> cards);
    bool CanSlash(ProgramSkillFrame f); void Slash(ProgramSkillFrame f);
}
internal abstract class MouZhangXiuAnniversaryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouZhangXiuAnniversaryHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouZhangXiuFuxiDescriptor : MouZhangXiuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhangXiuFuxi; }
internal sealed class ResolveMouZhangXiuHaoyiDescriptor : MouZhangXiuAnniversaryDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhangXiuHaoyi; }
internal sealed class MouZhangXiuAnniversaryHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouZhangXiuAnniversaryRuntime>().Resolve(f, operation);
}
