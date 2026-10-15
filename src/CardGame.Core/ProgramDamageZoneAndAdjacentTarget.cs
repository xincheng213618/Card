using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DamageZoneAcquisitionStage { Selecting, MovementChildren }
public sealed record DamageZoneAcquisitionReceipt
{
    private IReadOnlyList<int> _cards = [];
    private IReadOnlyList<CardLocation> _from = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public long WindowId { get; init; }
    public long DamageId { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int ActualTurn { get; init; }
    public int VictimSeat { get; init; }
    public int NextZone { get; init; }
    public DamageZoneAcquisitionStage Stage { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record DamageZoneAcquisitionStartedEvent(long ProgramFrameId, DamageZoneAcquisitionReceipt Receipt) : IGameEvent;
public sealed record DamageZoneAcquisitionPaidEvent(long ProgramFrameId, DamageZoneAcquisitionReceipt Receipt) : IGameEvent;
public sealed record DamageZoneAcquisitionCompletedEvent(long ProgramFrameId, CardConversionSource Source, int ActualTurn) : IGameEvent;
public sealed record AdjacentRedTargetDraft
{
    private IReadOnlyList<int> _original = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public long CardUseFrameId { get; init; }
    public long ActionId { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record AdjacentRedTargetAddedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId, int ActorSeat,
    CardConversionSource Source, string GameplayHash, IReadOnlyList<int> OriginalTargets, IReadOnlyList<int> AddedTargets,
    IReadOnlyList<int> ResultTargets) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DamageZoneAcquisitionReceipt? DamageZoneAcquisition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AdjacentRedTargetDraft? AdjacentRedTarget { get; init; }
}
internal interface IDamageZoneAndAdjacentTargetHost
{
    SkillProgramStepOutcome ObtainEachDamageTargetZoneThenSelfOnly(ProgramSkillFrame frame);
    SkillProgramStepOutcome JoinAdjacentRedCardTarget(ProgramSkillFrame frame);
}
internal abstract class DamageZoneAndAdjacentTargetDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new DamageZoneAndAdjacentTargetHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(6));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(e.Op == SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly
            ? SkillProgramTriggerWindow.AfterDamageApplied : SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class ObtainEachDamageTargetZoneThenSelfOnlyDescriptor : DamageZoneAndAdjacentTargetDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly; }
internal sealed class JoinAdjacentRedCardTargetDescriptor : DamageZoneAndAdjacentTargetDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.JoinAdjacentRedCardTarget; }
internal sealed class DamageZoneAndAdjacentTargetHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly
            ? host.GetCapability<IDamageZoneAndAdjacentTargetHost>().ObtainEachDamageTargetZoneThenSelfOnly(f)
            : host.GetCapability<IDamageZoneAndAdjacentTargetHost>().JoinAdjacentRedCardTarget(f);
}
internal static class DamageZoneAndAdjacentTargetContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op is SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly or SkillProgramEffectOp.JoinAdjacentRedCardTarget)) return;
        if (t.Effects is not [var e] || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.UsageScope is not null || t.UsageLimit is not null || t.IncludeResponseUses || t.OnlyDesignatedCardTargets || t.SingleActionInstance ||
            t.AllowNoEventTarget || t.MarkerCost is not null || t.ChoiceGroup is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
            (e.Op == SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly
                ? t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
                    t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || t.Subject != SkillProgramTriggerSubject.Any || t.OwnerRelation is not null
                : e.Op != SkillProgramEffectOp.JoinAdjacentRedCardTarget || t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
                    t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Subject is not null || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own))
            throw new InvalidOperationException($"{path}: damage-zone acquisition or adjacent red targeting requires its exact mandatory native boundary.");
    }
}
