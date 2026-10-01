namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The owning use records actual damage participants, including redirected damage.

    private void RecordCompletedCardDamageParticipant(long cardUseFrameId, int targetSeat)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(item => item.Id == cardUseFrameId);
        if (use?.Action?.ConversionChain.FirstOrDefault() is not { } conversion ||
            _contentRegistry.GetSkill(conversion.SkillId).Program?.Triggers.Any(trigger =>
                trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DrawCompletedCardParticipants)) != true) return;
        if (!use.CompletedDamageParticipants.Contains(targetSeat))
            ReplaceRuntimeFrame(use.Id, use with
            { CompletedDamageParticipants = Array.AsReadOnly(use.CompletedDamageParticipants.Append(targetSeat).ToArray()) });
    }

    private void DrawProgramCompletedCardParticipants(ProgramSkillFrame frame, string stateId, int threshold)
    {
        var use = frame.WindowContext?.CardUse ?? throw new InvalidOperationException("Completed-card participant draws require a card-use context.");
        var owner = _players[frame.OwnerSeat];
        var participants = (LifecycleCardUse(use.ParentCardUseFrameId) ?? throw new InvalidOperationException("The participant draw lost its card-use owner.")).CompletedDamageParticipants.Order().ToArray();
        var ownerDrawn = 0;
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.completed-participant-draw");
        if (owner.IsAlive) ownerDrawn += DrawCards(owner, 1, true, reason).Count;
        foreach (var seat in participants)
            if (_players[seat].IsAlive)
            {
                var drawn = DrawCards(_players[seat], 1, true, reason).Count;
                if (seat == owner.Seat) ownerDrawn += drawn;
            }
        var key = (frame.OwnerSeat, frame.SkillId, "participant-draw-count:" + stateId);
        var count = checked(_programPhaseUses.GetValueOrDefault(key) + ownerDrawn);
        _programPhaseUses[key] = count;
        if (count >= threshold) SetProgramBooleanState(frame, stateId, true);
        UpdateLifecycleCardUse(use.ParentCardUseFrameId, parent => parent with { CompletedDamageParticipants = [] });
    }

    private SkillProgramStepOutcome RecastProgramSelectedCards(ProgramSkillFrame frame, string stateId, int threshold)
    {
        if (frame.TriggerId is not null || frame.SelectedCardIds is not [var id] ||
            _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat))
            throw new InvalidOperationException("Recasting requires one selected physical owner hand card.");
        var owner = _players[frame.OwnerSeat];
        var card = GetHand(owner).Single(item => item.Id == id);
        MoveCard(card, CardLocation.Hand(owner.Seat), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        var drawn = DrawCards(owner, 1, true, CardMoveReasons.RecastDraw);
        AdvanceEventRulesAndQueueFact(new CardRecastEvent(owner.Seat, card.Id, card.Kind, drawn.Count));
        var key = (frame.OwnerSeat, frame.SkillId, "recast-card-count:" + stateId);
        var count = checked(_programPhaseUses.GetValueOrDefault(key) + 1);
        _programPhaseUses[key] = count;
        if (count >= threshold) SetProgramBooleanState(frame, stateId, true);
        return SkillProgramStepOutcome.Continue;
    }

    private IReadOnlyList<PromptChoice> PairedHandRevealChoices(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var target = _players[frame.SelectedTargetSeats.Single()];
        return GetHand(target).Select(card => new PromptChoice(new ChoiceId("paired-hand-reveal." + card.Id),
            "确定同时展示的手牌：" + card.DisplayName, [card.Id], [target.Seat], new Dictionary<string, string>
            {
                ["program-action"] = "paired-hand-reveal", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["result-bind"] = effect.ResultBind!
            })).ToArray();
    }

    private SkillProgramStepOutcome RevealProgramSelectedHandAgainstTarget(ProgramSkillFrame frame, string resultBind)
    {
        if (frame.TriggerId is not null || frame.SelectedCardIds is not [var ownerCard] ||
            frame.SelectedTargetSeats is not [var targetSeat] || targetSeat == frame.OwnerSeat ||
            _cardZones.GetLocation(ownerCard) != CardLocation.Hand(frame.OwnerSeat) ||
            !_players[targetSeat].IsAlive || GetHand(_players[targetSeat]).Count == 0)
            throw new InvalidOperationException("Paired hand reveal requires one physical owner card and another living nonempty hand.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, targetSeat, "双方同时展示：秘密确定一张手牌。", [], [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = targetSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description), Choices = PairedHandRevealChoices(frame, effect)
        };
        _status = _players[targetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolvePairedHandRevealChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Paired reveal lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var canonical = PairedHandRevealChoices(frame, effect).SingleOrDefault(choice => choice.Id == selected.Id);
        if (effect.Op != SkillProgramEffectOp.RevealSelectedHandAgainstTarget || canonical is null ||
            !AssistedChoicesEqual([canonical], [selected]) || frame.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind))
            throw new InvalidOperationException("Paired reveal choice changed its physical card, target or result binding.");
        var owner = _players[frame.OwnerSeat];
        var target = _players[frame.SelectedTargetSeats.Single()];
        var ownerCard = GetHand(owner).Single(card => card.Id == frame.SelectedCardIds.Single());
        var targetCard = GetHand(target).Single(card => card.Id == canonical.Cards.Single());
        var ownerKind = GetProgramCardIdentityMatches(owner, ownerCard).FirstOrDefault()?.Identity.OutputKind ?? ownerCard.Kind;
        var targetKind = GetProgramCardIdentityMatches(target, targetCard).FirstOrDefault()?.Identity.OutputKind ?? targetCard.Kind;
        var outcome = IsSlashCard(ownerKind) && targetKind != CardKind.Dodge ? "damage" :
            !IsSlashCard(ownerKind) && targetKind == CardKind.Dodge ? "obtain" : "none";
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame), owner.Seat,
            effect.ResultBind!, [ToSnapshot(ownerCard), ToSnapshot(targetCard)]));
        CommitProgramChoiceResult(frame.Id, effect.ResultBind!, outcome, target.Seat, "双方同时公开实体手牌。");
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertPairedHandRevealChoice(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.RevealSelectedHandAgainstTarget ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) || frame.ChoiceBindings.Any(binding => binding.Name == paused.ResultBind)) return;
        if (frame.TriggerId is not null || frame.SelectedCardIds is not [var ownerCard] ||
            frame.SelectedTargetSeats is not [var target] || _cardZones.GetLocation(ownerCard) != CardLocation.Hand(frame.OwnerSeat) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != target ||
            !AssistedChoicesEqual(prompt.Choices, PairedHandRevealChoices(frame, paused)))
            throw new InvalidOperationException("Paired reveal lost its private physical hand choice.");
    }

    private sealed partial class ProgramSkillHost : IRevealRecastProgramHost
    {
        public SkillProgramStepOutcome RecastSelectedCards(ProgramSkillFrame frame, string stateId, int threshold) => engine.RecastProgramSelectedCards(frame, stateId, threshold);
        public void DrawCompletedCardParticipants(ProgramSkillFrame frame, string stateId, int threshold) => engine.DrawProgramCompletedCardParticipants(frame, stateId, threshold);
        public SkillProgramStepOutcome RevealSelectedHandAgainstTarget(ProgramSkillFrame frame, string resultBind) => engine.RevealProgramSelectedHandAgainstTarget(frame, resultBind);
    }
}
