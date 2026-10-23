using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyWeiYanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IHappyWeiYanTargetQuery : IProgramSelectionQuery { bool Allows(int owner, int target); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 42300, SkillProgramEffectOp.ResolveHappyWeiYanGujing)]
internal sealed class HappyWeiYanGujingTargetFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) => queries.Get<IHappyWeiYanTargetQuery>().Allows(context.OwnerSeat, targetSeat);
}
internal abstract class HappyWeiYanDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new HappyWeiYanHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class ResolveHappyWeiYanZiguDescriptor() : HappyWeiYanDescriptor(SkillProgramEffectOp.ResolveHappyWeiYanZigu);
internal sealed class ResolveHappyWeiYanEzhiDescriptor() : HappyWeiYanDescriptor(SkillProgramEffectOp.ResolveHappyWeiYanEzhi);
internal sealed class ResolveHappyWeiYanBoneReturnDescriptor() : HappyWeiYanDescriptor(SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn);
internal sealed class ResolveHappyWeiYanGujingDescriptor() : HappyWeiYanDescriptor(SkillProgramEffectOp.ResolveHappyWeiYanGujing);
internal sealed class HappyWeiYanHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IHappyWeiYanHost>().Resolve(frame, operation);
}
internal sealed class HappyZiguCommittedContract : IProgramCommittedUseContract
{
    public string SkillId => "happy:zigu";
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (trigger.Id != "first-equipment" || trigger.Optional != true || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !trigger.IncludeResponseUses ||
            trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 || trigger.UsageLimit is not null || trigger.UsageScope is not null ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveHappyWeiYanZigu, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Happy Zigu requires its optional first actual equipment Use, before the equipment enters its slot.");
    }
}
internal sealed class HappyEzhiCommittedContract : IProgramCommittedUseContract
{
    public string SkillId => "happy:ezhi";
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (trigger.Id != "bone-use" || trigger.Optional || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !trigger.IncludeResponseUses ||
            trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 || trigger.UsageLimit is not null || trigger.UsageScope is not null ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveHappyWeiYanEzhi, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Happy Ezhi requires the exact committed true Use and its native response-use opt-in.");
    }
}

