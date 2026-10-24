using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OldHandDispositionReason = "program.old-hand-disposition";
    // Movement lineage distinguishes a real acquisition from a temporary trip
    // through Processing (for example a cancelled use returning to its owner).
    private int[] OldHandDispositionIds(int owner, int turn) => GetHand(_players[owner])
        .Where(card => !_cardMovements.Any(m => m.CardId == card.Id && m.TurnNumber == turn &&
            m.To == CardLocation.Hand(owner) && ActualHandGainOriginalSource(m) != CardLocation.Hand(owner)))
        .Select(c => c.Id).Order().ToArray();
    private bool OldHandDispositionCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) &&
        EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p => p.Id == f.SkillId && p.GameplayHash == f.GameplayHash);
    private SkillProgramStepOutcome ReplaceDiscardWithOldHandDisposition(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!HalfHandDebtLifecycleMatches(f, SkillProgramTriggerWindow.DiscardPhaseStarting) ||
            _phase != TurnPhase.Discard || f.OldHandDisposition is not null)
            throw new InvalidOperationException("Old-hand replacement lost its exact initial discard parent.");
        var r = new OldHandDispositionReceipt { ActualTurn = _turnNumber, ParentFrameId = f.WindowContext!.ParentFrameId,
            InstructionIndex = f.InstructionIndex, Source = new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId),
            GameplayHash = f.GameplayHash, OldIds = OldHandDispositionIds(f.OwnerSeat, _turnNumber) };
        ReplaceRuntimeTop(f = f with { OldHandDisposition = r });
        _pendingTurnDelayedEffects |= DelayedTurnEffects.SkipDiscardPhase;
        if (r.OldIds.Count == 0 || !_players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat))
        { FinishOldHandDisposition(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishOldHandDisposition(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private static Dictionary<string,string> OldHandParameters(ProgramSkillFrame f, string branch) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_OldHandDisposition.OldHandParameters(f,branch);
    private int[] RemainingOldHandIds(ProgramSkillFrame f) => f.OldHandDisposition!.OldIds.Except(f.OldHandDisposition.DisposedIds)
        .Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat)).ToArray();
    private IReadOnlyList<PromptChoice> OldHandChoices(ProgramSkillFrame f)
    {
        var r = f.OldHandDisposition!; var others = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).OrderBy(p => p.Seat).ToArray();
        var choices = new List<PromptChoice>();
        if (r.Stage == "mode")
        {
            choices.Add(new(new($"old-hand.{f.Id}.give"), "将所有旧手牌分配给其他角色", [], [], OldHandParameters(f,"give")));
            choices.Add(new(new($"old-hand.{f.Id}.discard"), "弃置旧手牌，再弃置一名其他角色至多等量张牌", [], [], OldHandParameters(f,"discard")));
        }
        else if (r.Stage == "give")
            foreach (var id in RemainingOldHandIds(f)) foreach (var p in others)
                choices.Add(new(new($"old-hand.{f.Id}.{id}.{p.Seat}"), $"将【{GetHand(_players[f.OwnerSeat]).Single(c => c.Id == id).DisplayName}】交给 {p.Name}",
                    [id], [p.Seat], OldHandParameters(f,"card-give")));
        else if (r.Stage == "target")
            foreach (var p in others)
                choices.Add(new(new($"old-hand.{f.Id}.target.{p.Seat}"), $"弃置 {p.Name} 的牌（至多{r.ActualDiscardCount}张）", [], [p.Seat], OldHandParameters(f,"target")));
        else if (r.Stage == "foreign" && r.TargetSeat is { } target && _players[target].IsAlive && r.ForeignDiscardCount < r.ActualDiscardCount)
            choices.AddRange(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, target,
                [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment], OwnedCardMoveIntent.Discard)
                .Select(c => { var ps = c.Parameters.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                    ps["program-action"] = "old-hand-disposition"; ps["branch"] = "foreign-discard";
                    return new PromptChoice(c.Id,c.Description,c.Cards,c.Targets,ps); }));
        if (r.Stage is "target" or "foreign")
            choices.Add(new(new($"old-hand.{f.Id}.finish"), "结束弃牌（至多等量）", [], [], OldHandParameters(f,"finish")));
        return r.Stage == "give" ? PresentCardDistribution(f, choices, "old-hand") : Array.AsReadOnly(choices.ToArray());
    }
    private void PublishOldHandDisposition(ProgramSkillFrame f)
    {
        if (!OldHandDispositionCurrent(f)) { FinishOldHandDisposition(f); return; }
        var choices = OldHandChoices(f);
        if (choices.Count == 0) { FinishOldHandDisposition(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "素俭：处理非本回合获得的手牌。",
            choices.SelectMany(c=>c.Cards).Distinct().ToArray(), choices.SelectMany(c=>c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId,skill.Name,skill.Name,skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("old-hand-disposition")]
    private void ResolveOldHandDisposition(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OldHandDisposition: not null } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !OldHandDispositionCurrent(f) ||
            !AssistedChoicesEqual([selected], OldHandChoices(f).Where(c=>c.Id==selected.Id).ToArray()))
            throw new InvalidOperationException("Old-hand disposition lost its current exact private choice.");
        AssertOldHandDisposition(f); ClearPendingDecision(); var r = f.OldHandDisposition!;
        switch (selected.Parameters["branch"])
        {
            case "give":
                ReplaceRuntimeTop(f = f with { OldHandDisposition = r with { Stage = "give" } }); PublishOldHandDisposition(f); return;
            case "discard":
                PayOldHandDisposition(f, RemainingOldHandIds(f), CardLocation.DiscardPile, "target", null); return;
            case "card-give":
                PayOldHandDisposition(f, selected.Cards, CardLocation.Hand(selected.Targets.Single()), "give", selected.Targets.Single()); return;
            case "target":
                ReplaceRuntimeTop(f = f with { OldHandDisposition = r with { Stage = "foreign", TargetSeat = selected.Targets.Single() } });
                PublishOldHandDisposition(f); return;
            case "foreign-discard":
                if (r.TargetSeat is not { } victim || !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
                    !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
                    throw new InvalidOperationException("Old-hand foreign discard lost its current source slot.");
                var cards = _cardZones.CardsAt(new(zone,victim));
                if (slot < 0 || slot >= cards.Count) throw new InvalidOperationException("Foreign discard slot no longer exists.");
                PayOldHandDisposition(f, [cards[slot].Id], CardLocation.DiscardPile, "foreign", victim); return;
            case "finish": FinishOldHandDisposition(f); return;
            default: throw new InvalidOperationException("Unknown old-hand disposition branch.");
        }
    }
    private void PayOldHandDisposition(ProgramSkillFrame f, IReadOnlyList<int> ids, CardLocation destination, string next, int? target)
    {
        var r = f.OldHandDisposition!;
        if (ids.Count == 0) { FinishOldHandDisposition(f); return; }
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { OldHandDisposition = r with { Stage = "movement", ReturnStage = next,
            TargetSeat = target, PaidIds = ids, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        var logicalDiscard = destination == CardLocation.DiscardPile
            ? CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, f.OwnerSeat, ids, new(OldHandDispositionReason), f.Id) : null;
        MoveProgramCardsFromMultipleSources(ids, destination, new(OldHandDispositionReason), (next=="give" ? NativeGiveCompletionRecords(f.Id,f.OwnerSeat,target!.Value,ids,_movementSequence,(_,records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = active.OldHandDisposition!;
            var actual = records.Count(m=>m.To==destination || next=="foreign" && m.To==CardLocation.OutsideGame);
            paid = paid with { After = _movementSequence,
                DisposedIds = next is "give" or "target" ? paid.DisposedIds.Concat(ids).Distinct().ToArray() : paid.DisposedIds,
                ActualDiscardCount = next == "target" ? actual : paid.ActualDiscardCount,
                ForeignDiscardCount = next == "foreign" ? paid.ForeignDiscardCount + actual : paid.ForeignDiscardCount };
            ReplaceRuntimeTop(active with { OldHandDisposition = paid });
            AdvanceEventRulesAndQueueFact(new OldHandDispositionPaymentEvent(f.Id,paid.Source,paid.GameplayHash,paid.ActualTurn,next,target,
                ids,before,paid.After,actual));
        }) : (_,records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = active.OldHandDisposition!;
            var actual = records.Count(m=>m.To==destination || next=="foreign" && m.To==CardLocation.OutsideGame);
            paid = paid with { After = _movementSequence,
                DisposedIds = next is "give" or "target" ? paid.DisposedIds.Concat(ids).Distinct().ToArray() : paid.DisposedIds,
                ActualDiscardCount = next == "target" ? actual : paid.ActualDiscardCount,
                ForeignDiscardCount = next == "foreign" ? paid.ForeignDiscardCount + actual : paid.ForeignDiscardCount };
            ReplaceRuntimeTop(active with { OldHandDisposition = paid });
            AdvanceEventRulesAndQueueFact(new OldHandDispositionPaymentEvent(f.Id,paid.Source,paid.GameplayHash,paid.ActualTurn,next,target,
                ids,before,paid.After,actual));
        }));
        CompleteNativeLogicalDiscardInvocation(logicalDiscard);
        if (!TryBeginCardsMovedProgramWindow(f.Id)) ReturnRuntimeProgramMovement(f.Id);
    }
    private SkillProgramEffect? OldHandPausedEffect(ProgramSkillFrame f) => f.InstructionIndex < 1 ? null :
        ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 260)]
    private bool IsOldHandDispositionMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition && f.OldHandDisposition is { Stage: "movement" } r &&
        r.InstructionIndex == f.InstructionIndex && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Movement, 21701)]
    private bool ReturnOldHandDispositionMovement(ProgramSkillFrame f)
    {
        if (f.OldHandDisposition is not { } r) return false;
        if (f.PendingMovementContinuation is not { } p || !IsOldHandDispositionMovement(f,OldHandPausedEffect(f),p))
            throw new InvalidOperationException("Old-hand disposition consumed an unrelated movement return.");
        AssertOldHandDisposition(f);
        if (GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, OldHandDisposition = r with { Stage = r.ReturnStage } });
        if (!OldHandDispositionCurrent(f) || r.ReturnStage == "give" && RemainingOldHandIds(f).Length == 0 ||
            r.ReturnStage == "target" && r.ActualDiscardCount == 0 || r.ReturnStage == "foreign" &&
            (r.ForeignDiscardCount >= r.ActualDiscardCount || r.TargetSeat is not { } target || !_players[target].IsAlive))
            FinishOldHandDisposition(f);
        else PublishOldHandDisposition(f);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21701)]
    private bool ResumeOldHandDisposition(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OldHandDisposition: not null } f || f.Id != id) return false;
        AssertOldHandDisposition(f);
        if (GainGiftHasFinalGameEnd()) return true;
        if (f.PendingMovementContinuation is not null) { if (!TryDrainFireTargetMovement(f)) ReturnOldHandDispositionMovement(f); return true; }
        if (!OldHandDispositionCurrent(f)) { ClearPendingDecision(); FinishOldHandDisposition(f); return true; }
        if (_pendingDecision is null) PublishOldHandDisposition(f);
        return true;
    }
    private void FinishOldHandDisposition(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { OldHandDisposition = null, PendingMovementContinuation = null }); FinishProgramSkill(f,true); }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 520)]
    private void AssertOldHandDisposition(ProgramSkillFrame f)
    {
        if (f.OldHandDisposition is not { } r) return;
        if (r.ActualTurn != _turnNumber || r.ParentFrameId != f.WindowContext?.ParentFrameId || r.Source != new CardConversionSource(f.SkillId,f.TriggerId!,f.OwnerSeat,f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || OldHandPausedEffect(f)?.Op != SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition ||
            !HalfHandDebtLifecycleMatches(f,SkillProgramTriggerWindow.DiscardPhaseStarting) || r.OldIds.Distinct().Count()!=r.OldIds.Count ||
            r.DisposedIds.Any(id=>!r.OldIds.Contains(id)) || r.DisposedIds.Distinct().Count()!=r.DisposedIds.Count ||
            r.Stage is not ("mode" or "give" or "target" or "foreign" or "movement") || r.ForeignDiscardCount > r.ActualDiscardCount ||
            r.ActualDiscardCount < 0 || r.ForeignDiscardCount < 0)
            throw new InvalidOperationException("Old-hand disposition lost its frame-owned identity or exact progress.");
        if (r.Stage == "movement")
        {
            var destination = r.ReturnStage == "give" && r.TargetSeat is { } recipient ? CardLocation.Hand(recipient) : CardLocation.DiscardPile;
            var expectedOwner = r.ReturnStage == "foreign" ? r.TargetSeat : f.OwnerSeat;
            var paidMovements = _cardMovements.Where(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.Reason.Value==OldHandDispositionReason).ToArray();
            if (r.PaidIds.Count == 0 || r.Before > r.After || r.ReturnStage is not ("give" or "target" or "foreign") ||
                r.ReturnStage is "give" or "foreign" && (r.TargetSeat is not { } target || target==f.OwnerSeat || !IsValidPlayerSeat(target)) ||
                r.PaidIds.Distinct().Count()!=r.PaidIds.Count || paidMovements.Length!=r.PaidIds.Count ||
                paidMovements.Any(m=>!r.PaidIds.Contains(m.CardId) || m.From.OwnerSeat!=expectedOwner ||
                    (r.ReturnStage=="foreign" ? m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) : m.From!=CardLocation.Hand(f.OwnerSeat)) ||
                    m.To!=destination && !(r.ReturnStage=="foreign" && m.To==CardLocation.OutsideGame)) ||
                r.ReturnStage is "give" or "target" && r.PaidIds.Any(id=>!r.OldIds.Contains(id) || !r.DisposedIds.Contains(id)) ||
                CompleteProgramEventHistory().OfType<OldHandDispositionPaymentEvent>().Count(e=>e.FrameId==f.Id && e.Before==r.Before && e.After==r.After &&
                    e.Source==r.Source && e.GameplayHash==r.GameplayHash && e.ActualTurn==r.ActualTurn && e.ReturnStage==r.ReturnStage &&
                    e.TargetSeat==r.TargetSeat && e.CardIds.SequenceEqual(r.PaidIds) && e.ActualCount==paidMovements.Length) != 1)
                throw new InvalidOperationException("Old-hand disposition lost its actual paid movement receipt.");
        }
        else if (ReferenceEquals(f,_resolutionStack.LastOrDefault()) && _pendingDecision is { } decision &&
            (decision.PlayerSeat != f.OwnerSeat || !decision.IsPrivate || !AssistedChoicesEqual(decision.Choices,OldHandChoices(f))))
            throw new InvalidOperationException("Old-hand disposition published an invalid private prompt.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ReplaceDiscardWithOldHandDisposition)]
    private PromptChoice SelectAiOldHandDisposition(PendingDecision decision, ProgramSkillFrame f)
    {
        var r=f.OldHandDisposition!;
        if (r.Stage=="mode") return decision.Choices.First(c=>c.Parameters["branch"]==
            (_players.Any(p=>p.IsAlive && p.Seat!=f.OwnerSeat && AreProgramDistributionAllies(_players[f.OwnerSeat],p)) ? "give" : "discard"));
        if (r.Stage=="give") return decision.Choices.OrderByDescending(c=>AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets.Single()]))
            .ThenBy(c=>GetHand(_players[c.Targets.Single()]).Count).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
        if (r.Stage=="target") return decision.Choices.Where(c=>c.Targets.Count==1)
            .OrderBy(c=>AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets.Single()])).ThenBy(c=>c.Targets.Single()).First();
        return decision.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="foreign-discard") ?? decision.Choices.First();
    }
    [ProgramCapability]
    private sealed class OldHandDispositionCapability(GameEngine engine) : IOldHandDispositionHost
    { public SkillProgramStepOutcome ReplaceDiscardWithOldHandDisposition(ProgramSkillFrame f) => engine.ReplaceDiscardWithOldHandDisposition(f); }
}
