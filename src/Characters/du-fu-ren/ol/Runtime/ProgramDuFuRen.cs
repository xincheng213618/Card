using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DuFuRenStage { GiftChoice, GiftChildren, RedChoice, RecoveryChildren }
public sealed record DuFuRenGain(long BatchId, int OwnerSeat, int RecipientSeat, int CardId,
    long EntrySequence, long GainSequence, CardLocation From, Suit Suit);
public sealed partial record CardMovementBatchContext
{
    private readonly IReadOnlyList<DuFuRenGain>? _duFuRenGains;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DuFuRenGain>? DuFuRenGains { get => _duFuRenGains; init => _duFuRenGains = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record DuFuRenReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long WindowId, int Turn, int TurnOwner, DuFuRenStage Stage, int? DamageSourceSeat = null,
    long? DamageFrameId = null, long? BatchId = null, int? RecipientSeat = null,
    int? CardId = null, CardLocation? From = null, long Before = 0, long After = 0, long GiftSequence = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuFuRenReceipt? DuFuRen { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuFuRenDamageReceipt? DuFuRenDamage { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuFuRenDamageReceipt? DuFuRenDamage { get; init; }
}
public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuFuRenDamageReceipt? DuFuRenDamage { get; init; }
}
public sealed record DuFuRenStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int Turn, int TurnOwner, DuFuRenStage Stage, long? DamageFrameId, long? BatchId) : IGameEvent;
public sealed record DuFuRenCompletedEvent(long FrameId, bool Applied) : IGameEvent;

internal interface IDuFuRenHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class DuFuRenDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new DuFuRenHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
}
internal sealed class DuFuRenHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IDuFuRenHost>().Execute(f, operation);
}
internal static class DuFuRenComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveYiseOwnedGain or SkillProgramEffectOp.GiveForNextActualPhases;
    [ProgramCatalogValidation(180)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op)))) throw new InvalidOperationException($"{path}: Du Fu Ren requires its native gain/preparation/off-turn damage boundaries.");
        foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))))
        {
            if (t.Effects is not [var e] || !IsOperation(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
                t.Subject != SkillProgramTriggerSubject.Owner || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null ||
                t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.DeferredTurnEndOnly || t.AllowNoEventTarget ||
                t.IncludeResponseUses || t.NoDyingAtActivation || t.SingleActionInstance || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution ||
                t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.OwnerRelation is not null || t.GainPhaseQualification is not null ||
                t.ExcludedReasons.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.DestinationZones.Count != 0 ||
                (e.Op == SkillProgramEffectOp.ResolveYiseOwnedGain
                    ? t.Window != SkillProgramTriggerWindow.CardsMoved || t.Optional || !t.SourceZones.SequenceEqual([CardZoneKind.Hand]) || t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own
                    : t.Window is not (SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.AfterDamageApplied) || !t.Optional || t.SourceZones.Count != 0 ||
                      t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
                      t.DamageOccurrence != (t.Window == SkillProgramTriggerWindow.AfterDamageApplied ? SkillProgramDamageOccurrence.PerDamage : null)))
                throw new InvalidOperationException($"{path}: Du Fu Ren requires one unfiltered mandatory actual owned-gain instruction or optional actual preparation/off-turn after-damage gift.");
        }
    }
}
