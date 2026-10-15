using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum ProgramBao2025Stage
{
    TakeTarget, TakeCard, GainChildren, ResetChoice, ResetHpChildren,
    AssaultCost, AssaultTargets, AssaultCostChildren, AssaultUse, AssaultOutcomes, AssaultOutcomeChildren,
    DyingTarget, DyingRecipientChoice, DyingSkillChildren, DyingRecoveryChildren
}
public sealed record LimitedSkillResetEntry(CardConversionSource Source, string GameplayHash, string UsageId, int Before);
public sealed record ProgramBao2025Receipt
{
    private IReadOnlyList<int> _cards = [], _targets = [], _selected = [], _damaged = [];
    private IReadOnlyList<CardLocation> _from = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    private IReadOnlyList<string> _lostGrants = [];
    public SkillProgramEffectOp Operation { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public long WindowId { get; init; }
    public long? OriginalUseId { get; init; }
    public long? OriginalActionId { get; init; }
    public int ActualTurn { get; init; }
    public int ActualTurnOwner { get; init; }
    public ProgramBao2025Stage Stage { get; init; }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedTargets { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DamagedTargets { get => _damaged; init => _damaged = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
    public IReadOnlyList<string> LostGrantIds { get => _lostGrants; init => _lostGrants = Array.AsReadOnly(value.ToArray()); }
    public int? Recipient { get; init; }
    public int? GainedCardId { get; init; }
    public CardKind? GainedCardKind { get; init; }
    public CardLocation? GainedFrom { get; init; }
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public long? AssaultUseId { get; init; }
    public long? AssaultActionId { get; init; }
    public int OutcomeCursor { get; init; }
    public int HpBefore { get; init; }
    public int HpAfter { get; init; }
    public string? GrantedSkillId { get; init; }
    public bool ReplacedSkills { get; init; }
    public bool RecoveryIssued { get; init; }
}
public sealed record ProgramBao2025StartedEvent(long FrameId, ProgramBao2025Receipt Receipt) : IGameEvent;
public sealed record ProgramBao2025MovementIssuedEvent(long FrameId, ProgramBao2025Receipt Receipt) : IGameEvent;
public sealed record ConvertedNonBasicSlashIssuedEvent(long CardUseFrameId, long ActionId, CardConversionSource Source,
    string GameplayHash, int MaterialId, CardKind MaterialKind, CardLocation From) : IGameEvent;
public sealed record ConvertedNonBasicSlashPolicy(long CardUseFrameId, long ActionId, CardConversionSource Source,
    string GameplayHash, CardActionCost Material);
public sealed record ProgramBao2025HpPaidEvent(long FrameId, CardConversionSource Source, int Before, int After) : IGameEvent;
public sealed record ProgramLimitedSkillInstancesResetEvent(long FrameId, CardConversionSource Source, IReadOnlyList<LimitedSkillResetEntry> Entries) : IGameEvent;
public sealed record ChosenAssaultUseIssuedEvent(long FrameId, CardConversionSource Source, long UseId, long ActionId, IReadOnlyList<int> SelectedTargets) : IGameEvent;
public sealed record ChosenAssaultTargetOutcomeEvent(long FrameId, long UseId, int TargetSeat, bool OwnerDealtDamage, int? RandomDiscardedCardId) : IGameEvent;
public sealed record ChosenAssaultActualDamageObservedEvent(long ProgramFrameId, long UseId, long DamageFrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record DyingRecipientSkillChoiceEvent(long FrameId, CardConversionSource Source, int RecipientSeat, bool Replace,
    string GrantedSkillId, IReadOnlyList<string> LostGrantIds) : IGameEvent;
public sealed record ProgramBao2025CompletedEvent(long FrameId, CardConversionSource Source, bool Completed) : IGameEvent;
public sealed record ChosenAssaultReturn(long ProgramFrameId, int InstructionIndex);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramBao2025Receipt? Bao2025 { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ChosenAssaultReturn? ChosenAssault { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ConvertedNonBasicSlashPolicy? NonBasicSlashPolicy { get; init; }
}
internal interface IBao2025ProgramHost { SkillProgramStepOutcome BeginBao2025(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal abstract class Bao2025Descriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new Bao2025Handler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(8));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition", "sourceBind", "skillIds");
        var reset = Op == SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill;
        var grant = Op == SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover;
        if (reset != r.Has("sourceBind") || grant != r.Has("skillIds")) throw new InvalidOperationException($"{r.Path}: exact reset/grant configuration required.");
        var ids = grant ? r.RequiredIdentifierArray("skillIds") : [];
        if (grant && (ids.Count != 2 || ids.Distinct(StringComparer.Ordinal).Count() != 2)) throw new InvalidOperationException($"{r.Path}: requires exactly two distinct recipient skills.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: reset ? r.RequiredIdentifier("sourceBind") : null, skillIds: ids);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(
        e.Op == SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill ? SkillProgramTriggerWindow.CardUseCompleted :
        e.Op == SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.DyingEntering)];
}
internal sealed class TakeConvertedSlashTargetAndResetLimitedSkillDescriptor : Bao2025Descriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill; }
internal sealed class PayTwoForChosenAssaultAndApplyTargetOutcomesDescriptor : Bao2025Descriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes; }
internal sealed class DyingRecipientChooseReplaceOrGrantThenRecoverDescriptor : Bao2025Descriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover; }
internal sealed class Bao2025Handler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IBao2025ProgramHost>().BeginBao2025(f, e);
}
internal static class Bao2025Contract
{
    internal static void ValidateReferences(ContentSkillDefinition skill, IReadOnlyDictionary<string, ContentSkillDefinition> skills)
    {
        if (skill.Program is not { } program) return;
        bool LimitedAssault(string? id) => id is not null && skills.TryGetValue(id, out var definition) &&
            (definition.Tags & SkillTag.Limited) != 0 && definition.Program?.Triggers.Any(t => t.UsageScope == SkillUsageScope.Game && t.UsageLimit == 1 &&
                t.Effects is [{ Op: SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes }]) == true;
        foreach (var trigger in program.Triggers)
        foreach (var effect in trigger.Effects)
        {
            if (effect.Op == SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill &&
                (trigger.SourceSkillId != skill.Id || !program.ViewAs.Any(v => v.Id == trigger.SourceViewAsId && v.NonBasicUnrespondableSlash) || !LimitedAssault(effect.SourceBind)))
                throw new InvalidOperationException($"Skill '{skill.Id}' requires its own exact non-basic Slash source and a configured game-limited chosen-assault reset target.");
            if (effect.Op == SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover &&
                (effect.SkillIds.Count != 2 || !skills.TryGetValue(effect.SkillIds[0], out var growth) ||
                 growth.Program?.Triggers.Any(t => t.Effects is [{ Op: SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill }]) != true || !LimitedAssault(effect.SkillIds[1])))
                throw new InvalidOperationException($"Skill '{skill.Id}' must grant the exact executable death-growth and game-limited chosen-assault capabilities in that order.");
        }
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op is SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill or
            SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes or SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover)) return;
        if (t.Effects is not [var e] || e.Condition.Kind != SkillProgramConditionKind.Always || e.Target != SkillProgramEffectTarget.Owner ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.IncludeResponseUses || t.MarkerCost is not null || t.ChoiceGroup is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
            (e.Op == SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill
                ? t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource ||
                    t.Subject is not null || t.Optional || !t.SingleActionInstance || t.SourceSkillId is null || t.SourceViewAsId is null || t.UsageScope is not null || t.UsageLimit is not null
                : t.Window != (e.Op == SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.DyingEntering) ||
                    t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || !t.Optional || t.UsageScope != SkillUsageScope.Game || t.UsageLimit != 1 ||
                    t.SingleActionInstance || t.SourceSkillId is not null || t.SourceViewAsId is not null))
            throw new InvalidOperationException($"{path}: Bao2025 requires its exact native conversion, preparation or own-Dying boundary.");
    }
}
