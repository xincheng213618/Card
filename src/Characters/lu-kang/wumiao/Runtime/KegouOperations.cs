using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum WuLuKangKegouStage { OpponentChoice, Pindian, PindianChildren, GainChildren, OpponentSlashChoice, SlashIssuing, NativeSlash, NativeSlashChildren, RepeatChoice, Completed }
public sealed record WuLuKangKegouQualification(int ActualTurn, int TurnOwner, int TurnOrdinal, long HistoryCutoff, bool Used, bool Played);
public sealed record WuLuKangKegouGain(int CardId, int PrintedRank, CardLocation From, CardLocation To, long Sequence);
public sealed record WuLuKangKegouReceipt(CardConversionSource Source, string GameplayHash, long WindowId, WuLuKangKegouQualification Qualification)
{
    private readonly IReadOnlyList<int> _nativeTargets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> NativeTargets { get => _nativeTargets; init => _nativeTargets = Array.AsReadOnly(value.ToArray()); }
    public bool NativeTargetsAdjusted { get; init; }
    private readonly IReadOnlyList<WuLuKangKegouGain> _gains = Array.AsReadOnly(Array.Empty<WuLuKangKegouGain>());
    public IReadOnlyList<WuLuKangKegouGain> Gains { get => _gains; init => _gains = Array.AsReadOnly(value.ToArray()); }
    public WuLuKangKegouStage Stage { get; init; }
    public int Iteration { get; init; }
    public int? OpponentSeat { get; init; }
    public long? PindianId { get; init; }
    public PindianResult? Result { get; init; }
    public bool PindianCancelled { get; init; }
    public int GainIndex { get; init; }
    public bool SlashAccepted { get; init; }
    public long NativeUseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed record WuLuKangKegouSlashOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int Iteration, long PindianId, int ActorSeat, int TargetSeat, long UseId, long ActionId);
public sealed record WuLuKangKegouStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, WuLuKangKegouQualification Qualification) : IGameEvent;
public sealed record WuLuKangKegouPindianIssuedEvent(long FrameId, int Iteration, long PindianId, int OpponentSeat) : IGameEvent;
public sealed record WuLuKangKegouPindianReturnedEvent(long FrameId, int Iteration, long PindianId, PindianResult Result) : IGameEvent;
public sealed record WuLuKangKegouGainPaidEvent(long FrameId, int Iteration, int CardId, int PrintedRank, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record WuLuKangKegouSlashDecidedEvent(long FrameId, int Iteration, int OpponentSeat, bool Accepted) : IGameEvent;
public sealed record WuLuKangKegouSlashTargetEvent(long FrameId, int Iteration, int Index, int Seat, bool Adjusted) : IGameEvent;
public sealed record WuLuKangKegouSlashIssuedEvent(WuLuKangKegouSlashOrigin Origin) : IGameEvent;
public sealed record WuLuKangKegouSlashReturnedEvent(long FrameId, int Iteration, long UseId, long ActionId) : IGameEvent;
public sealed record WuLuKangKegouRepeatDecidedEvent(long FrameId, int Iteration, bool Repeat) : IGameEvent;
public sealed record WuLuKangKegouCompletedEvent(long FrameId, int Iterations) : IGameEvent;
internal sealed class ResolveWuLuKangKegouDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangKegou;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuKangKegouHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuLuKangKegouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangKegou;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IWuLuKangHost>().Kegou(f);
}
