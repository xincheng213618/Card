using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IMaWanProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class MaWanHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMaWanProgramHost>().Resolve(frame, operation);
}
internal abstract class MaWanDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new MaWanHandler(operation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveHunjiangSimultaneousChoicesDescriptor() : MaWanDescriptor(SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => []; }
internal sealed class OfferHunjiangPhaseSlashTargetsDescriptor() : MaWanDescriptor(SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]; }

public enum MaWanHunjiangStage { PrivateVotes, DrawChildren, AppendTargets }
public sealed record MaWanHunjiangVote(int Seat, bool AllowExtraTarget);
public sealed record MaWanHunjiangReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, int ActualTurnNumber,
    long PhaseInstanceId, MaWanHunjiangStage Stage, int DrawCount = 0, int SequenceBefore = 0, int SequenceAfter = 0,
    long? CardUseId = null, long? ActionId = null)
{
    private readonly IReadOnlyList<int> _participants = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<MaWanHunjiangVote> _votes = Array.AsReadOnly(Array.Empty<MaWanHunjiangVote>());
    private readonly IReadOnlyList<int> _selectedTargets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Participants { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MaWanHunjiangVote> Votes { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedTargets { get => _selectedTargets; init => _selectedTargets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MaWanHunjiangReceipt? MaWanHunjiang { get; init; }
}
public sealed record MaWanHunjiangStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurnNumber, long PhaseInstanceId, int ParticipantCount) : IGameEvent;
public sealed record MaWanHunjiangParticipantFrozenEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record MaWanHunjiangVoteRevealedEvent(long FrameId, int Index, int Seat, bool AllowExtraTarget, bool Unanimous) : IGameEvent;
public sealed record MaWanHunjiangEntitledEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurnNumber, long PhaseInstanceId, int TargetSeat) : IGameEvent;
public sealed record MaWanHunjiangDrawIssuedEvent(long FrameId, int OwnerSeat, int Count, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record MaWanHunjiangUseIssuedEvent(long UseId, long ActionId, long GrantFrameId, CardConversionSource Source, int TargetSeat, int ActualTurnNumber, long PhaseInstanceId) : IGameEvent;
public sealed record MaWanHunjiangTargetAddedEvent(long ProgramFrameId, long UseId, long ActionId, CardConversionSource Source, int TargetSeat, int AddedIndex) : IGameEvent;
public sealed record MaWanHunjiangRedirectedEvent(long ProgramFrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int TargetIndex, int OriginalTargetSeat, int NewTargetSeat) : IGameEvent;

public sealed record MaWanHunjiangUseReceipt(long UseId, long ActionId, int ActorSeat, int ActualTurnNumber, long PhaseInstanceId, bool Offered = false)
{
    private readonly IReadOnlyList<MaWanHunjiangEntitledEvent> _grants = Array.AsReadOnly(Array.Empty<MaWanHunjiangEntitledEvent>());
    private readonly IReadOnlyList<int> _originalTargets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _addedTargets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<MaWanHunjiangEntitledEvent> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> OriginalTargets { get => _originalTargets; init => _originalTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AddedTargets { get => _addedTargets; init => _addedTargets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MaWanHunjiangUseReceipt? MaWanHunjiang { get; init; }
}

internal static class MaWanComposition
{
    [ProgramCatalogValidation(170)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets)) ||
            p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices)))
            throw new InvalidOperationException($"{path}: Hunjiang cannot reverse its active simultaneous decision and issued finalized-use entries.");
        var uses = p.Activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices)).ToArray();
        var offers = p.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets)).ToArray();
        if (uses.Length + offers.Length == 0) return;
        if (p.Id != "ol:hunjiang" || uses.Length != 1 || offers.Length != 1 || p.Activations.Count != 1 || p.Triggers.Count != 1 ||
            p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException($"{path}: Hunjiang requires its unique paired activation and issued Slash trigger without unrelated policy entries.");
        foreach (var a in uses)
            if (a.Effects is not [{ Op: SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
                a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 16 || a.TargetKind != SkillProgramTargetKind.OtherLivingInAttackRange ||
                a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always ||
                a.Id != "simultaneous-range-choices" || a.UsageGroup != a.Id || a.MarkerCost is not null || a.CardCountExpression is not null || a.ContinueAfterOwnerDeath ||
                a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot ||
                !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) || a.EquipmentSlots.Count != 0 || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0)
                throw new InvalidOperationException($"{path}: Hunjiang requires the exact zero-card once-per-Play activation with one or more in-range other participants.");
        foreach (var t in offers)
            if (t.Effects is not [{ Op: SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
                t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Optional ||
                !t.CardKinds.SequenceEqual(new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }) || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null ||
                t.Id != "issued-phase-slash-targets" || t.UsageScope is not null || t.Subject is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
                t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 ||
                t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null ||
                t.MovementOccurrence is not null || t.DamageOccurrence is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
                t.MarkerCost is not null || t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.DynamicUsageLimit is not null || t.GainPhaseQualification is not null ||
                t.AllowNoEventTarget || t.IncludeResponseUses || t.SingleActionInstance || t.NoDyingAtActivation || t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets ||
                t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly ||
                t.MinimumRank != 1 || t.MaximumRank != 13 || t.DiscardOwnerScope != default || t.HpChangeOccurrence != default ||
                t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own)
                throw new InvalidOperationException($"{path}: Hunjiang extra targets require its mandatory native finalized Slash-use actor binding.");
    }
}
