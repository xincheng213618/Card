using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IAnniversaryRuanJiHost
{ SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }

internal abstract class AnniversaryRuanJiDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new AnniversaryRuanJiHandler(operation);
    public override ProgramOperationInteraction Interaction => operation == SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered:
        ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(operation switch
        {
            SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen => SkillProgramTriggerWindow.PlayPhaseStarting,
            SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse => SkillProgramTriggerWindow.CardUseCommitted,
            _ => SkillProgramTriggerWindow.CardUseTargetsFinalized
        })];
}
internal sealed class ResolveAnniversaryRuanJiZhaowenDescriptor() : AnniversaryRuanJiDescriptor(SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen);
internal sealed class DrawAnniversaryRuanJiShownRedUseDescriptor() : AnniversaryRuanJiDescriptor(SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse);
internal sealed class ResolveAnniversaryRuanJiJiudunDescriptor() : AnniversaryRuanJiDescriptor(SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun);
internal sealed class AnniversaryRuanJiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnniversaryRuanJiHost>().Begin(frame, operation);
}

internal static class AnniversaryRuanJiComposition
{
    internal const string Zhaowen = "anniversary:zhaowen", Jiudun = "anniversary:jiudun";
    internal static bool Own(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen or
        SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse or SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun;
    internal static void ValidateRedTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "shown-red-use" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Subject is not null || t.Optional || !t.IncludeResponseUses ||
            t.SingleActionInstance || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 ||
            t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.NamedUsageGroup is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects is not
                [{ Op: SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse, Target: SkillProgramEffectTarget.Owner,
                   TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Zhaowen's shown-red reward requires its exact unbudgeted actor actual-Use instruction, including genuine response Uses.");
    }
    [ProgramCatalogValidation(42800)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => Own(e.Op))) return;
        static bool Effect(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is
            [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op &&
            t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null;
        var valid = program.Id switch
        {
            Zhaowen => program.Triggers.Count == 2 && program.Triggers.All(t => t.Id switch
            {
                "show-hand" => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting && t.Subject == SkillProgramTriggerSubject.Owner &&
                    t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && Effect(t, SkillProgramEffectOp.ResolveAnniversaryRuanJiZhaowen),
                "shown-red-use" => t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                    !t.Optional && t.IncludeResponseUses && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && Effect(t, SkillProgramEffectOp.DrawAnniversaryRuanJiShownRedUse),
                _ => false
            }),
            Jiudun => program.ViewAs.Count == 0 && program.Triggers is [var t] && t.Id == "incoming-black" &&
                t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                t.OnlyDesignatedCardTargets && t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                t.Suits.Count == 0 && Effect(t, SkillProgramEffectOp.ResolveAnniversaryRuanJiJiudun),
            _ => false
        };
        if (!valid || program.Activations.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 ||
            program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Ruan Ji requires his exact whole-hand actual-turn grant, actual shown-red Use and optional other-owner black target bindings.");
        if (program.Id == Zhaowen) ValidateRedTrigger(program.Triggers.Single(t => t.Id == "shown-red-use"));
        if (program.Id == Zhaowen)
        {
            var ordinary = CardCatalog.ImplementedCards.Where(c => c.CategoryName == "锦囊牌" && c.Kind is not
                (CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning or CardKind.AllianceFeast or
                 CardKind.SowBeans or CardKind.TransplantWood or CardKind.ReturnSoul or CardKind.StarShift or CardKind.BeamSwap or
                 CardKind.HandRedistribution or CardKind.WenheChaos or CardKind.StoreGrain or CardKind.Reinforcements or CardKind.UnitedEffort or
                 CardKind.SameBoat or CardKind.StriveUpstream or CardKind.AgainstCurrent or CardKind.WumiaoFlood or CardKind.MantianGuohai)).Select(c => c.Kind).ToHashSet();
            if (program.ViewAs.Count != ordinary.Count || !program.ViewAs.Select(v => v.OutputKind).ToHashSet().SetEquals(ordinary))
                throw new InvalidOperationException(path + ": Zhaowen must expose all native ordinary-trick conversion names, including Qizheng.");
        }
        foreach (var v in program.ViewAs)
            if (program.Id != Zhaowen || !CardCatalog.Get(v.OutputKind).CategoryName.Equals("锦囊牌", StringComparison.Ordinal) ||
                v.OutputKind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning || v.InputCount != 1 ||
                !v.SourceZones.SequenceEqual([CardZoneKind.Hand]) || v.InputSuits.Count != 0 || v.InputKinds.Count != 0 ||
                v.UseEffectiveInputSuit.GetValueOrDefault() || !v.AllowSameKind || v.DamageBonus != 0 || v.RecoveryBonus != 0 ||
                v.UsesPerPhase is not null || v.UsesPerActualTurn is not null ||
                v.ForResponse != (v.OutputKind == CardKind.Nullification) || v.ForPlay != (v.OutputKind != CardKind.Nullification))
                throw new InvalidOperationException(path + ": Zhaowen keeps a genuine one-Hand ordinary-trick conversion; shown identity and per-name actual-turn quota are checked dynamically.");
    }
}
internal sealed class AnniversaryRuanJiCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => AnniversaryRuanJiComposition.Zhaowen;
    public void ValidateTrigger(SkillProgramTrigger trigger) => AnniversaryRuanJiComposition.ValidateRedTrigger(trigger);
}

public enum RuanJiStage { Revealing, Drawing, DrawChildren, DiscardChoice, DiscardChildren, WineIssued }
public sealed record RuanJiShownEntity(int Id, CardKind Kind, Suit Suit, bool? IsRed);
public sealed record RuanJiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int InstructionIndex, long ParentId, int ActualTurn, long UseId, long ActionId, bool WasDrunk, RuanJiStage Stage)
{
    private readonly IReadOnlyList<RuanJiShownEntity> _hand = [];
    public IReadOnlyList<RuanJiShownEntity> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
    public int DiscardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? BatchId { get; init; }
    public long? WineId { get; init; }
    public long? WineActionId { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RuanJiReceipt? RuanJi { get; init; } }

public sealed record RuanJiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, long ParentId, int ActualTurn, long UseId, long ActionId, bool WasDrunk) : IGameEvent;
// These entities have already been publicly revealed by the owning native frame.
public sealed record RuanJiShownEntityAuthorizedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int CardId, CardKind Kind, Suit Suit, bool? IsRed) : IGameEvent;
public sealed record RuanJiNameUsedEvent(long ActionId, CardConversionSource Source, int ActualTurn, CardKind Name, int CardId) : IGameEvent;
public sealed record RuanJiDrawReturnedEvent(long FrameId, long? AttemptId, int Actual, long Before, long After) : IGameEvent;
public sealed record RuanJiDiscardPaidEvent(long FrameId, int CardId, long BatchId, long Before, long After) : IGameEvent;
public sealed record RuanJiWineIssuedEvent(long FrameId, long UseId, long ActionId, long OriginalUseId) : IGameEvent;
public sealed record RuanJiWineReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
