namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanRunDyingOwnedCard(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        var effect = trigger.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.SelectDyingOwnedCard);
        if (effect is null) return true;
        if (context.Window != SkillProgramTriggerWindow.DyingEntering || context.TargetSeat is not { } victim || !IsValidPlayerSeat(victim) ||
            !_players[victim].IsAlive || _players[victim].Hp > 0) return false;
        return effect.Zones.Any(zone => _cardZones.CardsAt(new(zone, victim)).Any(c =>
            !IsForeignEquipmentDiscardPrevented(candidate.OwnerSeat, c, new(zone, victim), OwnedCardMoveIntent.Discard) &&
            !(zone == CardZoneKind.Equipment && victim == candidate.OwnerSeat && IsActiveProgramSourceEquipmentCard(victim, candidate.SkillId, candidate.SkillInstanceId, c))));
    }
    private bool ExactDyingOwnedCardEntry(ProgramSkillFrame root, out DyingFrame dying, out ProgramLifecycleTriggerWindowFrame entry)
    {
        dying = null!; entry = null!;
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index < 2 || _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame e || e.Window != SkillProgramTriggerWindow.DyingEntering ||
            e.Continuation != ProgramLifecycleContinuation.ResumeDyingEntry || _resolutionStack[index - 2] is not DyingFrame d ||
            e.ResumeDyingFrameId != d.Id || e.OwnerSeat != d.VictimSeat || root.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering } context ||
            context.ParentFrameId != e.Id || context.OwnerSeat != root.OwnerSeat || context.TargetSeat != d.VictimSeat || e.CandidateIndex < 0 || e.CandidateIndex >= e.Candidates.Count ||
            !MountObserverCandidateMatches(root, e.Candidates[e.CandidateIndex])) return false;
        dying = d; entry = e; return true;
    }
    private SkillProgramStepOutcome SelectDyingOwnedCard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.InstructionIndex != 1 || frame.DyingOwnedCard is not null || !ExactDyingOwnedCardEntry(frame, out var dying, out _))
            throw new InvalidOperationException("Dying card selection requires its exact entry candidate and victim.");
        var choices = DyingOwnedCardChoices(frame, effect, dying.VictimSeat);
        if (choices.Count == 0) { CancelProgramBindingAndCleanup(frame, "濒死角色没有可选择的区域牌。"); return SkillProgramStepOutcome.AwaitChild; }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat, "请选择濒死角色的一张牌；他人暗手牌仅显示牌位。",
            choices.SelectMany(c => c.Cards).Distinct().Order().ToArray(), [], SourceSeat: frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = dying.VictimSeat, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DyingOwnedCardChoices(ProgramSkillFrame root, SkillProgramEffect effect, int victim) =>
        Array.AsReadOnly(BuildOwnedCardPaymentChoices(root.Id, root.OwnerSeat, victim, effect.Zones, OwnedCardMoveIntent.Discard)
            .Select(c => c with { Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(c.Parameters) { ["program-action"] = "dying-owned-card" }) }).ToArray());

    private void ResolveDyingOwnedCardChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.InstructionIndex != 1 || frame.DyingOwnedCard is not null ||
            !ExactDyingOwnedCardEntry(frame, out var dying, out var entry) || _pendingDecision?.PlayerSeat != frame.OwnerSeat)
            throw new InvalidOperationException("A dying card choice lost its exact entry and owner.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.SelectDyingOwnedCard || choice.Targets.Count != 0 ||
            !Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"), out var zone) || !effect.Zones.Contains(zone) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot) ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            choice.Parameters.GetValueOrDefault("card-owner-seat") != dying.VictimSeat.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The dying card choice changed its published zone or slot.");
        var expected = DyingOwnedCardChoices(frame, effect, dying.VictimSeat).SingleOrDefault(c => c.Id == choice.Id);
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[dying.VictimSeat].IsAlive || _players[dying.VictimSeat].Hp > 0 ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) || expected is null || !expected.Cards.SequenceEqual(choice.Cards))
        { CancelProgramBindingAndCleanup(frame, "濒死状态、牌位或来源实例失效，未移动任何牌。"); return; }
        var location = new CardLocation(zone, dying.VictimSeat); var cards = _cardZones.CardsAt(location);
        if (slot < 0 || slot >= cards.Count) { CancelProgramBindingAndCleanup(frame, "濒死牌位已失效。"); return; }
        var card = cards[slot]; var nonBasic = !MatchesProgramCardCategory(card.Kind, [SkillProgramCardCategory.Basic]);
        var before = _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
        ReplaceRuntimeTop(frame = frame with { SelectedTargetSeats = Array.AsReadOnly(new[] { dying.VictimSeat }),
            DyingOwnedCard = new(0, dying.Id, entry.Id, dying.VictimSeat, effect.ResultBind!, nonBasic, nonBasic ? card.Id : null,
                nonBasic ? location : null, before) });
        // Basic cards never enter a binding, public reveal fact, label or receipt identity.
        // Nonbasic identities remain private until the following real discard commits.
        SetProgramCardSet(frame.Id, effect.ResultBind!, nonBasic ? [card.Id] : [], SkillProgramCardSetVisibility.Private,
            nonBasic ? [location] : [], selectionActorSeat: dying.VictimSeat);
        AdvanceEventRulesAndQueueFact(new ProgramDyingCardSelectionResolvedEvent(frame.Id, dying.Id, frame.OwnerSeat, dying.VictimSeat, nonBasic));
        AdvanceRuntimeProgram(frame.Id);
    }
    private void AssertDyingOwnedCardReceipt(ProgramSkillFrame root)
    {
        if (root.DyingOwnedCard is not { } r) return;
        if (!ExactDyingOwnedCardEntry(root, out var dying, out var entry) || r.InstructionIndex != 0 || r.DyingFrameId != dying.Id ||
            r.EntryFrameId != entry.Id || r.VictimSeat != dying.VictimSeat || root.InstructionIndex is < 1 or > 4 ||
            root.SelectedTargetSeats is not [var target] || target != dying.VictimSeat || r.MovementSequenceBefore < 0 ||
            (r.NonBasic ? r.PaidCardId is null || r.SourceLocation is null : r.PaidCardId is not null || r.SourceLocation is not null) ||
            r.SourceLocation is { } source && (source.OwnerSeat != r.VictimSeat || source.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)) ||
            !CompleteProgramEventHistory().OfType<ProgramDyingCardSelectionResolvedEvent>().Any(e => e.FrameId == root.Id && e.DyingFrameId == r.DyingFrameId &&
                e.OwnerSeat == root.OwnerSeat && e.VictimSeat == r.VictimSeat && e.NonBasic == r.NonBasic))
            throw new InvalidOperationException("The dying card receipt lost its exact victim or private selection.");
        var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
        if (plan.Instructions.Count != 4 || plan.Instructions[0].Op != SkillProgramEffectOp.SelectDyingOwnedCard || plan.Instructions[0].ResultBind != r.ResultBind ||
            root.CardSetBindings.SingleOrDefault(b => b.Name == r.ResultBind) is not { Visibility: SkillProgramCardSetVisibility.Private } bound ||
            bound.SelectionActorSeat != r.VictimSeat ||
            (r.NonBasic ? bound.CardIds is not [var id] || id != r.PaidCardId || bound.SourceLocations is not [var from] || from != r.SourceLocation
                : bound.CardIds.Count != 0 || bound.SourceLocations.Count != 0))
            throw new InvalidOperationException("The dying selected card binding lost its original identity or empty Basic result.");
    }
    private bool IsValidDyingOwnedCardProgramSelection(ProgramSkillFrame frame, bool awaitingSelection)
    {
        if (!IsValidPlayerSeat(frame.OwnerSeat) || !ExactDyingOwnedCardEntry(frame, out var dying, out _) ||
            !IsValidPlayerSeat(dying.VictimSeat)) return false;
        var program = _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program;
        if (program is null || program.GameplayHash != frame.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        if (plan.Instructions.Count != 4 || plan.Instructions[0].Op != SkillProgramEffectOp.SelectDyingOwnedCard)
            return false;
        if (frame.DyingOwnedCard is not null)
        {
            // Payment children may already have recovered this original victim.
            // The issued receipt retains the entry and selection without repricing HP.
            AssertDyingOwnedCardReceipt(frame);
            return true;
        }
        if (!awaitingSelection || frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 ||
            frame.SelectedTargetSeats.Count != 0 || !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || !_players[dying.VictimSeat].IsAlive ||
            _players[dying.VictimSeat].Hp > 0 ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != frame.OwnerSeat || prompt.SourceSeat != frame.OwnerSeat || prompt.TargetSeat != dying.VictimSeat ||
            prompt.SkillPrompt?.SkillId != frame.SkillId || prompt.ValidTargetSeats.Count != 0)
            return false;
        var choices = DyingOwnedCardChoices(frame, plan.Instructions[0], dying.VictimSeat);
        return choices.Count > 0 && prompt.Choices.Count == choices.Count &&
            prompt.ValidCardIds.SequenceEqual(choices.SelectMany(choice => choice.Cards).Distinct().Order()) &&
            prompt.Choices.Zip(choices).All(pair => pair.First.Id == pair.Second.Id &&
                pair.First.Description == pair.Second.Description && pair.First.Cards.SequenceEqual(pair.Second.Cards) &&
                pair.First.Targets.SequenceEqual(pair.Second.Targets) && pair.First.ContentIds.SequenceEqual(pair.Second.ContentIds) &&
                pair.First.Parameters.Count == pair.Second.Parameters.Count &&
                pair.Second.Parameters.All(parameter => pair.First.Parameters.GetValueOrDefault(parameter.Key) == parameter.Value));
    }
    private PromptChoice SelectAiDyingOwnedCard(PendingDecision decision) => decision.Choices
        .OrderBy(c => c.Cards.Count == 1 && _cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Any(card => card.Id == c.Cards[0] &&
            !MatchesProgramCardCategory(card.Kind, [SkillProgramCardCategory.Basic])) ? 0 : 1)
        .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    private sealed partial class ProgramSkillHost : IDyingOwnedCardProgramHost
    {
        public SkillProgramStepOutcome SelectDyingOwnedCard(ProgramSkillFrame f, SkillProgramEffect e) => engine.SelectDyingOwnedCard(f, e);
    }
}
