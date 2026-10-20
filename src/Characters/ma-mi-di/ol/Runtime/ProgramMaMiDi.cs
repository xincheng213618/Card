using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IMaMiDiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MaMiDiDescriptor(SkillProgramEffectOp op, SkillProgramTriggerWindow window) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler { get; } = new MaMiDiHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(window)];
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class MaMiDiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMaMiDiHost>().Resolve(frame, op);
}

public enum MaMiDiStage { MaximumChildren, TargetChoice, TargetChildren }
public sealed record MaMiDiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int Turn, int TurnOwner, long? UseId, long? ActionId, int GrantCount, int TargetCount) : IGameEvent;
public sealed record MaMiDiMaximumIssuedEvent(long FrameId, SkillProgramEffectOp Op, int OwnerSeat, int MaximumBefore, int MaximumAfter, int HpBefore, int HpAfter) : IGameEvent;
public sealed record MaMiDiFrozenTargetEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record MaMiDiFrozenGrantEvent(long FrameId, int Index, long GrantFrameId) : IGameEvent;
public sealed record MaMiDiDiscardIssuedEvent(long FrameId, int Index, long GrantFrameId, int TargetSeat, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Before, long After) : IGameEvent;
public sealed record MaMiDiTargetSkippedEvent(long FrameId, int Index, long GrantFrameId, int TargetSeat) : IGameEvent;
public sealed record MaMiDiCompletedEvent(long FrameId) : IGameEvent;
public sealed record MaMiDiResponseLinkedEvent(long WindowId, long ResponseActionId, long ParentFrameId, long? IncomingActionId, int ResponseActor, int NativeActor, int Provider, int IncomingActor, CardKind ResponseKind, CardKind IncomingKind, bool? ResponseIsRed, bool? IncomingIsRed, ProgramCardContinuation Continuation, int Turn, int TurnOwner) : IGameEvent;
public sealed record MaMiDiDiscardStep(int Index, long GrantFrameId, int TargetSeat, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Before, long After)
{
    private readonly IReadOnlyList<int> _grain = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> WoodenOxGrain { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MaMiDiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int Instruction, long WindowId, int Turn, int TurnOwner, MaMiDiStage Stage,
    long? UseId = null, long? ActionId = null, int Cursor = 0, int MaximumBefore = 0, int MaximumAfter = 0, int HpBefore = 0, int HpAfter = 0, MaMiDiResponseLinkedEvent? Response = null)
{
    private readonly IReadOnlyList<BingjieGrantedEvent> _grants = Array.AsReadOnly(Array.Empty<BingjieGrantedEvent>());
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<MaMiDiDiscardStep> _steps = Array.AsReadOnly(Array.Empty<MaMiDiDiscardStep>());
    public IReadOnlyList<BingjieGrantedEvent> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MaMiDiDiscardStep> Steps { get => _steps; init => _steps = Array.AsReadOnly(value.Select(s => s with { WoodenOxGrain = s.WoodenOxGrain }).ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MaMiDiReceipt? MaMiDi { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MaMiDiBingjieUseReceipt? MaMiDiBingjie { get; init; } }

internal static class MaMiDiComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.PayBingjieTurnMaximumHp or SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards or SkillProgramEffectOp.GrowZhengdingMatchingResponse;
    [ProgramCatalogValidation(330)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op))) && !p.Activations.Any(a => a.Effects.Any(e => Handles(e.Op)))) return;
        if (p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Ma Mi Di requires its native maximum-HP, issued-target and actual-response instructions without unrelated policies.");
        if (p.Id == "ol:bingjie" ? p.Triggers.Count != 2 || p.Triggers.Count(t => t.Effects is [{ Op: SkillProgramEffectOp.PayBingjieTurnMaximumHp }]) != 1 || p.Triggers.Count(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards }]) != 1 :
            p.Id != "ol:zhengding" || p.Triggers.Count != 1 || p.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.GrowZhengdingMatchingResponse }])
            throw new InvalidOperationException(path + ": Bingjie needs both paid-Play and issued-use entries; Zhengding needs its one actual completed-response entry.");
        foreach (var t in p.Triggers)
        {
            if (t.Effects is not [var e] || !Handles(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.UsageLimit is not null || t.UsageScope is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null ||
                t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 ||
                t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null ||
                t.SourceSkillId is not null || t.SourceViewAsId is not null || t.DamageOccurrence is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.MovementOccurrence is not null ||
                t.IncludeResponseUses || t.SingleActionInstance || t.OnlyDesignatedCardTargets || t.AllowNoEventTarget || t.DeferredTurnEndOnly || t.NoDyingAtActivation || t.AllowOwnDiscardPhaseEnded ||
                t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.GainPhaseQualification is not null ||
                (e.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp ? t.Window != SkillProgramTriggerWindow.PlayPhaseStarting || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || !t.Optional || t.Priority != 0 || t.Id != "pay-at-actual-play-start" :
                 e.Op == SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards ? t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Subject is not null || t.Optional || t.Priority != -1000 || t.Id != "issued-turn-target-discards" :
                 t.Window != SkillProgramTriggerWindow.CardResponseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Subject is not null || t.Optional || t.Priority != 0 || t.Id != "same-color-foreign-response"))
                throw new InvalidOperationException(path + ": Ma Mi Di requires the exact unfiltered actual-Play, issued finalized-use or mandatory completed-response contract.");
        }
    }
}
