using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DynamicDiscardDamageStage { Choosing, Paid, DamageIssued, DamageCompleted, PenaltyIssued, Finished }
public sealed record DynamicDiscardDamageReceipt
{
    private IReadOnlyList<int> _cardIds = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _from = Array.AsReadOnly(Array.Empty<CardLocation>());
    [JsonConstructor]
    public DynamicDiscardDamageReceipt(int instructionIndex, int targetSeat, int frozenTargetHp,
        int frozenDistance, int frozenAttackRange, int actualTurn, DynamicDiscardDamageStage stage,
        IReadOnlyList<int> cardIds, IReadOnlyList<CardLocation> originalLocations)
    {
        InstructionIndex=instructionIndex;TargetSeat=targetSeat;FrozenTargetHp=frozenTargetHp;
        FrozenDistance=frozenDistance;FrozenAttackRange=frozenAttackRange;ActualTurn=actualTurn;
        Stage=stage;CardIds=cardIds;OriginalLocations=originalLocations;
    }
    public int InstructionIndex { get; init; }
    public int TargetSeat { get; init; }
    public int FrozenTargetHp { get; init; }
    public int FrozenDistance { get; init; }
    public int FrozenAttackRange { get; init; }
    public int ActualTurn { get; init; }
    public DynamicDiscardDamageStage Stage { get; init; }
    public IReadOnlyList<int> CardIds { get=>_cardIds; init=>_cardIds=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> OriginalLocations { get=>_from; init=>_from=Array.AsReadOnly(value.ToArray()); }
    public long? DamageFrameId { get; init; }
    public int? ActualDamageTargetSeat { get; init; }
    public long? DyingFrameId { get; init; }
    public bool? DyingSurvived { get; init; }
    public long PaymentSequenceBefore { get; init; }
    public long PaymentSequenceAfter { get; init; }
}
public sealed record DynamicDiscardDamagePaidEvent(long FrameId, int OwnerSeat, int TargetSeat,
    int FrozenHp, int ActualTurn, IReadOnlyList<int> CardIds) : IGameEvent
{
    private IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DynamicDiscardDamagePenaltyEvent(long FrameId, int OwnerSeat, int TargetSeat,
    long DamageFrameId, long DyingFrameId, int ActualTurn) : IGameEvent;
public sealed record DynamicDiscardDamageCompletedEvent(long FrameId,int OwnerSeat,int OriginalTargetSeat,
    long? DamageFrameId,int? ActualTargetSeat,long? DyingFrameId,bool? Survived,int ActualTurn) : IGameEvent;

public interface IDynamicDiscardDamageHost
{ SkillProgramStepOutcome DiscardTargetHpCardsAndDamage(ProgramSkillFrame frame); }
internal sealed class DiscardTargetHpCardsAndDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardTargetHpCardsAndDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardTargetHpCardsAndDamageHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (e,c) => c.Damage(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if(target!=SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("A variable HE discard damage requires selectedTarget.");
        var e=new SkillProgramEffect(Op,target,1,r.Condition());RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadTargetSet(1)];
}
public sealed class DiscardTargetHpCardsAndDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.DiscardTargetHpCardsAndDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)
        =>((IDynamicDiscardDamageHost)h).DiscardTargetHpCardsAndDamage(f);
}
