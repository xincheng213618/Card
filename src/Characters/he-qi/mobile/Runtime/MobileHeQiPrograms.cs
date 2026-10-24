using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IMobileHeQiHost { SkillProgramStepOutcome View(ProgramSkillFrame frame); }
internal sealed class ResolveMobileHeQiShanxiViewDescriptor : ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileHeQiShanxiView;
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler { get; } = new MobileHeQiViewHandler();
 public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(1));
 public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new ReadCardSet(MobileHeQiComposition.DiscardBind)];
}
internal sealed class MobileHeQiViewHandler : ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileHeQiShanxiView;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMobileHeQiHost>().View(f);
}
internal static class MobileHeQiComposition
{
 internal const string Skill = "mobile:shanxi", Activation = "red-basic-discard-and-view", DiscardBind = "mobile-shanxi-discard";
 [ProgramCatalogValidation(50100)] internal static void Validate(string path, SkillProgram p)
 {
  if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveMobileHeQiShanxiView)) return;
  if (p.Id != Skill || p.Triggers.Count != 0 || p.Activations is not [var a] || a.Id != Activation || a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 1 || a.UsesPerTurn is not null || a.UsesPerPhase != 1 || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || !a.CardCategories.SequenceEqual([SkillProgramCardCategory.Basic]) ||
      a.Effects is not [{ Op: SkillProgramEffectOp.DiscardSelected, Amount: 1 }, { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Destination: SkillProgramCardDestination.DiscardPile, Amount: 1, AwaitMovementTriggers: true, SkipIfNoCards: true } move, { Op: SkillProgramEffectOp.ResolveMobileHeQiShanxiView }] || move.ResultBind != DiscardBind || move.ChooserRef?.Kind != ProgramParticipantRef.Owner || move.CardOwnerRef?.Kind != ProgramParticipantRef.SelectedTarget || !move.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]))
   throw new InvalidOperationException(path + ": mobile Shanxi needs the original red basic payment, native foreign discard and private directional hand view.");
 }
}
public sealed record MobileHeQiViewReceipt(CardConversionSource Source, string Hash, int Target, long InvocationId, int CardId, CardLocation From, CardKind DiscardedKind, int Viewer, int Holder)
{
 private IReadOnlyList<int> _hand = Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileHeQiViewReceipt? MobileHeQiView { get; init; } }
public sealed record MobileHeQiHandViewedEvent(long FrameId, CardConversionSource Source, long DiscardInvocationId, int DiscardCardId, CardKind DiscardedKind, int ViewerSeat, int HolderSeat, int Count) : IGameEvent;
public sealed record MobileHeQiViewConfirmedEvent(long FrameId, int ViewerSeat) : IGameEvent;
