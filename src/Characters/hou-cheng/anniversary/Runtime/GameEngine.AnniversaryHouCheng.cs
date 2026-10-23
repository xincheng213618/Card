using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HouChengAction = "anniversary-hou-cheng", HouChengObtainReason = "skill-program.anniversary:xianniang.obtain", HouChengGiveReason = "skill-program.anniversary:xianniang.give";
    [ProgramCapability] private sealed class AnniversaryHouChengHost(GameEngine e) : IAnniversaryHouChengHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f) => e.BeginHouCheng(f); }
    private static CardConversionSource HouChengSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasHouChengContent => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryHouChengXianniang);
    private bool HouChengForeignDiscard(int owner, ProgramSkillWindowContext c)
    {
        if (c is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch }) return false;
        return batch.Movements.Any(move => move.To == CardLocation.DiscardPile && _cardMovements.Contains(move) &&
            ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Any(m => m.CardId == move.CardId && m.CompletionSequence == move.Sequence && m.From.OwnerSeat == owner && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(i => i.InvocationId == m.InvocationId && i.ExecutorSeat != owner && i.TurnNumber == batch.TurnNumber && m.CompletionSequence > i.SequenceBefore && m.CompletionSequence <= i.SequenceAfter)));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47100)]
    private bool CanOfferHouCheng(ProgramTriggerCandidate candidate, SkillProgramTrigger t, ProgramSkillWindowContext c) => candidate.SkillId != AnniversaryHouChengComposition.Skill ||
        t.Id == "foreign-discard" && HouChengForeignDiscard(candidate.OwnerSeat, c) || t.Id == "after-damage" && c is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, TargetSeat: { } target, DamageFrameId: { } id } && target == candidate.OwnerSeat &&
        _resolutionStack.OfType<DamageFrame>().Any(d => d.Id == id && d.TargetSeat == target && d.Amount == c.Amount);
    private bool HouChengParent(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } c || f.SkillId != AnniversaryHouChengComposition.Skill || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) return false;
        if (f.TriggerId == "foreign-discard") return HouChengForeignDiscard(f.OwnerSeat, c) &&
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } w && c.MovementBatch is { } batch && w.Batch.Id == batch.Id && w.Batch.TurnNumber == batch.TurnNumber && w.Batch.Movements.SequenceEqual(batch.Movements) && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]);
        return f.TriggerId == "after-damage" && c is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, TargetSeat: { } target, DamageFrameId: { } id } && target == f.OwnerSeat &&
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } d && d.ParentFrameId == id && d.TargetSeat == target && d.TriggerWindow == c.Window && d.CandidateIndex >= 0 && d.CandidateIndex < d.Candidates.Count && MountObserverCandidateMatches(f, d.Candidates[d.CandidateIndex].ToProgramCandidate()) &&
            _resolutionStack.OfType<DamageFrame>().Any(a => a.Id == id && a.TargetSeat == target && a.Amount == c.Amount);
    }
    private SkillProgramStepOutcome BeginHouCheng(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryHouCheng is not null || !HouChengParent(f)) throw new InvalidOperationException("Xianniang needs its original actual damage or foreign logical discard candidate.");
        var r = new AnniversaryHouChengReceipt(HouChengSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, _roundNumber, _turnNumber, _turnProgression.OwnerSeat, AnniversaryHouChengStage.Source);
        ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryHouChengStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Round, r.Turn, r.TurnOwner));
        PublishHouCheng(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IEnumerable<int> HouChengSources(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && GetHand(p).Count >= GetHand(_players[f.OwnerSeat]).Count).Select(p => p.Seat);
    private IEnumerable<int> HouChengRecipients(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && p.Seat != f.AnniversaryHouCheng!.Victim).Select(p => p.Seat);
    private Card HouChengSlot(ProgramSkillFrame f, PromptChoice c)
    {
        var r = f.AnniversaryHouCheng!; var owner = r.Stage == AnniversaryHouChengStage.Obtain ? r.Victim : f.OwnerSeat;
        return _cardZones.CardsAt(new(Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]), owner))[int.Parse(c.Parameters["slot-index"], CultureInfo.InvariantCulture)];
    }
    private IReadOnlyList<PromptChoice> HouChengChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryHouCheng!;
        PromptChoice Choice(string verb, string text, IReadOnlyList<int>? targets = null) => new(new($"hou-cheng.{f.Id}.{r.Stage}.{r.Selected.Count}.{verb}.{targets?.FirstOrDefault(-1)}"), text, [], targets ?? [], new Dictionary<string, string> { ["program-action"] = HouChengAction, ["verb"] = verb });
        if (r.Stage == AnniversaryHouChengStage.Source) return Array.AsReadOnly(HouChengSources(f).Select(s => Choice("source", $"从 {_players[s].Name} 处获得至多两张牌", [s])).Append(Choice("stop", "不获得牌，结束献酿")).ToArray());
        if (r.Stage == AnniversaryHouChengStage.Recipient) return Array.AsReadOnly(HouChengRecipients(f).Select(s => Choice("recipient", $"交给 {_players[s].Name} 至多 {r.Obtained} 张牌", [s])).Append(Choice("stop", "不交牌，结算献酿")).ToArray());
        if (r.Stage is not (AnniversaryHouChengStage.Obtain or AnniversaryHouChengStage.Give)) return [];
        var owner = r.Stage == AnniversaryHouChengStage.Obtain ? r.Victim : f.OwnerSeat;
        var cards = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, owner, [CardZoneKind.Hand, CardZoneKind.Equipment], r.Stage == AnniversaryHouChengStage.Obtain ? OwnedCardMoveIntent.Obtain : OwnedCardMoveIntent.Transfer,
                canSelect: (zone, _) => !(r.Stage == AnniversaryHouChengStage.Obtain && owner == f.OwnerSeat && zone == CardZoneKind.Hand))
            .Where(c => !r.Selected.Contains(HouChengSlot(f, c).Id))
            .Select(c => c with { Id = new($"hou-cheng.{f.Id}.{r.Stage}.{r.Selected.Count}.{c.Id}"), Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = HouChengAction, ["verb"] = "card" } });
        return Array.AsReadOnly(cards.Append(Choice("finish", r.Selected.Count == 0 ? "不选牌，继续结算" : $"确定已选 {r.Selected.Count} 张牌")).ToArray());
    }
    private void PublishHouCheng(ProgramSkillFrame f)
    {
        var r = f.AnniversaryHouCheng!;
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { SettleHouCheng(f); return; }
        if (r.Stage is AnniversaryHouChengStage.Obtain or AnniversaryHouChengStage.Give && (r.Selected.Count >= (r.Stage == AnniversaryHouChengStage.Obtain ? 2 : r.Obtained) || HouChengChoices(f).Count == 1)) { MoveHouCheng(f); return; }
        var choices = HouChengChoices(f); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage switch { AnniversaryHouChengStage.Source => "献酿：选择手牌数不小于你的角色。", AnniversaryHouChengStage.Obtain => "献酿：选择至多两张牌获得。", AnniversaryHouChengStage.Recipient => "献酿：可交给另一名其他角色至多等量的牌。", _ => "献酿：选择至多等量的牌交出。" }, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHouChengXianniang)] private PromptChoice HouChengAi(PendingDecision p) => p.Choices.First();
    [ProgramAnswer(HouChengAction)] private void AnswerHouCheng(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHouCheng: { } r } f || !ValidHouCheng(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !HouChengChoices(f).Any(a => AssistedChoicesEqual([a], [c]))) throw new InvalidOperationException("Xianniang requires its exact current private selection and original frame.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        switch (c.Parameters["verb"])
        {
            case "stop": SettleHouCheng(f); return;
            case "source": ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Victim = c.Targets.Single(), Stage = AnniversaryHouChengStage.Obtain, Selected = [] } }); break;
            case "recipient": ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Recipient = c.Targets.Single(), Stage = AnniversaryHouChengStage.Give, Selected = [] } }); break;
            case "card": ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Selected = r.Selected.Append(HouChengSlot(f, c).Id).ToArray() } }); break;
            case "finish": MoveHouCheng(f); return;
            default: throw new InvalidOperationException("Unknown Xianniang choice.");
        }
        PublishHouCheng(f);
    }
    private void MoveHouCheng(ProgramSkillFrame f)
    {
        var r = f.AnniversaryHouCheng!; var giving = r.Stage == AnniversaryHouChengStage.Give;
        if (!ValidHouCheng(f) || r.Stage is not (AnniversaryHouChengStage.Obtain or AnniversaryHouChengStage.Give) || r.Selected.Count > (giving ? r.Obtained : 2)) throw new InvalidOperationException("Xianniang cannot replace or repay its selected native entities.");
        if (giving ? !HouChengRecipients(f).Contains(r.Recipient) : !HouChengSources(f).Contains(r.Victim)) { SettleHouCheng(f); return; }
        var cardOwner = giving ? f.OwnerSeat : r.Victim; var recipient = giving ? r.Recipient : f.OwnerSeat;
        if (r.Selected.Any(id => _cardZones.GetLocation(id) is not { Zone: CardZoneKind.Hand or CardZoneKind.Equipment, OwnerSeat: { } owner } || owner != cardOwner)) throw new InvalidOperationException("Xianniang lost its unpaid selected cards.");
        if (r.Selected.Count == 0) { if (giving) SettleHouCheng(f); else { ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Stage = AnniversaryHouChengStage.Recipient, Obtained = 0 } }); SettleHouCheng(f); } return; }
        var before = _movementSequence; var reason = new CardMoveReason(giving ? HouChengGiveReason : HouChengObtainReason);
        ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Stage = giving ? AnniversaryHouChengStage.GiveChildren : AnniversaryHouChengStage.ObtainChildren, Before = before, After = before, Moves = [], BatchId = 0 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        void Paid(long batch, IReadOnlyList<CardMovementRecord> moves)
        {
            var active = GetActiveProgramFrame(f.Id); var current = active.AnniversaryHouCheng!;
            var count = moves.Count(m => m.To == CardLocation.Hand(recipient) && m.From != m.To);
            ReplaceRuntimeTop(active with { AnniversaryHouCheng = current with { BatchId = batch, Moves = moves, After = _movementSequence, Obtained = giving ? current.Obtained : count } });
            AdvanceEventRulesAndQueueFact(new AnniversaryHouChengMovementEvent(f.Id, r.Source, r.Hash, r.Round, r.Turn, r.TurnOwner, giving, cardOwner, recipient, count, before, _movementSequence, batch));
        }
        MoveProgramCardsFromMultipleSources(r.Selected, CardLocation.Hand(recipient), reason, giving ? NativeGiveCompletionRecords(f.Id, cardOwner, recipient, r.Selected, before, Paid) : Paid, disclosure: CardMovementDisclosure.FaceDown);
        ContinueHouCheng(f.Id); AdvanceRulesAndPublishState();
    }
    private bool DrainHouCheng(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void SettleHouCheng(ProgramSkillFrame f)
    {
        var r = f.AnniversaryHouCheng!;
        ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Stage = AnniversaryHouChengStage.HpChildren, Selected = [] }, PendingMovementContinuation = null }); ContinueHouCheng(f.Id);
    }
    private int HouChengRoundGain(int owner, int round) => ProgramEventHistory<AnniversaryHouChengMovementEvent>().Where(e => !e.Giving && e.Source.OwnerSeat == owner && e.Round == round && ValidHouChengMovement(e)).Sum(e => e.ActualCount);
    private void ContinueHouCheng(long id)
    {
        var f = GetActiveProgramFrame(id); if (!ValidHouCheng(f)) throw new InvalidOperationException("Xianniang lost its original movement or damage receipt.");
        var r = f.AnniversaryHouCheng!; if (DrainHouCheng(f)) return;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); r = f.AnniversaryHouCheng!;
        if (r.Stage == AnniversaryHouChengStage.ObtainChildren)
        {
            if (r.Obtained == 0) { SettleHouCheng(f); return; }
            ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = r with { Stage = AnniversaryHouChengStage.Recipient, Selected = [], Moves = [], Before = 0, After = 0, BatchId = 0 } }); PublishHouCheng(f); return;
        }
        if (r.Stage == AnniversaryHouChengStage.GiveChildren) { SettleHouCheng(f); return; }
        if (r.Stage != AnniversaryHouChengStage.HpChildren) { PublishHouCheng(f); return; }
        if (!r.HpPaid && _players[f.OwnerSeat].IsAlive && HouChengRoundGain(f.OwnerSeat, r.Round) > 2)
        {
            var result = ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1, (before, after, actual) =>
            {
                var active = GetActiveProgramFrame(f.Id); ReplaceRuntimeFrame(active.Id, active with { AnniversaryHouCheng = active.AnniversaryHouCheng! with { HpPaid = true, HpBefore = before, HpAfter = after } });
                AdvanceEventRulesAndQueueFact(new AnniversaryHouChengHpPaidEvent(f.Id, before, after, actual));
            });
            if (result == SkillProgramStepOutcome.AwaitChild || DrainHouCheng(GetActiveProgramFrame(f.Id))) return;
        }
        f = GetActiveProgramFrame(id); AdvanceEventRulesAndQueueFact(new AnniversaryHouChengCompletedEvent(id));
        ReplaceRuntimeTop(f = f with { AnniversaryHouCheng = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 47100)] private bool ResumeHouCheng(long id)
    { if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHouCheng: not null } f || f.Id != id) return false; ContinueHouCheng(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47100)] private bool ReturnHouChengMovement(ProgramSkillFrame f) => f.AnniversaryHouCheng is not null && ResumeHouCheng(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47100)] private bool RetainHouCheng(ProgramSkillFrame f) => f.AnniversaryHouCheng is not null && ValidHouCheng(f);
}
