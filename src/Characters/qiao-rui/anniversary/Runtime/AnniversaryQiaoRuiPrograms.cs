using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryQiaoRuiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryQiaoRuiDescriptor : ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler => new AnniversaryQiaoRuiHandler(Op);
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
 public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
 public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch {
  SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw => SkillProgramTriggerWindow.TurnEnding,
  SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
  SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss => SkillProgramTriggerWindow.CardsMoved,
  _ => SkillProgramTriggerWindow.CardUseCompleted })];
}
internal sealed class ResolveAnniversaryQiaoRuiAishouDrawDescriptor : AnniversaryQiaoRuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw; }
internal sealed class ResolveAnniversaryQiaoRuiAishouPrepareDescriptor : AnniversaryQiaoRuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare; }
internal sealed class ResolveAnniversaryQiaoRuiAishouLastLossDescriptor : AnniversaryQiaoRuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss; }
internal sealed class ResolveAnniversaryQiaoRuiSaoweiDescriptor : AnniversaryQiaoRuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei; }
internal sealed class AnniversaryQiaoRuiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryQiaoRuiHost>().Begin(f, op); }
internal static class AnniversaryQiaoRuiComposition
{
 internal const string Aishou = "anniversary:aishou", Saowei = "anniversary:saowei";
 [ProgramCatalogValidation(49400)] internal static void Validate(string path, SkillProgram p)
 {
  if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw or SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare or SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss or SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei)) return;
  bool Effect(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
  if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0) throw new InvalidOperationException(path + ": Qiao Rui requires his original native trigger operations.");
  if (p.Id == Aishou && p.Triggers is [var draw, var prepare, var loss] && draw.Id == "ending-marked-draw" && draw.Window == SkillProgramTriggerWindow.TurnEnding && draw.Subject == SkillProgramTriggerSubject.Owner && draw.Optional && Effect(draw, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouDraw) && prepare.Id == "prepare-marked-discard" && prepare.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && prepare.Subject == SkillProgramTriggerSubject.Owner && !prepare.Optional && Effect(prepare, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouPrepare) && loss.Id == "outside-last-mark-loss" && loss.Window == SkillProgramTriggerWindow.CardsMoved && loss.Subject == SkillProgramTriggerSubject.Owner && !loss.Optional && loss.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch && loss.SourceZones.SequenceEqual([CardZoneKind.Hand]) && Effect(loss, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiAishouLastLoss)) return;
  if (p.Id == Saowei && p.Triggers is [var slash] && slash.Id == "completed-other-slash" && slash.Window == SkillProgramTriggerWindow.CardUseCompleted && slash.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && slash.Optional && !slash.IncludeResponseUses && slash.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) && Effect(slash, SkillProgramEffectOp.ResolveAnniversaryQiaoRuiSaowei)) return;
  throw new InvalidOperationException(path + ": Qiao Rui requires the exact ending draw, preparation discard, atomic outside loss and completed other Slash.");
 }
}
public enum AnniversaryQiaoRuiStage { Drawing, DrawChildren, DiscardChildren, HealthChildren, SlashChoice, Issuing, NativeSlash, NativeTail, ClaimChildren, Completed }
public sealed record AnniversaryQiaoRuiPlan(int CardId, CardLocation From, bool Adjusted = false)
{
 private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryQiaoRuiReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int Turn, AnniversaryQiaoRuiStage Stage)
{
 private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
 private IReadOnlyList<long> _batches = Array.AsReadOnly(Array.Empty<long>());
 public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
 public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
 public NativeDrawOutcome? Draw { get; init; }
 public int Requested { get; init; }
 public AnniversaryQiaoRuiPlan? Plan { get; init; }
 public long Before { get; init; }
 public long After { get; init; }
 public long UseId { get; init; }
 public long ActionId { get; init; }
 public bool Returned { get; init; }
 public bool Damaged { get; init; }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryQiaoRuiReceipt? AnniversaryQiaoRui { get; init; } }
public sealed record AnniversaryQiaoRuiUseOrigin(long FrameId, CardConversionSource Source, string Hash, int CardId, CardLocation From, long UseId, long ActionId);
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryQiaoRuiUseOrigin? AnniversaryQiaoRuiUse { get; init; } }
public sealed record AnniversaryQiaoRuiStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int Turn) : IGameEvent;
public sealed record AnniversaryQiaoRuiMarkedEvent(long FrameId, CardConversionSource Source, string Hash, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record AnniversaryQiaoRuiDrawReturnedEvent(long FrameId, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryQiaoRuiDiscardedEvent(long FrameId, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryQiaoRuiMaximumHpEvent(long FrameId, long LossBatchId, int Before, int After) : IGameEvent;
public sealed record AnniversaryQiaoRuiUseIssuedEvent(AnniversaryQiaoRuiUseOrigin Origin) : IGameEvent;
public sealed record AnniversaryQiaoRuiDamageEvent(long FrameId, long UseId, long DamageId, int Target, int Amount) : IGameEvent;
public sealed record AnniversaryQiaoRuiUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryQiaoRuiNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;
public sealed record AnniversaryQiaoRuiClaimedEvent(long FrameId, long UseId, int CardId, long Before, long After) : IGameEvent;
public sealed record AnniversaryQiaoRuiCompletedEvent(long FrameId) : IGameEvent;
