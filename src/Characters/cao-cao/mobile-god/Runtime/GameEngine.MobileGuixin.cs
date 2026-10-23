using System.Collections.ObjectModel;
using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MobileGuixinReason = "skill-program.mobile:guixin.obtain";
    private static readonly CardZoneKind[] MobileGuixinZones = [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment];
    private static CardConversionSource MobileGuixinSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class MobileGuixinCapability(GameEngine e) : IMobileGuixinHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginMobileGuixin(f); }

    private SkillProgramStepOutcome BeginMobileGuixin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MobileGuixin is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out _) || damage.TargetSeat != f.OwnerSeat ||
            f.WindowContext!.OccurrenceIndex < 0 || f.WindowContext.OccurrenceIndex >= damage.Amount)
            throw new InvalidOperationException("Mobile Guixin requires its actual positive original damage-point owner candidate.");
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var r = new MobileGuixinReceipt(MobileGuixinSource(f), f.GameplayHash, window.Id, damage.Id, f.WindowContext.OccurrenceIndex, damage.Amount)
        {
            OtherSeats = _players.Where(p => p.Seat != f.OwnerSeat).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).Select(p => p.Seat).ToArray()
        };
        ReplaceRuntimeTop(f with { MobileGuixin = r });
        AdvanceEventRulesAndQueueFact(new MobileGuixinStartedEvent(f.Id, r.Source, r.GameplayHash, window.Id, damage.Id, r.OccurrenceIndex, damage.Amount, string.Join(",", r.OtherSeats)));
        ResumeMobileGuixin(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40300)] private bool ResumeMobileGuixin(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileGuixin: not null } initial || initial.Id != id) return false;
        while (_pendingDecision is null)
        {
            var f = GetActiveProgramFrame(id); RequireMobileGuixin(f); var r = f.MobileGuixin!;
            if (r.Stage is MobileGuixinStage.MovementChildren or MobileGuixinStage.TurnoverChildren)
            {
                if (DrainMobileGuixinChildren(f)) return true;
                if (r.Stage == MobileGuixinStage.TurnoverChildren) { FinishMobileGuixin(f, false); return true; }
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null,
                    MobileGuixin = r = r with { Stage = MobileGuixinStage.Choosing, Cursor = r.Cursor + 1 } });
            }
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
                r.Payments.Count == 0 && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            { FinishMobileGuixin(f, true); return true; }
            if (r.Cursor == r.OtherSeats.Count)
            {
                var before = _players[f.OwnerSeat].IsFaceDown;
                var after = ShenwuCannotTurnOver(_players[f.OwnerSeat]) ? before : !before;
                ReplaceRuntimeTop(f = f with { MobileGuixin = r with { Stage = MobileGuixinStage.TurnoverChildren,
                    TurnoverIssued = true, FaceDownBefore = before, FaceDownAfter = after } });
                TurnOverProgramTarget(id, f.OwnerSeat, f.OwnerSeat);
                AdvanceEventRulesAndQueueFact(new MobileGuixinTurnoverIssuedEvent(id, before, _players[f.OwnerSeat].IsFaceDown));
                continue;
            }
            var target = r.OtherSeats[r.Cursor];
            var choices = _players[target].IsAlive ? MobileGuixinChoices(f) : [];
            if (choices.Count == 0)
            {
                ReplaceRuntimeTop(f with { MobileGuixin = r with { Cursor = r.Cursor + 1 } });
                AdvanceEventRulesAndQueueFact(new MobileGuixinSeatSkippedEvent(id, r.Cursor, target));
                continue;
            }
            var skill = _contentRegistry.GetSkill(f.SkillId);
            ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
            _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "归心：获得该角色区域里的一张牌。",
                choices.SelectMany(c => c.Cards).Distinct().ToArray(), [target], f.OwnerSeat)
            { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = target, Choices = choices,
                SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
            _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        }
        return true;
    }
    private IReadOnlyList<PromptChoice> MobileGuixinChoices(ProgramSkillFrame f)
    {
        var r = f.MobileGuixin!;
        if (r.Stage != MobileGuixinStage.Choosing || r.Cursor >= r.OtherSeats.Count) return [];
        var target = r.OtherSeats[r.Cursor];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, target, MobileGuixinZones, OwnedCardMoveIntent.Obtain)
            .Where(c => c.Parameters.GetValueOrDefault("source-zone") != nameof(CardZoneKind.Judgment) ||
                !c.Cards.Any(id => _resolutionStack.OfType<JudgmentFrame>().Any(j => j.TargetSeat == target && j.CardId == id)))
            .Select(c => c with { Id = new("mobile-guixin." + f.Id + "." + r.Cursor + "." + c.Id.Value),
                Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(c.Parameters)
                { ["program-action"] = "mobile-guixin", ["cursor"] = r.Cursor.ToString(CultureInfo.InvariantCulture) }) }).ToArray());
    }
    [ProgramAnswer("mobile-guixin")] private void AnswerMobileGuixin(PromptChoice chosen)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Mobile Guixin lost its active original frame.");
        RequireMobileGuixin(f); var r = f.MobileGuixin!;
        var exact = MobileGuixinChoices(f).SingleOrDefault(c => c.Id == chosen.Id && c.Cards.SequenceEqual(chosen.Cards) &&
            c.Targets.SequenceEqual(chosen.Targets) && c.Parameters.OrderBy(p => p.Key).SequenceEqual(chosen.Parameters.OrderBy(p => p.Key)))
            ?? throw new InvalidOperationException("Mobile Guixin requires its exact current public card or opaque hand slot.");
        var target = r.OtherSeats[r.Cursor];
        if (!_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive || r.Payments.Count == 0 &&
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Mobile Guixin lost an unpaid live participant or its original instance.");
        var zone = Enum.Parse<CardZoneKind>(exact.Parameters["source-zone"]);
        var slot = int.Parse(exact.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var from = new CardLocation(zone, target);
        var cards = zone switch
        {
            CardZoneKind.Hand => GetHand(_players[target]), CardZoneKind.Equipment => GetEquipment(_players[target]),
            CardZoneKind.Judgment => GetJudgment(_players[target]), _ => throw new InvalidOperationException("Unsupported Guixin area.")
        };
        var card = cards[slot]; var before = _movementSequence;
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect,
            MobileGuixin = r with { Stage = MobileGuixinStage.MovementChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), new(MobileGuixinReason), (batch, movements) =>
        {
            var parent = GetActiveProgramFrame(f.Id);
            var payment = new MobileGuixinPayment(r.Cursor, target, card.Id, from, batch, before, _movementSequence) { Movements = movements };
            ReplaceRuntimeTop(parent with { MobileGuixin = parent.MobileGuixin! with { Payments = parent.MobileGuixin.Payments.Append(payment).ToArray() } });
            AdvanceEventRulesAndQueueFact(new MobileGuixinMovementPaidEvent(f.Id, r.Cursor, target, batch, movements.Count, before, _movementSequence));
        }, disclosure: zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        ResumeMobileGuixin(f.Id);
    }
    private bool DrainMobileGuixinChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void FinishMobileGuixin(ProgramSkillFrame f, bool cancelled)
    {
        var r = f.MobileGuixin!;
        if (f.PendingMovementContinuation is not null || r.Stage == MobileGuixinStage.MovementChildren)
            throw new InvalidOperationException("Mobile Guixin cannot retire before its paid movement child returns.");
        ReplaceRuntimeTop(f = f with { MobileGuixin = r with { Stage = MobileGuixinStage.Finished } });
        AdvanceEventRulesAndQueueFact(new MobileGuixinCompletedEvent(f.Id, r.Payments.Count, r.TurnoverIssued, cancelled));
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40300)] private bool ReturnMobileGuixinMovement(ProgramSkillFrame f) => f.MobileGuixin is not null && ResumeMobileGuixin(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40300)] private bool RetainMobileGuixinPaid(ProgramSkillFrame f) =>
        f.MobileGuixin is { } r && (r.Payments.Count > 0 || r.TurnoverIssued) && ValidMobileGuixin(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShenCaoCaoGuixin)] private PromptChoice AiMobileGuixin(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.First();
}
