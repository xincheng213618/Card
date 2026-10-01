namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesProgramJudgmentReplacement(
        CharacterState owner,
        SkillProgramTrigger trigger,
        int targetSeat,
        string reason) =>
        trigger.Window == SkillProgramTriggerWindow.JudgmentReplacing &&
        CanPayProgramMarkerCost(owner, trigger.MarkerCost) &&
        (trigger.Subject == SkillProgramTriggerSubject.Any || owner.Seat == targetSeat) &&
        !trigger.ExcludedReasons.Contains(reason, StringComparer.Ordinal) &&
        trigger.Effects[0].Condition.Evaluate(CreateSkillContext(owner));

    private IReadOnlyList<Card> GetProgramJudgmentReplacementCards(
        CharacterState owner,
        SkillProgramTrigger trigger,
        int judgmentSubjectSeat,
        string judgmentReason)
    {
        if (trigger.Window != SkillProgramTriggerWindow.JudgmentReplacing ||
            trigger.Effects.Count == 0 ||
            trigger.Effects[0] is not { Op: SkillProgramEffectOp.ReplaceJudgment } replacement ||
            !replacement.Condition.Evaluate(CreateSkillContext(owner)))
            return [];
        var cards = new List<Card>();
        if (replacement.Zones.Contains(CardZoneKind.Hand)) cards.AddRange(GetHand(owner));
        if (replacement.Zones.Contains(CardZoneKind.Equipment)) cards.AddRange(GetEquipment(owner));
        return cards
            .Where(card => !IsProtectedJudgmentSourceEquipment(
                owner, card, judgmentSubjectSeat, judgmentReason))
            .Where(card => replacement.Suits.Contains(EffectiveSuit(owner, card)))
            .OrderBy(card => card.Id)
            .ToArray();
    }

    private (SkillProgram Program, SkillProgramTrigger Trigger) GetProgramJudgmentReplacement(
        JudgmentTriggerCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ProgramId) ||
            string.IsNullOrWhiteSpace(candidate.ProgramTriggerId) ||
            string.IsNullOrWhiteSpace(candidate.SkillInstanceId) ||
            string.IsNullOrWhiteSpace(candidate.GameplayHash))
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
        JudgmentFrame pending,
        JudgmentTriggerCandidate candidate)
    {
        if (ActiveJudgment?.Id != pending.Id ||
            !ReferenceEquals(CurrentJudgmentCandidate(pending), candidate) ||
            GetJudgmentCard(pending) is not { } judgmentCard)
            throw new InvalidOperationException("The configured judgment replacement is not current.");
        var (program, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[candidate.OwnerSeat];
        var enabled = owner.IsAlive &&
            HasRuntimeSkillInstance(owner, program.Id, candidate.SkillInstanceId) &&
            MatchesProgramJudgmentReplacement(owner, trigger, pending.TargetSeat, pending.Reason);
        var replacementCards = enabled
            ? GetProgramJudgmentReplacementCards(
                owner, trigger, pending.TargetSeat, pending.Reason)
            : [];
        if (replacementCards.Count == 0)
        {
            AdvanceJudgmentCandidate(pending);
            return;
        }

        SetJudgmentFrameStep(pending.Id, ResolutionFrameStep.AwaitingResponse);
        AdvanceEventRulesAndQueueFact(new JudgmentReplacementRequestedEvent(
            pending.Id,
            pending.Id,
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
            AdvanceRulesAndPublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveProgramJudgmentReplacementChoice(PromptChoice selected)
    {
        var pending = ActiveJudgment ??
            throw new InvalidOperationException("The configured judgment replacement continuation is missing.");
        var candidate = CurrentJudgmentCandidate(pending) ??
            throw new InvalidOperationException("The configured judgment replacement candidate is missing.");
        var (program, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[candidate.OwnerSeat];
        var oldJudgmentCard = GetJudgmentCard(pending) ??
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
            AdvanceEventRulesAndQueueFact(new JudgmentReplacementResolvedEvent(
                pending.Id, pending.Id, pending.TargetSeat, owner.Seat, pending.Reason,
                Used: false, oldJudgmentCard.Id, null, null, null, null));
            AdvanceEventRulesAndQueueFact(new ProgramJudgmentReplacementResolvedEvent(
                pending.Id, program.Id, trigger.Id, owner.Seat, pending.TargetSeat,
                Activated: false, oldJudgmentCard.Id, null,
                trigger.Effects[0].OldCardDestination!.Value, 0, 0));
            AddLog("JudgmentReplacementSkipped",
                $"{owner.Name} 选择不发动【{_contentRegistry!.Skills[program.Id].Name}】。",
                owner.Seat, pending.TargetSeat);
            AdvanceJudgmentCandidate(pending);
            return;
        }

        var replacement = GetProgramJudgmentReplacementCards(
            owner, trigger, pending.TargetSeat, pending.Reason)
            .SingleOrDefault(card => card.Id == cardId) ??
            throw new InvalidOperationException("The selected configured replacement card is no longer legal.");
        var binding = EnabledUniqueProgramTriggers(owner, SkillProgramTriggerWindow.JudgmentReplacing)
            .Single(item => item.SkillId == program.Id && item.Trigger.Id == trigger.Id);
        var shared = new ProgramTriggerCandidate(owner.Seat, program.Id, trigger.Id,
            binding.SkillInstanceId, program.GameplayHash, trigger.Priority);
        var context = new ProgramSkillWindowContext(
            SkillProgramTriggerWindow.JudgmentReplacing, pending.Id, owner.Seat,
            SourceSeat: pending.SourceSeat, TargetSeat: pending.TargetSeat,
            Facts: CaptureProgramTriggerFacts(owner),
            JudgmentReplacement: new ProgramJudgmentReplacementContext(
                pending.Id, pending.TargetSeat, pending.Reason,
                oldJudgmentCard.Id, replacement.Id));
        BeginProgramBinding(shared, context);
    }

    private void ReplaceProgramJudgment(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var context = frame.WindowContext?.JudgmentReplacement ??
            throw new InvalidOperationException("Replacement primitive requires its frozen judgment choice.");
        var pending = ActiveJudgment ??
            throw new InvalidOperationException("Replacement primitive lost its judgment continuation.");
        if (pending.Id != context.JudgmentFrameId ||
            GetJudgmentCard(pending)?.Id != context.OldCardId ||
            pending.TargetSeat != context.SubjectSeat ||
            !_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Replacement primitive no longer matches its judgment.");
        var owner = _players[frame.OwnerSeat];
        var trigger = GetProgramTrigger(frame);
        var replacement = GetProgramJudgmentReplacementCards(
            owner, trigger, pending.TargetSeat, pending.Reason)
            .SingleOrDefault(card => card.Id == context.ReplacementCardId) ??
            throw new InvalidOperationException("The selected replacement card is no longer legal.");
        var old = GetJudgmentCard(pending) ??
            throw new InvalidOperationException("The replacement primitive lost its judgment card.");
        var committedSuit = EffectiveSuit(_players[pending.TargetSeat], replacement);
        var destination = effect.OldCardDestination ??
            throw new InvalidOperationException("Replacement has no old-card destination.");
        CommitProgramJudgmentReplacement(pending, owner, old, replacement, committedSuit, destination);
        var active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(active with { WindowContext = active.WindowContext! with
        {
            JudgmentReplacement = context with
            {
                ReplacementSuit = committedSuit,
                ReplacementRank = replacement.Rank
            }
        } });
        AdvanceEventRulesAndQueueFact(new JudgmentReplacementResolvedEvent(
            pending.Id, pending.Id, pending.TargetSeat, owner.Seat, pending.Reason,
            Used: true, old.Id, replacement.Id, replacement.Kind, committedSuit, replacement.Rank));
        AddLog("JudgmentReplaced",
            $"{owner.Name} 发动【{_contentRegistry!.Skills[frame.SkillId].Name}】替换了判定牌。",
            owner.Seat, pending.TargetSeat);
    }

    private void CompleteProgramJudgmentReplacementBinding(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext?.JudgmentReplacement ??
            throw new InvalidOperationException("Replacement program lost its frozen judgment context.");
        var pending = ActiveJudgment ??
            throw new InvalidOperationException("Replacement program lost its judgment continuation.");
        if (_resolutionStack.LastOrDefault() is not JudgmentFrame parent ||
            parent.Id != context.JudgmentFrameId || pending.Id != parent.Id)
            throw new InvalidOperationException("Replacement program did not return to its judgment frame.");
        var effect = GetProgramTrigger(frame).Effects[0];
        AdvanceEventRulesAndQueueFact(new ProgramJudgmentReplacementResolvedEvent(
            parent.Id, frame.SkillId, frame.TriggerId!, frame.OwnerSeat, context.SubjectSeat,
            Activated: context.ReplacementSuit is not null, context.OldCardId,
            context.ReplacementSuit is not null ? context.ReplacementCardId : null,
            effect.OldCardDestination!.Value, context.DrawnCards, context.RecoveredHp));
        AdvanceJudgmentCandidate(pending);
    }
    private void CommitProgramJudgmentReplacement(
        JudgmentFrame pending,
        CharacterState owner,
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
        var batch = BeginCardMovementBatch([oldFrom, replacementFrom, CardLocation.Processing], [oldTo, CardLocation.Processing, oldFrom]);
        var movements = new List<CardMovementRecord>(3);
        var committed = false;
        try
        {
            _cardZones.MoveBatch([
                new CardTransfer(oldJudgmentCard.Id, oldFrom, oldTo),
                new CardTransfer(replacement.Id, replacementFrom, CardLocation.Processing)
            ]);
            _cardZones.Move(replacement.Id, CardLocation.Processing, oldFrom);
            ReplaceJudgmentFrame(GetJudgmentFrame(pending.Id) with
            {
                CardId = replacement.Id,
                CardKind = replacement.Kind,
                Suit = committedSuit
            });

            movements.Add(RecordMovement(
                oldJudgmentCard, oldFrom, oldTo, CardMoveReasons.ProgramJudgmentOldCard));
            ClearJudgmentEffectiveKindAfterMove(oldJudgmentCard, oldFrom, oldTo);
            movements.Add(RecordMovement(
                replacement, replacementFrom, CardLocation.Processing,
                CardMoveReasons.ProgramJudgmentReplace));
            ClearJudgmentEffectiveKindAfterMove(replacement, replacementFrom, CardLocation.Processing);
            ResolveSilverLionRemoval(replacement, replacementFrom, CardMoveReasons.ProgramJudgmentReplace);
            ResolveWoodenOxMove(replacement, replacementFrom, CardLocation.Processing);
            movements.Add(RecordMovement(
                replacement, CardLocation.Processing, oldFrom,
                CardMoveReasons.ProgramJudgmentReplace));
            ClearJudgmentEffectiveKindAfterMove(replacement, CardLocation.Processing, oldFrom);
            committed = true;
        }
        finally
        {
            CompleteCardMovementBatch(batch, movements, committed);
        }
    }

    private bool IsAiProgramJudgmentReplacementPending() =>
        _pendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: var playerSeat } &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiProgramJudgmentReplacement()
    {
        var pending = ActiveJudgment ??
            throw new InvalidOperationException("AI configured judgment replacement is missing.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI configured judgment replacement prompt is missing.");
        var candidate = CurrentJudgmentCandidate(pending) ??
            throw new InvalidOperationException("AI configured judgment candidate is missing.");
        var (_, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[candidate.OwnerSeat];
        var judgmentCard = GetJudgmentCard(pending) ??
            throw new InvalidOperationException("AI configured judgment replacement lost its current card.");
        var parent = _resolutionStack.OfType<ProgramSkillFrame>()
            .LastOrDefault(frame => frame.Id == pending.ParentFrameId);
        var successSuits = parent is null ? null :
            _contentRegistry.GetSkill(parent.SkillId).Program?.Triggers
                .SingleOrDefault(item => item.Id == parent.TriggerId)?.Effects
                .Select(effect => effect.Condition)
                .FirstOrDefault(condition => condition.Kind == SkillProgramConditionKind.BoundCardsMatchSuits &&
                    condition.SourceBind == pending.ProgramResultBind)?.Suits;
        var (cardId, thought) = _aiBrains[owner.Seat].ChooseJudgmentReplacement(
            CreateSnapshot(owner.Seat), pending.TargetSeat, pending.Reason, decision.ValidCardIds,
            judgmentCard.Kind, EffectiveSuit(_players[pending.TargetSeat], judgmentCard),
            judgmentCard.Rank, successSuits, ++_thoughtSequence);
        AddThought(thought);
        var selected = cardId is { } id
            ? decision.Choices.Single(choice => choice.Cards.SequenceEqual([id]))
            : trigger.Optional
                ? decision.Choices.Single(choice => choice.Cards.Count == 0)
                : decision.Choices.First(choice => choice.Cards.Count == 1);
        ResolveProgramJudgmentReplacementChoice(selected);
        AdvanceRulesAndPublishState();
    }
}
