namespace CardGame.Core;

public sealed record ProgramLiejieCostColorFrozenEvent(long FrameId, string SourceBind,
    string SkillInstanceId, string GameplayHash, int Count, int RedCount, long BeforeMoveSequence) : IGameEvent;
public sealed record ProgramLiejieSelectionStartedEvent(long FrameId, int InstructionIndex,
    long ParentFrameId, int SourceSeat, int RedCount, int MaximumCount) : IGameEvent;
public sealed record ProgramLiejieSourceDiscardResolvedEvent(long FrameId, int Count) : IGameEvent;

public sealed record LiejieDiscardPendingState(int SourceSeat, int MaximumCount)
{
    public int InstructionIndex { get; init; }
    public long ParentFrameId { get; init; }
    public int FrozenRedCount { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? BatchId { get; init; }
    private IReadOnlyList<int> _candidateIds = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _candidateLocations = Array.AsReadOnly(Array.Empty<CardLocation>());
    private IReadOnlyList<int> _selectedIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> CandidateIds { get => _candidateIds; init => _candidateIds = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> CandidateLocations { get => _candidateLocations; init => _candidateLocations = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedIds { get => _selectedIds; init => _selectedIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial class GameEngine
{
    private long LiejieMoveSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static string LiejieDiscardReason(ProgramSkillFrame frame) => $"skill-program.{frame.SkillId}.liejie-discard";

    private bool IsLiejieDiscardDestination(CardMovementRecord movement) =>
        movement.To == CardLocation.DiscardPile || movement.From.Zone == CardZoneKind.Equipment &&
        movement.To == CardLocation.OutsideGame && GetAttackCard(movement.CardId).IsGeneralWeapon;

    private bool IsAtomicLiejieCostMove(ProgramSkillFrame frame, string sourceBind)
    {
        if (frame.TriggerId is null) return false;
        var trigger = GetProgramTrigger(frame);
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.LiejieSourceDiscard && e.SourceBind == sourceBind)) return false;
        LiejieSourceDiscardContract.ValidateTrigger(frame.SkillId, trigger);
        var binding = frame.CardSetBindings.Single(b => b.Name == sourceBind);
        return frame.InstructionIndex == 2 && ExactLiejieParent(frame) &&
            CompleteProgramEventHistory().OfType<ProgramLiejieCostColorFrozenEvent>().Count(e => e.FrameId == frame.Id &&
                e.SourceBind == sourceBind && e.SkillInstanceId == frame.SkillInstanceId && e.GameplayHash == frame.GameplayHash &&
                e.BeforeMoveSequence == LiejieMoveSequence && e.Count == binding.CardIds.Count) == 1;
    }

    private void FreezeLiejieDiscardColors(ProgramSkillFrame frame, string sourceBind,
        IReadOnlyList<int> selectedIds, IReadOnlyList<CardLocation> selectedLocations)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (!plan.Instructions.Any(e => e.Op == SkillProgramEffectOp.LiejieSourceDiscard && e.SourceBind == sourceBind)) return;
        LiejieSourceDiscardContract.ValidateTrigger(frame.SkillId, plan.Trigger!);
        var bound = frame.CardSetBindings.SingleOrDefault(b => b.Name == sourceBind);
        if (frame.InstructionIndex != 2 || !ExactLiejieParent(frame) || bound is null ||
            !selectedIds.SequenceEqual(bound.CardIds) || !selectedLocations.SequenceEqual(bound.SourceLocations) ||
            selectedIds.Count is < 1 or > 3 || selectedLocations.Count != selectedIds.Count ||
            selectedIds.Distinct().Count() != selectedIds.Count ||
            selectedLocations.Any(l => l.OwnerSeat != frame.OwnerSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            CompleteProgramEventHistory().OfType<ProgramLiejieCostColorFrozenEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Liejie colors require the exact unpaid owner HE binding.");
        var red = selectedIds.Select((id, i) =>
        {
            if (_cardZones.GetLocation(id) != selectedLocations[i]) throw new InvalidOperationException("Liejie cost left its original HE source.");
            return IsRedSuit(GetProgramEffectiveSuit(_players[frame.OwnerSeat],
                _cardZones.CardsAt(selectedLocations[i]).Single(c => c.Id == id)));
        }).Count(value => value);
        AdvanceEventRulesAndQueueFact(new ProgramLiejieCostColorFrozenEvent(frame.Id, sourceBind,
            frame.SkillInstanceId, frame.GameplayHash, selectedIds.Count, red, LiejieMoveSequence));
    }

    private bool ExactLiejieParent(ProgramSkillFrame frame)
    {
        if (frame.TriggerId is null || frame.WindowContext is not
            { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } context ||
            context.OwnerSeat != frame.OwnerSeat || context.TargetSeat != frame.OwnerSeat ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            frame.GameplayHash != _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index <= 0 || _resolutionStack[index - 1] is not DamageTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.TriggerWindow != context.Window ||
            parent.TargetSeat != frame.OwnerSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(f => f.Id == parent.ParentFrameId) is not { } damage ||
            damage.Id != context.DamageFrameId || damage.SourceSeat != parent.SourceSeat ||
            damage.TargetSeat != frame.OwnerSeat || damage.Amount != context.Amount) return false;
        // Native children may own the current attack. The original applied damage
        // stays tied to its immutable requested fact and live outer damage frame.
        var requests = CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Where(e => e.ResolutionId == damage.Id).ToArray();
        if (requests is not [var request] || request.SourceSeat != damage.SourceSeat || request.TargetSeat != damage.TargetSeat ||
            request.Amount != damage.Amount || request.Nature != damage.Nature ||
            context.SourceSeat != (request.SourceLess ? null : damage.SourceSeat)) return false;
        return CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id &&
            e.SkillId == frame.SkillId && e.BindingId == frame.TriggerId && e.SkillInstanceId == frame.SkillInstanceId &&
            e.OwnerSeat == frame.OwnerSeat && e.Window == context.Window) == 1;
    }

    private ProgramLiejieCostColorFrozenEvent ExactLiejiePaidColors(ProgramSkillFrame frame, string bind)
    {
        var bound = frame.CardSetBindings.Single(b => b.Name == bind);
        var facts = CompleteProgramEventHistory().OfType<ProgramLiejieCostColorFrozenEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (facts is not [var colors] || colors.SourceBind != bind || colors.SkillInstanceId != frame.SkillInstanceId ||
            colors.GameplayHash != frame.GameplayHash || colors.Count != bound.CardIds.Count || colors.Count is < 1 or > 3 ||
            colors.RedCount < 0 || colors.RedCount > colors.Count || bound.SourceLocations.Count != colors.Count)
            throw new InvalidOperationException("Liejie lost its frozen first-clause color receipt.");
        var moves = _cardMovements.Where(m => m.Sequence > colors.BeforeMoveSequence &&
            m.Reason.Value == $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}" && bound.CardIds.Contains(m.CardId)).ToArray();
        if (moves.Length != colors.Count || moves.Select(m => m.CardId).Distinct().Count() != colors.Count ||
            moves.Any(m => m.From != bound.SourceLocations[bound.CardIds.ToList().IndexOf(m.CardId)] ||
                m.From.OwnerSeat != frame.OwnerSeat || !IsLiejieDiscardDestination(m)))
            throw new InvalidOperationException("Liejie colors require its completed original HE discard invoice.");
        return colors;
    }

    private SkillProgramStepOutcome BeginLiejieSourceDiscard(ProgramSkillFrame input, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (!ExactLiejieParent(frame) || frame.InstructionIndex != 4 || effect.Op != SkillProgramEffectOp.LiejieSourceDiscard ||
            frame.LiejieDiscardPending is not null || CompleteProgramEventHistory().OfType<ProgramLiejieSelectionStartedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Liejie requires its exact unpaid damage-source selection.");
        var colors = ExactLiejiePaidColors(frame, effect.SourceBind!);
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) || colors.RedCount == 0 ||
            frame.WindowContext!.SourceSeat is not { } source || source == frame.OwnerSeat || !_players[source].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var cards = BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, source,
            [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => ResolveLiejieSlot(source, c)).ToArray();
        if (cards.Length == 0) return SkillProgramStepOutcome.Continue;
        var draft = new LiejieDiscardPendingState(source, Math.Min(colors.RedCount, cards.Length))
        {
            InstructionIndex = frame.InstructionIndex, ParentFrameId = frame.WindowContext.ParentFrameId,
            FrozenRedCount = colors.RedCount, CandidateIds = cards.Select(c => c.Id).ToArray(),
            CandidateLocations = cards.Select(c => _cardZones.GetLocation(c.Id)).ToArray()
        };
        ReplaceRuntimeTop(frame = frame with { LiejieDiscardPending = draft });
        AdvanceEventRulesAndQueueFact(new ProgramLiejieSelectionStartedEvent(frame.Id, draft.InstructionIndex,
            draft.ParentFrameId, source, draft.FrozenRedCount, draft.MaximumCount));
        PublishLiejieDiscard(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private Card ResolveLiejieSlot(int source, PromptChoice choice)
    {
        if (choice.Parameters.GetValueOrDefault("card-owner-seat") != source.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("Liejie source slot lost its actual HE owner.");
        var cards = zone == CardZoneKind.Hand ? GetHand(_players[source]) : GetEquipment(_players[source]);
        if (slot < 0 || slot >= cards.Count) throw new InvalidOperationException("Liejie source slot is no longer available.");
        return cards[slot];
    }

    private IReadOnlyList<PromptChoice> LiejieDiscardChoices(ProgramSkillFrame frame)
    {
        var draft = frame.LiejieDiscardPending!;
        var choices = BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, draft.SourceSeat,
            [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Where(c => { var id = ResolveLiejieSlot(draft.SourceSeat, c).Id;
                return draft.CandidateIds.Contains(id) && !draft.SelectedIds.Contains(id); })
            .Select(c => c with { Id = new($"liejie-pick.{frame.Id}.{draft.SelectedIds.Count}.{c.Id.Value}"),
                Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                    c.Parameters.ToDictionary(k => k.Key, v => v.Key == "program-action" ? "liejie-pick" : v.Value)) }).ToList();
        choices.Add(new(new($"liejie-finish.{frame.Id}.{draft.SelectedIds.Count}"),
            $"完成选择（弃置{draft.SelectedIds.Count}张）。", [], [],
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            { ["program-action"] = draft.SelectedIds.Count == 0 ? "liejie-decline" : "liejie-finish",
              ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) })));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishLiejieDiscard(ProgramSkillFrame frame)
    {
        var choices = LiejieDiscardChoices(frame); var draft = frame.LiejieDiscardPending!;
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            $"【{skill.Name}】弃置伤害来源至多{draft.MaximumCount}张牌（已选{draft.SelectedIds.Count}张）。",
            choices.SelectMany(c => c.Cards).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.SourceSeat,
          SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 弃置伤害来源", skill.Description), Choices = choices };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveLiejieDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Liejie lost its program frame.");
        AssertLiejieSourceDiscard(frame, ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect);
        var draft = frame.LiejieDiscardPending!;
        if (draft.Paid || _pendingDecision is not { PlayerSeat: var chooser, IsPrivate: true } || chooser != frame.OwnerSeat ||
            !LiejieDiscardChoices(frame).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) &&
                c.Parameters.OrderBy(p => p.Key).SequenceEqual(selected.Parameters.OrderBy(p => p.Key))))
            throw new InvalidOperationException("Liejie requires its exact published private HE choice.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.SourceSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("The unpaid Liejie participants or source skill changed.");
        ClearPendingDecision();
        if (selected.Parameters["program-action"] == "liejie-pick")
        {
            var card = ResolveLiejieSlot(draft.SourceSeat, selected);
            draft = draft with { SelectedIds = draft.SelectedIds.Append(card.Id).ToArray() };
            ReplaceRuntimeTop(frame = frame with { LiejieDiscardPending = draft });
            if (draft.SelectedIds.Count < draft.MaximumCount) { PublishLiejieDiscard(frame); return; }
        }
        if (draft.SelectedIds.Count == 0) { CompleteLiejieSourceDiscard(frame, 0); return; }
        var ids = draft.SelectedIds.ToArray();
        if (ids.Any(id => !draft.CandidateIds.Contains(id) ||
            _cardZones.GetLocation(id) != draft.CandidateLocations[draft.CandidateIds.ToList().IndexOf(id)] ||
            IsForeignEquipmentDiscardPrevented(frame.OwnerSeat,
                _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id), _cardZones.GetLocation(id), OwnedCardMoveIntent.Discard)))
            throw new InvalidOperationException("Liejie selected HE payment is no longer legal.");
        var before = LiejieMoveSequence;
        ReplaceRuntimeTop(frame with { LiejieDiscardPending = draft with { Paid = true, Before = before, After = before },
            PendingMovementContinuation = new(draft.SourceSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(LiejieDiscardReason(frame)), (batch, records) =>
        {
            var active = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(active with { LiejieDiscardPending = active.LiejieDiscardPending! with { BatchId = batch, After = LiejieMoveSequence } });
            AdvanceEventRulesAndQueueFact(new ProgramLiejieSourceDiscardEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame),
                frame.OwnerSeat, draft.SourceSeat, records.Count, Array.AsReadOnly(ids)));
        });
        AdvanceRuntimeProgram(frame.Id);
    }

    private void CompleteLiejieSourceDiscard(ProgramSkillFrame frame, int count)
    {
        AdvanceEventRulesAndQueueFact(new ProgramLiejieSourceDiscardResolvedEvent(frame.Id, count));
        ReplaceRuntimeTop(frame = frame with { LiejieDiscardPending = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true);
    }

    private bool ResumeLiejieSourceDiscard(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id || frame.LiejieDiscardPending is not { Paid: true }) return false;
        AssertLiejieSourceDiscard(frame, ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        CompleteLiejieSourceDiscard(frame, frame.LiejieDiscardPending.SelectedIds.Count);
        return true;
    }
    private bool ReturnLiejieSourceDiscardMovement(ProgramSkillFrame frame)
    {
        if (frame.LiejieDiscardPending is not { Paid: true }) return false;
        AdvanceRuntimeProgram(frame.Id); return true;
    }

    private void AssertLiejieSourceDiscard(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var starts = history.OfType<ProgramLiejieSelectionStartedEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (frame.LiejieDiscardPending is not { } draft)
        {
            if (starts.Length > 0 && !history.OfType<ProgramLiejieSourceDiscardResolvedEvent>().Any(e => e.FrameId == frame.Id))
                throw new InvalidOperationException("An unfinished Liejie source discard lost its owning receipt.");
            return;
        }
        var colors = ExactLiejiePaidColors(frame, paused.SourceBind!);
        if (paused.Op != SkillProgramEffectOp.LiejieSourceDiscard || frame.InstructionIndex != 4 || !ExactLiejieParent(frame) ||
            draft.InstructionIndex != frame.InstructionIndex || draft.ParentFrameId != frame.WindowContext!.ParentFrameId ||
            draft.SourceSeat != frame.WindowContext.SourceSeat || draft.SourceSeat == frame.OwnerSeat ||
            draft.FrozenRedCount != colors.RedCount || draft.MaximumCount is < 1 or > 3 ||
            draft.MaximumCount != Math.Min(draft.FrozenRedCount, draft.CandidateIds.Count) ||
            draft.CandidateIds.Count != draft.CandidateLocations.Count || draft.CandidateIds.Distinct().Count() != draft.CandidateIds.Count ||
            draft.CandidateLocations.Any(l => l.OwnerSeat != draft.SourceSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            draft.SelectedIds.Count > draft.MaximumCount || draft.SelectedIds.Distinct().Count() != draft.SelectedIds.Count ||
            draft.SelectedIds.Any(id => !draft.CandidateIds.Contains(id)) || starts is not [var started] ||
            started != new ProgramLiejieSelectionStartedEvent(frame.Id, draft.InstructionIndex, draft.ParentFrameId,
                draft.SourceSeat, draft.FrozenRedCount, draft.MaximumCount) ||
            history.OfType<ProgramLiejieSourceDiscardResolvedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Liejie source discard lost its exact owning instruction or frozen quota.");
        if (!draft.Paid)
        {
            if (frame.PendingMovementContinuation is not null || draft.Before != 0 || draft.After != 0 || draft.BatchId is not null ||
                draft.CandidateIds.Where((id, i) => _cardZones.GetLocation(id) != draft.CandidateLocations[i]).Any() ||
                history.OfType<ProgramLiejieSourceDiscardEvent>().Any(e => e.FrameId == frame.Id))
                throw new InvalidOperationException("Unpaid Liejie candidates changed before selection completed.");
            if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
                 !prompt.Choices.Select(c => c.Id).SequenceEqual(LiejieDiscardChoices(frame).Select(c => c.Id))))
                throw new InvalidOperationException("Liejie lost its exact private chooser prompt.");
            return;
        }
        var moves = _cardMovements.Where(m => m.Sequence > draft.Before && m.Sequence <= draft.After && m.Reason.Value == LiejieDiscardReason(frame)).ToArray();
        if (draft.SelectedIds.Count == 0 || draft.BatchId is null || draft.After <= draft.Before || draft.After > LiejieMoveSequence ||
            frame.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != draft.SourceSeat ||
            moves.Length != draft.SelectedIds.Count || !moves.Select(m => m.CardId).Order().SequenceEqual(draft.SelectedIds.Order()) ||
            moves.Any(m => m.From != draft.CandidateLocations[draft.CandidateIds.ToList().IndexOf(m.CardId)] || !IsLiejieDiscardDestination(m)) ||
            history.OfType<ProgramLiejieSourceDiscardEvent>().Where(e => e.FrameId == frame.Id).ToArray() is not [var paid] ||
            paid.SourceSeat != draft.SourceSeat || paid.OwnerSeat != frame.OwnerSeat || paid.SkillId != frame.SkillId ||
            paid.BindingId != GetProgramBindingId(frame) || paid.DiscardCount != moves.Length || !paid.DiscardedCardIds.SequenceEqual(draft.SelectedIds))
            throw new InvalidOperationException("Paid Liejie continuation lost its original physical HE invoice.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && !LiejieSourceDiscardFirstChild(frame, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Paid Liejie continuation lost its exact native first child.");
    }

    private bool IsLiejieAwaitedMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.LiejieSourceDiscard && frame.LiejieDiscardPending is { Paid: true } draft &&
        draft.InstructionIndex == frame.InstructionIndex && pending.SubjectSeat == draft.SourceSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;

    private bool LiejieSourceDiscardFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (LiejieInitialClauseFirstChild(frame, child)) return true;
        if (frame.LiejieDiscardPending is not { Paid: true } draft || !ExactLiejieParent(frame)) return false;
        bool EquipmentPayment(CardKind kind) => _cardMovements.Any(m => m.Sequence > draft.Before && m.Sequence <= draft.After &&
            draft.SelectedIds.Contains(m.CardId) && m.CardKind == kind && m.From == CardLocation.Equipment(draft.SourceSeat) &&
            m.To == CardLocation.DiscardPile && m.Reason.Value == LiejieDiscardReason(frame));
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId == frame.Id && moved.Batch.AwaitingProgramFrameId == frame.Id &&
                moved.Batch.OriginOwnerSeat == frame.OwnerSeat && moved.Batch.OriginSkillId == frame.SkillId &&
                moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > draft.Before && m.Sequence <= draft.After && m.Reason.Value == LiejieDiscardReason(frame) && moved.Batch.Id == draft.BatchId && IsLiejieDiscardDestination(m) ||
                     m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(draft.SourceSeat) &&
                     m.To == CardLocation.DiscardPile && EquipmentPayment(CardKind.WoodenOx)));
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (!EquipmentPayment(CardKind.SilverLion)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.ParentFrameId == frame.Id && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.SourceSeat == draft.SourceSeat && hp.Change.TargetSeat == draft.SourceSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, frame) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == draft.SourceSeat &&
            recovery.Attempt.TargetSeat == draft.SourceSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason?.Value == LiejieDiscardReason(frame);
    }

