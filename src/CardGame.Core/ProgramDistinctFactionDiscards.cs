namespace CardGame.Core;

public sealed record ProgramDiscardReceipt(int OwnerSeat, int CardId, CardLocation Source, Suit EffectiveSuit, int MovementSequence,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CardLocation? Destination = null);
public sealed record ProgramDistinctFactionDiscardDraft(string Stage, IReadOnlyList<int> SelectedSeats,
    IReadOnlyList<int> ParticipantSeats, int ParticipantIndex, IReadOnlyList<ProgramDiscardReceipt> Receipts, int RewardIndex = 0);
internal interface IDistinctFactionDiscardHost { SkillProgramStepOutcome BeginDistinctFactionDiscards(ProgramSkillFrame frame); }
internal sealed class DiscardDistinctFactionParticipantsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDistinctFactionParticipants;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardDistinctFactionParticipantsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOwnCardDiscard,
        static (e,c) => c.ChooseOwnCardDiscard(new(SkillProgramEffectOp.ChooseOwnCardDiscard, SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e,r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireActivationEntry()];
}
public sealed class DiscardDistinctFactionParticipantsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDistinctFactionParticipants;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IDistinctFactionDiscardHost)h).BeginDistinctFactionDiscards(f);
}
