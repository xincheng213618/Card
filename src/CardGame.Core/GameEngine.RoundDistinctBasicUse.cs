namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool RoundDistinctBasicNameUsed(int ownerSeat, string skillId,
        ProgramRoundDistinctBasicUsePolicy policy, CardKind outputKind) =>
        CompleteProgramEventHistory().OfType<RoundDistinctBasicUseAcceptedEvent>().Any(e =>
            e.Receipt.ActorSeat == ownerSeat && e.Receipt.Source.SkillId == skillId &&
            e.Receipt.StateId == policy.StateId && e.Receipt.LedgerId == policy.LedgerId &&
            e.Receipt.RoundNumber == _roundNumber && e.Receipt.CanonicalName == ProgramBasicCardName(outputKind));

    private bool CanUseRoundDistinctBasicConversion(CharacterState owner, IndexedSkillProgramInstance instance,
        SkillProgramViewAs rule, bool forResponse, bool dyingUse = false)
    {
        if (rule.RoundDistinctBasicUse is not { } policy) return true;
        if (_winner != Winner.None || !owner.IsAlive || _roundNumber < 1 ||
            RoundDistinctBasicNameUsed(owner.Seat, instance.SkillId, policy, rule.OutputKind) ||
            IsCardUseForbidden(owner.Seat, rule.OutputKind, CardActionType.Use)) return false;
        if (dyingUse)
            return rule.OutputKind is CardKind.Peach or CardKind.Alcohol && ActiveDying is { } dying &&
                dying.ResponderSeat == owner.Seat && _players[dying.VictimSeat].IsAlive &&
                _players[dying.VictimSeat].Hp <= 0 &&
                (rule.OutputKind == CardKind.Peach ? CanUsePeachToRescue(owner.Seat, dying.VictimSeat) :
                    dying.VictimSeat == owner.Seat && !HasSelfCardTargetProhibition(owner.Seat));
        return forResponse ? rule.OutputKind == CardKind.Dodge &&
            ActiveFactionDefense is null && IsProgramResponseCardUse(owner, CardKind.Dodge) : rule.ForPlay;
    }

    private bool IsRoundDistinctBasicMaterial(CharacterState owner, Card card)
    {
        var location = _cardZones.GetLocation(card.Id);
        return location == CardLocation.Hand(owner.Seat) ||
            location == CardLocation.WoodenOxGrain(owner.Seat) && UsesFormalWoodenOx &&
            GetEquipment(owner).Any(e => e.Kind == CardKind.WoodenOx);
    }

    private bool MatchesRoundDistinctBasicMaterials(CharacterState owner, SkillProgramViewAs rule,
        IReadOnlyList<Card> cards)
    {
        if (rule.RoundDistinctBasicUse is null) return true;
        if (cards.Count != 2 || cards.Select(c => c.Id).Distinct().Count() != 2 ||
            cards.Any(c => !IsRoundDistinctBasicMaterial(owner, c))) return false;
        var color = SuitColor(EffectiveSuit(owner, cards[0]));
        return color is not null && SuitColor(EffectiveSuit(owner, cards[1])) == color;
    }

    private void ObserveRoundDistinctBasicUse(IGameEvent payload)
    {
        if (!_contentRegistry.ProgramDependencies.UsesRoundDistinctBasicUse) return;
        if (payload is CardUseDeclaredEvent declared)
        {
            if (LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use ||
                action.ConversionChain.All(s => ViewAsRule(s)?.RoundDistinctBasicUse is null)) return;
            if (declared.SourceSeat != use.SourceSeat || declared.CardId != use.CardId || declared.CardKind != use.CardKind ||
                action.ActorSeat != use.SourceSeat || action.ProviderSeat != use.SourceSeat || action.RequesterSeat is not null ||
                action.EffectiveKind != use.CardKind || !(use.PhysicalCardIds ?? []).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)))
                throw new InvalidOperationException("A pair conversion lost its native declared-use identity.");
            IssueRoundDistinctBasicUse(use, action, false);
        }
        else if (payload is CardActionAcceptedEvent { Action: { Type: CardActionType.Response } action } &&
                 action.ConversionChain.Any(s => ViewAsRule(s)?.RoundDistinctBasicUse is not null))
        {
            if (action.EffectiveKind != CardKind.Dodge || action.ActorSeat != action.ProviderSeat ||
                action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null ||
                ActiveFactionDefense is not null || ActiveCardAttack is not { } attack ||
                !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) || attack.TargetSeat != action.ActorSeat ||
                LifecycleCardUse(attack.ResolutionId) is not { } use || action.ParentActionId != use.Action?.ActionId ||
                action.OpponentSeat != attack.SourceSeat || !IsProgramResponseCardUse(_players[action.ActorSeat], CardKind.Dodge))
                throw new InvalidOperationException("A pair conversion cannot turn a provision into a Dodge use.");
            IssueRoundDistinctBasicUse(use, action, true);
        }
    }

    private void IssueRoundDistinctBasicUse(CardUseFrame use, CardActionContext action, bool response)
    {
        if (action.ConversionChain is not [var source] ||
            ViewAsRule(source) is not { RoundDistinctBasicUse: { } policy } rule ||
            !IsValidPlayerSeat(action.ActorSeat) || source.OwnerSeat != action.ActorSeat ||
            string.IsNullOrWhiteSpace(source.SkillInstanceId) || action.ActionId <= 0 || _roundNumber < 1 ||
            action.EffectiveKind != rule.OutputKind || action.PhysicalCards.Count != 2 ||
            action.PhysicalCards.Select(c => c.CardId).Distinct().Count() != 2 ||
            action.PhysicalCards.Any(c => c.From.OwnerSeat != action.ActorSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.WoodenOxGrain)) ||
            action.PhysicalCards[0].EffectiveIsRed is not { } color || action.PhysicalCards[1].EffectiveIsRed != color ||
            _contentRegistry.GetSkill(source.SkillId).Program is not { } program)
            throw new InvalidOperationException("A pair conversion lost its exact two same-color hand-like costs and source.");
        // The native producer revalidates the published selection before moving
        // either cost. Movement observers may remove its qualification before
        // CardActionAccepted, so this issued identity is not a second shard query.
        var existing = CompleteProgramEventHistory().OfType<RoundDistinctBasicUseAcceptedEvent>()
            .Where(e => e.Receipt.CardActionId == action.ActionId).ToArray();
        if (existing.Length != 0)
        {
            if (existing.Length == 1 && use.RoundDistinctBasicUses?.Contains(existing[0].Receipt) == true) return;
            throw new InvalidOperationException("An accepted pair conversion cannot be issued twice.");
        }
        if (RoundDistinctBasicNameUsed(action.ActorSeat, source.SkillId, policy, action.EffectiveKind))
            throw new InvalidOperationException("This method already used the canonical basic name in this actual round.");
        var receipt = new RoundDistinctBasicUseReceipt(source, program.GameplayHash, policy.StateId, policy.LedgerId,
            _roundNumber, _turnNumber, _turnProgression.OwnerSeat, action.ActionId, use.Id, action.ActorSeat,
            action.EffectiveKind, ProgramBasicCardName(action.EffectiveKind), color, response,
            action.ParentActionId, action.OpponentSeat,
            !response && action.EffectiveKind is CardKind.Peach or CardKind.Alcohol && ActiveDying is { } dying &&
                dying.ResponderSeat == action.ActorSeat && use.TargetSeats.SequenceEqual([dying.VictimSeat]) ? dying.FrameId : null);
        ReplaceRuntimeFrame(use.Id, use with { RoundDistinctBasicUsesIssued = true,
            RoundDistinctBasicUses = (use.RoundDistinctBasicUses ?? []).Append(receipt).ToArray() });
        AdvanceEventRulesAndQueueFact(new RoundDistinctBasicUseAcceptedEvent(receipt));
    }

    private void AssertRoundDistinctBasicUses(CardUseFrame use)
    {
        var containsPolicy = use.Action?.ConversionChain.Any(s => ViewAsRule(s)?.RoundDistinctBasicUse is not null) == true;
        if (use.RoundDistinctBasicUses is null && !containsPolicy && !use.RoundDistinctBasicUsesIssued) return;
        var receipts = use.RoundDistinctBasicUses ?? throw new InvalidOperationException("A declared pair use lost its owning receipt.");
        if (!use.RoundDistinctBasicUsesIssued || receipts.Count == 0 || receipts.Select(r => r.CardActionId).Distinct().Count() != receipts.Count ||
            containsPolicy && !receipts.Any(r => !r.IsResponseUse && r.CardActionId == use.Action!.ActionId))
            throw new InvalidOperationException("A pair use lost its unique accepted action receipt.");
        // The accepted fact is queued by the native action observer before that
        // action's own fact. Its preceding real turn/round facts, rather than the
        // current counters or another copy of the receipt, prove its lifetime.
        var timeline = CompleteProgramEventHistory().Where(e => e is RoundDistinctBasicUseAcceptedEvent or
            TurnStartedEvent or RoundStartedEvent or PlayerDyingEvent).ToArray();
        var accepted = new List<(RoundDistinctBasicUseAcceptedEvent Fact, TurnStartedEvent? Turn, RoundStartedEvent? Round, int Index)>();
        TurnStartedEvent? precedingTurn = null;
        RoundStartedEvent? precedingRound = null;
        for (var index = 0; index < timeline.Length; index++)
        {
            switch (timeline[index])
            {
                case TurnStartedEvent turn: precedingTurn = turn; break;
                case RoundStartedEvent round: precedingRound = round; break;
                case RoundDistinctBasicUseAcceptedEvent fact: accepted.Add((fact, precedingTurn, precedingRound, index)); break;
            }
        }
        var facts = accepted.Select(e => e.Fact).ToArray();
        foreach (var r in receipts)
        {
            var matching = accepted.Where(e => e.Fact.Receipt == r).ToArray();
            if (matching is not [var issued] || issued.Turn is not { } nativeTurn || issued.Round is not { } nativeRound ||
                r.ActualTurnNumber != nativeTurn.TurnNumber || r.ActualTurnOwnerSeat != nativeTurn.ActorSeat ||
                r.RoundNumber != nativeRound.RoundNumber || facts.Count(e => e.Receipt.CardActionId == r.CardActionId) != 1)
                throw new InvalidOperationException("A pair conversion changed the real turn or round preceding its accepted fact.");
            if (r.OwnerFrameId != use.Id || r.CardActionId <= 0 || r.RoundNumber < 1 || r.RoundNumber > _roundNumber ||
                r.Source.OwnerSeat != r.ActorSeat || !IsValidPlayerSeat(r.ActorSeat) || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
                r.CanonicalName != ProgramBasicCardName(r.EffectiveKind) ||
                _contentRegistry.GetSkill(r.Source.SkillId).Program is not { } program || program.GameplayHash != r.GameplayHash ||
                program.ViewAs.SingleOrDefault(v => v.Id == r.Source.BindingId) is not { RoundDistinctBasicUse: { } policy } rule ||
                policy.StateId != r.StateId || policy.LedgerId != r.LedgerId || rule.OutputKind != r.EffectiveKind ||
                facts.Count(e => e.Receipt == r) != 1 || facts.Count(e =>
                    e.Receipt.RoundNumber == r.RoundNumber && e.Receipt.ActorSeat == r.ActorSeat &&
                    e.Receipt.Source.SkillId == r.Source.SkillId && e.Receipt.StateId == r.StateId &&
                    e.Receipt.LedgerId == r.LedgerId && e.Receipt.CanonicalName == r.CanonicalName) != 1)
                throw new InvalidOperationException("A pair conversion lost its stable accepted round ledger or content identity.");
            CardActionContext? action;
            if (r.IsResponseUse)
            {
                action = CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>()
                    .Where(e => e.Action.ActionId == r.CardActionId).Select(e => e.Action).SingleOrDefault();
                if (action is null || action.Type != CardActionType.Response || r.EffectiveKind != CardKind.Dodge ||
                    action.ActorSeat != r.ActorSeat ||
                    action.ResponderSeat != r.ActorSeat || action.ParentActionId != use.Action?.ActionId ||
                    action.OpponentSeat != r.OpponentSeat || action.ParentActionId != r.ParentActionId ||
                    !IsSlashCard(use.CardKind))
                    throw new InvalidOperationException("A pair Dodge receipt lost its original native Slash parent.");
            }
            else
            {
                action = use.CurrentSlashFirePolicy is { } fire ? fire.OriginalAction : use.Action;
                if (use.CurrentSlashFirePolicy is not null) AssertCurrentSlashFirePolicy(use);
                var originalUse = use.CurrentSlashFirePolicy is null ? use : use with { CardKind = r.EffectiveKind, Action = action };
                if (action is null || action.Type != CardActionType.Use || action.ActionId != r.CardActionId ||
                    !(use.PhysicalCardIds ?? []).SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) ||
                    CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id &&
                        e.SourceSeat == r.ActorSeat && e.CardId == use.CardId && e.CardKind == r.EffectiveKind) != 1 ||
                    (action.ActorSeat != r.ActorSeat || use.SourceSeat != r.ActorSeat) &&
                        !MatchesDeclaredActualDamageUse(originalUse, action.ActorSeat, r.ActorSeat))
                    throw new InvalidOperationException("A pair use lost its exact original native declaration and materials.");
            }
            if (action.ActionId != r.CardActionId || action.ProviderSeat != r.ActorSeat || action.RequesterSeat is not null ||
                action.EffectiveKind != r.EffectiveKind || action.ConversionChain is not [var source] || source != r.Source ||
                action.PhysicalCards.Count != 2 || action.PhysicalCards.Select(c => c.CardId).Distinct().Count() != 2 ||
                action.PhysicalCards.Any(c => c.From.OwnerSeat != r.ActorSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.WoodenOxGrain) ||
                    c.EffectiveIsRed != r.FrozenIsRed))
                throw new InvalidOperationException("A pair conversion changed its frozen source, color or native costs.");
            AssertRoundDistinctBasicDyingParent(use, r, timeline, issued.Index);
        }
    }

    private void AssertRoundDistinctBasicDyingParent(CardUseFrame use, RoundDistinctBasicUseReceipt receipt,
        IReadOnlyList<IGameEvent> timeline, int acceptedIndex)
    {
        if (receipt.IsResponseUse || use.DyingResponse is null)
        {
            if (receipt.DyingFrameId is not null)
                throw new InvalidOperationException("An ordinary pair use cannot claim a dying response parent.");
            return;
        }
        var response = use.DyingResponse;
        var useIndex = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (receipt.DyingFrameId is not { } dyingId || response.ResolutionId != dyingId || useIndex < 1 ||
            _resolutionStack[useIndex - 1] is not DyingFrame dying || dying.Id != dyingId ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            dying.ResponderSeat != receipt.ActorSeat || response.ResponderSeat != receipt.ActorSeat ||
            use.SourceSeat != receipt.ActorSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            (receipt.EffectiveKind == CardKind.Peach
                ? !response.UsedPeach || response.PeachCardId != use.CardId || response.UsedAlcohol || response.AlcoholCardId is not null
                : receipt.EffectiveKind != CardKind.Alcohol || !response.UsedAlcohol || response.AlcoholCardId != use.CardId ||
                    response.UsedPeach || response.PeachCardId is not null || dying.VictimSeat != receipt.ActorSeat))
            throw new InvalidOperationException("A pair rescue lost its exact native dying parent, responder or use.");
        var entries = timeline.Select((fact, index) => (Fact: fact, Index: index))
            .Where(e => e.Fact is PlayerDyingEvent entered && entered.ResolutionId == dyingId).ToArray();
        if (entries is not [var entry] || entry.Index >= acceptedIndex ||
            entry.Fact is not PlayerDyingEvent original || original.VictimSeat != dying.VictimSeat || original.KillerSeat != dying.KillerSeat)
            throw new InvalidOperationException("A pair rescue lost the real dying entry preceding its accepted fact.");
    }
}
