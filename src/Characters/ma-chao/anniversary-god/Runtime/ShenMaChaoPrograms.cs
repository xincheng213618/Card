using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShenMaChaoStage { StartingMount, EquipmentUse, EquipmentChildren, DrawAttempt, DrawChildren }
public sealed record ShenMaChaoReceipt(CardConversionSource Source, string GameplayHash, long ParentId,
    SkillProgramEffectOp Operation, ShenMaChaoStage Stage, int Cursor = 0, int CardId = 0, long UseId = 0,
    long ActionId = 0, Suit Suit = Suit.None, int DrawCount = 0, long Before = 0, long After = 0)
{
    private IReadOnlyList<int> _seats = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenMaChaoReceipt? ShenMaChao { get; init; } }
public sealed record ShouliStartingMountIssuedEvent(long FrameId, int Index, int RecipientSeat, int CardId, long UseId) : IGameEvent;
public sealed record ShouliStartingMountFinishedEvent(long FrameId, int Index, int RecipientSeat, int CardId, long UseId) : IGameEvent;
public sealed record ShouliBorrowedMountEvent(long ActionId, CardConversionSource Source, int TurnNumber, int TurnOwnerSeat,
    int CardId, CardLocation From) : IGameEvent;
public sealed record HengwuDrawIssuedEvent(long FrameId, int OwnerSeat, long ActionId, Suit Suit, int Count) : IGameEvent;
public sealed record HengwuDrawReturnedEvent(long FrameId, int OwnerSeat, int Requested, int Actual, long Before, long After) : IGameEvent;

internal interface IShenMaChaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class ShenMaChaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenMaChaoHandler(op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindows(
        op == SkillProgramEffectOp.DealShouliStartingMounts ? [SkillProgramTriggerWindow.GameStarting] :
        [SkillProgramTriggerWindow.CardUseCommitted, SkillProgramTriggerWindow.CardResponseAccepted])];
}
internal sealed class DealShouliStartingMountsDescriptor() : ShenMaChaoDescriptor(SkillProgramEffectOp.DealShouliStartingMounts);
internal sealed class DrawHengwuFieldSuitEquipmentDescriptor() : ShenMaChaoDescriptor(SkillProgramEffectOp.DrawHengwuFieldSuitEquipment);
internal sealed class ShenMaChaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShenMaChaoHost>().Resolve(f, op);
}
internal sealed class HengwuCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => ShenMaChaoComposition.Hengwu;
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "actual-use-suit" || t.Window != SkillProgramTriggerWindow.CardUseCommitted || !t.Optional || !t.IncludeResponseUses ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Subject is not null || t.SingleActionInstance ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.UsageLimit is not null || t.UsageScope is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.RequireNoCardConversion is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.DrawHengwuFieldSuitEquipment, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Hengwu requires the exact optional actual-use actor contract with a native suit-count draw.");
    }
}
internal static class ShenMaChaoComposition
{
    internal const string Shouli = "anniversary:shouli", Hengwu = "anniversary:hengwu", Slash = "field-offensive-mount-slash", Dodge = "field-defensive-mount-dodge";
    [ProgramCatalogValidation(39000)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.DealShouliStartingMounts or SkillProgramEffectOp.DrawHengwuFieldSuitEquipment)) return;
        if (p.Activations.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.Any(t => t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] || t.UsageLimit is not null))
            throw new InvalidOperationException(path + ": Shen Ma Chao needs his exact setup and actual-card trigger composition.");
        var valid = p.Id == Shouli ? p.Triggers is [{ Id: "starting-mount-sequence", Window: SkillProgramTriggerWindow.GameStarting, Optional: false,
            Effects: [{ Op: SkillProgramEffectOp.DealShouliStartingMounts }] }] && p.ViewAs.Count == 2 && p.ViewAs.All(v => v.FieldMountConversion == true && v.InputCount == 1 && v.ForResponse &&
                v.SourceZones.SequenceEqual([CardZoneKind.Equipment, CardZoneKind.Judgment]) && v.Condition.Kind == SkillProgramConditionKind.Always &&
                v.InputKinds.Count > 0 && v.InputKinds.All(k => EquipmentCatalog.IsEquipment(k) && EquipmentCatalog.Get(k).Slot == (v.OutputKind == CardKind.Slash ? EquipmentSlot.OffensiveHorse : EquipmentSlot.DefensiveHorse))) &&
            p.ViewAs.Any(v => v.Id == Slash && v.OutputKind == CardKind.Slash && v.ForPlay) && p.ViewAs.Any(v => v.Id == Dodge && v.OutputKind == CardKind.Dodge && !v.ForPlay) &&
            p.Modifiers.Count == 0 :
            p.Id == Hengwu && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.Triggers is [var use, var response] &&
            use.Id == "actual-use-suit" && use.Window == SkillProgramTriggerWindow.CardUseCommitted && use.IncludeResponseUses &&
            response.Id == "actual-response-suit" && response.Window == SkillProgramTriggerWindow.CardResponseAccepted &&
            p.Triggers.All(t => t.Optional && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && !t.SingleActionInstance &&
                t.Effects is [{ Op: SkillProgramEffectOp.DrawHengwuFieldSuitEquipment }]);
        if (!valid) throw new InvalidOperationException(path + ": Shen Ma Chao requires the official field-mount and Hengwu native semantics.");
    }
}
