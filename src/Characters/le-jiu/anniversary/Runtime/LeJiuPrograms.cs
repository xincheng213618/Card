using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IAnniversaryLeJiuHost { void Arm(ProgramSkillFrame f); void Bind(ProgramSkillFrame f); }
internal abstract class AnniversaryLeJiuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler => new AnniversaryLeJiuHandler(op);
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardDamageModifier, static (_, c) => c.PublicControlValue(4));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ArmAnniversaryLeJiuCuijinDescriptor() : AnniversaryLeJiuDescriptor(SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin);
internal sealed class BindAnniversaryLeJiuCuijinCompletionDescriptor() : AnniversaryLeJiuDescriptor(SkillProgramEffectOp.BindAnniversaryLeJiuCuijinCompletion);
internal sealed class AnniversaryLeJiuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host)
    {
        var h = host.GetCapability<IAnniversaryLeJiuHost>();
        if (op == SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin) h.Arm(f); else h.Bind(f);
        return SkillProgramStepOutcome.Continue;
    }
}
internal static class AnniversaryLeJiuComposition
{
    internal const string Skill = "anniversary:cuijin", Pay = "pay-original-use", Reward = "issued-undamaged-use";
    [ProgramCatalogValidation(44100)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin or SkillProgramEffectOp.BindAnniversaryLeJiuCuijinCompletion)) return;
        if (p.Id != Skill || p.Triggers.Count != 2 || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.CardIdentities.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.Triggers.Any(t => !Exact(t)))
            throw new InvalidOperationException(path + ": Cuijin requires one actual-use discard cost and its once-issued whole-use undamaged result.");
    }
    internal static bool Exact(SkillProgramTrigger t)
    {
        if (t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Priority != 0 || t.UsageScope is not null || t.UsageLimit is not null ||
            t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.IncludeResponseUses || t.OnlyDesignatedCardTargets ||
            t.SingleActionInstance || t.CardCategories.Count != 0 || !t.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel]) ||
            t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)) return false;
        if (t.Id == Pay) return t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.Optional && t.IsSkillInvocation && t.Effects is
            [{ Op: SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin, Target: SkillProgramEffectTarget.Owner, TargetReference: null },
             { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Target: SkillProgramEffectTarget.Owner, Amount: 1, Destination: SkillProgramCardDestination.DiscardPile,
                 AwaitMovementTriggers: true, ResultBind: null, ChooserRef.Kind: ProgramParticipantRef.Owner, CardOwnerRef.Kind: ProgramParticipantRef.Owner } cost,
             { Op: SkillProgramEffectOp.ArmAnniversaryLeJiuCuijin, Target: SkillProgramEffectTarget.Owner, TargetReference: null }] &&
             cost.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && cost.CardKinds.Count == 0 && cost.CardCategories.Count == 0 && cost.Suits.Count == 0 && !cost.SkipIfNoCards;
        return t.Id == Reward && t.Window == SkillProgramTriggerWindow.CardUseCompleted && !t.Optional && !t.IsSkillInvocation && t.Effects is
            [{ Op: SkillProgramEffectOp.BindAnniversaryLeJiuCuijinCompletion, Target: SkillProgramEffectTarget.Owner, TargetReference: null },
             { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 2, TargetReference: null, NumberExpression: null },
             { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.Owner, Amount: 1, TargetReference.Kind: ProgramParticipantRef.Actor, NumberExpression: null }];
    }
}
public sealed record LeJiuCuijinGrant(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, int Actor, int Turn,
    long InvocationId, int CardId, CardLocation From, CardLocation To, long MovementSequence);
public sealed record LeJiuCuijinFrame(CardConversionSource Source, string Hash, long ParentId, long UseId, long ActionId, int Actor, int Turn, int Before,
    bool Reward = false, LeJiuCuijinGrant? Grant = null);
public sealed partial record CardUseFrame
{
    private IReadOnlyList<LeJiuCuijinGrant>? _leJiuCuijin;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<LeJiuCuijinGrant>? LeJiuCuijin
    { get => _leJiuCuijin; init => _leJiuCuijin = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeJiuCuijinFrame? LeJiuCuijin { get; init; } }
public sealed record LeJiuCuijinQualifiedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, long UseId, long ActionId, int Actor, int Turn, int Before) : IGameEvent;
public sealed record LeJiuCuijinArmedEvent(LeJiuCuijinGrant Grant) : IGameEvent;
public sealed record LeJiuCuijinRewardBoundEvent(long FrameId, long GrantFrameId, long UseId, long ActionId, int OwnerSeat, int Actor) : IGameEvent;
