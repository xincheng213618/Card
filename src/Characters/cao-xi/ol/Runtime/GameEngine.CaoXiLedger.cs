namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasCaoXiGangshuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveGangshuAttributeIncrement);
    private bool HasCaoXiCapability => HasCaoXiGangshuCapability || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJianxuanRepeatedDraw);
    private IGameEvent? _caoXiHistoryTail;
    private int _caoXiHistoryCount = -1;
    private IReadOnlyList<CaoXiGangshuIncreasedEvent> _caoXiActiveUnits = [];
    private IReadOnlyList<CaoXiGangshuIncreasedEvent> ActiveCaoXiGangshuUnits()
    {
        if (!HasCaoXiGangshuCapability) return [];
        var history = CompleteProgramEventHistory().ToArray(); var tail = history.LastOrDefault();
        if (_caoXiHistoryCount == history.Length && ReferenceEquals(_caoXiHistoryTail, tail)) return _caoXiActiveUnits;
        var expired = history.OfType<CaoXiGangshuUnitExpiredEvent>().Select(e => e.GrantFrameId).ToHashSet();
        _caoXiActiveUnits = Array.AsReadOnly(history.OfType<CaoXiGangshuIncreasedEvent>().Where(e => !expired.Contains(e.FrameId)).ToArray());
        _caoXiHistoryTail = tail; _caoXiHistoryCount = history.Length; return _caoXiActiveUnits;
    }
    private bool IsCaoXiDrawUnitEligible(CaoXiGangshuIncreasedEvent unit, ActualDiscardRecoveryPhaseKey? phase)
    {
        if (unit.Query != SkillRuleQuery.DrawCount) return false;
        if (phase is not { Kind: ActualDiscardRecoveryPhaseKind.Draw } draw) return true;
        return unit.GrantedAfterActualPhaseToken < draw.Token && unit.GrantedInDrawPhaseToken != draw.Token && !CompleteProgramEventHistory().OfType<CaoXiGangshuDrawUnitIssuedEvent>()
            .Any(e => e.GrantFrameId == unit.FrameId && e.Phase.Token != draw.Token);
    }
    private int CaoXiGangshuBonus(CharacterState player, SkillRuleQuery query)
    {
        if (!HasCaoXiGangshuCapability || !player.IsAlive || query is not (SkillRuleQuery.AttackRange or SkillRuleQuery.DrawCount or SkillRuleQuery.SlashLimit)) return 0;
        var phase = CurrentActualDiscardRecoveryPhase();
        return ActiveCaoXiGangshuUnits().Count(e => e.Source.OwnerSeat == player.Seat && e.Query == query &&
            (query != SkillRuleQuery.DrawCount || IsCaoXiDrawUnitEligible(e, phase)));
    }
    private CaoXiNumbers CurrentCaoXiNumbers(int seat)
    {
        var player = _players[seat]; var draw = GetTurnDrawCount(player);
        // The recorded next-draw number includes future units acquired during an
        // already running Draw, while that phase's native count excludes them.
        var allDrawUnits = player.IsAlive ? ActiveCaoXiGangshuUnits().Count(e => e.Source.OwnerSeat == seat && e.Query == SkillRuleQuery.DrawCount) : 0;
        return new(GetAttackRange(seat), checked(draw + allDrawUnits - CaoXiGangshuBonus(player, SkillRuleQuery.DrawCount)), GetSlashUseLimit(player));
    }
    private IReadOnlyList<CaoXiGangshuIncreasedEvent> FreezeCaoXiGangshuDrawUse(CharacterState player)
    {
        if (!HasCaoXiGangshuCapability || !player.IsAlive || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase ||
            phase.ActorSeat != player.Seat || !IsRecordedActualDiscardRecoveryPhase(phase)) return [];
        return Array.AsReadOnly(ActiveCaoXiGangshuUnits().Where(e => e.Source.OwnerSeat == player.Seat && IsCaoXiDrawUnitEligible(e, phase)).ToArray());
    }
    private void CaptureCaoXiGangshuDrawUse(CharacterState player, IReadOnlyList<CaoXiGangshuIncreasedEvent> frozen)
    {
        if (!HasCaoXiGangshuCapability || frozen.Count == 0) return;
        if (CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase || phase.ActorSeat != player.Seat ||
            !player.IsAlive || !IsRecordedActualDiscardRecoveryPhase(phase) || frozen.Select(e => e.FrameId).Distinct().Count() != frozen.Count ||
            frozen.Any(e => e.Source.OwnerSeat != player.Seat || e.Query != SkillRuleQuery.DrawCount || !ActiveCaoXiGangshuUnits().Contains(e) || !IsCaoXiDrawUnitEligible(e, phase)))
            throw new InvalidOperationException("Gangshu draw usage requires the exact live actual-phase token and frozen attributable units.");
        var old = CompleteProgramEventHistory().OfType<CaoXiGangshuDrawUnitIssuedEvent>().Where(e => e.Phase == phase).ToArray();
        if (old.Length != 0)
        {
            if (!old.Select(e => e.GrantFrameId).Order().SequenceEqual(frozen.Select(e => e.FrameId).Order()))
                throw new InvalidOperationException("A frozen actual draw cannot replace its original Gangshu units.");
            return;
        }
        AdvanceEventRulesAndQueueFact(new CaoXiGangshuDrawCaptureEvent(phase, player.Seat));
        foreach (var unit in frozen)
            AdvanceEventRulesAndQueueFact(new CaoXiGangshuDrawUnitIssuedEvent(phase, unit.FrameId, unit.Source));
    }
    private void ExpireCaoXiGangshuUnits(int seat, string reason, long nativeId, Func<CaoXiGangshuIncreasedEvent, bool>? select = null)
    {
        foreach (var unit in ActiveCaoXiGangshuUnits().Where(e => e.Source.OwnerSeat == seat && (select is null || select(e))).ToArray())
            AdvanceEventRulesAndQueueFact(new CaoXiGangshuUnitExpiredEvent(unit.FrameId, seat, reason, nativeId));
    }
    private void CaptureCaoXiGangshuSlashOffset(CardAttackHandle attack)
    {
        if (!HasCaoXiGangshuCapability || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) || attack.SuccessfulDodgeResponses != attack.RequiredDodgeResponses ||
            IsSlashDodgeCancellationPrevented(attack) || !IsValidPlayerSeat(attack.TargetSeat)) return;
        var cursor = LifecycleCardUse(attack.ResolutionId)?.TargetIndex ?? 0;
        if (CompleteProgramEventHistory().OfType<CaoXiGangshuActualOffsetEvent>().Any(e => e.NativeId == attack.ResolutionId && e.OwnerSeat == attack.TargetSeat && e.Kind == "slash" && e.Cursor == cursor)) return;
        // This helper is called only from the real successful native Slash-Dodge
        // return, never from a generic response or an armor immunity event.
        AdvanceEventRulesAndQueueFact(new CaoXiGangshuActualOffsetEvent(attack.ResolutionId, attack.TargetSeat, "slash", cursor));
        ExpireCaoXiGangshuUnits(attack.TargetSeat, "actual-slash-offset", attack.ResolutionId);
    }
    private void CaptureCaoXiNativeCounterspellOffset(NullificationWindowFrame pending, CardActionContext action, bool invalidated)
    {
        if (!HasCaoXiGangshuCapability) return;
        if (_resolutionStack.LastOrDefault() is not NullificationWindowFrame current || current.Id != pending.Id || current.ParentFrameId != pending.ParentFrameId ||
            current.ChainDepth != pending.ChainDepth || current.EffectNullified != pending.EffectNullified || pending.ChainDepth < 1 || pending.NamedAmbushResponse is not null ||
            action.Type != CardActionType.Response || action.EffectiveKind != CardKind.Nullification || action.ActorSeat != action.ProviderSeat ||
            action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null || action.OpponentSeat != pending.SourceSeat ||
            action.ParentActionId != NullificationParentActionId(pending) ||
            !CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId && SameOverflowAction(e.Action, action)))
            throw new InvalidOperationException("Gangshu offset requires the exact accepted native counterspell node after its real invalidation decision.");
        if (invalidated || pending.ChainDepth == 1 && HasCurrentCardEnhancement(pending.ParentFrameId, CurrentCardEnhancement.Uncancelable) ||
            CompleteProgramEventHistory().OfType<CaoXiGangshuActualOffsetEvent>().Any(e => e.NativeId == pending.Id && e.OwnerSeat == action.ActorSeat && e.Kind == "nullification" && e.Cursor == pending.ChainDepth)) return;
        AdvanceEventRulesAndQueueFact(new CaoXiGangshuActualOffsetEvent(pending.Id, action.ActorSeat, "nullification", pending.ChainDepth));
        ExpireCaoXiGangshuUnits(action.ActorSeat, "actual-nullification-offset", pending.Id);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26810)]
    private void ObserveCaoXiGangshuExpiry(IGameEvent payload)
    {
        if (!HasCaoXiGangshuCapability) return;
        if (payload is ActualDrawPhaseCompletedEvent draw && !draw.Skipped && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase &&
            phase.ActorSeat == draw.OwnerSeat && phase.TurnNumber == draw.ActualTurnNumber)
        {
            var used = CompleteProgramEventHistory().OfType<CaoXiGangshuDrawUnitIssuedEvent>().Where(e => e.Phase == phase).Select(e => e.GrantFrameId).ToHashSet();
            ExpireCaoXiGangshuUnits(draw.OwnerSeat, "actual-draw-completed", draw.FrameId, e => e.Query == SkillRuleQuery.DrawCount && used.Contains(e.FrameId));
        }
        else if (payload is PlayerDiedEvent death) ExpireCaoXiGangshuUnits(death.VictimSeat, "owner-death", death.VictimSeat);
    }
}
