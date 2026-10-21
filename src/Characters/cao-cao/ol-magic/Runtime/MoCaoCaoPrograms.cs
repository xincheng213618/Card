using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MoCaoCaoBachaoStage { Commit, Pay, PaymentChildren, Target, Damage, DamageChildren }
public sealed record MoCaoCaoVote(int Seat, int CardId, CardKind? PrintedKind);
public sealed record MoCaoCaoBachaoReceipt(CardConversionSource Source, string GameplayHash, long WindowId, ActualDiscardRecoveryPhaseKey Phase)
{
    public MoCaoCaoBachaoStage Stage { get; init; }
    public int Cursor { get; init; }
    public int? TargetSeat { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int PaidCardId { get; init; }
    public bool Accepted { get; init; }
    private IReadOnlyList<int> _participants = Array.AsReadOnly(Array.Empty<int>()), _protected = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<MoCaoCaoVote> _votes = Array.AsReadOnly(Array.Empty<MoCaoCaoVote>());
    public IReadOnlyList<int> Participants { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> ProtectedSeats { get => _protected; init => _protected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MoCaoCaoVote> Votes { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MoCaoCaoBaibiClaim(long DamageWindowId, long DamageId, long UseId, long ActionId, int ActorSeat, int TargetSeat,
    int PhysicalWeaponId, long VirtualIssueId, string GameplayHash, string InstanceId, string WeaponSourceId);
public sealed record MoCaoCaoBaibiReceipt(MoCaoCaoBaibiClaim Claim)
{
    public int CardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool Paid { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoCaoCaoBachaoReceipt? MoCaoCaoBachao { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoCaoCaoBaibiReceipt? MoCaoCaoBaibi { get; init; }
}
public sealed record MoCaoCaoEquipmentIssuedEvent(long IssueId, CardConversionSource Source, string GameplayHash, int Hp,
    CardKind Weapon, CardKind Armor, long CauseId, bool Initial) : IGameEvent;
public sealed record MoCaoCaoBachaoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MoCaoCaoBachaoCommittedEvent(long FrameId, int Count) : IGameEvent;
public sealed record MoCaoCaoBachaoGiftEvent(long FrameId, int ParticipantSeat, bool NonBasic, long Before, long After, bool Arrived) : IGameEvent;
public sealed record MoCaoCaoBachaoDamageEvent(long FrameId, int ActorSeat, int TargetSeat) : IGameEvent;
public sealed record MoCaoCaoHandPrivilegedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int CardId) : IGameEvent;
public sealed record MoCaoCaoBachaoSelfIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MoCaoCaoBaibiIssuedEvent(MoCaoCaoBaibiClaim Claim) : IGameEvent;
public sealed record MoCaoCaoBaibiObtainedEvent(long FrameId, long DamageWindowId, int ActorSeat, int TargetSeat, long Before, long After, bool Arrived) : IGameEvent;
public sealed record MoCaoCaoCompletedEvent(long FrameId, string SkillId) : IGameEvent;

internal interface IMoCaoCaoFuzaiHost : IProgramRuntimeOperations { SkillProgramStepOutcome Initialize(ProgramSkillFrame frame); }
internal interface IMoCaoCaoBachaoHost : IProgramRuntimeOperations { SkillProgramStepOutcome Start(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MoCaoCaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MoCaoCaoHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(Op switch
    {
        SkillProgramEffectOp.InitializeMoCaoCaoFuzai => SkillProgramTriggerWindow.GameStarting,
        SkillProgramEffectOp.ResolveMoCaoCaoBachao => SkillProgramTriggerWindow.PlayPhaseStarting,
        _ => SkillProgramTriggerWindow.AfterDamageApplied
    })];
}
internal sealed class InitializeMoCaoCaoFuzaiDescriptor : MoCaoCaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMoCaoCaoFuzai; }
internal sealed class ResolveMoCaoCaoBachaoDescriptor : MoCaoCaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoCaoCaoBachao;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class ResolveMoCaoCaoBaibiDescriptor : MoCaoCaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoCaoCaoBaibi; }
internal sealed class MoCaoCaoHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => operation == SkillProgramEffectOp.InitializeMoCaoCaoFuzai
        ? host.GetCapability<IMoCaoCaoFuzaiHost>().Initialize(frame) : host.GetCapability<IMoCaoCaoBachaoHost>().Start(frame, operation);
}
