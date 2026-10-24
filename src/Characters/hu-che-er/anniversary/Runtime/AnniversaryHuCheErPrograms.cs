using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryHuCheErHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class AnniversaryHuCheErDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryHuCheErHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversaryHuCheErDaoji ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeDrawInvocation);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch
    { SkillProgramEffectOp.ResolveAnniversaryHuCheErDaoji => SkillProgramTriggerWindow.CardUseTargetsFinalized,
      SkillProgramEffectOp.RecordAnniversaryHuCheErFuzhongGain => SkillProgramTriggerWindow.CardsGained, _ => SkillProgramTriggerWindow.TurnEnding })];
}
internal sealed class ResolveAnniversaryHuCheErDaojiDescriptor : AnniversaryHuCheErDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHuCheErDaoji; }
internal sealed class RecordAnniversaryHuCheErFuzhongGainDescriptor : AnniversaryHuCheErDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordAnniversaryHuCheErFuzhongGain; }
internal sealed class SpendAnniversaryHuCheErFuzhongDescriptor : AnniversaryHuCheErDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendAnniversaryHuCheErFuzhong; }
internal sealed class AnniversaryHuCheErHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryHuCheErHost>().Execute(f, op);
}
internal static class AnniversaryHuCheErComposition
{
    internal const string Daoji = "anniversary:daoji", Fuzhong = "anniversary:fuzhong";
    [ProgramCatalogValidation(49500)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryHuCheErDaoji or SkillProgramEffectOp.RecordAnniversaryHuCheErFuzhongGain or SkillProgramEffectOp.SpendAnniversaryHuCheErFuzhong)) return;
        bool Basic(SkillProgramTrigger t) => t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && !t.IncludeResponseUses;
        var valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardIdentities.Count == 0 && p.CardPolicies.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0 && p.Triggers.All(Basic);
        if (p.Id == Daoji) valid &= p.Triggers is [var t] && t.Id == "first-foreign-weapon" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && !t.SingleActionInstance && t.Optional && t.CardCategories.SequenceEqual(new[] { SkillProgramCardCategory.Equipment }) && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryHuCheErDaoji, Target: SkillProgramEffectTarget.Owner }];
        else valid &= p.Id == Fuzhong && p.Triggers is [var gain, var end] && gain.Id == "foreign-turn-gain" && gain.Window == SkillProgramTriggerWindow.CardsGained && gain.Subject == SkillProgramTriggerSubject.Owner && gain.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && gain.DestinationZones.SequenceEqual(new[] { CardZoneKind.Hand }) && !gain.Optional && gain.Effects is [{ Op: SkillProgramEffectOp.RecordAnniversaryHuCheErFuzhongGain, Target: SkillProgramEffectTarget.Owner }] && end.Id == "ending-four" && end.Window == SkillProgramTriggerWindow.TurnEnding && end.Subject == SkillProgramTriggerSubject.Owner && !end.Optional && end.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && end.Condition is { Kind: SkillProgramTriggerConditionKind.Compare, Comparison: SkillProgramComparisonOperator.GreaterThanOrEqual, Left.Kind: SkillProgramTriggerValueKind.OwnerAttributedMarkerCount, Left.Marker: PlayerMarkerKind.AnniversaryHuCheErZhong, Right.Kind: SkillProgramTriggerValueKind.IntegerConstant, Right.Value: 4 } && end.Effects is [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLiving }, { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 }, { Op: SkillProgramEffectOp.SpendAnniversaryHuCheErFuzhong, Target: SkillProgramEffectTarget.Owner }];
        if (!valid) throw new InvalidOperationException(path + ": Hu Che Er requires the first foreign actual Weapon Use, per-batch foreign-turn hand gain and native ending damage followed by four-marker removal.");
    }
}
public enum AnniversaryHuCheErStage { Choice, MovementChildren }
public sealed record AnniversaryHuCheErReceipt(CardConversionSource Source, string Hash, long ParentId, long UseId, long ActionId, int Actor, int Turn, int TurnOwner, AnniversaryHuCheErStage Stage)
{
    public long Before { get; init; }
    public long After { get; init; }
    public HappyWeiYanGainRequestSettlement? Gain { get; init; }
    private IReadOnlyList<int> _materials = [];
    private IReadOnlyList<CardMovementRecord> _moves = [];
    public IReadOnlyList<int> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHuCheErReceipt? AnniversaryHuCheEr { get; init; } }
public sealed record AnniversaryHuCheErStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, long UseId, long ActionId, int Actor, int Turn, int TurnOwner) : IGameEvent;
public sealed record AnniversaryHuCheErWeaponClaimedEvent(long FrameId, long UseId, long ActionId, int Recipient, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryHuCheErSlashForbiddenEvent(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, int Actor, int Turn, int TurnOwner) : IGameEvent;
public sealed record AnniversaryHuCheErGainRecordedEvent(long FrameId, CardConversionSource Source, string Hash, long BatchId, int Turn, int TurnOwner, int Before, int After) : IGameEvent;
public sealed record AnniversaryHuCheErFourRemovedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int Before, int After) : IGameEvent;
public sealed record AnniversaryHuCheErCompletedEvent(long FrameId) : IGameEvent;
