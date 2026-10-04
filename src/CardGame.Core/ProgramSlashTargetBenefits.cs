using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SlashTargetBenefitStage { Offered, Declined, Paid, CancellationQualified, Settled, Cancelled }
public enum SlashTargetBenefitReturn { FinalizedSlash, LegacyVirtualSlash, DodgeCancelled, AfterActualTargetsSlash, AfterActualTargetsLegacy }
public enum SlashTargetBenefitDraftStage { ChoosingBenefit, ChoosingDiscard, PaidChildren, Complete }

// Scalar-only identities/receipts. Entity IDs stay on the trusted owning frame.
public sealed record SlashTargetBenefitReceipt(long OfferWindowId, int OfferCandidateIndex,
    ActualUseTargetIdentity Use, CardConversionSource Source, string GameplayHash, string SettlementBinding,
    SlashTargetBenefitStage Stage, long ProducerProgramId = 0, bool DrawBenefit = false,
    int? PaidCardId = null, CardLocation? PaidFrom = null, long SequenceBefore = 0,
    long SequenceAfter = 0, int ActualDrawCount = 0);
public sealed record SlashTargetBenefitDraft(SlashTargetBenefitReceipt Receipt,
    SlashTargetBenefitDraftStage Stage, bool Settlement, long SequenceBefore = 0,
    long SequenceAfter = 0, int? PaidCardId = null, CardLocation? PaidFrom = null);

public sealed record SlashTargetBenefitWindowFrame(long Id, long ParentFrameId,
    SlashTargetBenefitReturn ReturnKind, IReadOnlyList<ProgramTriggerCandidate> Candidates,
    IReadOnlyList<ProgramSkillWindowContext> Contexts, int CandidateIndex = 0,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.SlashTargetBenefitWindow, Step)
{
    private readonly IReadOnlyList<ProgramTriggerCandidate> _candidates = Array.AsReadOnly(Candidates.ToArray());
    public IReadOnlyList<ProgramTriggerCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<ProgramSkillWindowContext> _contexts = Array.AsReadOnly(Contexts.Select(FreezeContext).ToArray());
    public IReadOnlyList<ProgramSkillWindowContext> Contexts { get => _contexts; init => _contexts = Array.AsReadOnly(value.Select(FreezeContext).ToArray()); }
    // Only scalar context data is admitted for these two standalone operations.
    private static ProgramSkillWindowContext FreezeContext(ProgramSkillWindowContext c) =>
        c.Facts is null && c.CardUse is null && c.MovementBatch is null && c.HpChange is null &&
        c.Judgment is null && c.JudgmentReplacement is null && c.ProgramTarget is null &&
        c.ActualUseTarget is null && c.EarnedBenefit is null && c.PrepDiscardPromise is null &&
        c.ActualEndedEquipment is null && c.DamageFrameId is null && c.Amount == 0 &&
        c.MovementIndex is null && c.ResumeCandidateIndex is null && c.OptionalChooserSeat is null && c.SlashTargetBenefit is not null
            ? c : throw new InvalidOperationException("A Slash benefit window requires scalar-only exact contexts.");
}

public sealed record SlashTargetBenefitOfferedEvent(long CardUseFrameId, long WindowId, int CandidateIndex,
    int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record SlashTargetBenefitPaidEvent(long CardUseFrameId, long ProgramFrameId, long WindowId,
    int CandidateIndex, int ActorSeat, int TargetSeat, CardConversionSource Source, string GameplayHash,
    bool DrawBenefit, long SequenceBefore, long SequenceAfter, int ActualCount) : IGameEvent;
public sealed record SlashTargetBenefitCancellationEvent(long CardUseFrameId, long WindowId,
    int OfferCandidateIndex, int ActorSeat, int TargetSeat) : IGameEvent;
public sealed record SlashTargetBenefitSettledEvent(long CardUseFrameId, long ProgramFrameId,
    long OfferWindowId, int OfferCandidateIndex, int ActorSeat, int TargetSeat,
    bool Paid, long SequenceBefore, long SequenceAfter) : IGameEvent;

internal interface ISlashTargetBenefitProgramHost
{
    SkillProgramStepOutcome OfferSlashTargetBenefit(ProgramSkillFrame frame, string settlementBinding);
    SkillProgramStepOutcome SettleDodgeCancelledSlashBenefit(ProgramSkillFrame frame);
}
internal abstract class SlashTargetBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly(Op == SkillProgramEffectOp.OfferSlashTargetBenefit
            ? ["op", "target", "settlementBinding", "condition"] : ["op", "target", "condition"]);
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), stateId: Op == SkillProgramEffectOp.OfferSlashTargetBenefit ? r.RequiredIdentifier("settlementBinding") : null);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.OfferSlashTargetBenefit
            ? SkillProgramTriggerWindow.ActualSlashTargetBenefit : SkillProgramTriggerWindow.SlashDodgeCancelledBenefit)];
}
internal sealed class OfferSlashTargetBenefitDescriptor : SlashTargetBenefitDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferSlashTargetBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferSlashTargetBenefitHandler();
}
internal sealed class SettleDodgeCancelledSlashBenefitDescriptor : SlashTargetBenefitDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new SettleDodgeCancelledSlashBenefitHandler();
}
public sealed class OfferSlashTargetBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferSlashTargetBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((ISlashTargetBenefitProgramHost)h).OfferSlashTargetBenefit(f, e.StateId!);
}
public sealed class SettleDodgeCancelledSlashBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((ISlashTargetBenefitProgramHost)h).SettleDodgeCancelledSlashBenefit(f);
}
internal static class SlashTargetBenefitComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, SkillProgramTriggerSubject? subject, bool optional)
    {
        if (effects.Any(e => e.Op is SkillProgramEffectOp.OfferSlashTargetBenefit or SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit) ||
            window is SkillProgramTriggerWindow.ActualSlashTargetBenefit or SkillProgramTriggerWindow.SlashDodgeCancelledBenefit)
        {
            var expected = window == SkillProgramTriggerWindow.ActualSlashTargetBenefit ? SkillProgramEffectOp.OfferSlashTargetBenefit :
                window == SkillProgramTriggerWindow.SlashDodgeCancelledBenefit ? SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit : (SkillProgramEffectOp?)null;
            if (expected is null || subject != SkillProgramTriggerSubject.Owner || optional || effects.Count != 1 || effects[0].Op != expected ||
                effects[0].Condition.Kind != SkillProgramConditionKind.Always)
                throw new InvalidOperationException($"{path}: Slash target benefit/settlement requires its exact standalone mandatory owner window; benefit choice is internal.");
        }
    }
    internal static void ValidateProgram(SkillProgram p)
    {
        foreach (var t in p.Triggers.Where(t => t.Window is SkillProgramTriggerWindow.ActualSlashTargetBenefit or SkillProgramTriggerWindow.SlashDodgeCancelledBenefit))
            if (t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.UsageScope is not null ||
                t.ChoiceGroup is not null || t.EvaluateConditionAtResolution)
                throw new InvalidOperationException("A Slash benefit/settlement uses its own exact per-use target qualification and cannot add outer condition, marker, usage or choice-group costs.");
        foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferSlashTargetBenefit)))
            if (p.Triggers.SingleOrDefault(x => x.Id == t.Effects[0].StateId) is not
                { Window: SkillProgramTriggerWindow.SlashDodgeCancelledBenefit, Optional: false, Effects: [{ Op: SkillProgramEffectOp.SettleDodgeCancelledSlashBenefit }] })
                throw new InvalidOperationException("A Slash benefit must reference the same program's exact settlement binding.");
    }
}
