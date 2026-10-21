using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MouKrActionObservedEvent(long ActionId, int ActorSeat, CardKind Kind, Suit? Suit,
    SkillProgramCardCategory Category, bool MatchesPrevious, CardConversionSource? Source, string? GameplayHash) : IGameEvent;
public sealed record MouKrXianEarnedEvent(long FrameId, int OwnerSeat, long ActionId, int Actual) : IGameEvent;
public sealed record MouKrLiwenStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId) : IGameEvent;
public sealed record MouKrXianAllocatedEvent(long FrameId, int Index, int RecipientSeat) : IGameEvent;
public sealed record MouKrLiwenScheduledEvent(long FrameId, int Index, int Seat, int Count) : IGameEvent;
public sealed record MouKrLiwenDeclinedEvent(long FrameId, int Index, int Seat, int Removed) : IGameEvent;
public sealed record MouKrLiwenDrawReturnedEvent(long FrameId, int Index, int Requested, int Actual) : IGameEvent;
public sealed record MouKrLiwenNativeIssuedEvent(long FrameId, int Index, long UseId, long ActionId, int ActorSeat, int ProviderSeat) : IGameEvent;
public sealed record MouKrLiwenNativeReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MouKrLiwenCompletedEvent(long FrameId) : IGameEvent;
public sealed record MouKrVote(int Seat, bool Agree);
public sealed record MouKrScheduledSeat(int Seat, int Count);
public enum MouKrLiwenStage { Allocate, UseChoice, UseDraft, Issuing, NativeUse, Children, DrawChildren, Complete }
public sealed record MouKrLiwenReceipt(CardConversionSource Source, string GameplayHash, long WindowId)
{
    public MouKrLiwenStage Stage { get; init; }
    public int AllocationBudget { get; init; }
    public int Cursor { get; init; }
    public int RequestedDraw { get; init; }
    public bool DrawReturned { get; init; }
    private LegalAction? _selectedAction;
    public LegalAction? SelectedAction { get => _selectedAction; init => _selectedAction = value is null ? null : MouKongRongFreeze.Action(value); }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private PendingDecision? _originalDecision;
    public PendingDecision? OriginalDecision { get => _originalDecision; init => _originalDecision = value is null ? null : RequestedDeckBasicFrame.FreezeDecision(value); }
    private IReadOnlyList<int> _recipients = [], _cards = [], _targets = [];
    private IReadOnlyList<MouKrScheduledSeat> _schedule = [];
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouKrScheduledSeat> Schedule { get => _schedule; init => _schedule = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouKrLiwenNativeOrigin(long FrameId, int Index, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActorSeat, int ProviderSeat);
public sealed record MouKrZhengyiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long AttackId, int VictimSeat, int Amount) : IGameEvent;
public sealed record MouKrZhengyiVoteEvent(long FrameId, int Index, int Seat, bool Agree) : IGameEvent;
public sealed record MouKrZhengyiPayerEvent(long FrameId, int Index, int Seat, int Hp) : IGameEvent;
public sealed record MouKrZhengyiCommittedEvent(long FrameId, bool Prevent, int PayerCount) : IGameEvent;
public sealed record MouKrZhengyiHpIssuedEvent(long FrameId, int Index, int Seat, int Amount) : IGameEvent;
public sealed record MouKrZhengyiCompletedEvent(long FrameId) : IGameEvent;
public enum MouKrZhengyiStage { Voting, Committed, Paying, HealthChildren, Complete }
public sealed record MouKrZhengyiReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, long AttackId, int VictimSeat, int Amount)
{
    public MouKrZhengyiStage Stage { get; init; }
    public int Cursor { get; init; }
    public bool Prevent { get; init; }
    private IReadOnlyList<int> _eligible = [];
    private IReadOnlyList<MouKrVote> _votes = [];
    private IReadOnlyList<MouKrScheduledSeat> _payers = [];
    public IReadOnlyList<int> Eligible { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouKrVote> Votes { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouKrScheduledSeat> Payers { get => _payers; init => _payers = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouKrLiwenReceipt? MouKrLiwen { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouKrZhengyiReceipt? MouKrZhengyi { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouKrLiwenNativeOrigin? MouKrLiwenOrigin { get; init; }
}
internal interface IMouKongRongFacts : IProgramSelectionQuery
{
    SkillProgram Program(string id);
    string Name(int seat);
    long MovementSequence { get; }
    bool GameOver { get; }
    bool Alive(int seat);
    bool Human(int seat);
    bool Friendly(int actor, int target);
    int Hp(int seat);
    int Xian(int seat);
    IReadOnlyList<int> Living { get; }
    IReadOnlyList<T> History<T>() where T : IGameEvent;
    bool ParentMatches(ProgramSkillFrame frame);
    CardActionContext? Action(ProgramSkillFrame frame);
    MouKrZhengyiStartedEvent? Damage(ProgramSkillFrame frame);
    IReadOnlyList<LegalAction> Actions(ProgramSkillFrame frame, int seat);
    bool DraftValid(LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets);
}
internal interface IMouKongRongNative : IProgramRuntimeOperations
{
    int ChangeXian(ProgramSkillFrame frame, int seat, int delta);
    void IssueUse(ProgramSkillFrame frame, int actor, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets);
    SkillProgramStepOutcome LoseHp(ProgramSkillFrame frame, int seat, int amount);
    void PreventDamage(ProgramSkillFrame frame);
    bool Drain(ProgramSkillFrame frame);
    void Finish(ProgramSkillFrame frame);
    void Publish();
}
internal interface IMouKongRongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal static class MouKongRongFreeze
{
    internal static LegalAction Action(LegalAction a) => a with {
        TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()),
        AdditionalConversionSources = Array.AsReadOnly((a.AdditionalConversionSources ?? []).ToArray()),
        SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()),
        SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),
        SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()),
        SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(x => (IReadOnlyList<int>)Array.AsReadOnly(x.ToArray())).ToArray()) };
}
internal sealed class MouKongRongHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IMouKongRongHost>().Resolve(frame, operation);
}
internal abstract class MouKongRongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MouKongRongHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouKongRongLiwenDescriptor : MouKongRongDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouKongRongLiwen;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.WholeActualUseDeclaration);
}
internal sealed class ResolveMouKongRongZhengyiDescriptor : MouKongRongDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouKongRongZhengyi;
}
internal sealed class PayMouKongRongZhengyiDescriptor : MouKongRongDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayMouKongRongZhengyi;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
}
