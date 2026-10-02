namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOtherOwnedCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var chooserSeat = ResolveProgramParticipant(active, chooser);
        if (!_players[chooserSeat].IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "选牌角色已失效，技能剩余步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = BuildOtherOwnedCardDiscardChoices(active.Id, chooserSeat, zones, reason).ToList();
        if (choices.Count == 0) return SkillProgramStepOutcome.Continue;
        choices.Add(new PromptChoice(
            new ChoiceId($"program-other-owned-card.frame-{active.Id}.decline"),
            "不弃置其他角色的牌。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "choose-other-owned-card-decline",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["move-reason"] = reason.Value
            }));

        var skill = _contentRegistry!.GetSkill(active.SkillId);
        var targetSeats = choices.SelectMany(choice => choice.Targets).Distinct().Order().ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            chooserSeat,
            $"【{skill.Name}】可以弃置一名其他角色的一张牌。",
            choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray(),
            targetSeats,
            active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            SkillPrompt = new SkillPromptPresentation(
                active.SkillId,
                skill.Name,
                $"{skill.Name} · 选择其他角色的牌",
                skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> BuildOtherOwnedCardDiscardChoices(
        long frameId,
        int chooserSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var result = new List<PromptChoice>();
        foreach (var cardOwner in _players
                     .Where(player => player.IsAlive && player.Seat != chooserSeat)
                     .OrderBy(player => (player.Seat - chooserSeat + _playerCount) % _playerCount))
        {
            foreach (var zone in zones)
            {
                var cards = zone switch
                {
                    CardZoneKind.Hand => GetHand(cardOwner),
                    CardZoneKind.Equipment => GetEquipment(cardOwner),
                    CardZoneKind.Judgment => GetJudgment(cardOwner),
                    _ => throw new InvalidOperationException("Unsupported other-player card zone.")
                };
                for (var slot = 0; slot < cards.Count; slot++)
                {
                    if (IsForeignEquipmentDiscardPrevented(chooserSeat, cards[slot],
                            new CardLocation(zone, cardOwner.Seat), OwnedCardMoveIntent.Discard)) continue;
                    var hidden = zone == CardZoneKind.Hand;
                    result.Add(new PromptChoice(
                        new ChoiceId($"program-other-owned-card.frame-{frameId}.owner-{cardOwner.Seat}.zone-{zone}.slot-{slot}"),
                        hidden
                            ? $"弃置 {cardOwner.Name} 的第 {slot + 1} 个暗置手牌牌位。"
                            : $"弃置 {cardOwner.Name} 的【{cards[slot].DisplayName}】。",
                        hidden ? [] : [cards[slot].Id],
                        [cardOwner.Seat],
                        new Dictionary<string, string>
                        {
                            ["program-action"] = "choose-other-owned-card-discard",
                            ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["card-owner-seat"] = cardOwner.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["source-zone"] = zone.ToString(),
                            ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["move-reason"] = reason.Value
                        }));
                }
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private void ResolveProgramOtherOwnedCardDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The other-player card choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseOtherOwnedCardDiscard ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The other-player card choice does not match its suspended instruction.");

        var chooserSeat = ResolveProgramParticipant(frame, effect.ChooserRef!);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat)
            throw new InvalidOperationException("The other-player card chooser changed while suspended.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[chooserSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "选牌参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }

        var action = selected.Parameters.GetValueOrDefault("program-action");
        if (action == "choose-other-owned-card-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            ClearPendingDecision();
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        if (action != "choose-other-owned-card-discard" ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("card-owner-seat"), out var cardOwnerSeat) ||
            cardOwnerSeat == chooserSeat || cardOwnerSeat < 0 || cardOwnerSeat >= _playerCount ||
            selected.Targets.Count != 1 || selected.Targets[0] != cardOwnerSeat ||
            !_players[cardOwnerSeat].IsAlive ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !effect.Zones.Contains(zone) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The other-player card selection is malformed.");

        var source = new CardLocation(zone, cardOwnerSeat);
        var cards = _cardZones.CardsAt(source);
        if (slot < 0 || slot >= cards.Count)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的其他角色牌位已失效，技能结算已取消。");
            return;
        }
        var card = cards[slot];
        if (zone == CardZoneKind.Hand)
        {
            if (selected.Cards.Count != 0)
                throw new InvalidOperationException("An opaque hand slot must not expose its card identity.");
        }
        else if (selected.Cards.Count != 1 || selected.Cards[0] != card.Id)
        {
            throw new InvalidOperationException("The visible other-player card identity changed.");
        }

        ClearPendingDecision();
        if (IsForeignEquipmentDiscardPrevented(chooserSeat, card, source, OwnedCardMoveIntent.Discard))
        {
            CancelProgramBindingAndCleanup(frame, "目标装备已不能被他人弃置，技能结算已取消。");
            return;
        }
        MoveCard(card, source, CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        AdvanceRuntimeProgram(frame.Id);
    }

    private PromptChoice SelectAiProgramOtherOwnedCardDiscard(PendingDecision decision, ProgramSkillFrame frame)
    {
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var (selected, thought) = _aiBrains[decision.PlayerSeat].ChooseOtherOwnedCardDiscard(
            CreateSnapshot(decision.PlayerSeat), decision.Choices, skill.Name, ++_thoughtSequence);
        AddThought(thought);
        return selected;
    }
}
