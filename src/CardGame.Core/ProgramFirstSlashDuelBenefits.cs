using System.Text.Json.Serialization;

namespace CardGame.Core;

// Declaration facts form the turn ledger. They contain neither hidden entities
// nor a parallel pending card use; the original card frame owns the benefit.
public sealed record FirstSlashDuelUseRecordedEvent(int TurnNumber, int TurnOwnerSeat,
    int ActorSeat, long CardUseFrameId, long? ActionId, CardKind EffectiveKind, int Ordinal) : IGameEvent;
public sealed record FirstSlashDuelBenefitReceipt(int InstructionIndex, long WindowFrameId,
    long CardUseFrameId, long? ActionId, int TurnNumber, int TurnOwnerSeat, int TargetSeat,
    int FrozenDiamondCount, CardConversionSource Source, string GameplayHash,
    string? SelectedBranch = null, long MovementBefore = 0, long MovementAfter = 0, int DrawCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LegacyProducerFrameId = null);
public sealed record FirstSlashDuelLegacyWindowFrame(long Id, long ParentFrameId, long ProducerFrameId,
    IReadOnlyList<ProgramTriggerCandidate> Candidates, IReadOnlyList<ProgramSkillWindowContext> Contexts,
    int CandidateIndex = 0, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.FirstSlashDuelLegacyWindow, Step);
public sealed record FirstSlashDuelBenefitIssuedEvent(long ProgramFrameId,
    FirstSlashDuelBenefitReceipt Receipt) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FirstSlashDuelBenefitReceipt? FirstSlashDuelBenefit { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FirstSlashDuelDamageBonus { get; init; }
}
internal interface IFirstSlashDuelBenefitHost
{ SkillProgramStepOutcome ChooseFirstSlashDuelDiamondBenefit(ProgramSkillFrame frame); }
internal sealed class ChooseFirstSlashDuelDiamondBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new FirstSlashDuelBenefitHandler();
    // Public unknown-hand prior. The AI never reads the stored suit count.
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(6));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class FirstSlashDuelBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IFirstSlashDuelBenefitHost)host).ChooseFirstSlashDuelDiamondBenefit(frame);
}
internal static class FirstSlashDuelBenefitContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit)) return;
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit }] ||
            trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !trigger.Optional || trigger.IncludeResponseUses ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.CardKinds.Count != 0 ||
            trigger.CardCategories.Count != 0 || trigger.UsageScope is not null || trigger.UsageLimit is not null ||
            trigger.ChoiceGroup is not null || trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null || trigger.Priority != -1000)
            throw new InvalidOperationException($"{path}: the first Slash/Duel benefit requires one exact last-priority optional finalized actor trigger, with declaration-based qualification.");
    }
}
