using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZixiPlacementReason = "skill-program.le-da-qiao.zixi.place";
    private static readonly IReadOnlyList<CardKind> ZixiSlots = Array.AsReadOnly(new[] { CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning });
    private bool HasZixiPlacementCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement);
    private static CardConversionSource ZixiPlacementSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static string ZixiSlotName(CardKind kind) => kind switch { CardKind.Indulgence => "indulgence", CardKind.SupplyShortage => "supplyShortage", CardKind.Lightning => "lightning", _ => throw new InvalidOperationException("Unknown Qin judgment slot.") };

    // Membership persists only along the actual uninterrupted Judgment residency.
    // The printed kind never becomes a delayed trick and is never rewritten here.
    private CardKind? ZixiQinJudgmentSlot(int id)
    {
        if (!HasZixiPlacementCapability || id <= 0) return null;
        var placed = ProgramEventHistory<ZixiQinPlacedEvent>().LastOrDefault(e => e.CardId == id);
        // Host-only projections have no physical zone or placement entitlement.
        if (placed is null || _cardZones.GetLocation(id).Zone != CardZoneKind.Judgment ||
            placed.Source.SkillId != "le:zixi" || placed.Source.BindingId is not ("start-place-qin" or "end-place-qin") ||
            _contentRegistry.GetSkill(placed.Source.SkillId).Program?.GameplayHash != placed.GameplayHash ||
            !ZixiSlots.Contains(placed.SlotKind) || !HasQiqinEntityMark(id) ||
            ProgramEventHistory<ZixiPlacementCommittedEvent>().Count(e => e.FrameId == placed.FrameId && e.Source == placed.Source &&
                e.GameplayHash == placed.GameplayHash && e.TargetSeat == placed.TargetSeat && e.EntityCount > placed.Index && e.EntityCount <= 2) != 1 ||
            ProgramEventHistory<ZixiPlacementPaidEvent>().Count(e => e.FrameId == placed.FrameId && e.BatchId == placed.BatchId &&
                e.EntityCount > placed.Index && placed.MovementSequence > e.SequenceBefore && placed.MovementSequence <= e.PrimaryAfter) != 1 ||
            _cardMovements.Count(m => m.Sequence == placed.MovementSequence && m.CardId == id && m.CardKind == placed.PrintedKind &&
                m.From == placed.From && m.To == CardLocation.Judgment(placed.TargetSeat) && m.Reason.Value == ZixiPlacementReason) != 1) return null;
        var location = CardLocation.Judgment(placed.TargetSeat);
        foreach (var move in _cardMovements.Where(m => m.CardId == id && m.Sequence > placed.MovementSequence))
        {
            if (move.From != location || move.To.Zone != CardZoneKind.Judgment) return null;
            location = move.To;
        }
        return location == _cardZones.GetLocation(id) ? placed.SlotKind : null;
    }
    private bool IsZixiQinJudgmentCard(int id) => ZixiQinJudgmentSlot(id) is not null;
    private string? ZixiQinJudgmentSlotLabel(int id) => ZixiQinJudgmentSlot(id) switch
    { CardKind.Indulgence => "琴（乐槽）", CardKind.SupplyShortage => "琴（兵槽）", CardKind.Lightning => "琴（闪槽）", _ => null };
    private bool CanTransferZixiQinJudgmentCard(Card card, int target) => ZixiQinJudgmentSlot(card.Id) is { } slot &&
        IsValidPlayerSeat(target) && _cardZones.GetLocation(card.Id).OwnerSeat != target &&
        _players[target] is { IsAlive: true, JudgmentAreaAbolished: false } && !HasJudgmentEffectiveCard(_players[target], slot);
    private bool CanPlaceZixiQin(int owner, Card card)
    {
        var from = _cardZones.GetLocation(card.Id);
        return !card.IsGeneralWeapon && from.OwnerSeat == owner && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            HasQiqinEntityMark(card.Id) && !IsForeignEquipmentDiscardPrevented(owner, card, from, OwnedCardMoveIntent.Transfer);
    }
    private bool ZixiPlacementMother(ProgramSkillFrame f)
    {
        if (f.SkillId != "le:zixi" || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { } c ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.InstructionIndex != 1 ||
            c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        var index = _resolutionStack.FindIndex(s => s.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != c.ParentFrameId) return false;
        if (f.TriggerId == "start-place-qin" && c.Window == SkillProgramTriggerWindow.PlayPhaseStarting &&
            _resolutionStack[index - 1] is PlayPhaseStartingBoundaryFrame start && start.OwnerSeat == f.OwnerSeat &&
            start.ItemIndex >= 0 && start.ItemIndex < start.Items.Count && start.Items[start.ItemIndex].Candidate is { } candidate)
            return MountObserverCandidateMatches(f, candidate) && c.OccurrenceIndex == candidate.OccurrenceIndex;
        return f.TriggerId == "end-place-qin" && c.Window == SkillProgramTriggerWindow.PlayEnding &&
            _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame end && end.Window == c.Window && end.OwnerSeat == f.OwnerSeat &&
            end.CandidateIndex >= 0 && end.CandidateIndex < end.Candidates.Count && MountObserverCandidateMatches(f, end.Candidates[end.CandidateIndex]) &&
            c.OccurrenceIndex == end.Candidates[end.CandidateIndex].OccurrenceIndex;
    }
    [ProgramCapability]
    private sealed class LeDaQiaoZixiPlacementCapability(GameEngine engine) : ILeDaQiaoZixiPlacementHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginZixiPlacement(f); }
    private SkillProgramStepOutcome BeginZixiPlacement(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var phase = CurrentActualDiscardRecoveryPhase();
        if (f.ZixiPlacement is not null || !ZixiPlacementMother(f) || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("Zixi placement requires its exact real Play boundary and original source.");
        var r = new ZixiPlacementReceipt(ZixiPlacementSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, phase, f.InstructionIndex);
        ReplaceRuntimeTop(f = f with { ZixiPlacement = r, Step = ResolutionFrameStep.AwaitingResponse });
        AdvanceEventRulesAndQueueFact(new ZixiPlacementOpenedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.Phase));
        PublishZixiPlacement(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ZixiPlacementChoices(ProgramSkillFrame f)
    {
        var r = f.ZixiPlacement!; if (r.Paid) return [];
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string branch, string suffix, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets, CardKind? slot = null) =>
            new(new ChoiceId($"zixi-placement.frame-{f.Id}.{suffix}"), label, Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(targets.ToArray()),
                new Dictionary<string, string> { ["program-action"] = "zixi-placement", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                    ["slot"] = slot is { } kind ? ZixiSlotName(kind) : "" });
        if (r.Entities.Count < 2)
        foreach (var target in _players.Where(p => p.IsAlive && !p.JudgmentAreaAbolished && (r.TargetSeat is null || r.TargetSeat == p.Seat)))
        foreach (var slot in ZixiSlots.Where(slot => !HasJudgmentEffectiveCard(target, slot) && r.Entities.All(e => e.SlotKind != slot)))
        foreach (var card in GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])).Where(c => CanPlaceZixiQin(f.OwnerSeat, c) && r.Entities.All(e => e.CardId != c.Id)))
            choices.Add(Choice("select", $"select-{r.Entities.Count}.target-{target.Seat}.slot-{ZixiSlotName(slot)}.card-{card.Id}",
                $"将【{card.DisplayName}】放入 {target.Name} 的{CardCatalog.Get(slot).DisplayName}副槽", [card.Id], [target.Seat], slot));
        foreach (var e in r.Entities) choices.Add(Choice("undo", $"undo-{r.Entities.Count}.card-{e.CardId}", "取消这张琴", [e.CardId], []));
        choices.Add(Choice("confirm", $"confirm-{r.Entities.Count}", r.Entities.Count == 0 ? "不放置" : $"确认放置{r.Entities.Count}张琴", [], r.TargetSeat is { } seat ? [seat] : []));
        choices.Add(Choice("cancel", "cancel", "取消放置", [], []));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishZixiPlacement(ProgramSkillFrame f)
    {
        var choices = ZixiPlacementChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "姊希：选择至多两张琴，放置在同一角色的不同判定副槽", choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("zixi-placement")]
    private void AnswerZixiPlacement(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZixiPlacement: { Paid: false } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], ZixiPlacementChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Zixi placement lost its exact private published source, target or independent free slot.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { CompleteZixiPlacement(f, false); return; }
        var branch = choice.Parameters["branch"];
        if (branch == "select")
        {
            var id = choice.Cards.Single(); var card = GetAdvancedCard(id); var slot = ZixiSlots.Single(s => ZixiSlotName(s) == choice.Parameters["slot"]);
            ReplaceRuntimeTop(f = f with { ZixiPlacement = r with { TargetSeat = choice.Targets.Single(),
                Entities = r.Entities.Append(new(id, card.Kind, _cardZones.GetLocation(id), slot)).ToArray() } });
            PublishZixiPlacement(f); return;
        }
        if (branch == "undo")
        {
            var entities = r.Entities.Where(e => e.CardId != choice.Cards.Single()).ToArray();
            ReplaceRuntimeTop(f = f with { ZixiPlacement = r with { Entities = entities, TargetSeat = entities.Length == 0 ? null : r.TargetSeat } });
            PublishZixiPlacement(f); return;
        }
        if (branch == "cancel" || r.Entities.Count == 0) { CompleteZixiPlacement(f, false); return; }
        if (branch != "confirm" || r.TargetSeat is not { } target || !_players[target].IsAlive || _players[target].JudgmentAreaAbolished ||
            r.Entities.Any(e => !CanPlaceZixiQin(f.OwnerSeat, GetAdvancedCard(e.CardId)) || _cardZones.GetLocation(e.CardId) != e.From ||
                HasJudgmentEffectiveCard(_players[target], e.SlotKind))) throw new InvalidOperationException("Zixi cannot pay a stale or occupied placement selection.");
        var grains = r.Entities.Where(e => e.PrintedKind == CardKind.WoodenOx && e.From.Zone == CardZoneKind.Equipment)
            .SelectMany(e => GetWoodenOxGrain(_players[e.From.OwnerSeat!.Value]).Select(c => new ZixiPlacementGrain(c.Id, c.Kind, CardLocation.Grain(e.From.OwnerSeat.Value)))).ToArray();
        r = r with { Paid = true, Stage = ZixiPlacementStage.MovementChildren, SequenceBefore = _movementSequence,
            PrimaryAfter = _movementSequence, SequenceAfter = _movementSequence, Grains = grains };
        ReplaceRuntimeTop(f = f with { ZixiPlacement = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceEventRulesAndQueueFact(new ZixiPlacementCommittedEvent(f.Id, r.Source, r.GameplayHash, target, r.Entities.Count, r.SequenceBefore));
        for (var i = 0; i < r.Entities.Count; i++) { var e = r.Entities[i]; AdvanceEventRulesAndQueueFact(new ZixiPlacementEntityFrozenEvent(f.Id, i, e.CardId, e.PrintedKind, e.From, e.SlotKind)); }
        for (var i = 0; i < grains.Length; i++) AdvanceEventRulesAndQueueFact(new ZixiPlacementGrainFrozenEvent(f.Id, i, grains[i].CardId, grains[i].PrintedKind, grains[i].From));
        MoveProgramCardsFromMultipleSources(r.Entities.Select(e => e.CardId).ToArray(), CardLocation.Judgment(target), new(ZixiPlacementReason), (batch, moves) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = active.ZixiPlacement!;
            if (paid.BatchId != 0 || moves.Count != paid.Entities.Count || moves.Where((m, i) => m.CardId != paid.Entities[i].CardId ||
                    m.CardKind != paid.Entities[i].PrintedKind || m.From != paid.Entities[i].From || m.To != CardLocation.Judgment(target) ||
                    m.Reason.Value != ZixiPlacementReason || m.Sequence != paid.SequenceBefore + i + 1).Any())
                throw new InvalidOperationException("Zixi lost its one whole original Qin placement batch.");
            ReplaceRuntimeTop(active with { ZixiPlacement = paid with { BatchId = batch, PrimaryAfter = moves[^1].Sequence, SequenceAfter = moves[^1].Sequence } });
            for (var i = 0; i < moves.Count; i++) AdvanceEventRulesAndQueueFact(new ZixiQinPlacedEvent(f.Id, i, paid.Source, paid.GameplayHash,
                target, moves[i].CardId, moves[i].CardKind, moves[i].From, paid.Entities[i].SlotKind, moves[i].Sequence, batch));
            AdvanceEventRulesAndQueueFact(new ZixiPlacementPaidEvent(f.Id, batch, moves.Count, paid.SequenceBefore, moves[^1].Sequence));
        }, disclosure: CardMovementDisclosure.FaceUp);
        f = GetActiveProgramFrame(f.Id); r = f.ZixiPlacement! with { TailSealed = true, SequenceAfter = _movementSequence };
        ReplaceRuntimeTop(f = f with { ZixiPlacement = r });
        AdvanceEventRulesAndQueueFact(new ZixiPlacementTailSealedEvent(f.Id, r.BatchId, grains.Length, r.SequenceBefore, r.SequenceAfter));
        ContinueZixiPlacement(f.Id);
    }
    private void ContinueZixiPlacement(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZixiPlacement: { Paid: true } } f || f.Id != id || _pendingDecision is not null) return;
        AssertZixiPlacement(f);
        if (new ProgramSkillHost(this).TryStartPostInstructionWindow(id)) return;
        CompleteZixiPlacement(GetActiveProgramFrame(id), true);
    }
    private void CompleteZixiPlacement(ProgramSkillFrame f, bool completed)
    {
        var r = f.ZixiPlacement!;
        AdvanceEventRulesAndQueueFact(new ZixiPlacementCompletedEvent(f.Id, r.Paid, r.Entities.Count));
        ReplaceRuntimeTop(f = f with { ZixiPlacement = null, PendingMovementContinuation = null }); FinishProgramSkill(f, completed);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28710)]
    private bool ResumeZixiPlacement(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZixiPlacement: { Paid: true } } f || f.Id != id) return false; ContinueZixiPlacement(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28710)]
    private bool ReturnZixiPlacementMovement(ProgramSkillFrame f)
    { if (f.ZixiPlacement is not { Paid: true }) return false; ContinueZixiPlacement(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28710)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28710)]
    private bool PermitPaidZixiPlacement(ProgramSkillFrame f) => HasPaidZixiPlacement(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZixiQinJudgmentPlacement)]
    private PromptChoice ChooseAiZixiPlacement(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c =>
        c.Parameters.GetValueOrDefault("branch") == "select" && c.Targets.Single() != f.OwnerSeat) ??
        p.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "confirm");
}
