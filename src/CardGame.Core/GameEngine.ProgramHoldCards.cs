namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome HoldProgramTargetCards(ProgramSkillFrame frame, int chooserSeat,
        int holderSeat, IReadOnlyList<CardZoneKind> zones, string resultBind, int minimumCards)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.HoldCardSelection is not null || active.CardSetBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A card hold cannot overwrite an existing draft or binding.");
        if (holderSeat == frame.OwnerSeat)
            throw new InvalidOperationException("A card hold cannot target the skill owner's own cards.");
        var candidates = zones.SelectMany(zone =>
        {
            var location = new CardLocation(zone, holderSeat);
            return _cardZones.CardsAt(location)
                .Select((card, index) => (card.Id, Location: location, Slot: index));
        }).ToArray();
        var requiredCount = candidates.Length == 0
            ? 0
            : Math.Clamp(_players[holderSeat].Hp, minimumCards, candidates.Length);
        if (requiredCount < minimumCards)
        {
            CancelProgramBindingAndCleanup(active, "目标没有可扣置的区域牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        active = active with
        {
            HoldCardSelection = new(holderSeat, chooserSeat, resultBind, requiredCount,
                Array.AsReadOnly(candidates.Select(item => item.Id).ToArray()),
                Array.AsReadOnly(candidates.Select(item => item.Location).ToArray()),
                Array.Empty<int>(), minimumCards)
        };
        ReplaceRuntimeTop(active);
        PublishProgramHoldCardSelection(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramHoldCardSelection(ProgramSkillFrame frame)
    {
        var draft = frame.HoldCardSelection ?? throw new InvalidOperationException("Missing card-hold draft.");
        var selected = draft.SelectedCardIds.ToHashSet();
        var handSlotsSeen = 0;
        var choices = draft.SelectedCardIds.Count >= draft.RequiredCount
            ? []
            : draft.CandidateCardIds.Select((id, index) => (Id: id, Location: draft.CandidateLocations[index]))
            .Where(item => !selected.Contains(item.Id))
            .Select(item =>
            {
                var card = _cardZones.CardsAt(item.Location).Single(card => card.Id == item.Id);
                var hidden = item.Location.Zone == CardZoneKind.Hand &&
                             draft.ChooserSeat != draft.HolderSeat;
                var slot = item.Location.Zone == CardZoneKind.Hand ? ++handSlotsSeen : 0;
                return new PromptChoice(
                    new ChoiceId($"program-hold.frame-{frame.Id}.pick-{selected.Count}.card-{item.Id}"),
                    hidden ? $"选择目标第 {slot} 张手牌" : $"选择【{card.DisplayName}】（{card.RankText}）",
                    hidden ? [] : [item.Id], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "select-owned-cards",
                        ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["result-bind"] = draft.ResultBind,
                        ["source-zone"] = item.Location.Zone.ToString(),
                        ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["selection-index"] = selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            }).ToList();
        if (draft.SelectedCardIds.Count >= draft.MinimumCount)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"program-hold.frame-{frame.Id}.finish-{draft.SelectedCardIds.Count}"),
                draft.SelectedCardIds.Count > 0
                    ? $"完成扣置（已选 {draft.SelectedCardIds.Count} 张）。"
                    : "不扣置任何牌。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "finish-owned-cards",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = draft.ResultBind,
                    ["selection-index"] = draft.SelectedCardIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, draft.ChooserSeat,
            draft.RequiredCount > draft.SelectedCardIds.Count
                ? $"请选择要扣置到目标武将牌旁的牌（至多 {draft.RequiredCount} 张，已选 {draft.SelectedCardIds.Count} 张；完成前不移动牌）。"
                : "请确认扣置的牌。",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], draft.ChooserSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.HolderSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 选择扣置的牌", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[draft.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramHoldCardSelection(ProgramSkillFrame frame, PromptChoice selected)
    {
        var draft = frame.HoldCardSelection ?? throw new InvalidOperationException("Missing card-hold draft.");
        var finish = selected.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards";
        int? chosenCard = null;
        if (!finish)
        {
            // Visible candidates carry their card id in the choice; hidden hand slots
            // only carry it inside the stable choice id.
            if (selected.Cards.Count == 1) chosenCard = selected.Cards[0];
            else if (selected.Cards.Count == 0)
            {
                var value = selected.Id.Value;
                var marker = value.LastIndexOf(".card-", StringComparison.Ordinal);
                if (marker >= 0 && int.TryParse(value[(marker + 6)..], out var parsed)) chosenCard = parsed;
            }
            else chosenCard = -1;
        }
        if (draft.ResultBind != selected.Parameters.GetValueOrDefault("result-bind") ||
            selected.Parameters.GetValueOrDefault("selection-index") !=
            draft.SelectedCardIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            (finish
                ? draft.SelectedCardIds.Count < draft.MinimumCount || selected.Cards.Count != 0
                : chosenCard is not { } cardId || cardId < 0 ||
                  !draft.CandidateCardIds.Contains(cardId) ||
                  draft.SelectedCardIds.Contains(cardId)))
            throw new InvalidOperationException("The card-hold choice does not match its suspended instruction.");
        ClearPendingDecision();
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.HolderSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            draft.CandidateCardIds.Where((id, index) => _cardZones.GetLocation(id) != draft.CandidateLocations[index]).Any())
        {
            CancelProgramBindingAndCleanup(frame, "扣置参与者、技能实例或冻结来源已失效，未移动任何牌。");
            return;
        }
        var ids = finish ? draft.SelectedCardIds.ToArray() : draft.SelectedCardIds.Append(chosenCard!.Value).ToArray();
        if (!finish && ids.Length < draft.RequiredCount)
        {
            frame = frame with { HoldCardSelection = draft with { SelectedCardIds = Array.AsReadOnly(ids) } };
            ReplaceRuntimeTop(frame);
            PublishProgramHoldCardSelection(frame);
            return;
        }
        ReplaceRuntimeTop(frame with { HoldCardSelection = null });
        if (ids.Length > 0)
        {
            var moves = ids.Select(id =>
                (Card: _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id),
                 From: _cardZones.GetLocation(id))).ToArray();
            foreach (var (card, from) in moves)
                MoveCard(card, from, CardLocation.PojunHold(draft.HolderSeat), CardMoveReasons.PojunHold);
            SetProgramCardSet(frame.Id, draft.ResultBind, ids, SkillProgramCardSetVisibility.Private,
                moves.Select(_ => CardLocation.PojunHold(draft.HolderSeat)).ToArray());
            AddLog("SkillTriggered",
                $"{_players[frame.OwnerSeat].Name} 将 {ids.Length} 张牌扣置于 {_players[draft.HolderSeat].Name} 的武将牌旁。",
                frame.OwnerSeat, draft.HolderSeat);
            AdvanceEventRulesAndQueueFact(new ProgramHoldCardsPlacedEvent(frame.Id, frame.SkillId,
                GetProgramBindingId(frame), frame.OwnerSeat, draft.HolderSeat, draft.ResultBind,
                Array.AsReadOnly(ids)));
        }
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertProgramHoldCardSelection(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.HoldTargetCards)
        {
            if (frame.HoldCardSelection is not null)
                throw new InvalidOperationException("A card-hold draft outlived its suspended instruction.");
            return;
        }
        if (frame.HoldCardSelection is not { } draft)
            throw new InvalidOperationException("A suspended card-hold lost its private draft.");
        if (draft.ResultBind != paused.ResultBind || draft.RequiredCount <= 0 ||
            draft.RequiredCount > draft.CandidateCardIds.Count || draft.SelectedCardIds.Count >= draft.RequiredCount ||
            draft.MinimumCount != paused.MinimumCards || draft.MinimumCount > draft.RequiredCount ||
            draft.CandidateCardIds.Count != draft.CandidateLocations.Count ||
            draft.CandidateCardIds.Distinct().Count() != draft.CandidateCardIds.Count ||
            draft.SelectedCardIds.Distinct().Count() != draft.SelectedCardIds.Count ||
            draft.SelectedCardIds.Any(id => !draft.CandidateCardIds.Contains(id)) ||
            draft.CandidateLocations.Any(location =>
                location.OwnerSeat != draft.HolderSeat ||
                !paused.Zones.Contains(location.Zone) ||
                location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            frame.CardSetBindings.Any(binding => binding.Name == draft.ResultBind) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != draft.ChooserSeat ||
            decision.Choices.Count != (draft.SelectedCardIds.Count >= draft.RequiredCount
                ? 1
                : draft.CandidateCardIds.Count - draft.SelectedCardIds.Count +
                (draft.SelectedCardIds.Count >= draft.MinimumCount ? 1 : 0)) ||
            decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("result-bind") != draft.ResultBind ||
                (choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"
                    ? draft.SelectedCardIds.Count < draft.MinimumCount || choice.Cards.Count != 0
                    : choice.Parameters.GetValueOrDefault("program-action") != "select-owned-cards" ||
                      choice.Cards.Count > 1 ||
                      (choice.Cards.Count == 1
                          ? !draft.CandidateCardIds.Contains(choice.Cards[0]) ||
                            draft.SelectedCardIds.Contains(choice.Cards[0])
                          : choice.Parameters.GetValueOrDefault("source-zone") != nameof(CardZoneKind.Hand)))))
            throw new InvalidOperationException("A private card-hold draft lost its exact instruction, sources or prompt.");
    }

    /// <summary>
    /// Returns every held Pojun card to its holder's hand at the end of the current
    /// turn. Equipment cards return to the hand, not their former slot.
    /// </summary>
    internal void ReturnProgramPojunHoldsAtTurnEnd()
    {
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var held = _cardZones.CardsAt(CardLocation.PojunHold(seat)).ToArray();
            if (held.Length == 0) continue;
            MoveCards(held, CardLocation.PojunHold(seat), CardLocation.Hand(seat),
                CardMoveReasons.PojunHoldReturn);
            AddLog("SkillResolved",
                $"{_players[seat].Name} 获得武将牌旁的 {held.Length} 张破军扣置牌。",
                seat);
            AdvanceEventRulesAndQueueFact(new PojunHoldReturnedEvent(seat, Array.AsReadOnly(held.Select(card => card.Id).ToArray())));
        }
    }
}

public sealed record ProgramHoldCardsPlacedEvent(
    long FrameId, string SkillId, string BindingId, int OwnerSeat, int HolderSeat,
    string ResultBind, IReadOnlyList<int> CardIds) : IGameEvent;

public sealed record PojunHoldReturnedEvent(int HolderSeat, IReadOnlyList<int> CardIds) : IGameEvent;
