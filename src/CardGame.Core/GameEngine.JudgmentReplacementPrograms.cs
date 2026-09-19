namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesProgramJudgmentReplacement(
        PlayerRuntime owner,
        SkillProgramTrigger trigger,
        int targetSeat,
        string reason) =>
        trigger.Window == SkillProgramTriggerWindow.JudgmentReplacing &&
        (trigger.Subject == SkillProgramTriggerSubject.Any || owner.Seat == targetSeat) &&
        !trigger.ExcludedReasons.Contains(reason, StringComparer.Ordinal) &&
        trigger.Effects[0].Condition.Evaluate(CreateSkillContext(owner));

    private IReadOnlyList<Card> GetProgramJudgmentReplacementCards(
        PlayerRuntime owner,
        SkillProgramTrigger trigger)
    {
        if (trigger.Window != SkillProgramTriggerWindow.JudgmentReplacing ||
            trigger.Effects.Count == 0 ||
            trigger.Effects[0] is not { Op: SkillProgramTriggerEffectOp.ReplaceJudgment } replacement ||
            !replacement.Condition.Evaluate(CreateSkillContext(owner)))
            return [];
        var cards = new List<Card>();
        if (replacement.Zones.Contains(CardZoneKind.Hand)) cards.AddRange(GetHand(owner));
        if (replacement.Zones.Contains(CardZoneKind.Equipment)) cards.AddRange(GetEquipment(owner));
        return cards
            .Where(card => replacement.Suits.Contains(EffectiveSuit(owner, card)))
            .OrderBy(card => card.Id)
            .ToArray();
    }

    private (SkillProgram Program, SkillProgramTrigger Trigger) GetProgramJudgmentReplacement(
        JudgmentTriggerCandidate candidate)
    {
        if (!candidate.IsProgram || candidate.ProgramId is null || candidate.ProgramTriggerId is null ||
            candidate.GameplayHash is null || _contentRegistry is null)
            throw new InvalidOperationException("The judgment replacement candidate has no configured identity.");
        var program = _contentRegistry.Skills[candidate.ProgramId].Program ??
            throw new InvalidOperationException("The judgment replacement program is unavailable.");
        if (!string.Equals(program.GameplayHash, candidate.GameplayHash, StringComparison.Ordinal))
            throw new InvalidOperationException("A running judgment replacement definition changed.");
        var trigger = program.Triggers.Single(item => item.Id == candidate.ProgramTriggerId);
        if (trigger.Window != SkillProgramTriggerWindow.JudgmentReplacing)
            throw new InvalidOperationException("The configured judgment candidate no longer replaces judgments.");
        return (program, trigger);
    }

    private void BeginProgramJudgmentReplacementChoice(
        JudgmentResolution pending,
        JudgmentTriggerCandidate candidate)
    {
        if (!ReferenceEquals(_pendingJudgment, pending) ||
            !ReferenceEquals(pending.CurrentCandidate, candidate) ||
            pending.CurrentCard is not { } judgmentCard)
            throw new InvalidOperationException("The configured judgment replacement is not current.");
        var (program, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[candidate.OwnerSeat];
        var enabled = owner.IsAlive &&
            EnabledSkillPrograms(owner).Any(item =>
                item.Id == program.Id && item.GameplayHash == program.GameplayHash) &&
            MatchesProgramJudgmentReplacement(owner, trigger, pending.TargetSeat, pending.Reason);
        var replacementCards = enabled
            ? GetProgramJudgmentReplacementCards(owner, trigger)
            : [];
        if (replacementCards.Count == 0)
        {
            AdvanceJudgmentCandidate(pending);
            return;
        }

        SetJudgmentFrameState(pending, ResolutionFrameStep.AwaitingResponse);
        QueueGameEvent(new JudgmentReplacementRequestedEvent(
            pending.FrameId,
            pending.FrameId,
            pending.TargetSeat,
            owner.Seat,
            pending.Reason,
            judgmentCard.Id,
            judgmentCard.Kind,
            EffectiveSuit(_players[pending.TargetSeat], judgmentCard)));
        var choices = replacementCards.Select(card => new PromptChoice(
                new ChoiceId($"program-judgment-replace.{program.Id}.{trigger.Id}.card-{card.Id}"),
                $"用【{card.DisplayName}】替换 {_players[pending.TargetSeat].Name} 的判定牌。",
                [card.Id],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "program-judgment-replace",
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToList();
        if (trigger.Optional)
            choices.Add(new PromptChoice(
                new ChoiceId($"program-judgment-replace.{program.Id}.{trigger.Id}.skip"),
                "不发动技能，保留当前判定牌。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "program-judgment-replace-skip" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramJudgmentReplacement,
            owner.Seat,
            $"【{_contentRegistry!.Skills[program.Id].Name}】：是否替换当前判定牌？",
            replacementCards.Select(card => card.Id).ToArray(),
            [],
            SourceSeat: pending.TargetSeat,
            IncomingCard: judgmentCard.Kind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = pending.TargetSeat,
            Choices = choices.ToArray()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitProgramJudgmentReplacementAnswer(
        int actorSeat,
        PromptId prompt,
        ChoiceId choice)
    {
        var error = ValidateHumanPrompt(
            actorSeat,
            DecisionKind.ProgramJudgmentReplacement,
            prompt,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var selected = _pendingDecision!.Choices.SingleOrDefault(item => item.Id == choice);
        if (selected is null)
            return Reject(CommandErrorCode.InvalidChoice, "The judgment replacement choice is not available.");
        return Accept(() =>
        {
            ResolveProgramJudgmentReplacementChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveProgramJudgmentReplacementChoice(PromptChoice selected)
    {
        var pending = _pendingJudgment ??
            throw new InvalidOperationException("The configured judgment replacement continuation is missing.");
        var candidate = pending.CurrentCandidate ??
            throw new InvalidOperationException("The configured judgment replacement candidate is missing.");
        var (program, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[candidate.OwnerSeat];
        var oldJudgmentCard = pending.CurrentCard ??
            throw new InvalidOperationException("The configured judgment replacement lost its current card.");
        int? selectedCardId = selected.Parameters.GetValueOrDefault("action") switch
        {
            "program-judgment-replace" => int.Parse(
                selected.Parameters["card-id"], System.Globalization.CultureInfo.InvariantCulture),
            "program-judgment-replace-skip" when trigger.Optional => null,
            _ => throw new InvalidOperationException("Unsupported configured judgment replacement choice.")
        };
        ClearPendingDecision();
        if (selectedCardId is not { } cardId)
        {
            QueueGameEvent(new JudgmentReplacementResolvedEvent(
                pending.FrameId, pending.FrameId, pending.TargetSeat, owner.Seat, pending.Reason,
                Used: false, oldJudgmentCard.Id, null, null, null, null));
            QueueGameEvent(new ProgramJudgmentReplacementResolvedEvent(
                pending.FrameId, program.Id, trigger.Id, owner.Seat, pending.TargetSeat,
                Activated: false, oldJudgmentCard.Id, null,
                trigger.Effects[0].OldCardDestination!.Value, 0, 0));
            AddLog("JudgmentReplacementSkipped",
                $"{owner.Name} 选择不发动【{_contentRegistry!.Skills[program.Id].Name}】。",
                owner.Seat, pending.TargetSeat);
            AdvanceJudgmentCandidate(pending);
            return;
        }

        var replacement = GetProgramJudgmentReplacementCards(owner, trigger)
            .SingleOrDefault(card => card.Id == cardId) ??
            throw new InvalidOperationException("The selected configured replacement card is no longer legal.");
        var committedSuit = EffectiveSuit(_players[pending.TargetSeat], replacement);
        var oldDestination = trigger.Effects[0].OldCardDestination!.Value;
        CommitProgramJudgmentReplacement(
            pending, owner, oldJudgmentCard, replacement, committedSuit, oldDestination);
        var drawnCards = 0;
        var recoveredHp = 0;
        foreach (var effect in trigger.Effects.Skip(1))
        {
            if (!owner.IsAlive || !effect.Condition.Evaluate(CreateSkillContext(owner)) ||
                !effect.ReplacementSuits.Contains(committedSuit) ||
                replacement.Rank < effect.MinimumReplacementRank ||
                replacement.Rank > effect.MaximumReplacementRank)
                continue;
            if (effect.Op == SkillProgramTriggerEffectOp.Draw)
                drawnCards += DrawCards(owner, effect.Amount, log: true,
                    reason: CardMoveReasons.ProgramJudgmentReplace).Count;
            else if (effect.Op == SkillProgramTriggerEffectOp.Recover)
            {
                var amount = Math.Min(effect.Amount, owner.MaxHp - owner.Hp);
                if (amount <= 0) continue;
                var recovery = BeginRecovery(pending.FrameId, owner.Seat, owner.Seat, amount);
                owner.Hp += amount;
                recoveredHp += amount;
                QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, owner.Seat, amount, owner.Hp));
                PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            }
        }
        QueueGameEvent(new JudgmentReplacementResolvedEvent(
            pending.FrameId, pending.FrameId, pending.TargetSeat, owner.Seat, pending.Reason,
            Used: true, oldJudgmentCard.Id, replacement.Id, replacement.Kind, committedSuit, replacement.Rank));
        QueueGameEvent(new ProgramJudgmentReplacementResolvedEvent(
            pending.FrameId, program.Id, trigger.Id, owner.Seat, pending.TargetSeat,
            Activated: true, oldJudgmentCard.Id, replacement.Id, oldDestination,
            drawnCards, recoveredHp));
        AddLog("JudgmentReplaced",
            $"{owner.Name} 发动【{_contentRegistry!.Skills[program.Id].Name}】替换了判定牌。",
            owner.Seat, pending.TargetSeat);
        AdvanceJudgmentCandidate(pending);
    }

    private void CommitProgramJudgmentReplacement(
        JudgmentResolution pending,
        PlayerRuntime owner,
        Card oldJudgmentCard,
        Card replacement,
        Suit committedSuit,
        SkillProgramOldJudgmentCardDestination oldDestination)
    {
        var oldFrom = CardLocation.Judgment(pending.TargetSeat);
        if (_cardZones.GetLocation(oldJudgmentCard.Id) != oldFrom)
            throw new InvalidOperationException("The current judgment card is no longer in its judgment zone.");
        var replacementFrom = FindOwnedCardLocation(owner, replacement);
        if (replacementFrom.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("The configured replacement card is not in an owned input zone.");
        var oldTo = oldDestination == SkillProgramOldJudgmentCardDestination.OwnerHand
            ? CardLocation.Hand(owner.Seat)
            : CardLocation.DiscardPile;
        var lianyingOwnerSeat = GetLianyingOwnerBeforeHandLoss(replacementFrom);
        _cardZones.MoveBatch([
            new CardTransfer(oldJudgmentCard.Id, oldFrom, oldTo),
            new CardTransfer(replacement.Id, replacementFrom, CardLocation.Processing)
        ]);
        _cardZones.Move(replacement.Id, CardLocation.Processing, oldFrom);
        pending.CurrentCard = replacement;
        pending.WasReplaced = true;
        ReplaceJudgmentFrame(GetJudgmentFrame(pending.FrameId) with
        {
            CardId = replacement.Id,
            CardKind = replacement.Kind,
            Suit = committedSuit
        });

        RecordMovement(oldJudgmentCard, oldFrom, oldTo, CardMoveReasons.ProgramJudgmentOldCard);
        ClearJudgmentEffectiveKindAfterMove(oldJudgmentCard, oldFrom, oldTo);
        RecordMovement(replacement, replacementFrom, CardLocation.Processing,
            CardMoveReasons.ProgramJudgmentReplace);
        ClearJudgmentEffectiveKindAfterMove(replacement, replacementFrom, CardLocation.Processing);
        ResolveSilverLionRemoval(replacement, replacementFrom, CardMoveReasons.ProgramJudgmentReplace);
        ResolveWoodenOxMove(replacement, replacementFrom, CardLocation.Processing);
        QueueXiaojiTrigger(replacement, replacementFrom);
        RecordMovement(replacement, CardLocation.Processing, oldFrom,
            CardMoveReasons.ProgramJudgmentReplace);
        ClearJudgmentEffectiveKindAfterMove(replacement, CardLocation.Processing, oldFrom);
        QueueLianyingTriggerIfHandBecameEmpty(lianyingOwnerSeat, [replacement.Id]);
    }

    private bool IsAiProgramJudgmentReplacementPending() =>
        _pendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: var playerSeat } &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiProgramJudgmentReplacement()
    {
        var pending = _pendingJudgment ??
            throw new InvalidOperationException("AI configured judgment replacement is missing.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI configured judgment replacement prompt is missing.");
        var candidate = pending.CurrentCandidate ??
            throw new InvalidOperationException("AI configured judgment candidate is missing.");
        var (_, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[candidate.OwnerSeat];
        var judgmentCard = pending.CurrentCard ??
            throw new InvalidOperationException("AI configured judgment replacement lost its current card.");
        var (cardId, thought) = _aiBrains[owner.Seat].ChooseGuicaiReplacement(
            CreateSnapshot(owner.Seat), pending.TargetSeat, pending.Reason, decision.ValidCardIds,
            judgmentCard.Kind, EffectiveSuit(_players[pending.TargetSeat], judgmentCard),
            ++_thoughtSequence, judgmentCard.Rank, _rulesVersion, UsesClassicGanglieJudgment);
        AddThought(thought);
        var selected = cardId is { } id
            ? decision.Choices.Single(choice => choice.Cards.SequenceEqual([id]))
            : trigger.Optional
                ? decision.Choices.Single(choice => choice.Cards.Count == 0)
                : decision.Choices.First(choice => choice.Cards.Count == 1);
        ResolveProgramJudgmentReplacementChoice(selected);
        PublishState();
    }
}
