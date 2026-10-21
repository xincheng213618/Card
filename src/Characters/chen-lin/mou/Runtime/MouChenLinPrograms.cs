using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouChenLinStage { Revealing, RevealChildren, BranchTarget, UseChoice, IssuingUse, NativeUse, UseChildren, GiveChildren, ReturningChildren }
public sealed record MouChenLinCard(int Id, CardKind Kind, CardLocation From, string Label);
public sealed record MouChenLinUsePlan(int CardId, CardKind Kind, LegalActionKind Action, CardKind? Required)
{
    private readonly IReadOnlyList<int> _eligibleTargets = [];
    public IReadOnlyList<int> EligibleTargets { get => _eligibleTargets; init => _eligibleTargets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouChenLinNativeUse(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int Index, int Target, int CardId, CardKind Kind, long UseId, long ActionId);
public sealed record MouChenLinReceipt(CardConversionSource Source, string GameplayHash, int Turn, long ParentId, int Requested, int ForcedTarget = -1)
{
    public MouChenLinStage Stage { get; init; }
    public int Target { get; init; } = -1;
    public string Branch { get; init; } = "";
    public int Index { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool NativeReturned { get; init; }
    public MouChenLinUsePlan? Plan { get; init; }
    public MouChenLinNativeUse? Native { get; init; }
    private readonly IReadOnlyList<MouChenLinCard> _cards = [];
    public IReadOnlyList<MouChenLinCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _used = [];
    public IReadOnlyList<int> Used { get => _used; init => _used = Array.AsReadOnly(value.ToArray()); }
}
// The platform race adapter will own admission. These immutable local fields
// retain the actual source and successful physical Give invoices, never seats
// ordered as a substitute for response speed.
public sealed record MouChenLinGift(int Giver, int CardId, CardLocation From, long Sequence);
public enum MouChenLinInvitationStage { Racing, MovementChildren, ChooseTarget, Written }
public sealed record MouChenLinInvitation(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase)
{
    public MouChenLinInvitationStage Stage { get; init; }
    public PromptId PromptId { get; init; }
    public int? FirstGiver { get; init; }
    public bool Closed { get; init; }
    public int PendingGiver { get; init; } = -1;
    public MouChenLinCard? PendingCard { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<int> _eligible = [], _answered = [];
    public IReadOnlyList<int> Eligible { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Answered { get => _answered; init => _answered = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<MouChenLinGift> _gifts = [];
    public IReadOnlyList<MouChenLinGift> Gifts { get => _gifts; init => _gifts = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenLinReceipt? MouChenLin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenLinInvitation? MouChenLinInvitation { get; init; }
}
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenLinNativeUse? MouChenLinWrittenUse { get; init; } }
public sealed record MouChenLinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, long ParentId, int Requested, int ForcedTarget) : IGameEvent;
public sealed record MouChenLinRevealedEvent(long FrameId, int Index, int CardId, CardKind Kind) : IGameEvent;
public sealed record MouChenLinBranchEvent(long FrameId, string Branch, int Target) : IGameEvent;
public sealed record MouChenLinMovedEvent(long FrameId, MouChenLinStage Stage, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouChenLinUseIssuedEvent(MouChenLinNativeUse Origin) : IGameEvent;
public sealed record MouChenLinUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouChenLinGiftPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int Giver, int CardId, CardLocation From, long Sequence) : IGameEvent;
public sealed record MouChenLinFirstGiftEvent(long FrameId, int Giver, int CardId, long Sequence) : IGameEvent;
public sealed record MouChenLinInvitationStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, PromptId PromptId) : IGameEvent;
public sealed record MouChenLinInvitationAnswerEvent(long FrameId, int Actor, bool Gave) : IGameEvent;
public sealed record MouChenLinInvitationClosedEvent(long FrameId, int? FirstGiver) : IGameEvent;
public sealed record MouChenLinDamageTicketEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int Target) : IGameEvent;
public sealed record MouChenLinDamageTicketConsumedEvent(long FrameId, int Target, long AttackId, int ChainCursor) : IGameEvent;
public sealed record MouChenLinDamageReceipt(long AttackId, int ChainCursor, int Target, int Source, int Inherited)
{
    public int? TransferredTarget { get; init; }
    private readonly IReadOnlyList<long> _tickets = [];
    public IReadOnlyList<long> Tickets { get => _tickets; init => _tickets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenLinDamageReceipt? MouChenLinDamage { get; init; } }
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenLinDamageReceipt? MouChenLinDamage { get; init; } }
public sealed partial record JudgmentFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenLinDamageReceipt? MouChenLinDamage { get; init; } }
public sealed record MouChenLinDamageIssuedEvent(long AttackId, int Target, int ChainCursor, int Inherited, int Count) : IGameEvent;
public sealed record MouChenLinCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouChenLinRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame f); SkillProgramStepOutcome Invite(ProgramSkillFrame f); }
internal interface IMouChenLinFacts : IProgramSelectionQuery
{
    int Turn { get; } long Sequence { get; } bool Finished { get; }
    bool Alive(int seat); bool Human(int seat); bool Friendly(int owner, int other); string Name(int seat); int HandCount(int seat);
    SkillProgram Program(string id); bool Parent(ProgramSkillFrame f); IEnumerable<T> History<T>() where T : IGameEvent;
    ActualDiscardRecoveryPhaseKey? Phase(); bool Recorded(ActualDiscardRecoveryPhaseKey phase); string SkillName(string id); string SkillDescription(string id);
    IReadOnlyList<int> Living(); IReadOnlyList<MouChenLinCard> Revealed(ProgramSkillFrame f); IReadOnlyList<MouChenLinCard> Current(IEnumerable<int> ids);
    IReadOnlyList<MouChenLinCard> He(int actor);
    MouChenLinUsePlan? Plan(ProgramSkillFrame f, int cardId, int target);
}
internal interface IMouChenLinNative : IProgramRuntimeOperations
{
    void Publish(); void Finish(ProgramSkillFrame f); bool Drain(ProgramSkillFrame f); void Reveal(ProgramSkillFrame f); void Move(ProgramSkillFrame f); void Use(ProgramSkillFrame f);
    void Give(ProgramSkillFrame f); void CloseInvitation(ProgramSkillFrame f);
}
internal sealed class ResolveMouChenLinZhuanwenDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenLinZhuanwen;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new MouChenLinHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    // Like the bound-card native Use operations, this single custom instruction
    // owns the entire binding until settlement. Native Use/Give consume their
    // actual entities first; its final cleanup returns every retained entity.
    internal static IReadOnlyList<ProgramResourceOperation> WrittenResources() => [new CreateCardSet(MouChenLinRuntime.RevealBind, 5, true), new MoveCardSet(MouChenLinRuntime.RevealBind, null, SkillProgramCardDestination.DrawPileTop)];
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => WrittenResources();
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class MouChenLinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenLinZhuanwen;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouChenLinRuntime>().Resolve(f);
}
internal sealed class ResolveMouChenLinYaozuoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenLinYaozuo;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new MouChenLinYaozuoHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => ResolveMouChenLinZhuanwenDescriptor.WrittenResources();
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class MouChenLinYaozuoHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenLinYaozuo;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouChenLinRuntime>().Invite(f);
}
