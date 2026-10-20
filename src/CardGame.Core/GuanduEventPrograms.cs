using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IGuanduEventHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class GuanduEventDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new GuanduEventHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class ResolveGuanduRepeatDescriptor : GuanduEventDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduRepeat;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)]; }
internal sealed class RewardGuanduDamageDescriptor : GuanduEventDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardGuanduDamage;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]; }
internal sealed class ResolveGuanduArroganceDescriptor : GuanduEventDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGuanduArrogance;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]; }
internal sealed class GuanduEventHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IGuanduEventHost>().Execute(f, operation);
}
internal sealed class GuanduEventCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "guandu:battle-event";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "tenth-use" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses || t.Optional ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveGuanduRepeat, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("The Guandu tenth-card event requires its exact mandatory actual-use contract.");
    }
}
public sealed record GuanduActualCardActionEvent(long ActionId, long UseFrameId, CardActionType Type,
    int ActorSeat, CardKind Kind, int ActualTurnNumber, ActualDiscardRecoveryPhaseKey? Phase, int UseOrdinal) : IGameEvent
{
    public bool CountsAsUse { get; init; }
}
public sealed record GuanduDamageRewardedEvent(long UseFrameId, int SourceSeat) : IGameEvent;
public sealed record GuanduSlowAdvanceEarnedEvent(ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record GuanduSlowAdvanceConsumedEvent(long DrawFrameId, int OwnerSeat, int Credits) : IGameEvent;
public sealed record GuanduHoldLineUsedEvent(int RoundNumber, int OwnerSeat, int ActualTurnNumber) : IGameEvent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExcludedDiscardPhaseToken { get; init; }
}
public sealed record GuanduHoldLineConsumedEvent(ActualDiscardRecoveryPhaseKey Phase, int Credits) : IGameEvent;
public sealed record GuanduEventFlowIssuedEvent(long ProgramFrameId, SkillProgramEffectOp Operation,
    int OwnerSeat, int ActualTurnNumber, long? UseFrameId) : IGameEvent;
