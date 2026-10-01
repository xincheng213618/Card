namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IPublicPersistentPileProgramHost
    {
        public SkillProgramStepOutcome ExecutePublicPile(SkillProgramEffect effect, ProgramSkillFrame frame) => engine.ExecutePublicPile(effect, frame);
    }

    private SkillProgramStepOutcome ExecutePublicPile(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var owner = _players[frame.OwnerSeat];
        if (!owner.IsAlive) return SkillProgramStepOutcome.Continue;
        if (effect.Op == SkillProgramEffectOp.StoreTopCardInPublicPile)
        {
            var source = _publicPersistentPiles.GetValueOrDefault((frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId));
            if (source is not null && source.Capacity != effect.Amount || !SupportsMultiplePublicPiles && source is null && PublicPileSources(owner.Seat).Any())
                throw new InvalidOperationException("A public pile cannot mix source skill instances or capacities.");
            if (source is not null && PublicPileCards(source).Count >= effect.Amount) return SkillProgramStepOutcome.Continue;
            var card = DrawOneToProcessing(new("skill-program.public-pile.top"));
            if (card is null) return SkillProgramStepOutcome.Continue;
            source ??= EnsurePublicPileSource(frame, effect.Amount);
            ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(owner.Seat, 0, null) });
            MoveProcessingCardUnlessDestroyed(card, source.Location, new("skill-program.public-pile.store"));
            if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var sources = ReferencedPublicPileSources(owner.Seat, effect.SkillIds.Single(), frame.SkillInstanceId)
            .Where(s => effect.Op == SkillProgramEffectOp.ExchangePublicPileHand || s.Capacity == effect.Amount)
            .Where(s => PublicPileCards(s).Count > 0)
            .Where(s => PublicPileSourceSatisfiesFrozenTrigger(frame, s)).ToArray();
        if (sources.Length == 0) return SkillProgramStepOutcome.Continue;
        if (frame.PublicPileDraft is null)
        {
            if (sources.Length > 1)
                frame = frame with { PublicPileDraft = new(owner.Seat, effect.SkillIds.Single(), "source", -1, [], [], [], []) };
            else
            {
                if (effect.Op == SkillProgramEffectOp.DistributePublicPileIfAllSuits && PublicPileCards(sources[0]).Select(c => c.Suit).Distinct().Count() != 4)
                    return SkillProgramStepOutcome.Continue;
                frame = frame with { PublicPileDraft = CreatePublicPileDraft(frame, effect, sources[0]) };
            }
            ReplaceRuntimeTop(frame);
        }
        if (frame.PublicPileDraft!.Stage == "distribute" &&
            (frame.PublicPileDraft.RemainingIds.Count == 0 || !GetProgramTargetSeats(owner.Seat, SkillProgramTargetKind.OtherLiving).Any()))
        { ReplaceRuntimeTop(frame with { PublicPileDraft = null }); return SkillProgramStepOutcome.Continue; }
        PublishPublicPileChoice(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private ProgramPublicPileDraft CreatePublicPileDraft(ProgramSkillFrame frame, SkillProgramEffect effect, PublicPersistentPileSource source) =>
        new(source.OwnerSeat, source.SkillId, effect.Op == SkillProgramEffectOp.DistributePublicPileIfAllSuits ? "distribute" : "owned", source.Capacity,
            [], [], [], effect.Op == SkillProgramEffectOp.DistributePublicPileIfAllSuits ? PublicPileCards(source).Select(c => c.Id).ToArray() : [],
            SupportsMultiplePublicPiles ? source.SkillInstanceId : null, SupportsMultiplePublicPiles ? source.Location : null);

    private bool PublicPileSourceSatisfiesFrozenTrigger(ProgramSkillFrame frame, PublicPersistentPileSource source)
    {
        if (frame.WindowContext?.Facts is not { } facts || frame.TriggerId is not { } triggerId) return true;
        var trigger = ProgramInstructionResolver.Default.FindTrigger(_contentRegistry.GetSkill(frame.SkillId).Program!, triggerId)!;
        var count = facts.PublicPersistentPileCounts?.GetValueOrDefault(PublicPileIdentity(source.SkillId, source.SkillInstanceId)) ?? PublicPileCards(source).Count;
        return trigger.Condition.Evaluate(facts with { PublicPersistentPileCounts = null, OwnedZoneCounts = facts.OwnedZoneCounts with { PublicPersistentPile = count } }, frame.SkillId, frame.SkillInstanceId);
    }

    private static string PublicPileCardLabel(Card card) => card.DisplayName + " " + GetSuitDisplayName(card.Suit) + card.RankText;

    private void PublishPublicPileChoice(ProgramSkillFrame frame)
    {
        var draft = frame.PublicPileDraft!;
        var handOnly = ProgramInstructionResolver.Default.Resolve(frame,_contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect.Op == SkillProgramEffectOp.ExchangePublicPileHand;
        var choices = new List<PromptChoice>();
        void Add(string token, string label, IReadOnlyList<int> cards, IReadOnlyList<int> seats) => choices.Add(new(
            new ChoiceId($"public-pile.frame-{frame.Id}.{draft.Stage}.{token}"), label, cards, seats,
            new Dictionary<string, string> { ["program-action"] = "public-pile", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["stage"] = draft.Stage, ["token"] = token }));
        if (draft.Stage == "source")
        {
            var paused = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
            foreach (var source in ReferencedPublicPileSources(draft.OwnerSeat, draft.SourceSkillId, frame.SkillInstanceId)
                .Where(s => (paused.Op == SkillProgramEffectOp.ExchangePublicPileHand || s.Capacity == paused.Amount) && PublicPileCards(s).Count > 0 && PublicPileSourceSatisfiesFrozenTrigger(frame, s)))
                Add("source-" + PublicPileIdentity(source.SkillId, source.SkillInstanceId),
                    (_contentRegistry.GetSkill(source.SkillId).ProgramPresentation?.AuthorityName ?? "公开牌堆") + " · " + string.Join("、", PublicPileCards(source).Take(3).Select(PublicPileCardLabel)) + " · " + PublicPileCards(source).Count + "张", [], []);
        }
        else if (draft.Stage == "owned")
        {
            if (draft.OwnedIds.Count < PublicPileDraftCards(draft).Count)
                foreach (var card in PublicPileExchangeOwnedCards(frame).Where(card => !draft.OwnedIds.Contains(card.Id)))
                    Add("card-" + card.Id, "换入【" + card.DisplayName + " " + GetSuitDisplayName(card.Suit) + card.RankText + "】", [card.Id], []);
            Add("finish", draft.OwnedIds.Count == 0 ? (handOnly ? "不交换" : "不交换，检查公开牌堆花色") : "完成换入牌选择", [], []);
        }
        else if (draft.Stage == "pile")
            foreach (var card in PublicPileDraftCards(draft).Where(card => !draft.PileIds.Contains(card.Id)))
                Add("card-" + card.Id, "取出【" + card.DisplayName + " " + GetSuitDisplayName(card.Suit) + card.RankText + "】", [card.Id], []);
        else
            foreach (var id in draft.RemainingIds)
                foreach (var seat in GetProgramTargetSeats(draft.OwnerSeat, SkillProgramTargetKind.OtherLiving))
                    Add($"card-{id}.target-{seat}", "将【" + PublicPileCardLabel(PublicPileDraftCards(draft).Single(card => card.Id == id)) + "】交给 " + _players[seat].Name, [id], [seat]);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.OwnerSeat,
            draft.Stage == "owned" ? (handOnly ? "选择任意张手牌，与等量公开牌堆牌交换。" : "选择任意张手牌或装备牌，与等量公开牌堆牌交换。") : draft.Stage == "pile" ? "选择等量公开牌堆牌取回手牌。" : "将全部公开牌堆牌分配给其他角色。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), draft.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = draft.Stage == "owned", TargetSeat = draft.OwnerSeat,
            Choices = choices.ToArray(), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[draft.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
    }

    private void ResolvePublicPileChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing public pile program.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        AssertPublicPileDraft(frame, effect);
        var draft = frame.PublicPileDraft!;
        if (selected.Parameters.GetValueOrDefault("stage") != draft.Stage || selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("Public pile choice lost its suspended stage.");
        if (draft.Stage == "source")
        {
            var source = ReferencedPublicPileSources(draft.OwnerSeat, draft.SourceSkillId, frame.SkillInstanceId)
                .Single(s => "source-" + PublicPileIdentity(s.SkillId, s.SkillInstanceId) == selected.Parameters.GetValueOrDefault("token"));
            if (!PublicPileSourceSatisfiesFrozenTrigger(frame, source) || PublicPileCards(source).Count == 0 || selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("Public pile source selection lost its frozen qualification.");
            ClearPendingDecision();
            if (effect.Op == SkillProgramEffectOp.DistributePublicPileIfAllSuits && PublicPileCards(source).Select(c => c.Suit).Distinct().Count() != 4)
            { ReplaceRuntimeTop(frame with { PublicPileDraft = null }); AdvanceRuntimeProgram(frame.Id); return; }
            frame = frame with { PublicPileDraft = CreatePublicPileDraft(frame, effect, source) };
            ReplaceRuntimeTop(frame); PublishPublicPileChoice(frame); return;
        }
        var finish = selected.Parameters.GetValueOrDefault("token") == "finish";
        if (finish)
        {
            if (draft.Stage != "owned" || selected.Cards.Count != 0 || selected.Targets.Count != 0) throw new InvalidOperationException("Only the owned selection may finish early.");
            ClearPendingDecision();
            if (draft.OwnedIds.Count == 0) { ReplaceRuntimeTop(frame with { PublicPileDraft = null }); AdvanceRuntimeProgram(frame.Id); return; }
            draft = draft with { Stage = "pile" };
        }
        else
        {
            if (selected.Cards.Count != 1) throw new InvalidOperationException("One distinct physical card is required.");
            var id = selected.Cards.Single(); var location = _cardZones.GetLocation(id);
            if (draft.Stage == "owned")
            {
                if (selected.Targets.Count != 0 || draft.OwnedIds.Contains(id) || draft.OwnedIds.Count >= PublicPileDraftCards(draft).Count ||
                    location.OwnerSeat != draft.OwnerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                    effect.Op == SkillProgramEffectOp.ExchangePublicPileHand && location.Zone != CardZoneKind.Hand) throw new InvalidOperationException("Invalid owned public-pile exchange payment.");
                ClearPendingDecision();
                draft = draft with { OwnedIds = draft.OwnedIds.Append(id).ToArray(), OwnedLocations = draft.OwnedLocations.Append(location).ToArray() };
                if (draft.OwnedIds.Count == PublicPileDraftCards(draft).Count) draft = draft with { Stage = "pile" };
            }
            else if (draft.Stage == "pile")
            {
                if (selected.Targets.Count != 0 || location != PublicPileDraftLocation(draft) || draft.PileIds.Contains(id)) throw new InvalidOperationException("Invalid public-pile withdrawal.");
                var ids = draft.PileIds.Append(id).ToArray();
                ClearPendingDecision(); draft = draft with { PileIds = ids };
                if (ids.Length == draft.OwnedIds.Count)
                {
                    // Validate both sides before the first move; settle the entire swap before observers run.
                    if (draft.OwnedIds.Where((cardId, index) => _cardZones.GetLocation(cardId) != draft.OwnedLocations[index]).Any()) throw new InvalidOperationException("Exchange payment moved before commit.");
                    ReplaceRuntimeTop(frame with { PublicPileDraft = null, PendingMovementContinuation = new(draft.OwnerSeat, 0, null) });
                    var entering = draft.OwnedIds.Select(cardId => _cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(card => card.Id == cardId)).ToArray();
                    MoveProgramCardsFromMultipleSources(draft.OwnedIds, CardLocation.Processing, new("skill-program.public-pile.exchange-payment"));
                    MoveCards(PublicPileDraftCards(draft).Where(card => ids.Contains(card.Id)).ToArray(), PublicPileDraftLocation(draft), CardLocation.Hand(draft.OwnerSeat), new("skill-program.public-pile.exchange-obtain"));
                    foreach (var card in entering) MoveProcessingCardUnlessDestroyed(card, PublicPileDraftLocation(draft), new("skill-program.public-pile.exchange-deposit"));
                    if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
                    return;
                }
            }
            else
            {
                if (selected.Targets.Count != 1 || selected.Targets.Single() == draft.OwnerSeat || !_players[selected.Targets.Single()].IsAlive || !draft.RemainingIds.Contains(id) || location != PublicPileDraftLocation(draft)) throw new InvalidOperationException("Public pile requires all frozen cards to living other recipients.");
                var card = PublicPileDraftCards(draft).Single(card => card.Id == id);
                ClearPendingDecision();
                ReplaceRuntimeTop(frame with { PublicPileDraft = draft with { RemainingIds = draft.RemainingIds.Where(cardId => cardId != id).ToArray() },
                    ReexecuteParticipantInstruction = true, PendingMovementContinuation = new(draft.OwnerSeat, 0, null) });
                MoveCard(card, location, CardLocation.Hand(selected.Targets.Single()), new("skill-program.public-pile.distribute"));
                if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
                return;
            }
        }
        ReplaceRuntimeTop(frame with { PublicPileDraft = draft });
        PublishPublicPileChoice(GetActiveProgramFrame(frame.Id));
    }

    private IReadOnlyList<Card> PublicPileExchangeOwnedCards(ProgramSkillFrame frame)
    {
        var e=ProgramInstructionResolver.Default.Resolve(frame,_contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        return e.Op==SkillProgramEffectOp.ExchangePublicPileHand ? GetHand(_players[frame.OwnerSeat]) : GetHand(_players[frame.OwnerSeat]).Concat(GetEquipment(_players[frame.OwnerSeat])).ToArray();
    }

    private PromptChoice SelectAiPublicPileChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var draft = frame.PublicPileDraft!;
        var owner = _players[draft.OwnerSeat];
        if (draft.Stage == "source") return decision.Choices[0];
        if (draft.Stage == "owned")
        {
            var available = decision.Choices.Where(choice => choice.Cards.Count == 1).OrderBy(choice => GetKeepValue(PublicPileExchangeOwnedCards(frame).Single(card => card.Id == choice.Cards[0]), owner)).ThenBy(choice => choice.Cards[0]).FirstOrDefault();
            if (available is null || draft.OwnedIds.Count > 0) return decision.Choices.Single(choice => choice.Cards.Count == 0);
            var payment = PublicPileExchangeOwnedCards(frame).Single(card => card.Id == available.Cards[0]);
            return PublicPileDraftCards(draft).Any(card => GetKeepValue(card, owner) > GetKeepValue(payment, owner)) ? available : decision.Choices.Single(choice => choice.Cards.Count == 0);
        }
        if (draft.Stage == "pile") return decision.Choices.OrderByDescending(choice => GetKeepValue(PublicPileDraftCards(draft).Single(card => card.Id == choice.Cards[0]), owner)).ThenBy(choice => choice.Cards[0]).First();
        return decision.Choices.OrderByDescending(choice => AreProgramDistributionAllies(owner, _players[choice.Targets[0]]))
            .ThenBy(choice => GetHand(_players[choice.Targets[0]]).Count).ThenBy(choice => choice.Targets[0]).ThenBy(choice => choice.Cards[0]).First();
    }

    private void AssertPublicPileDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.PublicPileDraft is not { } draft) return;
        if (draft.Stage == "source")
        {
            if (draft.OwnerSeat != frame.OwnerSeat || draft.SourceSkillId != paused.SkillIds.SingleOrDefault() || draft.SourceLocation is not null || draft.SourceSkillInstanceId is not null || draft.Capacity != -1 || draft.OwnedIds.Count + draft.PileIds.Count + draft.RemainingIds.Count != 0)
                throw new InvalidOperationException("Public pile source draft is malformed.");
            return;
        }
        var source = PublicPileDraftSource(draft);
        if (draft.OwnerSeat != frame.OwnerSeat || source is null || source.SkillId != draft.SourceSkillId || source.Capacity != draft.Capacity ||
            (paused.Op == SkillProgramEffectOp.ExchangePublicPileHand ? draft.Capacity < 0 : draft.Capacity is < 1 or > 16 || paused.Amount != draft.Capacity) || paused.SkillIds.SingleOrDefault() != draft.SourceSkillId ||
            (draft.Stage == "distribute" ? paused.Op != SkillProgramEffectOp.DistributePublicPileIfAllSuits : paused.Op is not (SkillProgramEffectOp.ExchangePublicPile or SkillProgramEffectOp.ExchangePublicPileHand)) ||
            draft.Stage is not ("owned" or "pile" or "distribute") || draft.OwnedIds.Count > (draft.Capacity==0?PublicPileDraftCards(draft).Count:draft.Capacity) || draft.OwnedIds.Count != draft.OwnedLocations.Count ||
            draft.OwnedIds.Distinct().Count() != draft.OwnedIds.Count || draft.PileIds.Distinct().Count() != draft.PileIds.Count || draft.PileIds.Count > draft.OwnedIds.Count ||
            draft.RemainingIds.Distinct().Count() != draft.RemainingIds.Count || draft.RemainingIds.Count > (draft.Capacity==0?PublicPileDraftCards(draft).Count:draft.Capacity) ||
            draft.OwnedLocations.Any(location => paused.Op == SkillProgramEffectOp.ExchangePublicPileHand && location.Zone != CardZoneKind.Hand) ||
            draft.OwnedIds.Where((id, index) => draft.OwnedLocations[index].OwnerSeat != draft.OwnerSeat || draft.OwnedLocations[index].Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || _cardZones.GetLocation(id) != draft.OwnedLocations[index]).Any() ||
            draft.PileIds.Concat(draft.RemainingIds).Any(id => _cardZones.GetLocation(id) != PublicPileDraftLocation(draft)))
            throw new InvalidOperationException("Public pile draft lost capacity, physical ownership, equal cost or instruction provenance.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && frame.PendingMovementContinuation is null &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != draft.OwnerSeat || prompt.IsPrivate != (draft.Stage == "owned")))
            throw new InvalidOperationException("Public pile draft lost its chooser and visibility.");
    }

    private void CleanupLostPublicPersistentPiles()
    {
        foreach (var source in _publicPersistentPiles.Values.Where(source => !_players[source.OwnerSeat].IsAlive ||
            !_players[source.OwnerSeat].SkillGrants.Grants.Any(grant => grant.SkillId == source.SkillId && grant.SkillInstanceId == source.SkillInstanceId)).ToArray())
        {
            _publicPersistentPiles.Remove((source.OwnerSeat, source.SkillId, source.SkillInstanceId));
            for (var index = 0; index < _resolutionStack.Count; index++)
                if (_resolutionStack[index] is ProgramSkillFrame { PublicPileDraft: { } draft } active &&
                    draft.OwnerSeat == source.OwnerSeat && draft.SourceSkillId == source.SkillId && (draft.SourceSkillInstanceId is null || draft.SourceSkillInstanceId == source.SkillInstanceId))
                    ReplaceRuntimeFrame(_resolutionStack[index].Id, active with { PublicPileDraft = null });
            MoveCards(PublicPileCards(source).ToArray(), source.Location, CardLocation.DiscardPile, new("skill-program.public-pile.source-lost"));
        }
    }
}