    private bool LiejieInitialClauseFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.InstructionIndex is not (2 or 3) || frame.LiejieDiscardPending is not null ||
            frame.PendingMovementContinuation is not null || !ExactLiejieParent(frame)) return false;
        var trigger = GetProgramTrigger(frame);
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.LiejieSourceDiscard)) return false;
        LiejieSourceDiscardContract.ValidateTrigger(frame.SkillId, trigger);
        var colors = ExactLiejiePaidColors(frame, trigger.Effects[^1].SourceBind!);
        var costReason = $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
        var reason = frame.InstructionIndex == 2 ? costReason : $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.Draw}";
        var bound = frame.CardSetBindings.Single(b => b.Name == colors.SourceBind);
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        bool EquipmentPayment(CardKind kind) => frame.InstructionIndex == 2 && _cardMovements.Any(m =>
            m.Sequence > colors.BeforeMoveSequence && bound.CardIds.Contains(m.CardId) && m.CardKind == kind &&
            m.From == CardLocation.Equipment(frame.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason.Value == costReason);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId == frame.Id && moved.Batch.AwaitingProgramFrameId is null && moved.Batch.ParentFrameId == frame.Id &&
                moved.Batch.OriginOwnerSeat == frame.OwnerSeat && moved.Batch.OriginSkillId == frame.SkillId &&
                moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > colors.BeforeMoveSequence &&
                    (m.Reason.Value == reason && (frame.InstructionIndex == 2 ? bound.CardIds.Contains(m.CardId) &&
                        m.From.OwnerSeat == frame.OwnerSeat && IsLiejieDiscardDestination(m) :
                        m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat)) ||
                     m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(frame.OwnerSeat) &&
                        m.To == CardLocation.DiscardPile && EquipmentPayment(CardKind.WoodenOx)));
        if (!EquipmentPayment(CardKind.SilverLion)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.ParentFrameId == frame.Id &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == frame.OwnerSeat && hp.Change.TargetSeat == frame.OwnerSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, frame) &&
            recovery.Return.Continuation == PostEventContinuation.Program && recovery.Attempt.SourceSeat == frame.OwnerSeat &&
            recovery.Attempt.TargetSeat == frame.OwnerSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == costReason;
    }

    private ProgramSkillFrame? LiejieSourceDiscardObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !LiejieSourceDiscardFirstChild(root, _resolutionStack[index + 1])) continue;
            var exact = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { exact = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (exact) return root;
        }
        return null;
    }

    private bool IsLiejieSourceDiscardProgramDying() => ActiveDying is { } dying && LiejieSourceDiscardObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasLiejieSourceDiscardDamageObserver(long id) =>
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && LiejieSourceDiscardObserverRoot() is not null;
    private bool AllowsLiejieSourceDiscardNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || frame.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            frame.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or
                SkillProgramTriggerWindow.SkillsChanged) || LiejieSourceDiscardObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }

    private PromptChoice SelectAiLiejieDiscardChoice(PendingDecision decision) => decision.Choices
        .OrderBy(c => c.Parameters.GetValueOrDefault("program-action") == "liejie-pick" ? 0 : 1)
        .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
}
