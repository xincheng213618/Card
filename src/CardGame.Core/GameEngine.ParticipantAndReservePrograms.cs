namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool _gameStartingProgramsResolved;
    private bool BeginGameStartingPrograms()
    {
        _gameStartingProgramsResolved = true;
        var facts = _players.ToDictionary(player => player.Seat, CaptureProgramTriggerFacts);
        var candidates = _players.SelectMany(player => CollectEligibleProgramTriggerCandidates(player,
            SkillProgramTriggerWindow.GameStarting, facts[player.Seat])).OrderBy(candidate => candidate.OwnerSeat)
            .ThenByDescending(candidate => candidate.Priority).ToArray();
        if (candidates.Length == 0) return false;
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, _currentSeat,
            SkillProgramTriggerWindow.GameStarting, candidates, ProgramLifecycleContinuation.CompleteGameStarting,
            facts[_currentSeat]) { ParticipantFacts = facts,
                FrozenFactionPopulation = _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantFactionPopulationMarker)
                    ? new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(_players.GroupBy(player => GetEffectiveFactionId(player) ?? "")
                        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)) : null });
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
        return true;
    }

    // Dying entry and exit are mandatory immediate boundaries. Definitions are
    // restricted to automatic draws so the rescue cursor never exposes stale hands.
    private void ResolveAutomaticDyingTransitionPrograms(IGameEvent payload)
    {
        var transition = payload switch
        {
            PlayerDyingEvent entered => (entered.VictimSeat, SkillProgramTriggerWindow.DyingEntered),
            DyingResolvedEvent { Survived: true } exited => (exited.VictimSeat, SkillProgramTriggerWindow.DyingExited),
            _ => (-1, SkillProgramTriggerWindow.DyingEntered)
        };
        if (transition.Item1 < 0) return;
        var owner = _players[transition.Item1];
        foreach (var binding in EnabledUniqueProgramTriggers(owner, transition.Item2).Where(binding =>
            binding.Trigger.Condition.Evaluate(CaptureProgramTriggerFacts(owner), binding.SkillId, binding.SkillInstanceId)))
            foreach (var effect in binding.Trigger.Effects)
                if (effect.Op == SkillProgramEffectOp.Draw)
                    DrawCards(owner, effect.Amount, log: true, new CardMoveReason("program.dying-transition.draw"));
    }

    private sealed partial class ProgramSkillHost : IParticipantReserveProgramHost
    {
        public SkillProgramStepOutcome ExecuteParticipantReserve(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.ExecuteParticipantReserve(effect, frame);
    }

    private SkillProgramStepOutcome ExecuteParticipantReserve(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var owner = _players[frame.OwnerSeat];
        if (effect.Op == SkillProgramEffectOp.LoseHpUnclamped)
        {
            var before = owner.Hp;
            owner.Hp -= effect.Amount;
            RecordHpChange(frame.Id, null, owner.Seat, before, owner.Hp, HpChangeKind.Loss);
            AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(frame.Id, frame.SkillId, owner.Seat, effect.Amount, owner.Hp));
            if (owner.Hp > 0) return SkillProgramStepOutcome.Continue;
            BeginProgramSkillDying(frame.Id, owner);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (effect.Op == SkillProgramEffectOp.RecoverAllLiving)
        {
            var host = new ProgramSkillHost(this);
            foreach (var player in _players.Where(player => player.IsAlive)) host.Recover(frame.Id, owner.Seat, player.Seat, effect.Amount, null, null);
            return SkillProgramStepOutcome.Continue;
        }
        if (effect.Op == SkillProgramEffectOp.SpendMarkerOrLoseHp)
        {
            var choices = new List<PromptChoice>();
            if (owner.Markers.GetValueOrDefault(effect.Marker!.Value) >= effect.Amount)
                choices.Add(new(new ChoiceId("marker-payment.spend"), "移去" + PlayerMarkerCatalog.GetDisplayName(effect.Marker!.Value) + "标记", [], [], new Dictionary<string, string>
                    { ["program-action"] = "marker-payment", ["pay"] = "marker" }));
            choices.Add(new(new ChoiceId("marker-payment.hp"), "失去1点体力", [], [], new Dictionary<string, string>
                { ["program-action"] = "marker-payment", ["pay"] = "hp" }));
            var skill = _contentRegistry.GetSkill(frame.SkillId);
            _pendingDecision = new(DecisionKind.ProgramTrigger, owner.Seat, skill.Name + "：选择支付方式", [], [], owner.Seat)
            {
                PromptId = CreatePromptId(), IsPrivate = true, Choices = choices.ToArray(),
                SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
            };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (effect.Op == SkillProgramEffectOp.InitializePrivatePile)
        {
            var location = new CardLocation(CardZoneKind.PrivateReserve, owner.Seat);
            for (var index = 0; index < effect.Amount && EnsureDrawPile(); index++)
                MoveCard(_cardZones.CardsAt(CardLocation.DrawPile)[^1], CardLocation.DrawPile, location,
                    new CardMoveReason("program.private-reserve.initialize"));
            return SkillProgramStepOutcome.Continue;
        }
        if (effect.Op == SkillProgramEffectOp.ExchangePrivatePile)
        {
            var maximum = Math.Min(GetHand(owner).Count, _cardZones.CardsAt(new CardLocation(CardZoneKind.PrivateReserve, owner.Seat)).Count);
            if (maximum == 0) return SkillProgramStepOutcome.Continue;
            ReplaceRuntimeTop(frame with { PrivateReserveDraft = new(owner.Seat, "exchange-hand", maximum, [], []) });
            PublishPrivateReserveChoice(GetActiveProgramFrame(frame.Id));
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (effect.Op == SkillProgramEffectOp.GrantAttributedNatureEffect)
        {
            foreach (var seat in frame.SelectedTargetSeats)
                SetAttributedNatureMarker(owner.Seat, seat, effect.Marker!.Value, 1, frame.Id);
            return SkillProgramStepOutcome.Continue;
        }
        // Chaotic Ambition walks the field from the owner's next turn neighbour.
        var seats = effect.Op is SkillProgramEffectOp.RequestSlashByNearest or SkillProgramEffectOp.RequestLegalSlashByNearest
            ? Enumerable.Range(1, _playerCount - 1)
                .Select(offset => _players[(owner.Seat + offset) % _playerCount].Seat).ToArray()
            : effect.TargetKind is { } kind ? _players.Where(player =>
            (kind != SkillProgramTargetKind.OtherLiving || player.Seat != owner.Seat)).Select(player => player.Seat).ToArray()
            : frame.SelectedTargetSeats.ToArray();
        var key = "participant-cursor-" + (frame.InstructionIndex - 1);
        var cursor = frame.NumberBindings.SingleOrDefault(item => item.Name == key)?.Value ?? 0;
        if (cursor >= seats.Length) return SkillProgramStepOutcome.Continue;
        var seatNow = seats[cursor];
        var amount = cursor == 0 ? effect.Amount : effect.MinimumValue;
        ReplaceRuntimeTop(frame with { NumberBindings = frame.NumberBindings.Where(item => item.Name != key)
            .Append(new ProgramSkillNumberBinding(key, cursor + 1)).ToArray(), ReexecuteParticipantInstruction = true });
        frame = GetActiveProgramFrame(frame.Id);
        if (!_players[seatNow].IsAlive) return SkillProgramStepOutcome.Continue;
        if (effect.Op == SkillProgramEffectOp.DamageParticipants)
            return BeginProgramSkillDamage(frame, seatNow, amount, nature: effect.DamageNature);
        if (effect.Op == SkillProgramEffectOp.LoseHpParticipants)
            return new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, seatNow, amount);
        if (effect.Op == SkillProgramEffectOp.RequestLegalSlashByNearest)
            return RequestLegalSlashByNearestActor(GetActiveProgramFrame(frame.Id), seatNow, amount);
        if (effect.Op == SkillProgramEffectOp.RequestSlashByNearest)
            return RequestProgramSlashByNearest(frame, seatNow, amount);
        if (effect.Zones.SequenceEqual([CardZoneKind.Equipment]))
        {
            MoveCards(GetEquipment(_players[seatNow]).Where(card => !IsForeignEquipmentDiscardPrevented(
                frame.OwnerSeat, card, CardLocation.Equipment(seatNow), OwnedCardMoveIntent.Discard)).ToArray(), CardLocation.Equipment(seatNow), CardLocation.DiscardPile,
                new CardMoveReason("program.participants.discard-equipment"));
            return SkillProgramStepOutcome.Continue;
        }
        var required = Math.Min(amount, GetSelfDiscardableOwnedHand(_players[seatNow]).Count);
        if (required == 0) return SkillProgramStepOutcome.Continue;
        // Restore the committed cursor for private input; the draft resumes this instruction.
        ReplaceRuntimeTop(frame with { ReexecuteParticipantInstruction = false,
            PrivateReserveDraft = new(seatNow, "discard-hand", required, [], []) });
        PublishPrivateReserveChoice(GetActiveProgramFrame(frame.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishPrivateReserveChoice(ProgramSkillFrame frame)
    {
        var draft = frame.PrivateReserveDraft!;
        var starPhase = draft.Mode == "exchange-stars";
        var location = starPhase ? new CardLocation(CardZoneKind.PrivateReserve, draft.ChooserSeat) : CardLocation.Hand(draft.ChooserSeat);
        var selected = starPhase ? draft.SelectedReserveIds : draft.SelectedHandIds;
        var choices = _cardZones.CardsAt(location).Where(card => !selected.Contains(card.Id) &&
            (draft.Mode != "discard-hand" || !IsSelfHandCategoryDiscardForbidden(draft.ChooserSeat, card, location, OwnedCardMoveIntent.Discard))).Select(card =>
            new PromptChoice(new ChoiceId("private-reserve." + card.Id), card.DisplayName, [card.Id], [],
                new Dictionary<string, string> { ["program-action"] = "private-reserve-choice", ["card-id"] = card.Id.ToString() })).ToList();
        if (draft.Mode == "exchange-hand" && selected.Count > 0)
            choices.Add(new(new ChoiceId("private-reserve.finish"), "完成手牌选择", [], [],
                new Dictionary<string, string> { ["program-action"] = "private-reserve-choice", ["finish"] = "true" }));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.ChooserSeat,
            $"【{skill.Name}】选择{(starPhase ? "等量星" : "手牌")}", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.ChooserSeat,
          SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description), Choices = choices.ToArray() };
        _status = _players[draft.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolvePrivateReserveChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing reserve frame.");
        var draft = frame.PrivateReserveDraft ?? throw new InvalidOperationException("Missing reserve draft.");
        var finish = choice.Parameters.GetValueOrDefault("finish") == "true";
        var star = draft.Mode == "exchange-stars";
        var location = star ? new CardLocation(CardZoneKind.PrivateReserve, draft.ChooserSeat) : CardLocation.Hand(draft.ChooserSeat);
        var selected = star ? draft.SelectedReserveIds : draft.SelectedHandIds;
        if (finish && (draft.Mode != "exchange-hand" || selected.Count == 0) || !finish &&
            (choice.Cards.Count != 1 || selected.Contains(choice.Cards[0]) || _cardZones.GetLocation(choice.Cards[0]) != location))
            throw new InvalidOperationException("Invalid private reserve choice.");
        if (draft.Mode == "discard-hand" && choice.Cards.Any(id => IsSelfHandCategoryDiscardForbidden(draft.ChooserSeat,
            _cardZones.CardsAt(location).Single(card => card.Id == id), location, OwnedCardMoveIntent.Discard)))
            throw new InvalidOperationException("A protected hand card cannot be selected for self discard.");
        ClearPendingDecision();
        var ids = finish ? selected : selected.Append(choice.Cards[0]).ToArray();
        draft = star ? draft with { SelectedReserveIds = ids } : draft with { SelectedHandIds = ids };
        if (draft.Mode == "exchange-hand" && (finish || ids.Count == draft.RequiredCount))
            draft = draft with { Mode = "exchange-stars", RequiredCount = ids.Count };
        else if (ids.Count == draft.RequiredCount)
        {
            var reason = new CardMoveReason("program.private-reserve.exchange");
            if (draft.Mode == "discard-hand")
            {
                MoveCards(GetHand(_players[draft.ChooserSeat]).Where(card => ids.Contains(card.Id)).ToArray(), location,
                    CardLocation.DiscardPile, new CardMoveReason("program.participants.discard-hand"));
                frame = frame with { ReexecuteParticipantInstruction = true };
            }
            else
            {
                var reserve = new CardLocation(CardZoneKind.PrivateReserve, draft.ChooserSeat);
                var handCards = GetHand(_players[draft.ChooserSeat]).Where(card => draft.SelectedHandIds.Contains(card.Id)).ToArray();
                var starCards = _cardZones.CardsAt(reserve).Where(card => draft.SelectedReserveIds.Contains(card.Id)).ToArray();
                if (handCards.Length != ids.Count || starCards.Length != ids.Count) throw new InvalidOperationException("Exchange lost physical cards.");
                MoveCards(handCards, CardLocation.Hand(draft.ChooserSeat), CardLocation.Processing, reason);
                MoveCards(starCards, reserve, CardLocation.Hand(draft.ChooserSeat), reason);
                MoveCards(handCards, CardLocation.Processing, reserve, reason);
            }
            ReplaceRuntimeTop(frame with { PrivateReserveDraft = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        ReplaceRuntimeTop(frame with { PrivateReserveDraft = draft });
        PublishPrivateReserveChoice(GetActiveProgramFrame(frame.Id));
    }

    private void ResolveMarkerOrHpPayment(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing marker payment frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.SpendMarkerOrLoseHp) throw new InvalidOperationException("Missing marker payment instruction.");
        ClearPendingDecision();
        if (choice.Parameters.GetValueOrDefault("pay") == "marker")
        {
            PayProgramMarkerCost(_players[frame.OwnerSeat], new SkillProgramMarkerCost(effect.Marker!.Value, effect.Amount), frame.SkillId, frame.ActivationId);
            AdvanceRuntimeProgram(frame.Id);
        }
        else if (new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, frame.OwnerSeat, effect.Amount) == SkillProgramStepOutcome.Continue)
            AdvanceRuntimeProgram(frame.Id);
    }

    private void SetAttributedNatureMarker(int source, int target, PlayerMarkerKind marker, int amount, long id)
    {
        var player = _players[target];
        var previous = player.MarkerSourceCounts.GetValueOrDefault((marker, source));
        player.MarkerSourceCounts[(marker, source)] = amount;
        var count = player.Markers.GetValueOrDefault(marker) + amount - previous;
        if (amount == 0) player.MarkerSourceCounts.Remove((marker, source));
        if (count == 0) player.Markers.Remove(marker); else player.Markers[marker] = count;
        if (amount != previous) AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(id, target, marker, amount - previous, count, source, "program.nature-effect"));
    }
    private void AssertParticipantReserveDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.ReexecuteParticipantInstruction && paused.Op is not (SkillProgramEffectOp.DamageParticipants or
                SkillProgramEffectOp.LoseHpParticipants or SkillProgramEffectOp.DiscardParticipantCards or SkillProgramEffectOp.DiscardSelectedParticipantCards or
                SkillProgramEffectOp.RequestSlashByNearest or SkillProgramEffectOp.RequestLegalSlashByNearest or SkillProgramEffectOp.ChooseCategoryAlternativeDiscard or
                SkillProgramEffectOp.EscalatingDiscardOrDamage or SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard or
                SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected or SkillProgramEffectOp.ChooseHandCountIntervention or
                SkillProgramEffectOp.RevealHandColorDiscardAndTake or SkillProgramEffectOp.DrawThenPutOwnedCardOnTopParticipants or
                SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge or SkillProgramEffectOp.DistributePublicPileIfAllSuits or
                SkillProgramEffectOp.ObtainOneFromEachSelectedTarget or SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget))
            throw new InvalidOperationException("A participant cursor must resume its own committed instruction.");
        if (frame.PrivateReserveDraft is not { } draft) return;
        if (!IsValidPlayerSeat(draft.ChooserSeat) || draft.RequiredCount < 1 || draft.RequiredCount > 64 ||
            draft.Mode is not ("exchange-hand" or "exchange-stars" or "discard-hand") ||
            (draft.Mode == "discard-hand" ? paused.Op != SkillProgramEffectOp.DiscardParticipantCards : paused.Op != SkillProgramEffectOp.ExchangePrivatePile) ||
            draft.SelectedHandIds.Count > draft.RequiredCount || draft.SelectedReserveIds.Count > draft.RequiredCount ||
            draft.SelectedHandIds.Distinct().Count() != draft.SelectedHandIds.Count || draft.SelectedReserveIds.Distinct().Count() != draft.SelectedReserveIds.Count ||
            draft.SelectedHandIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(draft.ChooserSeat)) ||
            draft.SelectedReserveIds.Any(id => _cardZones.GetLocation(id) != new CardLocation(CardZoneKind.PrivateReserve, draft.ChooserSeat)) ||
            draft.Mode == "exchange-stars" && draft.SelectedHandIds.Count != draft.RequiredCount)
            throw new InvalidOperationException("A private reserve selection lost its physical owner or equal card cost.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision || decision.PlayerSeat != draft.ChooserSeat))
            throw new InvalidOperationException("A private reserve selection must retain its chooser's private prompt.");
    }
    private void ExpireAttributedNatureMarkers(int ownerSeat)
    {
        foreach (var player in _players)
            foreach (var marker in new[] { PlayerMarkerKind.Gale, PlayerMarkerKind.Mist })
                if (player.MarkerSourceCounts.ContainsKey((marker, ownerSeat))) SetAttributedNatureMarker(ownerSeat, player.Seat, marker, 0, ++_resolutionSequence);
    }
}
