using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouHuLieStage { Card, GiftTarget, Giving, GiveChildren, Clearing, ClearChildren, SlashOffer, SlashIssuing, NativeSlash, SlashChildren, Drawing, DrawChildren }
public sealed record MouHuLieCard(int Id, CardKind Kind, CardLocation From, string Label);
public sealed record MouHuLieYu(long MarkId, CardConversionSource Source, string GameplayHash, int Round, int CardId, CardKind Kind, CardLocation OriginalFrom);
public sealed record MouHuLieSlashOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Index, int Actor, int Target, long UseId, long ActionId);
public sealed record MouHuLieReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Round, int Turn, long DamageId = 0)
{
    public MouHuLieStage Stage { get; init; }
    public MouHuLieYu? Yu { get; init; }
    public int Target { get; init; } = -1;
    public int Index { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<MouHuLieCard> _cards = [];
    public IReadOnlyList<MouHuLieCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _actors = [];
    public IReadOnlyList<int> Actors { get => _actors; init => _actors = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore] public int Actor => Index < Actors.Count ? Actors[Index] : -1;
    [JsonIgnore] public int Count => Actors.Count;
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHuLieReceipt? MouHuLie { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHuLieSlashOrigin? MouHuLieSlash { get; init; } }
public sealed record MouHuLieStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int Round, int Turn, long DamageId) : IGameEvent;
public sealed record MouHuLieMarkedEvent(MouHuLieYu Yu, int InitialRecipient, long Sequence, bool Self) : IGameEvent;
public sealed record MouHuLieObtainedEvent(long MarkId, int Recipient, long Sequence) : IGameEvent;
public sealed record MouHuLieUsedDiscardEvent(MouHuLieYu Yu, long BatchId, long ParentFrameId, int ActualTurn, int CardId, long Sequence) : IGameEvent;
public sealed record MouHuLieClearedEvent(long FrameId, long MarkId, int Round) : IGameEvent;
public sealed record MouHuLieMovedEvent(long FrameId, MouHuLieStage Stage, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouHuLieYitouIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int Recipient) : IGameEvent;
public sealed record MouHuLieDamageEvent(long DamageId, long LeaseId, int Owner, int Turn, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record MouHuLieDrawReturnedEvent(long FrameId, long DamageId, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouHuLieSlashIssuedEvent(MouHuLieSlashOrigin Origin) : IGameEvent;
public sealed record MouHuLieSlashReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MouHuLieCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouHuLieRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouHuLieFacts : IProgramSelectionQuery
{
    int Round { get; } int Turn { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); string Name(int seat); int HandCount(int seat); int Keep(int actor, int card); bool Friendly(int actor, int other);
    SkillProgram Program(string id); IEnumerable<T> History<T>() where T : IGameEvent; bool Parent(ProgramSkillFrame frame);
    IReadOnlyList<int> Living(); IReadOnlyList<int> ActionOrder(IEnumerable<int> seats); IReadOnlyList<MouHuLieCard> Owned(int owner, bool handOnly); MouHuLieCard Current(int id);
    MouHuLieYu? ActiveYu(int owner, string instance); MouHuLieYu? UsedYu(ProgramSkillWindowContext context, int owner, string instance); int? YitouTarget(ProgramSkillWindowContext context, int owner); MouHuLieDamageEvent? Damage(ProgramTriggerCandidate c, ProgramSkillWindowContext context);
    bool CanSlash(ProgramSkillFrame frame);
}
internal interface IMouHuLieNative : IProgramRuntimeOperations
{
    void Publish(); void Finish(ProgramSkillFrame frame); bool Drain(ProgramSkillFrame frame); void Give(ProgramSkillFrame frame); void Clear(ProgramSkillFrame frame, MouHuLieCard card); void Use(ProgramSkillFrame frame);
}
internal abstract class MouHuLieDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouHuLieHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry | ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch { SkillProgramEffectOp.StartMouHuLieChuanyu => SkillProgramTriggerWindow.RoundStarting, SkillProgramEffectOp.RegiveMouHuLieChuanyu => SkillProgramTriggerWindow.DiscardPileReceived, SkillProgramEffectOp.EndMouHuLieChuanyu => SkillProgramTriggerWindow.RoundEnding, SkillProgramEffectOp.ResolveMouHuLieYitou => SkillProgramTriggerWindow.PlayPhaseStarting, _ => SkillProgramTriggerWindow.AfterDamageApplied })];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class StartMouHuLieChuanyuDescriptor : MouHuLieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.StartMouHuLieChuanyu; }
internal sealed class RegiveMouHuLieChuanyuDescriptor : MouHuLieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RegiveMouHuLieChuanyu; }
internal sealed class EndMouHuLieChuanyuDescriptor : MouHuLieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EndMouHuLieChuanyu; }
internal sealed class ResolveMouHuLieYitouDescriptor : MouHuLieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuLieYitou; }
internal sealed class DrawMouHuLieYitouDescriptor : MouHuLieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawMouHuLieYitou; }
internal sealed class MouHuLieHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouHuLieRuntime>().Resolve(f, op);
}
