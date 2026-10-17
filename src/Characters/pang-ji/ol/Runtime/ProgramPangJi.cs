using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum PangJiStage { CostChoice, CostChildren, ObtainChildren, Pindian, PindianChildren }
public sealed record PangJiEntity(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record BiguoTargetOrigin(long ArmFrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActualTurn, int TurnOwner);
public sealed record PangJiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int ActualTurn, int TurnOwner, PangJiStage Stage)
{
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public CardActionCost? Cost { get; init; }
    public BiguoTargetOrigin? Target { get; init; }
    public long PindianId { get; init; }
    public PindianResult? Result { get; init; }
    private readonly IReadOnlyList<PangJiEntity> _obtained = Array.AsReadOnly(Array.Empty<PangJiEntity>());
    public IReadOnlyList<PangJiEntity> Obtained { get => _obtained; init => _obtained = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PangJiReceipt? PangJi { get; init; } }
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BiguoTargetOrigin? BiguoTarget { get; init; } }

public sealed record PangJiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record PangJiCompletedEvent(long FrameId, SkillProgramEffectOp Op, bool Paid) : IGameEvent;
public sealed record BiguoCostCommittedEvent(long FrameId, int CardId, CardKind PrintedKind, CardLocation From, long Before) : IGameEvent;
public sealed record BiguoCostPaidEvent(long FrameId, long BatchId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record BiguoObtainEntityIssuedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind) : IGameEvent;
public sealed record BiguoObtainEntityPaidEvent(long FrameId, int Index, long BatchId, int CardId,
    CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record BiguoArmedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record BiguoTargetAcceptedEvent(long FrameId, BiguoTargetOrigin Origin) : IGameEvent;
public sealed record BiguoTargetResolvedEvent(long UseId, long ActionId, int OwnerSeat, bool Activated) : IGameEvent;
public sealed record BiguoPindianIssuedEvent(long FrameId, long PindianId, long ArmFrameId, long UseId, long ActionId, int OwnerSeat, long Before) : IGameEvent;
public sealed record BiguoPindianCardCategoryEvent(long FrameId, long PindianId, int Seat, int CardId,
    CardKind PrintedKind, SkillProgramCardCategory Category) : IGameEvent;
public sealed record BiguoCategoryBanEvent(long FrameId, long PindianId, int Seat,
    SkillProgramCardCategory Category, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record BiguoPindianReturnedEvent(long FrameId, long PindianId, int SourceSeat, int OpponentSeat,
    int SourceCardId, int OpponentCardId, int SourceRank, int OpponentRank) : IGameEvent;

internal interface IPangJiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class PangJiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new PangJiHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian ? SkillProgramTriggerWindow.DrawPhaseEnded : SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class ResolveBiguoDrawDescriptor() : PangJiDescriptor(SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian);
internal sealed class ResolveBiguoTargetDescriptor() : PangJiDescriptor(SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan);
internal sealed class PangJiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IPangJiHost>().Resolve(f, op);
}
internal static class PangJiComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        DouyuComposition.ValidateProgram(path, p);
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).ToArray();
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian or SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan)) return;
        if (p.Id != "ol:biguo" || p.Activations.Count != 0 || p.Triggers.Count != 2 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always)) throw new InvalidOperationException(path + ": Biguo requires his complete two native bindings.");
        var draw = p.Triggers[0]; var target = p.Triggers[1];
        if (draw.Id != "draw-end-category-gain" || draw.Window != SkillProgramTriggerWindow.DrawPhaseEnded || draw.Subject != SkillProgramTriggerSubject.Owner || !draw.Optional || draw.OwnerRelation is not null || draw.SingleActionInstance || draw.OnlyDesignatedCardTargets || draw.Effects is not [{ Op: SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian }] ||
            target.Id != "armed-target-pindian" || target.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || !target.Optional || target.OwnerRelation != SkillProgramCardActionOwnerRelation.Target || !target.SingleActionInstance || !target.OnlyDesignatedCardTargets || target.Effects is not [{ Op: SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan }] || !Plain(draw) || !Plain(target)) throw new InvalidOperationException(path + ": Biguo cannot change his real draw-end cost or issued target-trigger Pindian contract.");
    }
    private static bool Plain(SkillProgramTrigger t) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.MarkerCost is null && !t.IncludeResponseUses && !t.AllowNoEventTarget && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.SourceSkillId is null && t.SourceViewAsId is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementOccurrence is null && t.DamageOccurrence is null && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && !t.EvaluateConditionAtResolution && t.ChoiceGroup is null && t.OrderGroup is null;
}