public enum HappyWeiYanStage { Hand, Slot, Replacing, Placing, DamageTarget, Damage, DamageCard, GainChildren, Drawing, DrawChildren, BoneChoice, BoneDiscardChildren, ReturnChoice, IssuingUse, NativeUse, ReturnedUse, GujingSelection, GujingDiscardChildren, Finished }
public sealed record HappyWeiYanBone(int CardId, CardKind PrintedKind, int Holder, EquipmentSlot Slot, CardConversionSource Source, string GameplayHash, long FrameId, int Sequence);
public sealed record HappyWeiYanDiscardedBone(int CardId, CardKind PrintedKind, int Holder, EquipmentSlot Slot, int Sequence);
public sealed record HappyWeiYanGujingIssue(long FrameId, CardConversionSource Source, string GameplayHash);
public sealed record HappyWeiYanBoneUse(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int Ordinal, HappyWeiYanDiscardedBone Bone, LegalAction Action, long UseId, long ActionId);
public sealed record HappyWeiYanCancellation(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, long WindowId, long ParentId, CardActionContext Action, HappyWeiYanBone Bone, int Before, int After)
{ public NativeDrawOutcome? Draw { get; init; } }
public sealed partial class CharacterState
{
    // Private durable rule state, reconstructed by command replay. An entity retains its printed category.
    internal IReadOnlyList<HappyWeiYanBone> HappyWeiYanBones { get; set; } = Array.AsReadOnly(Array.Empty<HappyWeiYanBone>());
    internal HappyWeiYanGujingIssue? HappyWeiYanGujing { get; set; }
}
public sealed record HappyWeiYanReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn)
{
    public HappyWeiYanStage Stage { get; init; }
    public int Target { get; init; } = -1;
    public int CardId { get; init; }
    public EquipmentSlot? Slot { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public CardActionContext? OriginalAction { get; init; }
    public HappyWeiYanBone? SelectedBone { get; init; }
    public long NativeParentId { get; init; }
    public int Cursor { get; init; }
    public bool SlotAbolished { get; init; }
    public HappyWeiYanBoneUse? NativeUse { get; init; }
    private readonly IReadOnlyList<HappyWeiYanBone> _selectedBones = Array.AsReadOnly(Array.Empty<HappyWeiYanBone>());
    private readonly IReadOnlyList<HappyWeiYanDiscardedBone> _discarded = Array.AsReadOnly(Array.Empty<HappyWeiYanDiscardedBone>());
    public IReadOnlyList<HappyWeiYanBone> SelectedBones { get => _selectedBones; init => _selectedBones = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<HappyWeiYanDiscardedBone> Discarded { get => _discarded; init => _discarded = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardMovementRecord> _paid = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<CardMovementRecord> Paid { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyWeiYanReceipt? HappyWeiYan { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyWeiYanBoneUse? HappyWeiYanReturn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyWeiYanCancellation? HappyWeiYanCancellation { get; init; }
    private readonly IReadOnlyList<HappyWeiYanCancellation>? _happyWeiYanResponses;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<HappyWeiYanCancellation>? HappyWeiYanResponses { get => _happyWeiYanResponses; init => _happyWeiYanResponses = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record NullificationWindowFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyWeiYanCancellation? HappyWeiYanCancellation { get; init; } }
public sealed record HappyWeiYanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, int Target, long UseId, long ActionId) : IGameEvent;
public sealed record HappyWeiYanFirstEquipmentEvent(int ActualTurn, int Actor, long UseId, long ActionId) : IGameEvent;
public sealed record HappyWeiYanBonePlacedEvent(long FrameId, CardConversionSource Source, int Holder, EquipmentSlot Slot, int Before, int After) : IGameEvent;
public sealed record HappyWeiYanMovementEvent(long FrameId, HappyWeiYanStage Stage, int Before, int After, int Count) : IGameEvent;
public sealed record HappyWeiYanCompletedEvent(long FrameId) : IGameEvent;
public sealed record HappyWeiYanEzhiPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, long WindowId, long ParentId, long ActionId, int Actor, CardKind Kind, int BoneId, CardKind BoneKind, int Before, int After, bool Canceled) : IGameEvent;
public sealed record HappyWeiYanGujingIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record HappyWeiYanGujingAbolishedEvent(long FrameId, int Ordinal, int Holder, EquipmentSlot Slot, int Before, int After) : IGameEvent;
public sealed record HappyWeiYanBoneUseIssuedEvent(long FrameId, int Ordinal, int CardId, int DiscardSequence, long UseId, long ActionId) : IGameEvent;
public sealed record HappyWeiYanBoneUseReturnedEvent(long FrameId, int Ordinal, long UseId, long ActionId) : IGameEvent;

internal static class HappyWeiYanComposition
{
    internal const string Zigu = "happy:zigu", Ezhi = "happy:ezhi", Gujing = "happy:gujing";
    [ProgramCatalogValidation(42300)] internal static void Validate(string path, SkillProgram program)
    {
        var effects = program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects)).ToArray();
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.ResolveHappyWeiYanZigu or SkillProgramEffectOp.ResolveHappyWeiYanEzhi or SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn or SkillProgramEffectOp.ResolveHappyWeiYanGujing)) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardIdentities.Count != 0 || program.CardPolicies.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Happy Wei Yan's original-entity operations cannot be combined with unrelated identities, policies or modifiers.");
        if (program.Id == Ezhi)
        {
            if (program.Activations.Count != 0 || program.Triggers is not [var use, var ended] || use is not { Id: "bone-use", Window: SkillProgramTriggerWindow.CardUseCommitted, Optional: false, IncludeResponseUses: true, OwnerRelation: SkillProgramCardActionOwnerRelation.Observer, Effects: [{ Op: SkillProgramEffectOp.ResolveHappyWeiYanEzhi, Target: SkillProgramEffectTarget.Owner }] } ||
                ended is not { Id: "ended-bones", Window: SkillProgramTriggerWindow.AfterTurnEnded, Subject: SkillProgramTriggerSubject.Owner, Optional: false, Effects: [{ Op: SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn, Target: SkillProgramEffectTarget.Owner }] } ||
                ended.TurnOwnerScope != SkillProgramTurnOwnerScope.AnyLiving || program.Triggers.Any(t => t.UsageLimit is not null || t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always) || effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
                throw new InvalidOperationException(path + ": Happy Ezhi requires one committed real Use and one chronological actual-turn-end bone return.");
            return;
        }
        if (program.Id == Gujing)
        {
            if (program.Triggers.Count != 0 || program.Activations is not [{ Id: "bone-path", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerGame: 1, UsesPerTurn: null, UsesPerPhase: null, Effects: [{ Op: SkillProgramEffectOp.ResolveHappyWeiYanGujing, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }])
                throw new InvalidOperationException(path + ": Gujing requires its once-per-game real hand bone, selected other role and actual slot abolishment.");
            return;
        }
        if (program.Id != Zigu || program.Activations.Count != 0 || program.Triggers.Count != 2 || program.ViewAs.Count != 0 || program.CardIdentities.Count != 0 || program.CardPolicies.Count != 0 ||
            program.Triggers[0] is not { Id: "damage-bone", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Any, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Optional: true } ||
            program.Triggers[1] is not { Id: "first-equipment", Window: SkillProgramTriggerWindow.CardUseCommitted, Optional: true, IncludeResponseUses: true, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor } ||
            effects.Length != 2 || effects.Any(e => e.Op != SkillProgramEffectOp.ResolveHappyWeiYanZigu || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always) ||
            program.Triggers.Any(t => t.UsageLimit is not null || t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always))
            throw new InvalidOperationException(path + ": Happy Zigu requires one damage occurrence and the first real equipment Use, each bound to its original native parent.");
    }
}
