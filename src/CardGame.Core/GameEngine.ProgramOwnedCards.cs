namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramEffect GetOwnedSelectionEffect(ProgramSkillFrame frame) =>
        PhaseHandDebtOwnedSelectionEffect(frame, EquipmentPairOwnedSelectionEffect(frame, TurnDrawDebtOwnedSelectionEffect(frame, ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect)));

    private int GetProgramOwnerLostHp(ProgramSkillFrame frame)
    {
        // Lifecycle facts are frozen before earlier candidates can change HP.
        var facts = frame.WindowContext?.Facts;
        return facts is not null
            ? Math.Max(0, facts.CurrentMaxHp - facts.CurrentHp)
            : Math.Max(0, _players[frame.OwnerSeat].MaxHp - _players[frame.OwnerSeat].Hp);
    }

    private SkillProgramStepOutcome SelectProgramOwnedCards(ProgramSkillFrame frame, int cardOwnerSeat,
        int amount, SkillProgramNumberExpression? expression, IReadOnlyList<CardZoneKind> zones, string resultBind,
        int minimumCards, int maximumCards, IReadOnlyList<CardKind> cardKinds, IReadOnlyList<Suit> suits)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnedCardSelection is not null || active.CardSetBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("An owned-card selection cannot overwrite an existing draft or binding.");
        var candidates = zones.SelectMany(zone =>
        {
            var location = new CardLocation(zone, cardOwnerSeat);
            return _cardZones.CardsAt(location)
                .Where(card => CanSelectSelfDiscardBinding(active, resultBind, cardOwnerSeat, card, location))
                .Where(card => MatchesEquipmentPairOwnedCost(active, resultBind, card, location))
                .Where(card => MatchesDeferredHandDebtCost(active, resultBind, card, location))
                .Where(card => MatchesProgramOwnedSelectionKind(active, resultBind, _players[cardOwnerSeat], card, location, cardKinds))
                .Where(card => suits.Count == 0 || suits.Contains(GetProgramEffectiveSuit(_players[cardOwnerSeat], card)))
                .Select(card => (card.Id, Location: location));
        }).ToArray();
        var requested = maximumCards > 0 ? expression == SkillProgramNumberExpression.LivingPlayerCount
            ? Math.Min(maximumCards, _players.Count(player => player.IsAlive)) : maximumCards : expression switch
        {
            null => amount,
            SkillProgramNumberExpression.CategoryTargetTurnUsage => GetProgramCategoryTargetTurnUsage(active),
            SkillProgramNumberExpression.OwnerLostHp => GetProgramOwnerLostHp(active),
            SkillProgramNumberExpression.AllOwnedZoneCards => candidates.Length,
            SkillProgramNumberExpression.HandHalfFloor => GetHand(_players[cardOwnerSeat]).Count / 2,
            SkillProgramNumberExpression.LivingPlayersMinHp => GetLivingPlayersMinHp(),
            SkillProgramNumberExpression.LivingFactionCount => GetLivingFactionCount(),
            SkillProgramNumberExpression.LivingPlayerCount => _players.Count(player => player.IsAlive),
            SkillProgramNumberExpression.SelectedPairHandDifference => active.SelectedTargetSeats is { Count: 2 } pair
                ? Math.Abs(GetHand(_players[pair[0]]).Count - GetHand(_players[pair[1]]).Count)
                : throw new InvalidOperationException(
                    "A selected-pair hand difference requires two resolved program targets."),
            _ => throw new InvalidOperationException("Unsupported owned-card selection amount.")
        };
        var count = Math.Min(requested, candidates.Length);
        if (minimumCards > count)
        {
            CancelProgramBindingAndCleanup(active, "没有足够的合法区域牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (count <= 0)
        {
            SetProgramCardSet(frame.Id, resultBind, [], SkillProgramCardSetVisibility.Private, []);
            return SkillProgramStepOutcome.Continue;
        }
        if (expression == SkillProgramNumberExpression.AllOwnedZoneCards)
        {
            SetProgramCardSet(frame.Id, resultBind,
                candidates.Select(item => item.Id).ToArray(),
                SkillProgramCardSetVisibility.Private,
                candidates.Select(item => item.Location).ToArray(), selectionActorSeat: cardOwnerSeat);
            return SkillProgramStepOutcome.Continue;
        }
        active = active with
        {
            OwnedCardSelection = new(cardOwnerSeat, resultBind, count,
                Array.AsReadOnly(candidates.Select(item => item.Id).ToArray()),
                Array.AsReadOnly(candidates.Select(item => item.Location).ToArray()), [], minimumCards, maximumCards > 0)
        };
        ReplaceRuntimeTop(active);
        PublishProgramOwnedCardSelection(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramOwnedCardSelection(ProgramSkillFrame frame)
    {
        var draft = frame.OwnedCardSelection ?? throw new InvalidOperationException("Missing owned-card draft.");
        var selected = draft.SelectedCardIds.ToHashSet();
        var remaining = draft.RequiredCount - selected.Count;
        var choices = draft.CandidateCardIds.Select((id, index) => (Id: id, Location: draft.CandidateLocations[index]))
            .Where(item => !selected.Contains(item.Id))
            .Select(item =>
            {
                var card = _cardZones.CardsAt(item.Location).Single(card => card.Id == item.Id);
                var zoneName = item.Location.Zone switch
                {
                    CardZoneKind.Hand => "手牌",
                    CardZoneKind.Equipment => "装备",
                    CardZoneKind.Judgment => "判定牌",
                    CardZoneKind.PrivateReserve => "星",
                    _ => throw new InvalidOperationException("Unsupported owned-card selection zone.")
                };
                return new PromptChoice(new ChoiceId($"program-owned-set.frame-{frame.Id}.pick-{selected.Count}.card-{item.Id}"),
                    $"选择{zoneName}【{card.DisplayName}】（{card.RankText}）", [item.Id], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "select-owned-cards",
                        ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["result-bind"] = draft.ResultBind,
                        ["selection-index"] = selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            }).ToList();
        if (draft.AllowEarlyFinish && selected.Count >= draft.MinimumCount || GetOwnedSelectionEffect(frame).AllowDecline)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"program-owned-set.frame-{frame.Id}.finish-{selected.Count}"),
                $"完成选择（已选 {selected.Count} 张）。", [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "finish-owned-cards",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = draft.ResultBind,
                    ["selection-index"] = selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, draft.CardOwnerSeat,
            draft.MinimumCount > 0
                ? $"请选择自己的区域牌（至少 {draft.MinimumCount} 张，已选 {selected.Count} 张，最多还可选 {remaining} 张；完成前不移动牌）。"
                : $"请选择自己的区域牌（还需选择 {remaining} 张，选定前不移动牌）。",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], SourceSeat: frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.CardOwnerSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 选择区域牌", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[draft.CardOwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramOwnedCardSelection(ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice selected)
    {
        if (HasForeignDiscardCapability && frame.OwnedCardSelection is { } owned &&
            _pendingDecision?.PlayerSeat != owned.CardOwnerSeat)
            throw new InvalidOperationException("Owned selection lost its actual choosing participant.");
        var deferred = effect.Op == SkillProgramEffectOp.ResolveDeferredHandAlignment;
        if (deferred) effect = DeferredOwnedSelectionEffect(frame, effect);
        effect = PhaseHandDebtOwnedSelectionEffect(frame, EquipmentPairOwnedSelectionEffect(frame, TurnDrawDebtOwnedSelectionEffect(frame, effect)));
        var draft = frame.OwnedCardSelection ?? throw new InvalidOperationException("Missing owned-card draft.");
        var finish = selected.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards";
        if (effect.Op != SkillProgramEffectOp.SelectOwnedCards || draft.ResultBind != effect.ResultBind ||
            draft.CardOwnerSeat != (effect.TargetReference is { } cardOwner
                ? ResolveProgramParticipant(frame, cardOwner)
                : ResolveProgramEffectTarget(frame, effect.Target)) ||
            selected.Parameters.GetValueOrDefault("result-bind") != draft.ResultBind ||
            selected.Parameters.GetValueOrDefault("selection-index") != draft.SelectedCardIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Targets.Count != 0 ||
            (finish
                ? !draft.AllowEarlyFinish && !effect.AllowDecline || draft.SelectedCardIds.Count < draft.MinimumCount && !effect.AllowDecline || selected.Cards.Count != 0
                : selected.Parameters.GetValueOrDefault("program-action") != "select-owned-cards" ||
                  selected.Cards.Count != 1 || !draft.CandidateCardIds.Contains(selected.Cards[0]) ||
                  draft.SelectedCardIds.Contains(selected.Cards[0])))
            throw new InvalidOperationException("The owned-card choice does not match its suspended instruction.");
        ClearPendingDecision();
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.CardOwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            draft.CandidateCardIds.Where((id, index) => _cardZones.GetLocation(id) != draft.CandidateLocations[index]).Any())
        {
            CancelProgramBindingAndCleanup(frame, "选牌参与者、技能实例或冻结来源已失效，未移动任何所选牌。");
            return;
        }
        var ids = finish ? draft.SelectedCardIds.ToArray() : draft.SelectedCardIds.Append(selected.Cards[0]).ToArray();
        if (ids.Any(id => !CanSelectSelfDiscardBinding(frame, draft.ResultBind, draft.CardOwnerSeat, _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id), _cardZones.GetLocation(id))))
        { CancelProgramBindingAndCleanup(frame, "所选手牌不能自行弃置，未移动费用。"); return; }
        if (!finish && ids.Length < draft.RequiredCount)
        {
            frame = frame with { OwnedCardSelection = draft with { SelectedCardIds = Array.AsReadOnly(ids) } };
            ReplaceRuntimeTop(frame);
            PublishProgramOwnedCardSelection(frame);
            return;
        }
        ReplaceRuntimeTop(frame with { OwnedCardSelection = null });
        var locations = ids.Select(id => draft.CandidateLocations[draft.CandidateCardIds.ToList().IndexOf(id)]).ToArray();
        if (deferred) { CompleteDeferredAlignmentDiscard(frame.Id, ids, locations); return; }
        SetProgramCardSet(frame.Id, draft.ResultBind, ids, SkillProgramCardSetVisibility.Private, locations,
            ids.Length == 1 ? EffectiveSuit(_players[draft.CardOwnerSeat], _cardZones.CardsAt(locations[0]).Single(c => c.Id == ids[0])) : null,
            selectionActorSeat: draft.CardOwnerSeat);
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertProgramOwnedCardSelection(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op == SkillProgramEffectOp.ResolveDeferredHandAlignment && frame.OwnedCardSelection is not null) paused = DeferredOwnedSelectionEffect(frame, paused);
        if (paused.Op == SkillProgramEffectOp.SelectTurnDamageUseDebtPayment && frame.OwnedCardSelection is not null) paused = TurnDrawDebtOwnedSelectionEffect(frame, paused);
        if (paused.Op == SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment && frame.OwnedCardSelection is not null) paused = PhaseHandDebtOwnedSelectionEffect(frame, paused);
        if (paused.Op == SkillProgramEffectOp.SelectEquipmentPairAndPayment && frame.OwnedCardSelection is not null) paused = EquipmentPairOwnedSelectionEffect(frame, paused);
        if (paused.Op != SkillProgramEffectOp.SelectOwnedCards)
        {
            if (frame.OwnedCardSelection is not null)
                throw new InvalidOperationException("An owned-card draft outlived its suspended instruction.");
            return;
        }
        if (frame.OwnedCardSelection is not { } draft)
            throw new InvalidOperationException("A suspended owned-card selection lost its private draft.");
        if (draft.ResultBind != paused.ResultBind ||
            draft.CardOwnerSeat != (paused.TargetReference is { } cardOwner
                ? ResolveProgramParticipant(frame, cardOwner)
                : ResolveProgramEffectTarget(frame, paused.Target)) || draft.RequiredCount <= 0 ||
            draft.RequiredCount > draft.CandidateCardIds.Count || draft.SelectedCardIds.Count >= draft.RequiredCount ||
            draft.MinimumCount != paused.MinimumCards || draft.MinimumCount < 0 ||
            draft.AllowEarlyFinish != (paused.MaximumCards > 0) ||
            draft.MinimumCount > draft.RequiredCount ||
            draft.CandidateCardIds.Count != draft.CandidateLocations.Count ||
            draft.CandidateCardIds.Distinct().Count() != draft.CandidateCardIds.Count ||
            draft.SelectedCardIds.Distinct().Count() != draft.SelectedCardIds.Count ||
            draft.SelectedCardIds.Any(id => !draft.CandidateCardIds.Contains(id)) ||
            draft.CandidateLocations.Any(location => location.OwnerSeat != draft.CardOwnerSeat || !paused.Zones.Contains(location.Zone)) ||
            frame.CardSetBindings.Any(binding => binding.Name == draft.ResultBind) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != draft.CardOwnerSeat ||
            decision.Choices.Count != draft.CandidateCardIds.Count - draft.SelectedCardIds.Count +
                ((draft.AllowEarlyFinish && draft.SelectedCardIds.Count >= draft.MinimumCount || paused.AllowDecline) ? 1 : 0) ||
            decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("result-bind") != draft.ResultBind ||
                (choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"
                    ? !draft.AllowEarlyFinish && !paused.AllowDecline || draft.SelectedCardIds.Count < draft.MinimumCount && !paused.AllowDecline || choice.Cards.Count != 0
                    : choice.Parameters.GetValueOrDefault("program-action") != "select-owned-cards" ||
                      choice.Cards.Count != 1 || !draft.CandidateCardIds.Contains(choice.Cards[0]) ||
                      draft.SelectedCardIds.Contains(choice.Cards[0]))))
            throw new InvalidOperationException("A private owned-card draft lost its exact instruction, sources or prompt.");
    }
}
