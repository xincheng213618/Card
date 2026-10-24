using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryHouChengHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class AnniversaryHouChengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHouChengXianniang;
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryHouChengHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class AnniversaryHouChengHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHouChengXianniang;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryHouChengHost>().Begin(f);
}
internal static class AnniversaryHouChengComposition
{
    internal const string Skill = "anniversary:xianniang";
    [ProgramCatalogValidation(48700)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryHouChengXianniang)) return;
        if (p.Id != Skill || p.Triggers is not [var discard, var damage] || discard.Id != "foreign-discard" || discard.Window != SkillProgramTriggerWindow.CardsMoved || discard.Subject != SkillProgramTriggerSubject.Owner || discard.MovementOccurrence != SkillProgramMovementOccurrence.PerOwnerBatch || !discard.MovementDiscardOnly || !discard.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) || damage.Id != "after-damage" || damage.Window != SkillProgramTriggerWindow.AfterDamageApplied || damage.Subject != SkillProgramTriggerSubject.Owner || damage.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            p.Triggers.Any(t => !t.Optional || !t.IsSkillInvocation || t.UsageScope != SkillUsageScope.Round || t.UsageLimit != 1 || t.NamedUsageGroup is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveAnniversaryHouChengXianniang, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always, TargetReference: null, ActorReference: null }]) || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Xianniang requires its independent once-per-round actual foreign-discard and damage triggers.");
    }
}
public enum AnniversaryHouChengStage { Source, Obtain, ObtainChildren, Recipient, Give, GiveChildren, HpChildren, Complete }
public sealed record AnniversaryHouChengReceipt(CardConversionSource Source, string Hash, long ParentId, int Round, int Turn, int TurnOwner, AnniversaryHouChengStage Stage)
{
    public int Victim { get; init; } = -1;
    public int Recipient { get; init; } = -1;
    public int Obtained { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long BatchId { get; init; }
    public bool HpPaid { get; init; }
    public int HpBefore { get; init; }
    public int HpAfter { get; init; }
    private IReadOnlyList<int> _selected = [];
    private IReadOnlyList<CardMovementRecord> _moves = [];
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHouChengReceipt? AnniversaryHouCheng { get; init; } }
public sealed record AnniversaryHouChengStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Round, int Turn, int TurnOwner) : IGameEvent;
// Public facts contain counts and movement cutoffs, never an opponent's hidden material ids.
public sealed record AnniversaryHouChengMovementEvent(long FrameId, CardConversionSource Source, string Hash, int Round, int Turn, int TurnOwner, bool Giving, int CardOwner, int Recipient, int ActualCount, long Before, long After, long BatchId) : IGameEvent;
public sealed record AnniversaryHouChengHpPaidEvent(long FrameId, int Before, int After, int ActualLoss) : IGameEvent;
public sealed record AnniversaryHouChengCompletedEvent(long FrameId) : IGameEvent;
