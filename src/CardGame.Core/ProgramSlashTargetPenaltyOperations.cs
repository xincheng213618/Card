namespace CardGame.Core;

public enum SlashTargetPenaltyStage { Offered, CostChildren, RewardChildren, Complete, Cancelled }
public enum SlashTargetPenaltyReturn { FinalizedSlash, LegacySlash, AfterActualTargetsSlash, AfterActualTargetsLegacy }
public sealed record SlashTargetPenaltyIdentity(long CardUseFrameId, long? ActionId, int ActorSeat,
    int ProviderSeat, CardKind EffectiveKind, int TargetSeat, int ActualTurnNumber, int ActualTurnOwnerSeat,
    long? LegacyProducerProgramId, CardConversionSource Source, string GameplayHash, int FrozenDistance);
public sealed record SlashTargetPenaltyVisit(int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash);
public sealed record SlashTargetPenaltyDraft(SlashTargetPenaltyIdentity Identity, SlashTargetPenaltyStage Stage,
    bool Recast = false, long SequenceBefore = 0, long SequenceAfter = 0,
    IReadOnlyList<int>? PaidCardIds = null, IReadOnlyList<CardLocation>? PaidFrom = null,
    bool DrawAttempted = false, int ActualDrawCount = 0, long RewardBefore = 0, long RewardAfter = 0)
{
    private readonly IReadOnlyList<int> _ids = Array.AsReadOnly((PaidCardIds ?? []).ToArray());
    public IReadOnlyList<int> PaidCardIds { get => _ids; init => _ids = Array.AsReadOnly((value ?? []).ToArray()); }
    private readonly IReadOnlyList<CardLocation> _from = Array.AsReadOnly((PaidFrom ?? []).ToArray());
    public IReadOnlyList<CardLocation> PaidFrom { get => _from; init => _from = Array.AsReadOnly((value ?? []).ToArray()); }
}
public sealed record SlashTargetPenaltyEntry(ProgramTriggerCandidate Candidate, SlashTargetPenaltyIdentity Identity);
public sealed record SlashTargetPenaltyWindowFrame(long Id, long ParentFrameId, SlashTargetPenaltyReturn ReturnKind,
    IReadOnlyList<SlashTargetPenaltyEntry> Entries, int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.SlashTargetPenaltyWindow, Step)
{
    private readonly IReadOnlyList<SlashTargetPenaltyEntry> _entries = Array.AsReadOnly(Entries.ToArray());
    public IReadOnlyList<SlashTargetPenaltyEntry> Entries { get => _entries; init => _entries = Array.AsReadOnly(value.ToArray()); }
}
// Offered has only public actor/target/distance/source metadata. Private costs are
// first exposed here only after their real public discard movement has happened.
public sealed record SlashTargetPenaltyOfferedEvent(long WindowId, int CandidateIndex,
    SlashTargetPenaltyIdentity Identity) : IGameEvent;
public sealed record SlashTargetPenaltyPaidEvent(long ProgramFrameId, long CardUseFrameId, int ActorSeat,
    int TargetSeat, bool Recast, IReadOnlyList<int> CardIds, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record SlashTargetPenaltyFinishedEvent(long ProgramFrameId, long CardUseFrameId, int TargetSeat,
    bool Recast, int ActualDrawCount, bool Completed) : IGameEvent;
public sealed record SlashTargetPenaltyDrawIssuedEvent(long ProgramFrameId, int TargetSeat, int RequestedCount,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;

internal interface ISlashTargetPenaltyProgramHost
{
    SkillProgramStepOutcome RequireTargetDiscardOrEquipmentRecast(ProgramSkillFrame frame);
}
internal sealed class RequireTargetDiscardOrEquipmentRecastDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new RequireTargetDiscardOrEquipmentRecastHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.ActualSlashTargetPenalty)];
}
public sealed class RequireTargetDiscardOrEquipmentRecastHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ISlashTargetPenaltyProgramHost)host).RequireTargetDiscardOrEquipmentRecast(f);
}
internal static class SlashTargetPenaltyComposition
{
    internal static void Validate(SkillProgram p)
    {
        foreach (var t in p.Triggers.Where(t => t.Window == SkillProgramTriggerWindow.ActualSlashTargetPenalty ||
            t.Effects.Any(e => e.Op == SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast)))
            if (t.Window != SkillProgramTriggerWindow.ActualSlashTargetPenalty || t.Subject != SkillProgramTriggerSubject.Owner ||
                t.Optional || t.Effects is not [{ Op: SkillProgramEffectOp.RequireTargetDiscardOrEquipmentRecast, Condition.Kind: SkillProgramConditionKind.Always }] ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.UsageScope is not null ||
                t.ChoiceGroup is not null || t.EvaluateConditionAtResolution)
                throw new InvalidOperationException("A target discard/recast requires its standalone mandatory original Slash-target window.");
    }
}
