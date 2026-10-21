namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 61400)]
    private void RecordWushuangCombatUse(CardUseDeclaredEvent e)
    {
        if (!IsWushuangMode || ProgramEventHistory<WushuangCombatUseFactEvent>().Any(f => f.CardUseFrameId == e.ResolutionId)) return;
        var action = LifecycleCardUse(e.ResolutionId)?.Action;
        var kind = action?.EffectiveKind ?? e.CardKind;
        var ordinal = IsSlashCard(kind) ? 1 + ProgramEventHistory<WushuangCombatUseFactEvent>().Count(f => f.OwnerSeat == e.SourceSeat && IsSlashCard(f.Kind)) : 0;
        AdvanceEventRulesAndQueueFact(new WushuangCombatUseFactEvent(e.ResolutionId, _turnNumber, _roundNumber,
            e.SourceSeat, kind, action?.EffectiveSuit ?? e.PublicCard?.Suit ?? Suit.None, ordinal, _players[e.SourceSeat].HasAlcoholEffect));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 61401)]
    private void RecordWushuangCombatDamage(DamageAppliedEvent e)
    {
        if (!IsWushuangMode || _resolutionStack.OfType<DamageFrame>().LastOrDefault() is not { } f ||
            ProgramEventHistory<WushuangCombatDamageFactEvent>().Any(previous => previous.DamageFrameId == f.Id)) return;
        var request = ProgramEventHistory<DamageRequestedEvent>().LastOrDefault(r => r.ResolutionId == f.Id);
        AdvanceEventRulesAndQueueFact(new WushuangCombatDamageFactEvent(f.Id, _turnNumber, _roundNumber,
            e.SourceSeat, e.TargetSeat, e.Amount, request?.SourceCard, e.Nature, e.SourceLess));
    }

    private bool IsWushuangActualDraw(ProgramSkillWindowContext? c, int owner, out CardMovementRecord? movement)
    {
        movement = c?.MovementBatch is { } batch && c.MovementIndex is { } index && index >= 0 && index < batch.Movements.Count ? batch.Movements[index] : null;
        if (movement is not { From.Zone: CardZoneKind.DrawPile } m || m.To != CardLocation.Hand(owner) || m.Reason == CardMoveReasons.InitialDeal) return false;
        return ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Any(draw => draw.OwnerSeat == owner &&
            draw.SequenceBefore < m.Sequence && draw.SequenceAfter >= m.Sequence && draw.Reason == m.Reason);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 61400)]
    private bool AllowsWushuangCombatTrigger(ProgramTriggerLegalityContext gate)
    {
        if (!gate.SkillId.StartsWith("wushuang:tactic-", StringComparison.Ordinal) ||
            !int.TryParse(gate.SkillId.AsSpan("wushuang:tactic-".Length), out var id)) return true;
        if (!IsWushuangMode) return false;
        if (gate.WindowContext is not { } c) return true;
        if (c.Window == SkillProgramTriggerWindow.SkillsChanged) return true; // The acquisition bridge checks its exact paid parent.
        if (id is 2104 or 2143 or 2367)
        {
            if (!IsWushuangActualDraw(c, gate.OwnerSeat, out var movement)) return false;
            if (id == 2367)
            {
                var lucky = ProgramEventHistory<WushuangLuckyNumberEvent>().LastOrDefault(e => e.OwnerSeat == gate.OwnerSeat);
                return lucky is not null && _cardZones.CardsAt(_cardZones.GetLocation(movement!.CardId)).Single(card => card.Id == movement.CardId).Rank == lucky.Number;
            }
            return true;
        }
        if (id is 2120 or 2121 or 2122)
            return _currentSeat != gate.OwnerSeat && c.CardUse is { } use && use.ActorSeat != gate.OwnerSeat &&
                use.DesignatedTargetSeats is [var only] && only == gate.OwnerSeat &&
                (id == 2120 || id == 2121 && GetProgramCardCategory(use.EffectiveKind) == SkillProgramCardCategory.Basic ||
                 id == 2122 && GetProgramCardCategory(use.EffectiveKind) == SkillProgramCardCategory.Trick);
        if (id == 2116) return c.SourceSeat is { } source && source != gate.OwnerSeat && _players[source].IsAlive;
        if (id is 2174 or 2175) return IsWushuangDyingExitContext(gate.Candidate, c);
        if (id == 2130) return c.Amount >= 3 && c.TargetSeat is { } t && t != gate.OwnerSeat;
        if (id == 2131) return _currentSeat != gate.OwnerSeat && c.Amount >= 3 && c.SourceSeat is { } s && s != gate.OwnerSeat && _players[s].IsAlive;
        if (id is 2245 or 2246 or 2247) return _roundNumber == (id == 2247 ? 2 : 3);
        if (id == 2337) return c.CardUse is { } original && LifecycleCardUse(original.ParentCardUseFrameId) is { ExtraCardEffect: null, CardKind: CardKind.Snatch };
        return true;
    }

    [ProgramTargetRule(ProgramTargetRulePoint.TargetCountContributions, 61400)]
    private RuleQueryContribution? WushuangCombatExtraTargets(ProgramTargetCountQuery q)
    {
        if (!IsWushuangMode) return null;
        if (q.Kind == CardKind.IronChain && HasWushuangTactic(q.Actor.Seat, 2007))
            return new FiniteRuleQueryContribution("wushuang:iron-chain-all", SkillRuleOperation.Add, Math.Max(0, _playerCount - 2));
        if (!IsSlashCard(q.Kind) || ProgramEventHistory<WushuangCombatUseFactEvent>().Any(f => f.OwnerSeat == q.Actor.Seat && f.Round == _roundNumber && IsSlashCard(f.Kind))) return null;
        var amount = (HasWushuangTactic(q.Actor.Seat, 2033) ? 1 : 0) + (HasWushuangTactic(q.Actor.Seat, 2034) ? 2 : 0);
        return amount == 0 ? null : new FiniteRuleQueryContribution("wushuang:first-round-slash-targets", SkillRuleOperation.Add, amount);
    }

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 61400)]
    private int WushuangCombatDamageAmount(IDamageAttempt attack)
    {
        if (!IsWushuangMode) return 0;
        var bonus = 0; var source = attack.SourceSeat; var target = attack.TargetSeat;
        var facts = ProgramEventHistory<WushuangCombatDamageFactEvent>();
        if (!attack.IsSourceLess && !attack.IsChainPropagation)
        {
            if (attack.EffectiveCardKind is null)
                bonus += (HasWushuangTactic(source, 2171) ? 1 : 0) + (HasWushuangTactic(source, 2172) ? 2 : 0);
            if (!attack.IsChainPropagation && attack.EffectiveCardKind is { } kind && IsSlashCard(kind))
            {
                var use = ProgramEventHistory<WushuangCombatUseFactEvent>().LastOrDefault(f => f.CardUseFrameId == attack.ResolutionId);
                if (use?.SlashOrdinal % 3 == 0) bonus += (HasWushuangTactic(source, 2042) ? 1 : 0) + (HasWushuangTactic(source, 2043) ? 2 : 0);
                if (use?.Suit == Suit.Heart && HasWushuangTactic(source, 2045)) bonus += 2;
                if (HasWushuangTactic(source, 2115) && source == _currentSeat)
                {
                    var previous = ProgramEventHistory<TurnStartedEvent>().LastOrDefault(t => t.ActorSeat == source && t.TurnNumber < _turnNumber);
                    if (previous is not null && !ProgramEventHistory<WushuangCombatUseFactEvent>().Any(f => f.OwnerSeat == source && f.Turn == previous.TurnNumber && IsSlashCard(f.Kind))) bonus++;
                }
                if (ActiveBorrowedSword is { ActiveAttack: { } borrowedAttack } borrowed && borrowedAttack.ResolutionId == attack.ResolutionId && HasWushuangTactic(borrowed.SourceSeat, 2244)) bonus++;
            }
            if (HasWushuangTactic(source, 2127))
            {
                var tail = facts.SkipWhile(f => !ReferenceEquals(f, facts.LastOrDefault(x => x.SourceSeat == source && !x.SourceLess))).Skip(facts.Any(f => f.SourceSeat == source && !f.SourceLess) ? 1 : 0);
                if (!facts.Any(f => f.SourceSeat == source && !f.SourceLess)) tail = facts;
                bonus += tail.Count(f => f.TargetSeat == source && f.Amount > 0 && ProgramEventHistory<TurnStartedEvent>().LastOrDefault(t => t.TurnNumber == f.Turn)?.ActorSeat != source);
            }
            if (_roundNumber > 1)
            {
                var previousRoundDamage = facts.Where(f => f.Round == _roundNumber - 1 && f.SourceSeat == source && !f.SourceLess).Sum(f => f.Amount);
                bonus += HasWushuangTactic(source, 2188) && previousRoundDamage == 0 ? 1 : 0;
                bonus += HasWushuangTactic(source, 2189) && previousRoundDamage < 3 ? 1 : 0;
            }
            if (source == _currentSeat && !facts.Any(f => f.Turn == _turnNumber && f.SourceSeat == source && !f.SourceLess))
                bonus += (HasWushuangTactic(source, 2333) ? 1 : 0) + (HasWushuangTactic(source, 2334) ? 2 : 0);
            if (attack.EffectiveCardKind is null || GetProgramCardCategory(attack.EffectiveCardKind.Value) == SkillProgramCardCategory.Trick)
                bonus += (HasWushuangTactic(source, 2357) && _roundNumber >= 2 ? 2 : 0) +
                    (HasWushuangTactic(source, 2358) && _roundNumber >= 4 ? 1 : 0) + (HasWushuangTactic(source, 2359) && _roundNumber >= 3 ? 1 : 0);
        }
        if (target != _currentSeat && !facts.Any(f => f.Turn == _turnNumber && f.TargetSeat == target))
            bonus += (HasWushuangTactic(target, 2333) ? 1 : 0) + (HasWushuangTactic(target, 2334) ? 2 : 0);
        var received = ProgramEventHistory<WushuangIncomingReductionIssuedEvent>().Count(f => f.Round == _roundNumber && f.TargetSeat == target);
        if (HasWushuangTactic(target, 2196) || HasWushuangTactic(target, 2197))
            AdvanceEventRulesAndQueueFact(new WushuangIncomingReductionIssuedEvent(attack.ResolutionId, target, _roundNumber, received + 1));
        if (HasWushuangTactic(target, 2196) && received < 2) bonus--;
        if (HasWushuangTactic(target, 2197) && received < 3) bonus -= 2;
        return bonus;
    }

    private bool WushuangFixedDamage(IDamageAttempt attack) => !attack.IsSourceLess && HasWushuangTactic(attack.SourceSeat, 2210);
    [ProgramDamageHook(ProgramDamagePhase.AfterEquipmentChoices, 61400)]
    private ProgramDamageInterception WushuangNormalizeFixedDamage(IDamageAttempt attack, int amount, DamageNature nature)
    {
        if (!WushuangFixedDamage(attack)) return ProgramDamageInterception.Continue;
        if (attack.DamageAmount > 2) attack.ReduceFinalizedDamageAmount(attack.DamageAmount - 2);
        if (attack.DamageAmount < 2) attack.IncreaseFinalizedDamageAmount(2 - attack.DamageAmount);
        return ProgramDamageInterception.Continue;
    }
    private bool WushuangArmorBypass(IDamageAttempt attack) => !attack.IsSourceLess && HasWushuangTactic(attack.SourceSeat, 2177);
    private int WushuangArmorDamageMultiplier(IDamageAttempt attack) => !attack.IsSourceLess && HasWushuangTactic(attack.SourceSeat, 2193) ? 2 : 1;

    private bool WushuangDrunkenSlashUnrespondable(CardAttackHandle attack) => HasWushuangTactic(attack.CardUserSeat, 2001) &&
        (LifecycleCardUse(attack.ResolutionId)?.ConsumedAlcoholBoost == true ||
         ProgramEventHistory<WushuangCombatUseFactEvent>().LastOrDefault(e => e.CardUseFrameId == attack.ResolutionId) is { Drunken: true });

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 61400)]
    private void WushuangExtraAlcoholQuota(JianjiOrdinaryAlcoholPaidEvent e)
    {
        if (!IsWushuangMode || !e.CountsQuota) return;
        var extra = (HasWushuangTactic(e.ActorSeat, 2108) ? 1 : 0) + (HasWushuangTactic(e.ActorSeat, 2109) ? 2 : 0);
        if (extra > 0 && ProgramEventHistory<JianjiOrdinaryAlcoholPaidEvent>().Count(p => p.ActualTurn == _turnNumber && p.ActorSeat == e.ActorSeat && p.CountsQuota) < 1 + extra)
            _players[e.ActorSeat].UsedPlayPhaseAlcoholThisTurn = false;
    }

    private int WushuangDrawTwoCount(int sourceSeat) => HasWushuangTactic(sourceSeat, 2030) ? 3 : 2;

    private bool WushuangHasVisibleTargetHand(int source, CardKind kind) =>
        kind == CardKind.Dismantlement && HasWushuangTactic(source, 2005) || kind == CardKind.Snatch && HasWushuangTactic(source, 2006);

    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 61400)]
    private IEnumerable<CardSnapshot> WushuangVisibleTargetHand(int viewer) =>
        ActiveTargetCardSelection is { } selection && selection.SourceSeat == viewer && WushuangHasVisibleTargetHand(viewer, selection.EffectCardKind)
            ? GetHand(_players[selection.TargetSeat]).Select(ToSnapshot) : [];
}
