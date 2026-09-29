namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome RequestProgramNearestSlash(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.NearestSlashRequest is not null)
            throw new InvalidOperationException("A nearest-slash request loop is already active.");
        var responders = _players
            .Where(player => player.IsAlive && player.Seat != frame.OwnerSeat)
            .Select(player => player.Seat)
            .Order()
            .ToArray();
        if (responders.Length == 0)
            return SkillProgramStepOutcome.Continue;
        active = active with
        {
            NearestSlashRequest = new ProgramNearestSlashRequest(
                Array.AsReadOnly(responders), ResponderIndex: 0)
        };
        _resolutionStack[^1] = active;
        PublishProgramNearestSlash(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramNearestSlash(ProgramSkillFrame frame)
    {
        var request = frame.NearestSlashRequest ??
            throw new InvalidOperationException("Missing nearest-slash request state.");
        var responderIndex = request.ResponderIndex;
        while (responderIndex < request.ResponderSeats.Count &&
               !_players[request.ResponderSeats[responderIndex]].IsAlive)
        {
            responderIndex++;
        }

        if (responderIndex >= request.ResponderSeats.Count)
        {
            ClearPendingDecision();
            _resolutionStack[^1] = frame with { NearestSlashRequest = null };
            ContinueProgramSkill(frame.Id);
            return;
        }

        if (responderIndex != request.ResponderIndex)
        {
            request = request with { ResponderIndex = responderIndex };
            frame = frame with { NearestSlashRequest = request };
            _resolutionStack[^1] = frame;
        }

        var responderSeat = request.ResponderSeats[responderIndex];
        var responder = _players[responderSeat];
        var slashes = GetHand(responder).Where(card => IsSlashCard(card.Kind)).ToArray();
        if (slashes.Length == 0)
        {
            CommitProgramNearestSlashAnswer(frame, responderSeat, targetSeat: null, slashCardId: null);
            AddLog(
                "SkillTriggered",
                $"{responder.Name} 没有【杀】可出，失去1点体力。",
                responderSeat,
                frame.OwnerSeat);
            if (LoseProgramNearestSlashHp(frame.Id, frame.SkillId, responderSeat)) return;
            PublishProgramNearestSlash(GetActiveProgramFrame(frame.Id));
            return;
        }

        var nearestSeats = GetProgramTargetSeats(responderSeat, SkillProgramTargetKind.OtherLivingNearest);
        if (nearestSeats.Count == 0)
        {
            CommitProgramNearestSlashAnswer(frame, responderSeat, targetSeat: null, slashCardId: null);
            AddLog(
                "SkillTriggered",
                $"{responder.Name} 没有距离最近的其他角色，失去1点体力。",
                responderSeat,
                frame.OwnerSeat);
            if (LoseProgramNearestSlashHp(frame.Id, frame.SkillId, responderSeat)) return;
            PublishProgramNearestSlash(GetActiveProgramFrame(frame.Id));
            return;
        }

        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var choices = new List<PromptChoice>();
        foreach (var slash in slashes.OrderBy(card => card.Id))
        {
            foreach (var targetSeat in nearestSeats)
            {
                // Identity cards must go through their published conversion
                // source (plain use of them is never offered), so every slash is
                // enumerated with the shared conversion-choice variants helper.
                choices.AddRange(CreateConversionChoiceVariants(
                    responder, slash, CardKind.Slash, forResponse: false,
                    $"program-nearest-slash.frame-{frame.Id}.index-{responderIndex}.card-{slash.Id}.target-{targetSeat}",
                    $"使用【{slash.DisplayName}】攻击 {_players[targetSeat].Name}。",
                    [slash.Id], [targetSeat],
                    NearestSlashParameters(frame, responderIndex, "request-nearest-slash",
                        slash.Id, targetSeat)));
            }
        }

        choices.Add(new PromptChoice(
            new ChoiceId($"program-nearest-slash.frame-{frame.Id}.index-{responderIndex}.decline"),
            "不使用【杀】，失去1点体力。",
            [],
            [],
            NearestSlashParameters(frame, responderIndex, "request-nearest-slash-decline", null, null)));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            responderSeat,
            $"【{skill.Name}】：需对距离最近的一名其他角色使用一张【杀】，否则失去1点体力。",
            slashes.Select(card => card.Id).Order().ToArray(),
            nearestSeats.ToArray(),
            frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = responderSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 是否使用【杀】", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = responder.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private static Dictionary<string, string> NearestSlashParameters(
        ProgramSkillFrame frame, int responderIndex, string action, int? slashCardId, int? targetSeat) =>
        new()
        {
            ["program-action"] = action,
            ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["responder-index"] = responderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["card-id"] = slashCardId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            ["target-seat"] = targetSeat?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };

    private void CommitProgramNearestSlashAnswer(
        ProgramSkillFrame frame, int responderSeat, int? targetSeat, int? slashCardId)
    {
        QueueGameEvent(new ProgramNearestSlashAnsweredEvent(
            frame.Id, frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat,
            responderSeat, targetSeat, slashCardId, LostHp: targetSeat is null));
        AdvanceProgramNearestSlashCursor(frame);
    }

    private void AdvanceProgramNearestSlashCursor(ProgramSkillFrame frame)
    {
        var request = frame.NearestSlashRequest ??
            throw new InvalidOperationException("Missing nearest-slash request state.");
        _resolutionStack[^1] = frame with
        {
            NearestSlashRequest = request with { ResponderIndex = request.ResponderIndex + 1 }
        };
    }

    /// <summary>
    /// Returns true when the hp loss suspended into a dying continuation; the
    /// loop resumes through the program-skill dying continuation path.
    /// </summary>
    private bool LoseProgramNearestSlashHp(long frameId, string skillId, int targetSeat)
    {
        var target = _players[targetSeat];
        var before = target.Hp;
        var lost = Math.Min(target.Hp, 1);
        target.Hp = Math.Max(0, target.Hp - 1);
        RecordHpChange(frameId, null, targetSeat, before, target.Hp, HpChangeKind.Loss);
        QueueGameEvent(new ProgramSkillHpLostEvent(frameId, skillId, targetSeat, lost, target.Hp));
        if (target.Hp != 0) return false;
        BeginProgramSkillDying(frameId, target);
        return true;
    }

    private void ResolveProgramNearestSlashChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The nearest-slash request lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var request = frame.NearestSlashRequest ??
            throw new InvalidOperationException("The nearest-slash request lost its frozen progress.");
        if (effect.Op != SkillProgramEffectOp.RequestNearestSlash ||
            request.ResponderIndex < 0 || request.ResponderIndex >= request.ResponderSeats.Count ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("responder-index") !=
                request.ResponderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The nearest-slash choice does not match its suspended instruction.");
        var responderSeat = request.ResponderSeats[request.ResponderIndex];
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != responderSeat)
            throw new InvalidOperationException("The nearest-slash responder changed while suspended.");
        ClearPendingDecision();
        var action = selected.Parameters.GetValueOrDefault("program-action");
        if (action == "request-nearest-slash-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            CommitProgramNearestSlashAnswer(frame, responderSeat, targetSeat: null, slashCardId: null);
            AddLog(
                "SkillTriggered",
                $"{_players[responderSeat].Name} 选择不出【杀】，失去1点体力。",
                responderSeat,
                frame.OwnerSeat);
            if (LoseProgramNearestSlashHp(frame.Id, frame.SkillId, responderSeat)) return;
            PublishProgramNearestSlash(GetActiveProgramFrame(frame.Id));
            return;
        }

        if (action != "request-nearest-slash" ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            selected.Cards.Count != 1 || selected.Cards[0] != cardId ||
            selected.Targets.Count != 1 || selected.Targets[0] != targetSeat)
            throw new InvalidOperationException("The nearest-slash answer is malformed.");
        var location = _cardZones.GetLocation(cardId);
        var card = location.Zone == CardZoneKind.Hand && location.OwnerSeat == responderSeat
            ? _cardZones.CardsAt(location).SingleOrDefault(item => item.Id == cardId)
            : null;
        if (card is null || !IsSlashCard(card.Kind))
        {
            CancelProgramBindingAndCleanup(frame, "所选【杀】已离开用牌者手牌，技能剩余结算已取消。");
            return;
        }

        var nearestSeats = GetProgramTargetSeats(responderSeat, SkillProgramTargetKind.OtherLivingNearest);
        if (!_players[targetSeat].IsAlive || !nearestSeats.Contains(targetSeat))
            throw new InvalidOperationException("The nearest-slash target is not one of the nearest characters.");
        CommitProgramNearestSlashAnswer(frame, responderSeat, targetSeat, cardId);
        AddLog(
            "SkillTriggered",
            $"{_players[responderSeat].Name} 对 {_players[targetSeat].Name} 使用【{card.DisplayName}】。",
            responderSeat,
            targetSeat);
        // Publish the exact use variant the responder picked (identity or
        // view-as conversion, or the native card) so capture accepts it even
        // when view-as candidates exist.
        CardConversionSource? conversionSource = null;
        if (TryReadConversionSource(selected.Parameters, out var chosenSource))
            conversionSource = chosenSource;
        _selectedUseConversion = conversionSource;
        _hasSelectedUseConversionChoice = true;
        ResolveSlashCore(_players[responderSeat], _players[targetSeat], card, card.Kind, responderSeat,
            physicalCards: [card], countsTowardSlashLimit: false,
            conversionSource: conversionSource,
            programSkillCardUseFrameId: frame.Id);
    }

    private PromptChoice SelectAiProgramNearestSlash(PendingDecision decision, ProgramSkillFrame frame)
    {
        // A forced Slash is worth more than a guaranteed one-hp loss; the first
        // choice is the lowest card id against the nearest seat in seat order so
        // the answer stays deterministic.
        return decision.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "request-nearest-slash") ??
            decision.Choices.First();
    }

    private void AssertProgramNearestSlash(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.RequestNearestSlash)
        {
            if (frame.NearestSlashRequest is not null)
                throw new InvalidOperationException("A nearest-slash request outlived its suspended instruction.");
            return;
        }

        if (frame.NearestSlashRequest is not { } request)
            throw new InvalidOperationException("A suspended nearest-slash request lost its frozen progress.");
        if (request.ResponderIndex < 0 || request.ResponderIndex > request.ResponderSeats.Count ||
            request.ResponderSeats.Distinct().Count() != request.ResponderSeats.Count ||
            request.ResponderSeats.Contains(frame.OwnerSeat) ||
            request.ResponderSeats.Any(seat => !IsValidPlayerSeat(seat)))
            throw new InvalidOperationException("The nearest-slash responder cursor is invalid.");
        if (!ReferenceEquals(frame, _resolutionStack.LastOrDefault()))
            return;
        var responderSeat = request.ResponderSeats[request.ResponderIndex];
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != responderSeat ||
            decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("frame-id") !=
                    frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("responder-index") !=
                    request.ResponderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("program-action") switch
                {
                    "request-nearest-slash" => choice.Cards.Count != 1 || choice.Targets.Count != 1,
                    "request-nearest-slash-decline" => choice.Cards.Count != 0 || choice.Targets.Count != 0,
                    _ => true
                }))
            throw new InvalidOperationException("A nearest-slash response lost its exact frozen prompt.");
    }

    private bool ContinueProgramNearestSlashIfResumable(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame ||
            frame.Id != frameId ||
            frame.NearestSlashRequest is null)
        {
            return false;
        }

        if (_winner != Winner.None || _status == EngineStatus.Completed || !_players[frame.OwnerSeat].IsAlive)
        {
            _resolutionStack[^1] = frame with { NearestSlashRequest = null };
            return false;
        }

        PublishProgramNearestSlash(frame);
        return true;
    }
}
