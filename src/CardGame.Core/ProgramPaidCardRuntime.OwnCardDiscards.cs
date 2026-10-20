namespace CardGame.Core;

internal sealed partial class ProgramPaidCardRuntime
{
    internal SkillProgramStepOutcome ChooseProgramOwnCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference? chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason)
    {
        var active = _frames.ActiveProgram(frame.Id);
        var chooserSeat = chooser is { } reference
            ? ResolveProgramParticipant(active, reference)
            : frame.OwnerSeat;
        if (!_facts.IsAlive(chooserSeat))
        {
            _operations.Cancel(active, "弃牌角色已失效，技能剩余步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }

        var choices = BuildOwnCardDiscardChoices(active.Id, chooserSeat, zones, reason).ToList();
        if (choices.Count == 0) return SkillProgramStepOutcome.Continue;

        var skill = _facts.Skill(active.SkillId);
        _prompts.Set(new PendingDecision(
            DecisionKind.ProgramTrigger,
            chooserSeat,
            $"【{skill.Name}】请弃置一张牌。",
            choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray(),
            [chooserSeat],
            active.OwnerSeat)
        {
            PromptId = _prompts.NextId(),
            IsPrivate = true,
            SkillPrompt = new SkillPromptPresentation(
                active.SkillId,
                skill.Name,
                $"{skill.Name} · 弃置一张牌",
                skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        }, _facts.IsHuman(chooserSeat));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    internal IReadOnlyList<PromptChoice> BuildOwnCardDiscardChoices(
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
                CardZoneKind.Hand => _facts.Hand(chooserSeat),
                CardZoneKind.Equipment => _facts.Equipment(chooserSeat),
                _ => throw new InvalidOperationException("Unsupported own-card discard zone.")
            };
            for (var slot = 0; slot < cards.Count; slot++)
            {
                if (_facts.SelfDiscardForbidden(chooserSeat, cards[slot], new(zone, chooserSeat), OwnedCardMoveIntent.Discard)) continue;
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

    internal void ResolveProgramOwnCardDiscardChoice(PromptChoice selected)
    {
        var frame = _frames.Top as ProgramSkillFrame ??
            throw new InvalidOperationException("The own-card discard choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _facts.Skill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseOwnCardDiscard ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The own-card discard choice does not match its suspended instruction.");

        var chooserSeat = effect.ChooserRef is { } reference
            ? ResolveProgramParticipant(frame, reference)
            : frame.OwnerSeat;
        if (_prompts.Current is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat)
            throw new InvalidOperationException("The own-card discard chooser changed while suspended.");
        if (!_facts.IsAlive(frame.OwnerSeat) || !_facts.IsAlive(chooserSeat) ||
            !_facts.HasSkillInstance(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId))
        {
            _prompts.Clear();
            _operations.Cancel(frame, "弃牌参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }

        if (selected.Parameters.GetValueOrDefault("program-action") != "choose-own-card-discard" ||
            selected.Targets.Count != 1 || selected.Targets[0] != chooserSeat ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !effect.Zones.Contains(zone) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The own-card discard selection is malformed.");

        var source = new CardLocation(zone, chooserSeat);
        var cards = _facts.CardsAt(source);
        if (slot < 0 || slot >= cards.Count)
        {
            _prompts.Clear();
            _operations.Cancel(frame, "公布的牌位已失效，技能结算已取消。");
            return;
        }
        var card = cards[slot];
        if (selected.Cards.Count != 1 || selected.Cards[0] != card.Id)
            throw new InvalidOperationException("The own-card discard identity changed.");

        _prompts.Clear();
        if (_facts.SelfDiscardForbidden(chooserSeat, card, source, OwnedCardMoveIntent.Discard))
        { _operations.Cancel(frame, "当前手牌不能自行弃置，费用未支付。"); return; }
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        var logicalDiscard = _operations.CaptureLogicalDiscard(frame.OwnerSeat, chooserSeat, [card.Id], reason, frame.Id);
        _operations.Move(card, source, CardLocation.DiscardPile, reason);
        _operations.CompleteLogicalDiscard(logicalDiscard);
        _frames.AdvanceProgram(frame.Id);
    }
}
