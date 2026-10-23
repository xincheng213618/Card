using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OlGuixinReason = "skill-program.ol:guixin.obtain";
    private static CardConversionSource OlGuixinSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private static IReadOnlyList<CardZoneKind> OlGuixinPriority(CardZoneKind preferred) => preferred switch
    {
        CardZoneKind.Hand => [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment],
        CardZoneKind.Equipment => [CardZoneKind.Equipment, CardZoneKind.Hand, CardZoneKind.Judgment],
        CardZoneKind.Judgment => [CardZoneKind.Judgment, CardZoneKind.Hand, CardZoneKind.Equipment],
        _ => throw new InvalidOperationException("OL Guixin requires one HEJ priority region.")
    };
    [ProgramCapability] private sealed class OlGuixinCapability(GameEngine e) : IOlGuixinHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginOlGuixin(f); }
    private SkillProgramStepOutcome BeginOlGuixin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OlGuixin is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out _) || damage.TargetSeat != f.OwnerSeat ||
            f.WindowContext!.OccurrenceIndex < 0 || f.WindowContext.OccurrenceIndex >= damage.Amount)
            throw new InvalidOperationException("OL Guixin requires its exact original positive damage-point candidate.");
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var r = new OlGuixinReceipt(OlGuixinSource(f), f.GameplayHash, window.Id, damage.Id, f.WindowContext.OccurrenceIndex, damage.Amount)
        { OtherSeats = _players.Where(p => p.Seat != f.OwnerSeat).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).Select(p => p.Seat).ToArray() };
        ReplaceRuntimeTop(f with { OlGuixin = r });
        AdvanceEventRulesAndQueueFact(new OlGuixinStartedEvent(f.Id, r.Source, r.GameplayHash, window.Id, damage.Id, r.OccurrenceIndex, damage.Amount, string.Join(",", r.OtherSeats)));
        ResumeOlGuixin(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private static IReadOnlyList<PromptChoice> OlGuixinPriorityChoices(long id) => Array.AsReadOnly(MobileGuixinZones.Select(zone =>
        new PromptChoice(new("ol-guixin." + id + "." + zone), zone switch
        { CardZoneKind.Hand => "优先手牌区", CardZoneKind.Equipment => "优先装备区", _ => "优先判定区" }, [], [],
            new Dictionary<string, string> { ["program-action"] = "ol-guixin-priority", ["frame-id"] = id.ToString(CultureInfo.InvariantCulture), ["priority-zone"] = zone.ToString() })).ToArray());
    [ProgramContinuation(ProgramContinuationKind.Resume, 40700)] private bool ResumeOlGuixin(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlGuixin: not null } initial || initial.Id != id) return false;
        while (_pendingDecision is null)
        {
            var f = GetActiveProgramFrame(id); RequireOlGuixin(f); var r = f.OlGuixin!;
            if (r.Stage is OlGuixinStage.MovementChildren or OlGuixinStage.TurnoverChildren)
            {
                if (DrainMobileGuixinChildren(f)) return true;
                if (r.Stage == OlGuixinStage.TurnoverChildren) { FinishOlGuixin(f, false); return true; }
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null,
                    OlGuixin = r = r with { Stage = OlGuixinStage.Visiting, Cursor = r.Cursor + 1 } });
            }
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || r.Payments.Count == 0 &&
                !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            { FinishOlGuixin(f, true); return true; }
            if (r.Stage == OlGuixinStage.PriorityChoice)
            {
                ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
                var skill = _contentRegistry.GetSkill(f.SkillId);
                _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "归心：统一选择本次获得牌的优先区域。", [], [], f.OwnerSeat)
                { PromptId = CreatePromptId(), Choices = OlGuixinPriorityChoices(id), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
                _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
                AdvanceRulesAndPublishState(); return true;
            }
            if (r.Cursor == r.OtherSeats.Count)
            {
                var before = _players[f.OwnerSeat].IsFaceDown;
                ReplaceRuntimeTop(f = f with { OlGuixin = r with { Stage = OlGuixinStage.TurnoverChildren, TurnoverIssued = true,
                    FaceDownBefore = before, FaceDownAfter = ShenwuCannotTurnOver(_players[f.OwnerSeat]) ? before : !before } });
                TurnOverProgramTarget(id, f.OwnerSeat, f.OwnerSeat);
                AdvanceEventRulesAndQueueFact(new OlGuixinTurnoverIssuedEvent(id, before, _players[f.OwnerSeat].IsFaceDown)); continue;
            }
            var target = r.OtherSeats[r.Cursor];
            var candidates = _players[target].IsAlive ? OlGuixinCandidates(f, target) : [];
            if (candidates.Count == 0)
            {
                ReplaceRuntimeTop(f with { OlGuixin = r with { Cursor = r.Cursor + 1 } });
                AdvanceEventRulesAndQueueFact(new OlGuixinSeatSkippedEvent(id, r.Cursor, target)); continue;
            }
            var zone = OlGuixinPriority(r.PreferredZone!.Value).First(z => candidates.Any(c => c.From.Zone == z));
            var selectedZone = candidates.Where(c => c.From.Zone == zone).ToArray();
            var randomBefore = _random.State;
            var selected = selectedZone[_random.Next(selectedZone.Length)];
            var randomAfter = _random.State;
            var beforeMove = _movementSequence;
            ReplaceRuntimeTop(f with { OlGuixin = r with { Stage = OlGuixinStage.MovementChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources([selected.CardId], CardLocation.Hand(f.OwnerSeat), new(OlGuixinReason), (batch, records) =>
            {
                var parent = GetActiveProgramFrame(id);
                var payment = new OlGuixinPayment(new(r.Cursor, target, selected.CardId, selected.From, batch, beforeMove, _movementSequence)
                    { Movements = records }, randomBefore, randomAfter) { Candidates = candidates };
                ReplaceRuntimeTop(parent with { OlGuixin = parent.OlGuixin! with { Payments = parent.OlGuixin.Payments.Append(payment).ToArray() } });
                AdvanceEventRulesAndQueueFact(new OlGuixinMovementPaidEvent(id, r.Cursor, target, zone, batch, beforeMove, _movementSequence));
            }, disclosure: zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        }
        return true;
    }
    private IReadOnlyList<OlGuixinCandidate> OlGuixinCandidates(ProgramSkillFrame f, int target) =>
        Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, target, MobileGuixinZones, OwnedCardMoveIntent.Obtain).Select(c =>
        {
            var zone = Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]);
            var slot = int.Parse(c.Parameters["slot-index"], CultureInfo.InvariantCulture);
            var cards = zone switch { CardZoneKind.Hand => GetHand(_players[target]), CardZoneKind.Equipment => GetEquipment(_players[target]),
                CardZoneKind.Judgment => GetJudgment(_players[target]), _ => throw new InvalidOperationException("Unsupported OL Guixin source.") };
            return new OlGuixinCandidate(cards[slot].Id, new(zone, target));
        }).Where(c => c.From.Zone != CardZoneKind.Judgment || !_resolutionStack.OfType<JudgmentFrame>().Any(j => j.TargetSeat == target && j.CardId == c.CardId))
            .OrderBy(c => c.CardId).ToArray());
    [ProgramAnswer("ol-guixin-priority")] private void AnswerOlGuixinPriority(PromptChoice chosen)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("OL Guixin lost its original frame.");
        RequireOlGuixin(f); var r = f.OlGuixin!;
        if (r.Stage != OlGuixinStage.PriorityChoice || !OlGuixinPriorityChoices(f.Id).Any(c => c.Id == chosen.Id && c.Cards.SequenceEqual(chosen.Cards) &&
            c.Targets.SequenceEqual(chosen.Targets) && c.Parameters.OrderBy(p => p.Key).SequenceEqual(chosen.Parameters.OrderBy(p => p.Key))))
            throw new InvalidOperationException("OL Guixin needs one published priority-area choice for this actual damage point.");
        var zone = Enum.Parse<CardZoneKind>(chosen.Parameters["priority-zone"]);
        ClearPendingDecision(); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.ResolvingEffect, OlGuixin = r with { PreferredZone = zone, Stage = OlGuixinStage.Visiting } });
        AdvanceEventRulesAndQueueFact(new OlGuixinPriorityChosenEvent(f.Id, zone)); ResumeOlGuixin(f.Id);
    }
    private void FinishOlGuixin(ProgramSkillFrame f, bool cancelled)
    {
        var r = f.OlGuixin!;
        if (f.PendingMovementContinuation is not null || r.Stage == OlGuixinStage.MovementChildren)
            throw new InvalidOperationException("OL Guixin cannot retire a paid gain before its children return.");
        ReplaceRuntimeTop(f = f with { OlGuixin = r with { Stage = OlGuixinStage.Finished } });
        AdvanceEventRulesAndQueueFact(new OlGuixinCompletedEvent(f.Id, r.Payments.Count, r.TurnoverIssued, cancelled)); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40700)] private bool ReturnOlGuixinMovement(ProgramSkillFrame f) => f.OlGuixin is not null && ResumeOlGuixin(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40700)] private bool RetainOlGuixinPaid(ProgramSkillFrame f) =>
        f.OlGuixin is { } r && (r.Payments.Count > 0 || r.TurnoverIssued) && ValidOlGuixin(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOlShenCaoCaoGuixin)] private PromptChoice AiOlGuixin(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
}
