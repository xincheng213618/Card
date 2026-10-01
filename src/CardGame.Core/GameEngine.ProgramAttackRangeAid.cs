namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome RequestProgramAttackRangeAid(
        ProgramSkillFrame frame,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.AttackRangeAid is not null || active.SelectedTargetSeats.Count != 1)
            throw new InvalidOperationException("Attack-range aid requires one selected target and no active response chain.");

        var targetSeat = active.SelectedTargetSeats.Single();
        if (!IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("The attack-range aid target is no longer alive.");
        var responders = _players
            .Where(player => player.IsAlive && player.Seat != targetSeat &&
                             IsWithinAttackRange(player.Seat, targetSeat))
            .OrderBy(player => (player.Seat - active.OwnerSeat + _playerCount) % _playerCount)
            .Select(player => player.Seat)
            .ToArray();

        AdvanceEventRulesAndQueueFact(new ProgramAttackRangeAidStartedEvent(
            active.Id,
            active.SkillId,
            GetProgramBindingId(active),
            active.OwnerSeat,
            targetSeat,
            Array.AsReadOnly(responders)));
        AddLog(
            "ActiveSkill",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，" +
            $"共有 {responders.Length} 名角色依次响应。",
            active.OwnerSeat,
            targetSeat);

        if (responders.Length == 0)
            return SkillProgramStepOutcome.Continue;

        active = active with
        {
            AttackRangeAid = new ProgramAttackRangeAid(
                targetSeat,
                Array.AsReadOnly(responders),
                ResponderIndex: 0)
        };
        ReplaceRuntimeTop(active);
        PublishProgramAttackRangeAid(active, reason);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramAttackRangeAid(ProgramSkillFrame frame, CardMoveReason reason)
    {
        var aid = frame.AttackRangeAid ??
            throw new InvalidOperationException("Missing attack-range aid state.");
        var responderIndex = aid.ResponderIndex;
        while (responderIndex < aid.ResponderSeats.Count &&
               !_players[aid.ResponderSeats[responderIndex]].IsAlive)
        {
            responderIndex++;
        }

        if (responderIndex >= aid.ResponderSeats.Count)
        {
            ClearPendingDecision();
            ReplaceRuntimeTop(frame with { AttackRangeAid = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }

        if (responderIndex != aid.ResponderIndex)
        {
            aid = aid with { ResponderIndex = responderIndex };
            frame = frame with { AttackRangeAid = aid };
            ReplaceRuntimeTop(frame);
        }

        var responder = _players[aid.ResponderSeats[aid.ResponderIndex]];
        var target = _players[aid.TargetSeat];
        var choices = GetEquipment(responder)
            .Where(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
            .OrderBy(card => card.Id)
            .Select(card => new PromptChoice(
                new ChoiceId(
                    $"program-attack-range-aid.frame-{frame.Id}.index-{aid.ResponderIndex}.weapon-{card.Id}"),
                $"弃置武器【{card.DisplayName}】。",
                [card.Id],
                [],
                AttackRangeAidParameters(frame, aid, reason, "attack-range-aid-discard-weapon")))
            .Append(new PromptChoice(
                new ChoiceId(
                    $"program-attack-range-aid.frame-{frame.Id}.index-{aid.ResponderIndex}.draw-{target.Seat}"),
                $"令 {target.Name} 摸一张牌。",
                [],
                [target.Seat],
                AttackRangeAidParameters(frame, aid, reason, "attack-range-aid-draw")))
            .ToArray();
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            responder.Seat,
            $"{_players[frame.OwnerSeat].Name} 发动【{skill.Name}】：弃置一张武器牌，或令 {target.Name} 摸一张牌。",
            choices.SelectMany(choice => choice.Cards).ToArray(),
            [target.Seat],
            SourceSeat: frame.OwnerSeat)
        {
            PromptId = responder.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = target.Seat,
            SkillPrompt = new(
                frame.SkillId,
                skill.Name,
                $"{skill.Name} · 响应方式",
                "你在技能发动时能攻击到受益角色，因此必须选择：弃置一张武器，或令受益角色摸一张牌。"),
            Choices = choices
        };
        _status = responder.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private static Dictionary<string, string> AttackRangeAidParameters(
        ProgramSkillFrame frame,
        ProgramAttackRangeAid aid,
        CardMoveReason reason,
        string action) =>
        new()
        {
            ["program-action"] = action,
            ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["responder-index"] = aid.ResponderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["target-seat"] = aid.TargetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["move-reason"] = reason.Value
        };

    private void ResolveProgramAttackRangeAid(
        ProgramSkillFrame frame,
        SkillProgramEffect effect,
        PromptChoice selected)
    {
        var aid = frame.AttackRangeAid ??
            throw new InvalidOperationException("Missing attack-range aid state.");
        var expectedReason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        if (effect.Op != SkillProgramEffectOp.RequestAttackRangeAid ||
            aid.ResponderIndex < 0 || aid.ResponderIndex >= aid.ResponderSeats.Count ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("responder-index") !=
                aid.ResponderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("target-seat") !=
                aid.TargetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("move-reason") != expectedReason.Value)
            throw new InvalidOperationException("The attack-range aid choice does not match its suspended instruction.");

        var responderSeat = aid.ResponderSeats[aid.ResponderIndex];
        var responder = _players[responderSeat];
        var target = _players[aid.TargetSeat];
        int? discardedWeaponCardId = null;
        IReadOnlyList<int> drawnCardIds = [];
        switch (selected.Parameters.GetValueOrDefault("program-action"))
        {
            case "attack-range-aid-discard-weapon" when selected.Cards.Count == 1 && selected.Targets.Count == 0:
            {
                var weapon = GetEquipment(responder).SingleOrDefault(card =>
                    card.Id == selected.Cards.Single() &&
                    EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon) ??
                    throw new InvalidOperationException("The selected aid-response weapon is no longer equipped.");
                discardedWeaponCardId = weapon.Id;
                MoveCard(weapon, CardLocation.Equipment(responderSeat), CardLocation.Processing, expectedReason);
                MoveCard(weapon, CardLocation.Processing, CardLocation.DiscardPile, expectedReason);
                AddLog(
                    "SkillTriggered",
                    $"{responder.Name} 响应【{_contentRegistry!.GetSkill(frame.SkillId).Name}】，弃置武器【{weapon.DisplayName}】。",
                    responderSeat,
                    aid.TargetSeat);
                break;
            }
            case "attack-range-aid-draw" when selected.Cards.Count == 0 &&
                                                    selected.Targets.SequenceEqual([aid.TargetSeat]):
            {
                var drawn = target.IsAlive
                    ? DrawCards(target, 1, log: true, reason: expectedReason)
                    : [];
                drawnCardIds = Array.AsReadOnly(drawn.ToArray());
                AddLog(
                    "SkillTriggered",
                    $"{responder.Name} 响应【{_contentRegistry!.GetSkill(frame.SkillId).Name}】，令 {target.Name} 摸一张牌。",
                    responderSeat,
                    aid.TargetSeat);
                break;
            }
            default:
                throw new InvalidOperationException("The attack-range aid choice is malformed.");
        }

        AdvanceEventRulesAndQueueFact(new ProgramAttackRangeAidChoiceResolvedEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            frame.OwnerSeat,
            responderSeat,
            aid.TargetSeat,
            discardedWeaponCardId,
            drawnCardIds));
        ClearPendingDecision();
        frame = frame with
        {
            AttackRangeAid = aid with { ResponderIndex = aid.ResponderIndex + 1 }
        };
        ReplaceRuntimeTop(frame);
        PublishProgramAttackRangeAid(frame, expectedReason);
    }

    private PromptChoice SelectAiProgramAttackRangeAid(PendingDecision decision, ProgramSkillFrame frame)
    {
        var aid = frame.AttackRangeAid ??
            throw new InvalidOperationException("Missing AI attack-range aid state.");
        var responderSeat = aid.ResponderSeats[aid.ResponderIndex];
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var (choice, thought) = _aiBrains[responderSeat].ChooseProgramAttackRangeAidResponse(
            CreateSnapshot(responderSeat),
            frame.SkillId,
            skill.Name,
            aid.TargetSeat,
            decision.Choices,
            ++_thoughtSequence);
        AddThought(thought);
        return choice;
    }

    private void AssertProgramAttackRangeAid(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.RequestAttackRangeAid)
        {
            if (frame.AttackRangeAid is not null)
                throw new InvalidOperationException("An attack-range aid response outlived its suspended instruction.");
            return;
        }
        if (frame.AttackRangeAid is not { } aid)
            throw new InvalidOperationException("A suspended attack-range aid response lost its frozen progress.");
        if (aid.ResponderIndex < 0 || aid.ResponderIndex >= aid.ResponderSeats.Count)
            throw new InvalidOperationException("The attack-range aid responder cursor is invalid.");
        var responderSeat = aid.ResponderSeats[aid.ResponderIndex];
        var weapons = GetEquipment(_players[responderSeat])
            .Where(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
            .ToArray();
        var expectedReason = $"skill-program.{frame.SkillId}.{paused.Op}";
        if (frame.SelectedTargetSeats.Count != 1 || frame.SelectedTargetSeats.Single() != aid.TargetSeat ||
            aid.ResponderSeats.Count == 0 || aid.ResponderSeats.Distinct().Count() != aid.ResponderSeats.Count ||
            aid.ResponderSeats.Contains(aid.TargetSeat) || !aid.ResponderSeats.Contains(responderSeat) ||
            !_players[responderSeat].IsAlive || !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != responderSeat || decision.TargetSeat != aid.TargetSeat ||
            decision.Choices.Count != weapons.Length + 1 ||
            decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("frame-id") !=
                    frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("responder-index") !=
                    aid.ResponderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("target-seat") !=
                    aid.TargetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("move-reason") != expectedReason ||
                choice.Parameters.GetValueOrDefault("program-action") switch
                {
                    "attack-range-aid-discard-weapon" =>
                        choice.Cards.Count != 1 || choice.Targets.Count != 0 ||
                        weapons.All(card => card.Id != choice.Cards.Single()),
                    "attack-range-aid-draw" =>
                        choice.Cards.Count != 0 || !choice.Targets.SequenceEqual([aid.TargetSeat]),
                    _ => true
                }))
            throw new InvalidOperationException("An attack-range aid response lost its exact frozen prompt.");
    }
}
