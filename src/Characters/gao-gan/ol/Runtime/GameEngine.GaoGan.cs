namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksJuguanDrawObligations => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ObserveJuguanDrawObligation);
    private bool IsJuguanConversion(CardConversionSource source) => _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program is { } program &&
        program.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ObserveJuguanDrawObligation)) &&
        program.ViewAs.Any(v => v.Id == source.BindingId && v.UsesPerPhase == 1 && v.UsageGroup == "juguan" &&
            v.InputCount == 1 && v.SourceZones.SequenceEqual([CardZoneKind.Hand]) && v.ForPlay && !v.ForResponse &&
            v.OutputKind is CardKind.Slash or CardKind.Duel);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26200)]
    private void ObserveJuguanActualUseAndDamage(IGameEvent payload)
    {
        if (!TracksJuguanDrawObligations) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use &&
            use.Action is { Type: CardActionType.Use, PhysicalCards.Count: 1 } action &&
            action.ConversionChain.FirstOrDefault(IsJuguanConversion) is { } source)
        {
            if (use.JuguanUse is not null || action.ActorSeat != source.OwnerSeat || action.ProviderSeat != source.OwnerSeat ||
                action.PhysicalCards[0].From != CardLocation.Hand(source.OwnerSeat) || _phase != TurnPhase.Play || _currentSeat != source.OwnerSeat ||
                declared.CardId != use.CardId || declared.CardKind != use.CardKind || declared.SourceSeat != source.OwnerSeat)
                throw new InvalidOperationException("Juguan needs its actual native one-hand-card phase-limited use and original source.");
            var receipt = new JuguanUseReceipt(use.Id, action.ActionId, source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,
                _turnNumber, _cardUseDebitPhaseInstanceId, action.PhysicalCards[0].CardId, action.EffectiveKind);
            ReplaceRuntimeFrame(use.Id, use with { JuguanUse = receipt });
            AdvanceEventRulesAndQueueFact(new JuguanUseIssuedEvent(receipt));
            return;
        }
        if (payload is not DamageAppliedEvent { Amount: > 0 } applied || _resolutionStack.LastOrDefault() is not DamageFrame damage ||
            CurrentDamageAttempt is not { } attack || damage.ParentFrameId != attack.ResolutionId ||
            damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat || damage.Amount != applied.Amount || damage.Nature != applied.Nature ||
            attack.SourceSeat != applied.SourceSeat || attack.TargetSeat != applied.TargetSeat || attack.IsSourceLess != applied.SourceLess || !attack.DamageWasApplied ||
            !CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Any(e => e.ResolutionId == damage.Id && e.SourceSeat == applied.SourceSeat &&
                e.TargetSeat == applied.TargetSeat && e.Amount == applied.Amount && e.Nature == applied.Nature && e.SourceLess == applied.SourceLess)) return;
        if (LifecycleCardUse(attack.ResolutionId) is { JuguanUse: { } r } actual && !applied.SourceLess)
        {
            RequireJuguanUse(actual);
            if (!CompleteProgramEventHistory().OfType<JuguanVictimDamagedEvent>().Any(e => e.DamageFrameId == damage.Id && e.CardUseFrameId == actual.Id))
                AdvanceEventRulesAndQueueFact(new JuguanVictimDamagedEvent(actual.Id, r.CardActionId, r.Source.OwnerSeat,
                    applied.TargetSeat, damage.Id, _turnNumber, applied.SourceSeat, applied.Amount));
        }
        if (applied.SourceLess) return;
        foreach (var v in PendingJuguanVictims(applied.TargetSeat).Where(v => v.VictimSeat == applied.SourceSeat && v.DamageFrameId != damage.Id)
            .DistinctBy(v => (v.CardUseFrameId, v.OwnerSeat, v.VictimSeat)))
            if (!CompleteProgramEventHistory().OfType<JuguanRetaliationRecordedEvent>().Any(e => e.CardUseFrameId == v.CardUseFrameId && e.OwnerSeat == v.OwnerSeat && e.VictimSeat == v.VictimSeat))
                AdvanceEventRulesAndQueueFact(new JuguanRetaliationRecordedEvent(v.CardUseFrameId, v.OwnerSeat, v.VictimSeat, damage.Id, _turnNumber));
    }

    private IEnumerable<JuguanVictimDamagedEvent> PendingJuguanVictims(int owner)
    {
        var lastDeadline = CompleteProgramEventHistory().OfType<JuguanDeadlineSettledEvent>().Where(e => e.OwnerSeat == owner)
            .Select(e => e.ActualTurn).DefaultIfEmpty().Max();
        return CompleteProgramEventHistory().OfType<JuguanVictimDamagedEvent>().Where(e => e.OwnerSeat == owner && e.ActualTurn >= lastDeadline);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26201)]
    private void ObserveJuguanDeadlineAndActualDraw(IGameEvent payload)
    {
        if (!TracksJuguanDrawObligations) return;
        if (payload is TurnStartedEvent start && CompleteProgramEventHistory().OfType<JuguanUseIssuedEvent>().Any(e => e.Receipt.Source.OwnerSeat == start.ActorSeat))
        {
            var qualified = PendingJuguanVictims(start.ActorSeat).Any(v => !CompleteProgramEventHistory().OfType<JuguanRetaliationRecordedEvent>()
                .Any(r => r.CardUseFrameId == v.CardUseFrameId && r.OwnerSeat == v.OwnerSeat && r.VictimSeat == v.VictimSeat));
            AdvanceEventRulesAndQueueFact(new JuguanDeadlineSettledEvent(start.ActorSeat, start.TurnNumber, qualified));
        }
        if (payload is DrawPhaseSkippedEvent skipped && CurrentJuguanDeadline(skipped.OwnerSeat) is { Qualified: true } deadline &&
            !CompleteProgramEventHistory().OfType<JuguanSkippedDrawExpiredEvent>().Any(e => e.OwnerSeat == skipped.OwnerSeat && e.ActualTurn == deadline.ActualTurn))
            AdvanceEventRulesAndQueueFact(new JuguanSkippedDrawExpiredEvent(skipped.OwnerSeat, skipped.ActualTurnNumber, skipped.FrameId));
        if (payload is ActualDiscardRecoveryPhaseStartedEvent { Phase.Kind: ActualDiscardRecoveryPhaseKind.Draw } draw &&
            CurrentJuguanDeadline(draw.Phase.ActorSeat) is { Qualified: true } ready && ready.ActualTurn == draw.Phase.TurnNumber &&
            !CompleteProgramEventHistory().OfType<JuguanDrawPhaseBoundEvent>().Any(e => e.OwnerSeat == draw.Phase.ActorSeat && e.DeadlineTurn == ready.ActualTurn) &&
            !CompleteProgramEventHistory().OfType<JuguanSkippedDrawExpiredEvent>().Any(e => e.OwnerSeat == draw.Phase.ActorSeat && e.ActualTurn == ready.ActualTurn))
            AdvanceEventRulesAndQueueFact(new JuguanDrawPhaseBoundEvent(draw.Phase.ActorSeat, ready.ActualTurn, draw.Phase));
    }
    private JuguanDeadlineSettledEvent? CurrentJuguanDeadline(int owner) => CompleteProgramEventHistory().OfType<JuguanDeadlineSettledEvent>()
        .LastOrDefault(e => e.OwnerSeat == owner && e.ActualTurn == _turnNumber);
    private int JuguanActualDrawAdjustment(CharacterState player)
    {
        if (!TracksJuguanDrawObligations || !player.IsAlive || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Draw } phase ||
            phase.ActorSeat != player.Seat || CurrentJuguanDeadline(player.Seat) is not { Qualified: true } ready) return 0;
        return CompleteProgramEventHistory().OfType<JuguanDrawPhaseBoundEvent>().Count(e => e.OwnerSeat == player.Seat && e.DeadlineTurn == ready.ActualTurn && e.Phase == phase) == 1 &&
            !CompleteProgramEventHistory().OfType<JuguanSkippedDrawExpiredEvent>().Any(e => e.OwnerSeat == player.Seat && e.ActualTurn == ready.ActualTurn) ? 2 : 0;
    }

    private void RequireJuguanUse(CardUseFrame use)
    {
        if (use.JuguanUse is not { } r || r.CardUseFrameId != use.Id || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 1 } action ||
            action.ActionId != r.CardActionId || !action.ConversionChain.Contains(r.Source) || action.PhysicalCards[0].CardId != r.MaterialCardId ||
            action.PhysicalCards[0].From != CardLocation.Hand(r.Source.OwnerSeat) || r.ActualTurn > _turnNumber || r.PhaseInstance < 1 ||
            !IsJuguanConversion(r.Source) || _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
            CompleteProgramEventHistory().OfType<JuguanUseIssuedEvent>().Where(e => e.Receipt.CardUseFrameId == use.Id).ToArray() is not [var fact] || fact.Receipt != r)
            throw new InvalidOperationException("Juguan lost its exact issued native use, one real hand entity, source hash or actual action identity.");
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 26200)]
    private void AssertJuguanUses()
    {
        if (!TracksJuguanDrawObligations) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.JuguanUse is not null ||
            CompleteProgramEventHistory().OfType<JuguanUseIssuedEvent>().Any(e => e.Receipt.CardUseFrameId == u.Id))) RequireJuguanUse(use);
    }
}
