namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOwnCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference? chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var chooserSeat = chooser is { } reference
            ? ResolveProgramParticipant(active, reference)
            : frame.OwnerSeat;
        if (!_players[chooserSeat].IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "弃牌角色已失效，技能剩余步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = BuildOwnCardDiscardChoices(active.Id, chooserSeat, zones, reason).ToList();
        if (choices.Count == 0) return SkillProgramStepOutcome.Continue;

        var skill = _contentRegistry!.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            chooserSeat,
            $"【{skill.Name}】请弃置一张牌。",
            choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray(),
            [chooserSeat],
            active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            SkillPrompt = new SkillPromptPresentation(
                active.SkillId,
                skill.Name,
                $"{skill.Name} · 弃置一张牌",
                skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> BuildOwnCardDiscardChoices(
        long frameId,
        int chooserSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var result = new List<PromptChoice>();
        foreach (var zone in zones)
        {
            var cards = zone switch
            {
                CardZoneKind.Hand => GetHand(_players[chooserSeat]),
                CardZoneKind.Equipment => GetEquipment(_players[chooserSeat]),
                _ => throw new InvalidOperationException("Unsupported own-card discard zone.")
            };
            for (var slot = 0; slot < cards.Count; slot++)
            {
                result.Add(new PromptChoice(
                    new ChoiceId($"program-own-card.frame-{frameId}.zone-{zone}.slot-{slot}"),
                    zone == CardZoneKind.Hand
                        ? $"弃置手牌【{cards[slot].DisplayName}】。"
                        : $"弃置装备区的【{cards[slot].DisplayName}】。",
                    [cards[slot].Id],
                    [chooserSeat],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "choose-own-card-discard",
                        ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["source-zone"] = zone.ToString(),
                        ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["move-reason"] = reason.Value
                    }));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private void ResolveProgramOwnCardDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The own-card discard choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseOwnCardDiscard ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The own-card discard choice does not match its suspended instruction.");

        var chooserSeat = effect.ChooserRef is { } reference
            ? ResolveProgramParticipant(frame, reference)
            : frame.OwnerSeat;
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat)
            throw new InvalidOperationException("The own-card discard chooser changed while suspended.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[chooserSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "弃牌参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }

        if (selected.Parameters.GetValueOrDefault("program-action") != "choose-own-card-discard" ||
            selected.Targets.Count != 1 || selected.Targets[0] != chooserSeat ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !effect.Zones.Contains(zone) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The own-card discard selection is malformed.");

        var source = new CardLocation(zone, chooserSeat);
        var cards = _cardZones.CardsAt(source);
        if (slot < 0 || slot >= cards.Count)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的牌位已失效，技能结算已取消。");
            return;
        }
        var card = cards[slot];
        if (selected.Cards.Count != 1 || selected.Cards[0] != card.Id)
            throw new InvalidOperationException("The own-card discard identity changed.");

        ClearPendingDecision();
        MoveCard(card, source, CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        AdvanceRuntimeProgram(frame.Id);
    }

    private PromptChoice SelectAiProgramOwnCardDiscard(PendingDecision decision, ProgramSkillFrame frame)
    {
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var (selected, thought) = _aiBrains[decision.PlayerSeat].ChooseOwnCardDiscard(
            CreateSnapshot(decision.PlayerSeat), decision.Choices, skill.Name, ++_thoughtSequence);
        AddThought(thought);
        return selected;
    }
}
