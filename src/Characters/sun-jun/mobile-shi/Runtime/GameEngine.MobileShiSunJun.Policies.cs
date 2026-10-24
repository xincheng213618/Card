namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool ShiSjRealSuit(Suit? suit) => suit is Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond;
    private bool ShiSjSuitSeen(int actor, Suit suit) => ProgramEventHistory<ShiSunJunHandUseEvent>().Any(e => e.Turn == _turnNumber && e.Actor == actor && e.Suit == suit);
    private bool ShiSjHandSource(int actor, CardLocation from) => from.Zone == CardZoneKind.Hand && from.OwnerSeat is { } owner &&
        (owner == actor || HasShiLsHandView(actor, owner));
    private bool ShiSjOwnTurn(int actor) => _turnNumber > 0 && _turnProgression.OwnerSeat == actor && _currentSeat == actor &&
        !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == actor);

    // The outer alcohol prefilter asks for potential only; the material overload
    // always rechecks the actual authorized Hand source and conversion appearance.
    private bool HasShiSunJunFirstSuitAllowance(CharacterState actor, Card? material = null, CardConversionSource? conversion = null)
    {
        if (!actor.IsAlive || !ShiSjOwnTurn(actor.Seat) || !HasRuntimeSkill(actor, ShiSjXianshuai)) return false;
        if (material is null) return new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }.Any(s => !ShiSjSuitSeen(actor.Seat, s));
        if (material.Id <= 0 || !ShiSjHandSource(actor.Seat, _cardZones.GetLocation(material.Id))) return false;
        var suit = EffectiveSuit(actor, ApplyProgramUseAppearance(actor, material, conversion));
        return ShiSjRealSuit(suit) && !ShiSjSuitSeen(actor.Seat, suit);
    }
    private bool ShiSjMaterialAllowance(CharacterState actor, IReadOnlyList<int>? ids, Card? appearance = null, Suit? effectiveSuit = null)
    {
        if (ids is not { Count: > 0 } || ids.Distinct().Count() != ids.Count || ids.Any(id => id <= 0 || !ShiSjHandSource(actor.Seat, _cardZones.GetLocation(id)))) return false;
        var cards = ids.Select(GetAdvancedCard).ToArray();
        Suit[] suits = effectiveSuit is { } frozen ? [frozen] : appearance is not null && ids is [var id] && appearance.Id == id ? [EffectiveSuit(actor, appearance)] : cards.Select(c => EffectiveSuit(actor, c)).Distinct().ToArray();
        return suits is [var suit] && ShiSjRealSuit(suit) && ShiSjOwnTurn(actor.Seat) && HasRuntimeSkill(actor, ShiSjXianshuai) && !ShiSjSuitSeen(actor.Seat, suit);
    }
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 49200)]
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 49200)]
    private bool ShiSjFirstSuitSlashQuota(ProgramSlashQuotaQuery q) => ShiSjMaterialAllowance(q.Actor,
        q.PhysicalCardIds ?? (q.PhysicalCard is { Id: > 0 } card ? [card.Id] : []), q.PhysicalCard, q.EffectiveSuit);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 49200)]
    private void CaptureShiSjActualHandUse(CardUseDeclaredEvent fact)
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMobileShiXiongtu) ||
            LifecycleCardUse(fact.ResolutionId) is not { Action: { Type: CardActionType.Use, EffectiveSuit: { } suit } a } || !ShiSjRealSuit(suit) ||
            !ShiSjOwnTurn(a.ActorSeat) || a.PhysicalCards.Count == 0 || a.PhysicalCards.Any(c => !ShiSjHandSource(a.ActorSeat, c.From)) ||
            ProgramEventHistory<ShiSunJunHandUseEvent>().Any(e => e.UseId == fact.ResolutionId)) return;
        RecordShiSjHandAction(fact.ResolutionId, a, suit);
    }
    private void RecordShiSjHandAction(long useId, CardActionContext a, Suit suit)
    {
        if (!ShiSjOwnTurn(a.ActorSeat) || !ShiSjRealSuit(suit) || a.PhysicalCards.Count == 0 || a.PhysicalCards.Any(c => !ShiSjHandSource(a.ActorSeat, c.From)) ||
            ProgramEventHistory<ShiSunJunHandUseEvent>().Any(e => e.ActionId == a.ActionId)) return;
        var first = !ShiSjSuitSeen(a.ActorSeat, suit);
        AdvanceEventRulesAndQueueFact(new ShiSunJunHandUseEvent(useId, a.ActionId, a.ActorSeat, a.ProviderSeat, _turnNumber, suit, first,
            first && HasRuntimeSkill(_players[a.ActorSeat], ShiSjXianshuai)));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 49201)]
    private void CaptureShiSjResponseHandUse(CardActionAcceptedEvent fact)
    {
        var a = fact.Action;
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMobileShiXiongtu) || a.Type != CardActionType.Response || a.EffectiveSuit is not { } suit) return;
        if (IsCompletedResponseUse(a, ProgramCompletedResponseKind.Nullification) && ActiveNullificationWindow is { } counterspell)
        { RecordShiSjHandAction(counterspell.Id, a, suit); return; }
        // A Dodge against an actual Slash is a Use; an Arrow Barrage Dodge and
        // a Duel/Barbarian Slash retain their ordinary played-response meaning.
        if (IsCompletedResponseUse(a, ProgramCompletedResponseKind.Dodge) &&
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Action?.ActionId == a.ParentActionId && IsSlashCard(f.CardKind) && f.CardAttack?.TargetSeat == a.ActorSeat) is { } parent)
            RecordShiSjHandAction(parent.Id, a, suit);
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 49200)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity, 49200)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 49200)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit, 49200)]
    private bool ShiSjIssuedFirstSuitUse(long id, int actor) => LifecycleCardUse(id) is { Action: { Type: CardActionType.Use } a } && a.ActorSeat == actor &&
        ProgramEventHistory<ShiSunJunHandUseEvent>().Any(e => e.UseId == id && e.ActionId == a.ActionId && e.Actor == actor && e.Provider == a.ProviderSeat &&
            e.Turn == _turnNumber && e.Suit == a.EffectiveSuit && e.First && e.Exempt) && a.PhysicalCards.Count > 0 &&
        a.PhysicalCards.All(c => c.From.Zone == CardZoneKind.Hand);
}

internal static class ShiSunJunComposition
{
    [ProgramCatalogValidation(49200)] internal static void Validate(string path, SkillProgram p)
    {
        var activation = SkillProgramEffectOp.ResolveMobileShiXiongtu;
        var benefit = SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit;
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == activation || e.Op == benefit)) return;
        if (p.Id != "mobile:shi-xiongtu" || p.Activations is not [{ Id: "show-hand", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
                TargetKind: SkillProgramTargetKind.OtherLivingWithHand, UsesPerPhase: 2, UsesPerTurn: null, UsesPerGame: null } a] ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiXiongtu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            p.Triggers is not [{ Id: "other-damage", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.DamageSource,
                DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Optional: false, IsSkillInvocation: false } t] ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            p.BooleanStates is not [{ Id: "armed", InitialValue: false, Visibility: SkillProgramStateVisibility.Public, ResetScope: SkillProgramStateResetScope.Turn }] ||
            p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": Xiongtu requires its actual-Play two-cap native gate, one turn listener and shared turn-reset state.");
    }
}
