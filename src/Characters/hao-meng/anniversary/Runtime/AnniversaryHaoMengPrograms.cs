using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryHaoMengHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class AnniversaryHaoMengDescriptor : ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler => new AnniversaryHaoMengHandler(Op);
 public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
 public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance, Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse ? [] : [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ResolveAnniversaryHaoMengXiongmangUseDescriptor : AnniversaryHaoMengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse; }
internal sealed class ResolveAnniversaryHaoMengXiongmangConsequenceDescriptor : AnniversaryHaoMengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangConsequence; }
internal sealed class AnniversaryHaoMengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryHaoMengHost>().Begin(f, op); }
internal static class AnniversaryHaoMengComposition
{
 internal const string Skill = "anniversary:xiongmang", Activation = "distinct-hand-native-slash", Consequence = "issued-whole-slash-consequence";
 [ProgramCatalogValidation(49700)] internal static void Validate(string path, SkillProgram p)
 {
  if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse or SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangConsequence)) return;
  if (p.Id != Skill || p.ViewAs.Count != 4 || p.Activations is not [{ Id: Activation, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse, Target: SkillProgramEffectTarget.Owner }] }] || p.Triggers is not [{ Id: Consequence, Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.ConversionSource, SourceSkillId: Skill, SingleActionInstance: true, Optional: false, IsSkillInvocation: false, Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangConsequence, Target: SkillProgramEffectTarget.Owner }] }] || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0) throw new InvalidOperationException(path + ": Xiongmang requires the original native use and one whole-use consequence.");
  for (var n = 1; n <= 4; n++) if (p.ViewAs[n - 1] is not { OutputKind: CardKind.Slash, ForPlay: true, ForResponse: false, AllowSameKind: true, VariableInputCount: false } rule || rule.Id != "distinct-hand-" + n || rule.InputCount != n || !rule.SourceZones.SequenceEqual([CardZoneKind.Hand]) || rule.InputKinds.Count != 0 || rule.InputSuits.Count != 0 || rule.InputCategories.Count != 0 || rule.Condition.Kind != SkillProgramConditionKind.Always) throw new InvalidOperationException(path + ": Xiongmang requires precisely one to four Hand materials and actual Use direction.");
 }
}
public enum AnniversaryHaoMengStage { Cards, Targets, Issuing, NativeSlash, NativeTail, HealthChildren, Completed }
public sealed record AnniversaryHaoMengReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int Turn, AnniversaryHaoMengStage Stage)
{
 private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>()), _targets = Array.AsReadOnly(Array.Empty<int>());
 private IReadOnlyList<long> _batches = Array.AsReadOnly(Array.Empty<long>());
 public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
 public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
 public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
 public long UseId { get; init; }
 public long ActionId { get; init; }
 public long Before { get; init; }
 public long After { get; init; }
 public bool Returned { get; init; }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHaoMengReceipt? AnniversaryHaoMeng { get; init; } }
public sealed record AnniversaryHaoMengUseOrigin(long UseId, long ActionId, CardConversionSource Source, string Hash, int MaterialCount, long ProducerFrameId, int Turn, ActualDiscardRecoveryPhaseKey? Phase);
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHaoMengUseOrigin? AnniversaryHaoMengUse { get; init; } }
public sealed record AnniversaryHaoMengStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, long ParentId, int Turn) : IGameEvent;
public sealed record AnniversaryHaoMengCardChosenEvent(long FrameId, int Index, int CardId, Suit Suit) : IGameEvent;
public sealed record AnniversaryHaoMengTargetChosenEvent(long FrameId, int Index, int Target) : IGameEvent;
public sealed record AnniversaryHaoMengUseIssuedEvent(AnniversaryHaoMengUseOrigin Origin) : IGameEvent;
public sealed record AnniversaryHaoMengMaterialEvent(long UseId, int Index, CardActionCost Material, Suit EffectiveSuit) : IGameEvent;
public sealed record AnniversaryHaoMengUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryHaoMengNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;
public sealed record AnniversaryHaoMengConsequenceEvent(long FrameId, long UseId, long ActionId, int Owner, bool Damaged, int MaximumBefore, int MaximumAfter) : IGameEvent;
public sealed record AnniversaryHaoMengPhaseQuotaEvent(long FrameId, long UseId, int Owner, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record AnniversaryHaoMengCompletedEvent(long FrameId) : IGameEvent;