public sealed record GuanduEventFlowReceipt(SkillProgramEffectOp Operation, int ActualTurnNumber, long? UseFrameId);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuanduEventFlowReceipt? GuanduEventFlow { get; init; }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class GuanduEventCapability(GameEngine engine) : IGuanduEventHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.ExecuteGuanduEvent(f, op); }
    private GuanduBattleEvent? CurrentGuanduEvent => GuanduConfiguration?.Event;
    private void RecordGuanduCardAction(CardActionContext a, SkillProgramTriggerWindow window)
    {
        if (!IsGuanduActivity || window is not (SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted) ||
            ProgramEventHistory<GuanduActualCardActionEvent>().Any(e => e.ActionId == a.ActionId)) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Action?.ActionId == a.ActionId);
        var logicalUse = window == SkillProgramTriggerWindow.CardUseCommitted || a.Type == CardActionType.Use;
        var ordinal = ProgramEventHistory<GuanduActualCardActionEvent>().Count(e => e.CountsAsUse) + (logicalUse ? 1 : 0);
        AdvanceEventRulesAndQueueFact(new GuanduActualCardActionEvent(a.ActionId, use?.Id ?? 0, a.Type,
            a.ActorSeat, a.EffectiveKind, _turnNumber, CurrentActualDiscardRecoveryPhase(), ordinal) { CountsAsUse = logicalUse });
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 46002)]
    private void ObserveGuanduDeclaredUse(IGameEvent payload)
    {
        if (!IsGuanduActivity || payload is not CardUseDeclaredEvent declared ||
            LifecycleCardUse(declared.ResolutionId)?.Action is not { Type: CardActionType.Use } action) return;
        RecordGuanduCardAction(action, SkillProgramTriggerWindow.CardUseCommitted);
    }
    private DamageNature GuanduDamageNature(DamageNature nature) => CurrentGuanduEvent == GuanduBattleEvent.WuchaoFire &&
        nature == DamageNature.Normal ? DamageNature.Fire : nature;
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 210)]
    private int GuanduEventDamageBonus(IDamageAttempt damage)
    {
        if (damage.IsSourceLess || !IsGuanduActivity) return 0;
        if (CurrentGuanduEvent == GuanduBattleEvent.Underdog)
            return _players[damage.TargetSeat].Hp > _players[damage.SourceSeat].Hp ? 1 : 0;
        if (CurrentGuanduEvent != GuanduBattleEvent.Standoff || _roundNumber <= 4 || damage.IsChainPropagation ||
            damage.SourceSeat != _currentSeat || damage.CardUserSeat != damage.SourceSeat ||
            damage.EffectiveCardKind is not { } kind || !IsSlashCard(kind)) return 0;
        return ProgramEventHistory<GuanduActualCardActionEvent>().FirstOrDefault(e =>
            e.ActualTurnNumber == _turnNumber && e.ActorSeat == damage.SourceSeat && e.Type == CardActionType.Use && IsSlashCard(e.Kind))?.UseFrameId == damage.ResolutionId ? 1 : 0;
    }
    private int GuanduSlowEligibleCredits(int seat) => ProgramEventHistory<GuanduSlowAdvanceEarnedEvent>()
        .Where(e => e.Phase.ActualTurnOwnerSeat == seat)
        .Skip(ProgramEventHistory<GuanduSlowAdvanceConsumedEvent>().Where(e => e.OwnerSeat == seat).Sum(e => e.Credits))
        .Count(e => e.Phase.TurnNumber < _turnNumber);
    private bool GuanduSlowBonusAvailable(int seat) => GuanduSlowEligibleCredits(seat) > 0 &&
        _resolutionStack.OfType<DrawPhaseObligationFrame>().LastOrDefault()?.IsExtra != true;
    private int GuanduHoldCredits(int seat, long? excludedToken = null) => ProgramEventHistory<GuanduHoldLineUsedEvent>()
        .Where(e => e.OwnerSeat == seat)
        .Skip(ProgramEventHistory<GuanduHoldLineConsumedEvent>().Where(e => e.Phase.ActualTurnOwnerSeat == seat).Sum(e => e.Credits))
        .Count(e => excludedToken is null || e.ExcludedDiscardPhaseToken != excludedToken);
    private long? GuanduCurrentDiscardToken => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase &&
        !ProgramEventHistory<ActualPhaseEndedEvent>().Any(e => e.Phase == phase) ? phase.Token : null;
    private IEnumerable<RuleQueryContribution> GuanduEventRuleContributions(CharacterState p, SkillRuleQuery query)
    {
        var amount = query switch
        {
            SkillRuleQuery.DrawCount when CurrentGuanduEvent == GuanduBattleEvent.GrainShortage => -1,
            SkillRuleQuery.DrawCount when CurrentGuanduEvent == GuanduBattleEvent.SlowAdvance => GuanduSlowBonusAvailable(p.Seat) ? 1 : 0,
            SkillRuleQuery.HandLimit when CurrentGuanduEvent == GuanduBattleEvent.Standoff && _roundNumber <= 4 => _roundNumber,
            SkillRuleQuery.HandLimit when CurrentGuanduEvent == GuanduBattleEvent.HoldTheLine => -GuanduHoldCredits(p.Seat, GuanduCurrentDiscardToken),
            _ => 0
        };
        if (amount != 0) yield return new FiniteRuleQueryContribution($"guandu:event:{query}", SkillRuleOperation.Add, amount);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 46001)]
    private void ObserveGuanduEventPhases(IGameEvent e)
    {
        if (!IsGuanduActivity) return;
        if (e is ActualPhaseEndedEvent { Phase.Kind: ActualDiscardRecoveryPhaseKind.Play } ended &&
            CurrentGuanduEvent == GuanduBattleEvent.SlowAdvance && !ProgramEventHistory<GuanduActualCardActionEvent>().Any(a =>
                a.Phase == ended.Phase && a.ActorSeat == ended.Phase.ActualTurnOwnerSeat && IsSlashCard(a.Kind)))
            AdvanceEventRulesAndQueueFact(new GuanduSlowAdvanceEarnedEvent(ended.Phase));
        if (e is ActualDrawPhaseCompletedEvent { IsExtra: false, Skipped: false } draw && GuanduSlowEligibleCredits(draw.OwnerSeat) > 0)
            AdvanceEventRulesAndQueueFact(new GuanduSlowAdvanceConsumedEvent(draw.FrameId, draw.OwnerSeat, GuanduSlowEligibleCredits(draw.OwnerSeat)));
        if (e is ActualPhaseEndedEvent { Phase.Kind: ActualDiscardRecoveryPhaseKind.Discard } discarded && GuanduHoldCredits(discarded.Phase.ActualTurnOwnerSeat, discarded.Phase.Token) > 0)
            AdvanceEventRulesAndQueueFact(new GuanduHoldLineConsumedEvent(discarded.Phase, GuanduHoldCredits(discarded.Phase.ActualTurnOwnerSeat, discarded.Phase.Token)));
    }
    private bool CanUseGuanduRoundConversion(IndexedSkillProgramInstance instance, SkillProgramViewAs rule, CharacterState owner) =>
        instance.SkillId != "guandu:battle-event" || rule.Id != "hold-line" ||
        CurrentGuanduEvent == GuanduBattleEvent.HoldTheLine && !ProgramEventHistory<GuanduHoldLineUsedEvent>().Any(e =>
            e.OwnerSeat == owner.Seat && e.RoundNumber == _roundNumber);
    private void ConsumeGuanduRoundConversion(CardConversionSource source)
    {
        if (source.SkillId != "guandu:battle-event" || source.BindingId != "hold-line") return;
        if (CurrentGuanduEvent != GuanduBattleEvent.HoldTheLine || ProgramEventHistory<GuanduHoldLineUsedEvent>().Any(e =>
            e.OwnerSeat == source.OwnerSeat && e.RoundNumber == _roundNumber))
            throw new InvalidOperationException("The actual round's Slash-as-Dodge allowance was already paid.");
        AdvanceEventRulesAndQueueFact(new GuanduHoldLineUsedEvent(_roundNumber, source.OwnerSeat, _turnNumber)
        { ExcludedDiscardPhaseToken = GuanduCurrentDiscardToken });
    }
    private CardUseFrame? GuanduDamageRewardUse(int owner, ProgramSkillWindowContext context) =>
        context.SourceSeat == owner && CurrentDamageAttempt is { IsSourceLess: false } damage && damage.SourceSeat == owner &&
        LifecycleCardUse(damage.ResolutionId) is { Action.Type: CardActionType.Use } use &&
        !ProgramEventHistory<GuanduDamageRewardedEvent>().Any(e => e.UseFrameId == use.Id) ? use : null;
    private CardUseFrame? GuanduTenthUse(int owner, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.CardUse is not { } c ||
            LifecycleCardUse(c.ParentCardUseFrameId) is not { Action: { } action, ExtraCardEffect: null } use ||
            action.ActionId != c.CardActionId || action.ActorSeat != owner || action.Type != CardActionType.Use || !ExtraEffectCardKind(use.CardKind) ||
            use.CardKind is CardKind.Dodge or CardKind.Nullification) return null;
        return ProgramEventHistory<GuanduActualCardActionEvent>().Any(e => e.ActionId == action.ActionId && e.UseOrdinal % 10 == 0) ? use : null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 46000)]
    private bool AllowsGuanduEvent(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId != "guandu:battle-event") return true;
        if (!IsGuanduActivity || gate.WindowContext is not { } c) return false;
        return gate.Candidate.BindingId switch
        {
            "tenth-use" => CurrentGuanduEvent == GuanduBattleEvent.TenWinsTenLosses && GuanduTenthUse(gate.OwnerSeat, c) is not null,
            "damage-draw" => CurrentGuanduEvent == GuanduBattleEvent.GrainShortage && c.Amount > 0 && GuanduDamageRewardUse(gate.OwnerSeat, c) is not null,
            "turn-duel" => CurrentGuanduEvent == GuanduBattleEvent.SlayTheGenerals && gate.OwnerSeat == _currentSeat,
            "arrogance" => CurrentGuanduEvent == GuanduBattleEvent.Arrogance && gate.OwnerSeat == _currentSeat,
            "arrogance-discard" => CurrentGuanduEvent == GuanduBattleEvent.Arrogance && gate.OwnerSeat == _currentSeat &&
                GetHand(gate.Owner).Count + GetEquipment(gate.Owner).Count > 0 &&
                _players.Where(p => p.IsAlive && p.Seat != gate.OwnerSeat).All(p => p.Hp < gate.Owner.Hp),
            _ => false
        };
    }
    private SkillProgramStepOutcome ExecuteGuanduEvent(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (!IsGuanduActivity || f.WindowContext is not { } c || f.GuanduEventFlow is not null)
            throw new InvalidOperationException("A Guandu event lost its exact once-issued program.");
        if (op == SkillProgramEffectOp.ResolveGuanduRepeat)
        {
            var use = GuanduTenthUse(f.OwnerSeat, c) ?? throw new InvalidOperationException("The tenth-card event lost its actual use.");
            var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == c.ParentFrameId);
            var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
            ReplaceRuntimeFrame(use.Id, use with { ExtraCardEffect = new() { Source = source, ProgramFrameId = f.Id,
                ActionId = use.Action!.ActionId, AutomaticGameplayHash = f.GameplayHash, Simple = window.SimpleContinuation, Trick = window.TrickContinuation } });
            AdvanceEventRulesAndQueueFact(new AutomaticExtraCardEffectIssuedEvent(f.Id, source, use.Id, use.Action!.ActionId, f.GameplayHash));
            return SkillProgramStepOutcome.Continue;
        }
        var rewarding = op == SkillProgramEffectOp.RewardGuanduDamage;
        var useId = rewarding ? GuanduDamageRewardUse(f.OwnerSeat, c)?.Id : null;
        if (rewarding && useId is null) throw new InvalidOperationException("Grain shortage cannot reward a card twice.");
        if (!rewarding && op != SkillProgramEffectOp.ResolveGuanduArrogance) throw new InvalidOperationException("Unknown Guandu event operation.");
        if (!rewarding && _players.Any(p => p.IsAlive && GetHand(p).Count > GetHand(_players[f.OwnerSeat]).Count)) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { GuanduEventFlow = new(op, _turnNumber, useId) });
        AdvanceEventRulesAndQueueFact(new GuanduEventFlowIssuedEvent(f.Id, op, f.OwnerSeat, _turnNumber, useId));
        if (rewarding)
        {
            AdvanceEventRulesAndQueueFact(new GuanduDamageRewardedEvent(useId!.Value, f.OwnerSeat));
            BeginOperationProgramDraw(f, f.OwnerSeat, 1, new("guandu.grain-shortage.draw"));
            return SkillProgramStepOutcome.AwaitChild;
        }
        var result = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, 1);
        if (result == SkillProgramStepOutcome.AwaitChild || ResumeGuanduEventFlow(f.Id)) return SkillProgramStepOutcome.AwaitChild;
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 46000)]
    private bool ResumeGuanduEventFlow(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanduEventFlow: { } r } f || f.Id != id) return false;
        if (r.Operation == SkillProgramEffectOp.RewardGuanduDamage && f.NativeDraw is not null)
            throw new InvalidOperationException("Guandu draw cannot pass an unreturned draw request.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { GuanduEventFlow = null }); return false;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.RewardGuanduDamage)]
    private void ReturnGuanduEventDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.GuanduEventFlow is not { Operation: SkillProgramEffectOp.RewardGuanduDamage } ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1)
            throw new InvalidOperationException("A Guandu damage reward lost its exact native draw.");
        CompleteProgramDrawCards(f.Id, f.OwnerSeat, null, SkillProgramCardSetVisibility.Private, new("guandu.grain-shortage.draw"), 0, outcome.ActualDrawIds);
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 46000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 46000)]
    private bool RetainGuanduEventFlow(ProgramSkillFrame f) => f.GuanduEventFlow is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 46000)]
    private void AssertGuanduEventFlow(ProgramSkillFrame f, ProgramExecutionPlan plan)
    {
        if (f.GuanduEventFlow is not { } r) return;
        if (!IsGuanduActivity || f.SkillId != "guandu:battle-event" || r.ActualTurnNumber != _turnNumber ||
            plan.GetPausedInstruction(f.InstructionIndex).Effect.Op != r.Operation ||
            ProgramEventHistory<GuanduEventFlowIssuedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.OwnerSeat == f.OwnerSeat && e.Operation == r.Operation && e.ActualTurnNumber == r.ActualTurnNumber && e.UseFrameId == r.UseFrameId) != 1 ||
            (r.Operation == SkillProgramEffectOp.RewardGuanduDamage
                ? r.UseFrameId is not > 0 || ProgramEventHistory<GuanduDamageRewardedEvent>().Count(e => e.UseFrameId == r.UseFrameId && e.SourceSeat == f.OwnerSeat) != 1
                : r.Operation != SkillProgramEffectOp.ResolveGuanduArrogance || r.UseFrameId is not null))
            throw new InvalidOperationException("A Guandu event lost its exact instruction and once-issued payment.");
    }
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 10)]
    private bool IsGuanduArroganceDying() => _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is
        { GuanduEventFlow.Operation: SkillProgramEffectOp.ResolveGuanduArrogance } f && ActiveDying is { ResumesProgramSkill: true } d &&
        d.ParentFrameId == f.Id && d.VictimSeat == f.OwnerSeat;

    [ProgramCardLegality(ProgramCardLegalityStage.ConversionUsageBan, 30)]
    private bool GuanduRoundConversionUsageUnavailable(ProgramConfiguredViewAsQuery query) =>
        !CanUseGuanduRoundConversion(query.Instance, query.Rule, query.Owner);
}
