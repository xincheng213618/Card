using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZhaoZhongStage { PengbiSkill, SelfGrantChildren, PengbiTarget, OtherGrantChildren, InitialGive, GiftChildren, RecoveryChildren, UnlinkChildren, Neighbors, NeighborChildren, NextChoice, NextGiftChildren, NextDamageChildren, Drawing, DrawChildren }
public sealed record ZhaoZhongReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, ZhaoZhongStage Stage)
{
    public bool Paid { get; init; }
    public int TargetSeat { get; init; } = -1;
    public string? ChosenSkill { get; init; }
    public long LeaseId { get; init; }
    public int Cursor { get; init; }
    public int? CardId { get; init; }
    public CardKind? CardKind { get; init; }
    public CardLocation? From { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<int> _neighbors = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Neighbors { get => _neighbors; init => _neighbors = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhaoZhongReceipt? ZhaoZhong { get; init; }
}
public sealed record ZhaoZhongStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, int TargetSeat) : IGameEvent;
public sealed record ZhaoZhongCompletedEvent(long FrameId, SkillProgramEffectOp Op, bool Paid) : IGameEvent;
public sealed record PengbiCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, string SkillId) : IGameEvent;
public sealed record PengbiSkillGrantedEvent(long FrameId, int RecipientSeat, string SkillId, string GrantId, string SkillInstanceId, string SourceId) : IGameEvent;
public sealed record PengbiOtherChosenEvent(long FrameId, int? RecipientSeat) : IGameEvent;
public sealed record DiciAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record DiciGiftEntityEvent(long FrameId, long LeaseId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record DiciRecoveryIssuedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record DiciUnlinkIssuedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record DiciNeighborsFrozenEvent(long FrameId, int Count) : IGameEvent;
public sealed record DiciNeighborFrozenEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record DiciNeighborIssuedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record DiciLeaseIssuedEvent(long LeaseId, CardConversionSource Source, string GameplayHash, int IssuerSeat, int TargetSeat, int Ordinal, int ActualTurn) : IGameEvent;
public sealed record DiciLeaseDueEvent(long LeaseId, long ChangeId, int TargetSeat) : IGameEvent;
public sealed record DiciLeaseStartedEvent(long LeaseId, long ChangeId, long ProgramFrameId) : IGameEvent;
public sealed record DiciLeaseCancelledEvent(long LeaseId, int TargetSeat) : IGameEvent;
public sealed record DiciNextChoiceEvent(long FrameId, long LeaseId, string Branch) : IGameEvent;
public sealed record DiciThunderIssuedEvent(long FrameId, long LeaseId, int SourceSeat, int TargetSeat) : IGameEvent;
public sealed record BiriPotentialGainEvent(long BatchId, long Sequence, int CardId, int RecipientSeat, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record BiriPotentialHolderEvent(long BatchId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record BiriDrawIssuedEvent(long FrameId, long BatchId, long GainSequence, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record BiriDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;

internal interface IZhaoZhongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ZhaoZhongDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch
    {
        SkillProgramEffectOp.ResolvePengbiFirstActualTurn => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],
        SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterEnteredChain)],
        SkillProgramEffectOp.ResolveBiriFirstNonDrawGain => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)],
        _ => []
    };
    public override ISkillProgramEffectHandler Handler { get; } = new ZhaoZhongHandler(op);
}
internal sealed class PengbiFirstActualTurnDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolvePengbiFirstActualTurn)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class DiciGiftRecoveryAndNeighborChainDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain)
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override ProgramOperationLegalityPolicy LegalityPolicy { get; } = new(RequiresTargetHand: true, ExcludesOwnerAsTarget: true);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class IssuedDiciNextChainChoiceDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class BiriFirstNonDrawGainDescriptor() : ZhaoZhongDescriptor(SkillProgramEffectOp.ResolveBiriFirstNonDrawGain){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ZhaoZhongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IZhaoZhongHost>().Resolve(frame, op);
}
internal static class ZhaoZhongComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolvePengbiFirstActualTurn or SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain or SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice or SkillProgramEffectOp.ResolveBiriFirstNonDrawGain;
    [ProgramCatalogValidation(810)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var ops = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).ToArray();
        if (!ops.Any(e => Handles(e.Op) || e.Op == SkillProgramEffectOp.ResolveYintianFirstRecovery)) return;
        if (p.Id is not ("ol:pengbi" or "ol:dici" or "ol:yintian" or "ol:biri") || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || ops.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always)) throw new InvalidOperationException(path + ": Zhao Zhong requires his exact independent native lifecycle operations.");
        if (p.Id == "ol:dici")
        {
            if (p.Activations is not [{ Id: "give-recover-chain", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, Effects: [{ Op: SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain }] }] || p.Triggers is not [var debt] || debt.Id != "issued-next-chain" || debt.Window != SkillProgramTriggerWindow.CharacterEnteredChain || debt.Subject != SkillProgramTriggerSubject.Owner || debt.Optional || debt.Effects is not [{ Op: SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice }] || !Plain(debt)) throw new InvalidOperationException(path + ": Dici requires its exact real Hand donor and independent issued chain obligation.");
            return;
        }
        if (p.Activations.Count != 0 || p.Triggers is not [var t] || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || !Plain(t) || t.Effects.Count != 1 ||
            (p.Id == "ol:pengbi" ? t.Id != "first-own-actual-turn" || t.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || t.Effects[0].Op != SkillProgramEffectOp.ResolvePengbiFirstActualTurn :
             p.Id == "ol:yintian" ? t.Id != "first-global-recovery" || t.Window != SkillProgramTriggerWindow.AfterHpRecovered || t.Effects[0].Op != SkillProgramEffectOp.ResolveYintianFirstRecovery :
             t.Id != "first-global-non-draw-gain" || t.Window != SkillProgramTriggerWindow.CardsGained || t.Effects[0].Op != SkillProgramEffectOp.ResolveBiriFirstNonDrawGain)) throw new InvalidOperationException(path + ": Zhao Zhong's first-event programs cannot replace their exact native windows or quotas.");
    }
    private static bool Plain(SkillProgramTrigger t) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && t.OwnerRelation is null && !t.IncludeResponseUses && !t.SingleActionInstance && t.SourceZones.Count == 0 && t.DamageOccurrence is null && t.HpChangeOccurrence != SkillProgramHpChangeOccurrence.PerPoint;
}
