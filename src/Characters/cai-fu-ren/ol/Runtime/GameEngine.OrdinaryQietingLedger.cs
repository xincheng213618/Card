namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsOrdinaryCaiFuRenQietingTrigger(SkillProgramTrigger trigger) => trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting }];

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27596)]
    private void ObserveOrdinaryQietingUses(IGameEvent e)
    {
        if (!HasOrdinaryCaiFuRenCapability || e is OrdinaryQietingActualUseEvent || _turnNumber <= 0) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().ToArray())
            if (ProgramEventHistory<CardUseDeclaredEvent>().Any(d => d.ResolutionId == use.Id)) RecordOrdinaryQietingUse(use);
    }
    [NativeUsePreparation(27596)]
    private bool CaptureOrdinaryQietingFinalUse(CardUseFrame use)
    {
        if (HasOrdinaryCaiFuRenCapability && ProgramEventHistory<CardUseDeclaredEvent>().Any(d => d.ResolutionId == use.Id)) RecordOrdinaryQietingUse(use);
        return false;
    }
    private void RecordOrdinaryQietingUse(CardUseFrame use)
    {
        if (use.Action is { Type: not CardActionType.Use }) return;
        var targets = use.Action?.EffectiveDesignatedTargetSeats ?? use.TargetSeats;
        var old = ProgramEventHistory<OrdinaryQietingActualUseEvent>().LastOrDefault(e => e.NativeUseId == use.Id);
        var next = new OrdinaryQietingActualUseEvent(old?.ActualTurn ?? _turnNumber, old?.TurnOwnerSeat ?? _currentSeat,
            use.Id, use.Action?.ActionId, use.SourceSeat, targets.Any(t => t != use.SourceSeat), targets.Distinct().Count());
        if (next != old) AdvanceEventRulesAndQueueFact(next);
    }
    private bool OrdinaryQietingUsedOnAnother(OrdinaryQietingTurnContext c) => CompleteProgramEventHistory().Take(checked((int)c.HistoryCutoff)).OfType<OrdinaryQietingActualUseEvent>()
        .Where(e => e.ActualTurn == c.ActualTurn && e.TurnOwnerSeat == c.TurnOwnerSeat).GroupBy(e => e.NativeUseId).Select(g => g.Last()).Any(e => e.ActorSeat == c.TurnOwnerSeat && e.UsedOnAnother);
    private ProgramSkillWindowContext CreateOrdinaryCaiFuRenQietingContext(DeferredTurnEndFrame parent, ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != "ol:qieting" || !IsOrdinaryCaiFuRenQietingTrigger(GetProgramTrigger(candidate))) return context;
        var c = new OrdinaryQietingTurnContext(parent.TurnNumber, parent.OwnerSeat, CompleteProgramEventHistory().Count(), false);
        return context with { OrdinaryQieting = c with { UsedOnAnother = OrdinaryQietingUsedOnAnother(c) } };
    }
    private bool IsEligibleOrdinaryCaiFuRenQietingCandidate(DeferredTurnEndFrame parent, ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        var history = CompleteProgramEventHistory().ToArray();
        return candidate.SkillId == "ol:qieting" && candidate.BindingId == "ordinary-turn-end" && IsOrdinaryCaiFuRenQietingTrigger(GetProgramTrigger(candidate)) && context.Window == SkillProgramTriggerWindow.AfterTurnEnded && IsActualAfterTurnEndedParent(parent) && parent.Id == context.ParentFrameId && parent.AfterTurnEnded is { } w && w.ItemIndex >= 0 && w.ItemIndex < w.Items.Count && w.Items[w.ItemIndex].Candidate == candidate && context.Facts == w.Items[w.ItemIndex].Facts && context.OwnerSeat == candidate.OwnerSeat && context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat && context.OccurrenceIndex == candidate.OccurrenceIndex && IsValidPlayerSeat(candidate.OwnerSeat) && _players[candidate.OwnerSeat].IsAlive && candidate.OwnerSeat != parent.OwnerSeat && context.OrdinaryQieting is { UsedOnAnother: false } c && c.ActualTurn == parent.TurnNumber && c.TurnOwnerSeat == parent.OwnerSeat && c.HistoryCutoff >= 0 && c.HistoryCutoff <= history.Length && !OrdinaryQietingUsedOnAnother(c) && history.Take((int)c.HistoryCutoff).OfType<TurnStartedEvent>().Any(e => e.TurnNumber == c.ActualTurn && e.ActorSeat == c.TurnOwnerSeat) && history.Take((int)c.HistoryCutoff).OfType<TurnEndedEvent>().Any(e => e.TurnNumber == c.ActualTurn && e.ActorSeat == c.TurnOwnerSeat) && !history.Skip((int)c.HistoryCutoff).OfType<OrdinaryQietingActualUseEvent>().Any(e => e.ActualTurn == c.ActualTurn && e.TurnOwnerSeat == c.TurnOwnerSeat);
    }
}
