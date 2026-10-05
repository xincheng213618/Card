using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksRedOwnedLoss => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RevealRedLossAndDraw);
    private static bool OwnedLossZone(CardZoneKind zone) => zone is CardZoneKind.Hand or CardZoneKind.Equipment or
        CardZoneKind.Judgment or CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or
        CardZoneKind.Chunlao or CardZoneKind.PojunHold or CardZoneKind.PrivateReserve or CardZoneKind.PublicDeferredPile or
        CardZoneKind.PublicPersistentPile or CardZoneKind.PrivateTurnHold;
    private RedOwnedLossMaterial? CaptureRedOwnedLoss(Card card, CardLocation from, CardLocation to, long sequence)
    {
        // Freeze the original owner's effective suit at issuance, before equipment
        // grants/removal observers change that owner. The snapshot keeps printed suit.
        if (!TracksRedOwnedLoss || from.OwnerSeat is not { } owner || !OwnedLossZone(from.Zone) ||
            to.OwnerSeat == owner && OwnedLossZone(to.Zone) || CaptureMovementTiming() is not { } timing ||
            timing.Phase == TurnPhase.Play && timing.PhaseActorSeat == owner) return null;
        var suit = EffectiveSuit(_players[owner], card);
        return suit is Suit.Heart or Suit.Diamond ? new(owner, ToSnapshot(card), suit, timing, sequence) : null;
    }
    private int[] MatchingActualGainGiftIndexes(CardMovementBatchContext batch, int owner, CardLocation location) =>
        batch.Movements.Select((m, i) => (m, i)).GroupBy(x => x.m.CardId)
            .Where(g => GainGiftOriginalSource(g.OrderBy(x => x.m.Sequence).First().m).OwnerSeat != owner &&
                g.OrderBy(x => x.m.Sequence).Last().m.To == CardLocation.Hand(owner))
            .Select(g => g.OrderBy(x => x.m.Sequence).Last()).Where(x => x.m.To == location).Select(x => x.i).Order().ToArray();
    private CardLocation GainGiftOriginalSource(CardMovementRecord first)
    {
        if (first.From != CardLocation.Processing) return first.From;
        // A split processing hop does not create a foreign source by dropping the seat.
        var prior = _cardMovements.LastOrDefault(m => m.CardId == first.CardId && m.Sequence < first.Sequence);
        return prior?.To == CardLocation.Processing ? prior.From : first.From;
    }
    private int[] MatchingRedOwnerLossIndexes(CardMovementBatchContext batch, int owner, CardLocation location) =>
        batch.Movements.Select((m, i) => (m, i)).Where(x => x.m.RedOwnedLoss is { } loss &&
                loss.OwnerSeat == owner && loss.MovementSequence == x.m.Sequence)
            .GroupBy(x => x.m.CardId).Select(g => g.OrderBy(x => x.m.Sequence).First())
            .Where(x => x.m.From == location).Select(x => x.i).Order().ToArray();
    private bool GainGiftPhaseUsed(int owner, string skill, ActualDiscardRecoveryPhaseKey phase) =>
        CompleteProgramEventHistory().OfType<GainGiftPhaseIssuedEvent>().Any(e =>
            e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Phase.Token == phase.Token);
    private bool ExactGainGiftMovementParent(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context,
        out CardsMovedTriggerWindowFrame parent)
    {
        parent = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId)!;
        return parent is not null && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            context.MovementBatch is { } batch && parent.Batch.Id == batch.Id &&
            parent.Batch.Movements.SequenceEqual(batch.Movements) && parent.Candidates[parent.CandidateIndex] == candidate;
    }
    private bool CanRunGainGiftTrigger(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        var op = trigger.Effects.FirstOrDefault()?.Op;
        if (op is not (SkillProgramEffectOp.GiveAfterBatchGain or SkillProgramEffectOp.RevealRedLossAndDraw)) return true;
        if (_winner != Winner.None || _status == EngineStatus.Completed ||
            !ExactGainGiftMovementParent(candidate, context, out var parent)) return false;
        if (op == SkillProgramEffectOp.GiveAfterBatchGain)
            return context.Window == SkillProgramTriggerWindow.CardsGained && parent.Batch.DiscardRecoveryPhase is { } phase &&
                IsRecordedActualDiscardRecoveryPhase(phase) && !GainGiftPhaseUsed(candidate.OwnerSeat, candidate.SkillId, phase) &&
                MatchingActualGainGiftIndexes(parent.Batch, candidate.OwnerSeat, CardLocation.Hand(candidate.OwnerSeat)).Length >= 2 &&
                TransferableGainGiftCards(candidate.OwnerSeat).Length > 0 && _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat);
        return context.Window == SkillProgramTriggerWindow.CardsMoved &&
            MatchingOwnerBatchMovementIndexes(parent.Batch, candidate, trigger).Length > 0;
    }
    private Card[] TransferableGainGiftCards(int owner) => GetHand(_players[owner]).Concat(GetEquipment(_players[owner]))
        .Where(c => !c.IsGeneralWeapon).OrderBy(c => c.Id).ToArray();
    private static CardConversionSource GainGiftSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome ExecuteGainGift(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.GainGiftReceipt is not null) throw new InvalidOperationException("An issued gain/loss receipt cannot be paid twice.");
        if (_winner != Winner.None || _status == EngineStatus.Completed || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        if (effect.Op == SkillProgramEffectOp.DelegateJudgmentReplacement) return PayDelegatedJudgment(f, effect);
        var context = f.WindowContext ?? throw new InvalidOperationException("Gain/loss needs an actual movement window.");
        // Preserve occurrence identity from the actual parent, including PerBatch's cursor.
        var parent = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == context.ParentFrameId);
        var candidate = parent.Candidates[parent.CandidateIndex];
        if (!MountObserverCandidateMatches(f, candidate) || !CanRunGainGiftTrigger(candidate, GetProgramTrigger(f), context))
            throw new InvalidOperationException("Gain/loss lost its exact movement parent and current qualification.");
        var losses = effect.Op == SkillProgramEffectOp.RevealRedLossAndDraw
            ? MatchingOwnerBatchMovementIndexes(parent.Batch, candidate, GetProgramTrigger(f)).Select(i => parent.Batch.Movements[i].RedOwnedLoss!).ToArray()
            : Array.Empty<RedOwnedLossMaterial>();
        var r = new GainGiftProgramReceipt(f.InstructionIndex, effect.Op, GainGiftStage.Choosing, parent.Id, parent.Batch.Id,
            parent.Batch.DiscardRecoveryPhase, [], [], 0, 0, losses);
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = r });
        if (effect.Op == SkillProgramEffectOp.GiveAfterBatchGain) { PublishGainGiftChoice(f); return SkillProgramStepOutcome.AwaitChoice; }
        StartNextRedLossDraw(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome PayDelegatedJudgment(ProgramSkillFrame f, SkillProgramEffect effect)
    {
        var j = ActiveJudgment ?? throw new InvalidOperationException("Delegated payment has no actual judgment.");
        var d = j.DelegatedReplacement ?? throw new InvalidOperationException("Delegated payment lost its owning draft.");
        var c = f.WindowContext?.JudgmentReplacement;
        if (!ValidDelegatedJudgmentDraft(j, d) || d.Stage != DelegatedJudgmentStage.Chosen ||
            d.Source != GainGiftSource(f) || d.GameplayHash != f.GameplayHash || c is null ||
            d.SelectedCardId != c.ReplacementCardId || d.OldCardId != c.OldCardId)
            throw new InvalidOperationException("The delegated choice is not the original owner's unpaid actual card.");
        var from = _cardZones.GetLocation(c.ReplacementCardId);
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = new(f.InstructionIndex, effect.Op, GainGiftStage.ReplacementChildren,
            j.Id, 0, null, [], [], before, before, [], PaymentFrom: from, PaymentCardId: c.ReplacementCardId, PaymentTarget: c.SubjectSeat),
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ReplaceProgramJudgment(f, effect);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = f.GainGiftReceipt! with { After = _movementSequence } });
        if (!TryDrainFireTargetMovement(f)) ReturnGainGiftMovement(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    // Called by the real replacement commit before removal/recovery hooks run.
    private void RecordDelegatedJudgmentPayment(JudgmentFrame j, long batchId)
    {
        var f = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault();
        if (f?.GainGiftReceipt is not { Stage: GainGiftStage.ReplacementChildren } r || r.OriginalParentId != j.Id) return;
        ReplaceJudgmentFrame(GetJudgmentFrame(j.Id) with { DelegatedReplacement = j.DelegatedReplacement! with { Stage = DelegatedJudgmentStage.Paid } });
        ReplaceRuntimeTop(f with { GainGiftReceipt = r with { BatchId = batchId, After = _movementSequence } });
    }
    private IReadOnlyList<PromptChoice> GainGiftChoices(ProgramSkillFrame f)
    {
        var r = f.GainGiftReceipt!;
        var choices = TransferableGainGiftCards(f.OwnerSeat).SelectMany(card => _players.Where(p => p.IsAlive &&
            p.Seat != f.OwnerSeat && !r.GivenTargets.Contains(p.Seat)).Select(p => new PromptChoice(
                new($"gain-gift.{f.Id}.{r.GivenTargets.Count}.{card.Id}.{p.Seat}"), $"交给 {p.Name} 一张【{card.DisplayName}】。",
                [card.Id], [p.Seat], GainGiftParameters(f, "give")))).ToList();
        choices.Add(new(new($"gain-gift.{f.Id}.{r.GivenTargets.Count}.finish"), r.GivenTargets.Count == 0 ? "不发动弘援。" : "结束弘援。", [], [], GainGiftParameters(f, "finish")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private static Dictionary<string, string> GainGiftParameters(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "gain-after-batch", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
    private void PublishGainGiftChoice(ProgramSkillFrame f)
    {
        var choices = GainGiftChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "弘援：可交给至多两名其他角色各一张牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveGainGiftChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Gift has no owning program.");
        AssertGainGiftReceipt(f); var r = f.GainGiftReceipt!;
        if (r.Operation != SkillProgramEffectOp.GiveAfterBatchGain || r.Stage != GainGiftStage.Choosing ||
            _pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } p || p.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], GainGiftChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Gift choice lost its current private owner and exact choices.");
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "finish" || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishGainGift(f); return; }
        var card = choice.Cards.Single(); var target = choice.Targets.Single();
        var from = _cardZones.GetLocation(card); var before = _movementSequence;
        if (r.GivenTargets.Count == 0)
        {
            if (r.Phase is not { } phase || GainGiftPhaseUsed(f.OwnerSeat, f.SkillId, phase))
                throw new InvalidOperationException("Gift cannot spend an already issued actual-phase quota.");
            AdvanceEventRulesAndQueueFact(new GainGiftPhaseIssuedEvent(f.Id, GainGiftSource(f), f.GameplayHash, r.BatchId, phase));
        }
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = r = r with { Stage = GainGiftStage.GiftChildren,
            GivenCards = r.GivenCards.Append(card).ToArray(), GivenTargets = r.GivenTargets.Append(target).ToArray(),
            PaymentFrom = from, PaymentCardId = card, PaymentTarget = target, Before = before, After = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([card], CardLocation.Hand(target), new("program.batch-gain.gift"), (_, records) =>
        {
            var current = GetActiveProgramFrame(f.Id); var paid = current.GainGiftReceipt! with { After = records.Single().Sequence };
            ReplaceRuntimeTop(current with { GainGiftReceipt = paid });
            AdvanceEventRulesAndQueueFact(new GainGiftPaidEvent(f.Id, card, from, target, before, paid.After));
        });
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = f.GainGiftReceipt! with { After = _movementSequence } });
        if (!TryDrainFireTargetMovement(f)) ReturnGainGiftMovement(f);
    }
    private void StartNextRedLossDraw(ProgramSkillFrame f)
    {
        var r = f.GainGiftReceipt!;
        if (GainGiftHasFinalGameEnd()) return;
        if (r.LossIndex >= r.RedLosses.Count || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishGainGift(f); return; }
        var loss = r.RedLosses[r.LossIndex]; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = r = r with { Stage = GainGiftStage.RedDrawChildren,
            LossIndex = r.LossIndex + 1, Before = before, After = before, ActualDrawCount = 0 },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new RedOwnedLossRevealedEvent(f.Id, f.OwnerSeat, loss.Card, loss.EffectiveSuit, loss.MovementSequence));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 展示失去的【{loss.Card.DisplayName}】（{loss.Card.Suit} {loss.Card.Rank}），发动【明哲】摸一张牌。", f.OwnerSeat);
        var drawn = DrawCards(_players[f.OwnerSeat], 1, true, new("program.red-owned-loss.draw"));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { GainGiftReceipt = f.GainGiftReceipt! with { After = _movementSequence, ActualDrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new GainGiftDrawPaidEvent(f.Id, loss.MovementSequence, drawn.Count, before, _movementSequence));
        if (!TryDrainFireTargetMovement(f)) ReturnGainGiftMovement(f);
    }
    private bool ResumeGainGift(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GainGiftReceipt: not null } f || f.Id != id) return false;
        AssertGainGiftReceipt(f);
        if (GainGiftHasFinalGameEnd()) return true;
        if (f.PendingMovementContinuation is not null) { if (!TryDrainFireTargetMovement(f)) ReturnGainGiftMovement(f); return true; }
        if (f.GainGiftReceipt.Stage != GainGiftStage.Choosing) throw new InvalidOperationException("A paid receipt has no typed movement return.");
        if (_pendingDecision is null) PublishGainGiftChoice(f);
        return true;
    }
    private bool ReturnGainGiftMovement(ProgramSkillFrame f)
    {
        if (f.GainGiftReceipt is not { } r) return false;
        if (f.PendingMovementContinuation is not { } pending || !IsGainGiftMovement(f, GainGiftPausedEffect(f), pending))
            throw new InvalidOperationException("Gain/loss cannot consume another instruction's movement return.");
        AssertGainGiftReceipt(f);
        if (GainGiftHasFinalGameEnd()) return true;
        if (TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == GainGiftStage.RedDrawChildren) { StartNextRedLossDraw(f); return true; }
        if (r.Stage == GainGiftStage.GiftChildren && r.GivenTargets.Count < 2 && _players[f.OwnerSeat].IsAlive &&
            HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && TransferableGainGiftCards(f.OwnerSeat).Length > 0 &&
            _players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat && !r.GivenTargets.Contains(p.Seat)))
        { ReplaceRuntimeTop(f = f with { GainGiftReceipt = r with { Stage = GainGiftStage.Choosing } }); PublishGainGiftChoice(f); return true; }
        FinishGainGift(f); return true;
    }
    private void FinishGainGift(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { GainGiftReceipt = null }); FinishProgramSkill(f, true); }
    private bool GainGiftHasFinalGameEnd()
    {
        if (_status != EngineStatus.Completed) return false;
        if (_winner == Winner.None || !CompleteProgramEventHistory().OfType<GameEndedEvent>().Any(e => e.Winner == _winner))
            throw new InvalidOperationException("A terminal gain/loss diagnostic needs the actual final game-end fact.");
        return true;
    }
    private SkillProgramEffect? GainGiftPausedEffect(ProgramSkillFrame f) => f.InstructionIndex < 1 ? null :
        ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool IsGainGiftMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.GainGiftReceipt is { Stage: not GainGiftStage.Choosing } r && r.InstructionIndex == f.InstructionIndex &&
        effect?.Op == r.Operation && effect == GainGiftPausedEffect(f) && pending.SubjectSeat == f.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private IEnumerable<CardSnapshot> GainGiftPublicLostCards() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.GainGiftReceipt is { Stage: GainGiftStage.RedDrawChildren, LossIndex: > 0 })
        .Select(f => f.GainGiftReceipt!.RedLosses[f.GainGiftReceipt.LossIndex - 1].Card);
    private PromptChoice SelectAiGainGift(PendingDecision decision, ProgramSkillFrame f)
    {
        var view = CreateSnapshot(f.OwnerSeat); var self = view.Players.Single(p => p.Seat == f.OwnerSeat);
        var cards = self.Hand.Concat(self.Equipment).ToDictionary(c => c.Id);
        var gifts = decision.Choices.Where(c => c.Cards.Count == 1 && c.Targets.Count == 1)
            .Where(c => { var p = view.Players.Single(p => p.Seat == c.Targets[0]);
                return self.TeamId is not null && p.TeamId == self.TeamId ||
                    (self.Role is Role.Lord or Role.Loyalist) && (p.Role is Role.Lord or Role.Loyalist) || self.Role == Role.Rebel && p.Role == Role.Rebel; });
        return gifts.OrderBy(c => CardCatalog.Get(cards[c.Cards[0]].Kind).HandKeepValue).ThenBy(c => c.Targets[0]).ThenBy(c => c.Cards[0])
            .FirstOrDefault() ?? decision.Choices.Single(c => c.Cards.Count == 0);
    }
    private sealed partial class ProgramSkillHost : IDelegatedJudgmentAndGainGiftHost
    {
        public SkillProgramStepOutcome ExecuteGainGift(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.ExecuteGainGift(frame, effect);
    }
}
