namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RestrictWushuangQianxiJudgmentColor(ProgramSkillFrame f)
    {
        var outcome = f.JudgmentOutcomeBindings?.SingleOrDefault(o => o.Name == "qianxi-outcome");
        if (f.SkillId != "wushuang:skill-3180" || outcome is null || !IsValidProgramJudgmentOutcome(f, outcome) ||
            f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive ||
            GetSeatDistance(f.OwnerSeat, target) != 1)
            throw new InvalidOperationException("Qianxi requires its finalized judgment color and exact distance-one other target.");
        if (outcome.Suit is { } suit && suit != Suit.None)
        {
            var grant = _turnCardUseEffects.GrantHandColorRestriction(_turnNumber, _currentSeat, f.Id,
                f.InstructionIndex - 1, CreateProgramTurnEffectSource(f), target, IsRedSuit(suit));
            AdvanceEventRulesAndQueueFact(new HandCardColorRestrictionGrantedEvent(grant));
        }
        FinishWushuangOriginal(f);
    }
    private void SkipWushuangFenliPhase(ProgramSkillFrame f)
    {
        if (f.SkillId != "wushuang:skill-3125" || f.OwnerSeat != _currentSeat ||
            f.WindowContext is not { } c || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent || parent.Id != c.ParentFrameId || parent.OwnerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Fenli requires its exact actual own-phase boundary.");
        if (c.Window == SkillProgramTriggerWindow.DrawPhaseStarting)
        {
            ReplaceRuntimeFrame(parent.Id, parent with { ActualPhaseSubstitution = new(parent.Id, f.Id,
                f.OwnerSeat, f.SkillId, f.SkillInstanceId, _turnNumber, SkillProgramTurnPhase.Draw) });
            AdvanceEventRulesAndQueueFact(new ProgramTurnPhaseSubstitutedEvent(f.Id, f.SkillId, GetProgramBindingId(f),
                f.SkillInstanceId, f.OwnerSeat, _turnNumber, c.Window, SkillProgramTurnPhase.Draw));
        }
        else SkipProgramTurnPhases(f, c.Window switch
        {
            SkillProgramTriggerWindow.AfterNormalDraw => [SkillProgramTurnPhase.Play],
            SkillProgramTriggerWindow.DiscardPhaseStarting => [SkillProgramTurnPhase.Discard],
            _ => throw new InvalidOperationException("Fenli lost its applicable hand, health or equipment phase.")
        });
        FinishWushuangOriginal(f);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 61010)]
    private bool AllowsWushuangOriginalTrigger(ProgramTriggerLegalityContext gate)
    {
        var owner = gate.Owner; var c = gate.Context;
        switch (gate.SkillId)
        {
            case "wushuang:skill-3079":
                if (owner.Seat == _currentSeat) return false;
                if (c.Window != SkillProgramTriggerWindow.CardsMoved) return true;
                return c.MovementBatch is { } batch && c.MovementIndex is { } index && index >= 0 && index < batch.Movements.Count &&
                    new[] { batch.Movements[index] }.Any(m => m.To == CardLocation.DiscardPile &&
                    IsRedSuit(EffectiveSuit(owner, GetAdvancedCard(m.CardId))) &&
                    GetProgramDiscardSource(m) is { OwnerSeat: var discardedOwner, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } && discardedOwner == owner.Seat);
            case "wushuang:skill-3125":
                return gate.Candidate.BindingId switch
                {
                    "skip-draw-most-hand" => _players.Where(p => p.IsAlive).All(p => GetHand(p).Count <= GetHand(owner).Count),
                    "skip-play-most-hp" => owner.HasHpValue && _players.Where(p => p.IsAlive && p.HasHpValue).All(p => p.Hp <= owner.Hp),
                    "skip-discard-most-equipment" => GetEquipment(owner).Count > 0 && _players.Where(p => p.IsAlive).All(p => GetEquipment(p).Count <= GetEquipment(owner).Count),
                    _ => false
                };
            case "wushuang:skill-3180":
                return _players.Any(p => p.IsAlive && p.Seat != owner.Seat && GetSeatDistance(owner.Seat, p.Seat) == 1);
            case "wushuang:skill-3247":
                return c.CardUse is { } action && LifecycleCardUse(action.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } use } frame &&
                    use.ActorSeat == owner.Seat && owner.Seat == _currentSeat && _phase == TurnPhase.Play &&
                    CaoYingDamageKind(use.EffectiveKind) && frame.TargetSeats.Any(t => _players[t].IsAlive);
            case "wushuang:skill-3284":
                return c.CardUse is { } card && LifecycleCardUse(card.ParentCardUseFrameId)?.Action is { Type: CardActionType.Use } actual &&
                    actual.ActorSeat == _currentSeat && actual.ActorSeat != owner.Seat && CapturedActualTurnActorUseOrdinal(actual) == 2 &&
                    CardUseCategoryCatalog.Get(actual.EffectiveKind) is CardUseCategories.Basic or CardUseCategories.InstantTrick &&
                    GuanduDiscardable(owner.Seat, owner.Seat).Length > 0;
            case "wushuang:skill-3083":
                return _players.Any(p => p.IsAlive && GetHand(p).Count < Math.Min(5, p.MaxHp));
            default: return true;
        }
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 61010)]
    private bool AllowsWushuangOriginalActivation(ProgramActivationLegalityContext gate) =>
        gate.SkillId is not ("wushuang:skill-3282" or "wushuang:skill-3283") ||
        _players.Any(p => p.IsAlive && p.Seat != gate.OwnerSeat && GetHand(p).Count > 0);

    private bool WushuangYashiGrantQualified(CharacterState owner, SkillGrant grant) =>
        _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked) ||
        !ProgramEventHistory<WushuangYashiSuppressedEvent>().Any(e => e.Target == owner.Seat &&
            !ProgramEventHistory<WushuangYashiExpiredEvent>().Any(expired => expired.ProducerId == e.FrameId));

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 61009)]
    private void RecordWushuangTairanRecovery(IGameEvent payload)
    {
        if (payload is not RecoveryAppliedEvent { Amount: > 0 } applied ||
            _resolutionStack.LastOrDefault() is not RecoveryFrame recovery ||
            applied.SourceSeat != recovery.SourceSeat || applied.TargetSeat != recovery.TargetSeat ||
            applied.Amount > recovery.Amount) return;
        var parent = _resolutionStack.SingleOrDefault(f => f.Id == recovery.ParentFrameId);
        var producer = parent as ProgramSkillFrame;
        if (parent is RecoveryReplacementFrame replacement &&
            replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.Program)
            producer = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f =>
                f.Id == replacement.ParentFrameId && f.InstructionIndex == replacement.Attempt.Completion.InstructionIndex);
        if (producer is not { WushuangOriginalSkill: { Operation: SkillProgramEffectOp.ResolveWushuangTairan,
                Stage: "tairan-recovered" } } tairan ||
            applied.SourceSeat != tairan.OwnerSeat || applied.TargetSeat != tairan.OwnerSeat ||
            ProgramEventHistory<WushuangTairanHealedEvent>().Any(e => e.FrameId == tairan.Id)) return;
        AdvanceEventRulesAndQueueFact(new WushuangTairanHealedEvent(tairan.Id, tairan.OwnerSeat,
            tairan.SkillInstanceId, _turnNumber, applied.Amount));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 61010)]
    private void ExpireWushuangOriginalPolicies(IGameEvent payload)
    {
        if (payload is TurnStartedEvent start)
        {
            foreach (var issued in ProgramEventHistory<WushuangYashiSuppressedEvent>().Where(e => e.Target == start.ActorSeat &&
                e.ActualTurn < start.TurnNumber && !ProgramEventHistory<WushuangYashiExpiredEvent>().Any(x => x.ProducerId == e.FrameId)).ToArray())
            { AdvanceEventRulesAndQueueFact(new WushuangYashiExpiredEvent(issued.FrameId, issued.Target)); _currentTurnSkillSuppressionRevision++; }
            foreach (var player in _players)
            foreach (var grant in player.SkillGrants.Grants.Where(g => g.SourceId.StartsWith("acquired:wushuang-lingren:", StringComparison.Ordinal)).ToArray())
            {
                var turn = int.Parse(grant.SourceId["acquired:wushuang-lingren:".Length..].Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
                if (player.Seat == start.ActorSeat && turn < start.TurnNumber) player.SkillGrants.RemoveGrant(grant.GrantId);
            }
        }
        if (payload is TurnEndedEvent end)
            foreach (var issued in ProgramEventHistory<WushuangChoufaRewrittenEvent>().Where(e => e.Target == end.ActorSeat && e.ActualTurn <= end.TurnNumber &&
                !ProgramEventHistory<WushuangChoufaExpiredEvent>().Any(x => x.ProducerId == e.FrameId)).ToArray())
                AdvanceEventRulesAndQueueFact(new WushuangChoufaExpiredEvent(issued.FrameId, issued.Target));
    }

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 61010)]
    private int WushuangOriginalDamage(IDamageAttempt attack)
    {
        var bonus = 0;
        if (attack.EffectiveCardKind is { } kind && !attack.IsSourceLess && !attack.IsChainPropagation)
        {
            if (attack.CardUserSeat == attack.SourceSeat && IsSlashCard(kind) &&
                WushuangOriginalEnabled(_players[attack.SourceSeat], "wushuang:skill-3246")) bonus++;
            if (_currentSeat != attack.TargetSeat && CardCatalog.Get(kind).CategoryName == "锦囊牌" &&
                WushuangOriginalEnabled(_players[attack.TargetSeat], "wushuang:skill-3246")) bonus++;
        }
        if (!attack.IsSourceLess && !attack.IsChainPropagation)
            bonus += ProgramEventHistory<WushuangLingrenGuessedEvent>().Count(e => e.Owner == attack.SourceSeat && e.Target == attack.TargetSeat &&
                e.UseId == attack.ResolutionId && e.Correct >= 1 && e.ActualTurn == _turnNumber &&
                LifecycleCardUse(e.UseId)?.Action?.ActionId == e.ActionId);
        return bonus;
    }
    private ProgramCardIdentityMatch? WushuangChoufaIdentity(CharacterState owner, Card card)
    {
        var expired = ProgramEventHistory<WushuangChoufaExpiredEvent>().Select(e => e.ProducerId).ToHashSet();
        var issued = ProgramEventHistory<WushuangChoufaRewrittenEvent>().LastOrDefault(e => e.Target == owner.Seat &&
            e.CardIds.Contains(card.Id) && !expired.Contains(e.FrameId) &&
            !_cardMovements.Any(m => m.CardId == card.Id && m.From == CardLocation.Hand(owner.Seat) &&
                m.Sequence > e.MovementSequence));
        // Current hand identity applies only to the exact frozen card entities.
        if (issued is null) return null;
        var source = new CardConversionSource(issued.Source.SkillId, issued.Source.BindingId, issued.Source.OwnerSeat, issued.Source.SkillInstanceId);
        return new(_contentRegistry.GetSkill(issued.Source.SkillId).Program!, new SkillProgramCardIdentity(
            issued.Source.BindingId, [CardZoneKind.Hand], [], [], CardKind.Slash, new(SkillProgramConditionKind.Always, 0, [])), source);
    }

    private void NullifyWushuangCiweiUse(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects, CardUse: { } c } window ||
            LifecycleCardUse(c.ParentCardUseFrameId) is not { Action: { } action } use ||
            action.ActionId != c.CardActionId || action.ActorSeat != _currentSeat || action.ActorSeat == f.OwnerSeat ||
            CapturedActualTurnActorUseOrdinal(action) != 2 || CardUseCategoryCatalog.Get(action.EffectiveKind) is not (CardUseCategories.Basic or CardUseCategories.InstantTrick))
            throw new InvalidOperationException("Ciwei lost the actual other actor's second basic or ordinary trick use.");
        var paid = GetProgramCardSet(f, "ciwei-payment");
        if (paid.CardIds is not [var card]) throw new InvalidOperationException("Ciwei requires its one completed physical discard payment.");
        var movement = _cardMovements.LastOrDefault(m => m.CardId == card && m.From.OwnerSeat == f.OwnerSeat && m.To == CardLocation.DiscardPile);
        if (movement is null) throw new InvalidOperationException("Ciwei lost its physical discard invoice.");
        if (use.WushuangCiweiNullification is null)
        {
            var proof = new WushuangCiweiNullifiedEvent(f.Id, use.Id, window.ParentFrameId, f.OwnerSeat, action.ActionId,
                action.ActorSeat, action.EffectiveKind, f.SkillId, f.SkillInstanceId, f.GameplayHash, f.InstructionIndex, card, movement.Sequence);
            UpdateLifecycleCardUse(use.Id, value => value with { WushuangCiweiNullification = proof });
            AdvanceEventRulesAndQueueFact(proof);
        }
        FinishWushuangOriginal(f);
    }
    private bool IsWushuangCiweiIneffective(CardUseFrame use)
    {
        if (use.WushuangCiweiNullification is not { } proof) return false;
        if (proof.UseId != use.Id || use.Action is not { } action || action.ActionId != proof.ActionId || action.ActorSeat != proof.Actor ||
            action.EffectiveKind != proof.Kind || proof.SkillId != "wushuang:skill-3284" ||
            _contentRegistry.GetSkill(proof.SkillId).Program?.GameplayHash != proof.GameplayHash ||
            ProgramEventHistory<WushuangCiweiNullifiedEvent>().Count(e => e == proof) != 1 ||
            !_cardMovements.Any(m => m.Sequence == proof.PaidMovement && m.CardId == proof.PaidCard && m.From.OwnerSeat == proof.Owner && m.To == CardLocation.DiscardPile))
            throw new InvalidOperationException("Ciwei's ineffective use lost its exact issued action and paid entity.");
        return true;
    }
}
