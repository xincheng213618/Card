using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryMaTieHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class AnniversaryMaTieDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryMaTieHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.WholeActualUseDeclaration);
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveAnniversaryMaTieQuxianReward => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)],
        SkillProgramEffectOp.RecordAnniversaryMaTieZhuiji => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)], _ => []
    };
}
internal sealed class ResolveAnniversaryMaTieQuxianDescriptor : AnniversaryMaTieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian; }
internal sealed class ResolveAnniversaryMaTieQuxianRewardDescriptor : AnniversaryMaTieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryMaTieQuxianReward; }
internal sealed class RecordAnniversaryMaTieZhuijiDescriptor : AnniversaryMaTieDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordAnniversaryMaTieZhuiji; }
internal sealed class AnniversaryMaTieHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryMaTieHost>().Resolve(f, op);
}
internal static class AnniversaryMaTieComposition
{
    internal const string Quxian = "anniversary:quxian", Zhuiji = "anniversary:zhuiji";
    [ProgramCatalogValidation(46700)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = p.Triggers.SelectMany(t => t.Effects).Select(e => e.Op).ToArray();
        if (!ops.Any(o => o is SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian or SkillProgramEffectOp.ResolveAnniversaryMaTieQuxianReward or SkillProgramEffectOp.RecordAnniversaryMaTieZhuiji)) return;
        bool Effect(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null;
        var valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0;
        if (p.Id == Quxian) valid &= p.Triggers is [var a, var b, var reward] && a.Id == "turn-start" && a.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && a.Subject == SkillProgramTriggerSubject.Owner && a.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && a.Optional && Effect(a, SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian) && b.Id == "turn-ended" && b.Window == SkillProgramTriggerWindow.AfterTurnEnded && b.Subject == SkillProgramTriggerSubject.Owner && b.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && b.Optional && Effect(b, SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian) && reward.Id == "issued-slash-reward" && reward.Window == SkillProgramTriggerWindow.CardUseCommitted && reward.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && !reward.Optional && !reward.IsSkillInvocation && !reward.IncludeResponseUses && reward.CardKinds.SequenceEqual(new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }) && Effect(reward, SkillProgramEffectOp.ResolveAnniversaryMaTieQuxianReward);
        else valid &= p.Id == Zhuiji && p.Triggers is [var t] && t.Id == "completed-other-targets" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && t.SingleActionInstance && !t.Optional && !t.IncludeResponseUses && Effect(t, SkillProgramEffectOp.RecordAnniversaryMaTieZhuiji);
        if (!valid) throw new InvalidOperationException(path + ": Ma Tie requires both actual own turn boundaries, exact issued-use reward and completed directed distance composition.");
    }
    internal static LegalAction Freeze(LegalAction a) => a with { TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()), AdditionalConversionSources = a.AdditionalConversionSources is null ? null : Array.AsReadOnly(a.AdditionalConversionSources.ToArray()), SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()), SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()), SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(s => (IReadOnlyList<int>)Array.AsReadOnly(s.ToArray())).ToArray()) };
}
public enum AnniversaryMaTieStage { ObtainChildren, Target, SlashChoice, Issuing, NativeUse, NativeReturned, Penalties, HpChildren, RewardDrawing, RewardChildren }
public sealed record AnniversaryMaTieReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, int TurnOwner, AnniversaryMaTieStage Stage)
{
    public int Target { get; init; } = -1;
    public int Cursor { get; init; }
    public int PenaltyCursor { get; init; }
    public bool TargetDamaged { get; init; }
    public int ObtainedCard { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int Provider { get; init; } = -1;
    private LegalAction? _selectedAction;
    private PendingDecision? _originalDecision;
    public LegalAction? SelectedAction { get => _selectedAction; init => _selectedAction = value is null ? null : AnniversaryMaTieComposition.Freeze(value); }
    public PendingDecision? OriginalDecision { get => _originalDecision; init => _originalDecision = value is null ? null : RequestedDeckBasicFrame.FreezeDecision(value); }
    public bool FactionTried { get; init; }
    public FactionResponsePolicySource? FactionPolicy { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long RewardParent { get; init; }
    public int HpTarget { get; init; } = -1;
    public bool HpPaid { get; init; }
    public int HpBefore { get; init; }
    public int HpAfter { get; init; }
    private IReadOnlyList<int> _actors = [], _used = [], _materials = [];
    public IReadOnlyList<int> Actors { get => _actors; init => _actors = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Used { get => _used; init => _used = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryMaTieNativeOrigin(long FrameId, CardConversionSource Source, string Hash, int Index, long UseId, long ActionId, int Actor, int Provider, int Target);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryMaTieReceipt? AnniversaryMaTie { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryMaTieNativeOrigin? AnniversaryMaTieOrigin { get; init; } }
public sealed record AnniversaryMaTieStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, int TurnOwner) : IGameEvent;
public sealed record AnniversaryMaTieQueueEvent(long FrameId, int Target, int Index, int Actor) : IGameEvent;
public sealed record AnniversaryMaTieSelectedEvent(long FrameId, int Target, int Count) : IGameEvent;
public sealed record AnniversaryMaTieSlashIssuedEvent(AnniversaryMaTieNativeOrigin Origin) : IGameEvent;
public sealed record AnniversaryMaTieSlashReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryMaTieRewardIssuedEvent(long FrameId, long ParentId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryMaTieRewardReturnedEvent(long FrameId, long ParentId, long UseId, int ActualCount, long Before, long After) : IGameEvent;
public sealed record AnniversaryMaTieTargetDamagedEvent(long FrameId, long UseId, long DamageId, int Target, int Amount) : IGameEvent;
public sealed record AnniversaryMaTieHpPaidEvent(long FrameId, int Index, int Target, int Amount, int Before, int After) : IGameEvent;
public sealed record AnniversaryMaTieFactionEvent(long FrameId, int Index, int Actor, int Target, FactionResponsePolicySource Policy) : IGameEvent;
public sealed record AnniversaryMaTieCompletedEvent(long FrameId, int Used, bool Damaged) : IGameEvent;
public sealed record AnniversaryMaTieDistanceEvent(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, int Turn, int TurnOwner, int Target) : IGameEvent;
