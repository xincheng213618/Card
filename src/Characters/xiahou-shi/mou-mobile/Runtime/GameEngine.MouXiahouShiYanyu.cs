using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouXiahouShiDiscardReason = "skill-program.mou-mobile:yanyu.discard", MouXiahouShiDrawReason = "skill-program.mou-mobile:yanyu.Draw",
        MouXiahouShiSupportDrawReason = "skill-program.mou-mobile:yanyu.support.Draw", MouXiahouShiQiaoshiDrawReason = "skill-program.mou-mobile:qiaoshi.Draw";
    private bool HasMouXiahouShiCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi);
    private static CardConversionSource MouXiahouShiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static bool MouXiahouShiSlash(CardKind k) => k is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private int MouXiahouShiPhaseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouXiahouShiYanyuStartedEvent>()
        .Count(e => !e.Support && e.Source.OwnerSeat == owner && e.Source.SkillId == MouXiahouShiComposition.Yanyu && e.Phase == phase);
    private int MouXiahouShiTurnDiscards(int owner, MouXiahouShiTurnKey turn) => ProgramEventHistory<MouXiahouShiYanyuDiscardPaidEvent>()
        .Count(e => e.Source.OwnerSeat == owner && e.Source.SkillId == MouXiahouShiComposition.Yanyu && e.Turn == turn && ValidMouXiahouShiPaidHistory(e));
    [ProgramPhaseUsedProjection(40)]
    private int MouXiahouShiProjectedPhaseUses(int owner, string skill, int fallback) => skill == MouXiahouShiComposition.Yanyu &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && IsRecordedActualDiscardRecoveryPhase(phase)
        ? MouXiahouShiPhaseUses(owner, phase) : fallback;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 29800, SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard)]
    private bool CanStartMouXiahouShiYanyu(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == owner.Seat && IsRecordedActualDiscardRecoveryPhase(phase) && CurrentMouXiahouShiTurn() is not null && MouXiahouShiPhaseUses(owner.Seat, phase) < 2 &&
        new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Any(zone => _cardZones.CardsAt(new(zone, owner.Seat)).Any(c => MouXiahouShiSlash(c.Kind) &&
            !IsSelfHandCategoryDiscardForbidden(owner.Seat, c, new(zone, owner.Seat), OwnedCardMoveIntent.Discard)));
    private SkillProgramStepOutcome BeginMouXiahouShiDiscard(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouXiahouShiYanyu is not null || f.SkillId != MouXiahouShiComposition.Yanyu || f.ActivationId != "discard-slash" || f.TriggerId is not null ||
            f.InstructionIndex != 1 || f.WindowContext is not null || f.SelectedCardIds is not [var id] || f.SelectedTargetSeats.Count != 0 ||
            !CanStartMouXiahouShiYanyu(_players[f.OwnerSeat]) || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            CurrentActualDiscardRecoveryPhase() is not { } phase || CurrentMouXiahouShiTurn() is not { } turn ||
            _cardZones.GetLocation(id) is not { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } from || seat != f.OwnerSeat)
            throw new InvalidOperationException("Yanyu requires one currently owned payable Slash in the exact actual Play.");
        var card = GetAdvancedCard(id);
        if (!MouXiahouShiSlash(card.Kind) || IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard))
            throw new InvalidOperationException("Yanyu cannot recast or discard a forbidden non-Slash payment.");
        var before = _movementSequence;
        var r = new MouXiahouShiYanyuReceipt(MouXiahouShiSource(f), f.GameplayHash, turn, phase, false)
        { Stage = MouXiahouShiYanyuStage.Discarding, Before = before, After = before };
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouXiahouShiYanyuStartedEvent(f.Id, r.Source, r.GameplayHash, turn, phase, false, 0, 0));
        MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, from, new(MouXiahouShiDiscardReason));
        f = GetActiveProgramFrame(f.Id);
        var m = _cardMovements.Single(m => m.Sequence > before && m.CardId == id && m.From == from && m.Reason.Value == MouXiahouShiDiscardReason);
        var paid = new MouXiahouShiDiscardPayment(id, card.Kind, from, m.To, m.Sequence);
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = r with { Stage = MouXiahouShiYanyuStage.DiscardChildren, Payment = paid, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouXiahouShiYanyuDiscardPaidEvent(f.Id, r.Source, turn, phase, id, card.Kind, from, m.To, m.Sequence));
        SealMouXiahouShiMovements(f.Id, before); ContinueMouXiahouShi(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome BeginMouXiahouShiSupport(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouXiahouShiYanyu is not null || !ExactMouXiahouShiSupportContext(f) || CurrentActualDiscardRecoveryPhase() is not { } phase ||
            CurrentMouXiahouShiTurn() is not { } turn || MouXiahouShiTurnDiscards(f.OwnerSeat, turn) is not > 0)
            throw new InvalidOperationException("Yanyu support requires its current own native PlayEnding and real actual-turn paid Slashes.");
        var count = MouXiahouShiTurnDiscards(f.OwnerSeat, turn);
        var r = new MouXiahouShiYanyuReceipt(MouXiahouShiSource(f), f.GameplayHash, turn, phase, true)
        { Stage = MouXiahouShiYanyuStage.TargetChoice, WindowId = f.WindowContext!.ParentFrameId, FrozenDiscardCount = count, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = r });
        AdvanceEventRulesAndQueueFact(new MouXiahouShiYanyuStartedEvent(f.Id, r.Source, r.GameplayHash, turn, phase, true, r.WindowId, count));
        PublishMouXiahouShi(f, f.OwnerSeat, MouXiahouShiSupportChoices(f), $"燕语：令一名其他角色摸{checked(count * 3)}张牌。"); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouXiahouShiSupportChoices(ProgramSkillFrame f) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)
        .Select(p => new PromptChoice(new($"mou-xiahou-shi.{f.Id}.support.{p.Seat}"), $"令{p.Name}摸{checked(f.MouXiahouShiYanyu!.FrozenDiscardCount * 3)}张牌", [], [p.Seat], new Dictionary<string, string>
        { ["program-action"] = "mou-xiahou-shi-yanyu-support", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["target-seat"] = p.Seat.ToString(CultureInfo.InvariantCulture) })).ToArray());
    [ProgramAnswer("mou-xiahou-shi-yanyu-support")]
    private void AnswerMouXiahouShiSupport(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouXiahouShiYanyu!;
        if (!ValidMouXiahouShiYanyu(f) || !r.Support || r.Stage != MouXiahouShiYanyuStage.TargetChoice || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], MouXiahouShiSupportChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Yanyu support accepts only its exact living other target.");
        ClearPendingDecision(); var seat = selected.Targets.Single();
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = r with { TargetSeat = seat, Stage = MouXiahouShiYanyuStage.Drawing } });
        AdvanceEventRulesAndQueueFact(new MouXiahouShiYanyuTargetAcceptedEvent(f.Id, seat, r.FrozenDiscardCount, checked(r.FrozenDiscardCount * 3)));
        BeginOperationProgramDraw(f, seat, checked(r.FrozenDiscardCount * 3), new(MouXiahouShiSupportDrawReason));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard,
        SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport, SkillProgramEffectOp.ResolveMouXiahouShiQiaoshi)]
    private void ReturnMouXiahouShiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        var q = f.MouXiahouShiQiaoshi; var y = f.MouXiahouShiYanyu;
        var seat = q is not null ? q.DamageSourceSeat : y!.Support ? y.TargetSeat!.Value : f.OwnerSeat;
        var count = q is not null ? 2 : y!.Support ? checked(y.FrozenDiscardCount * 3) : 1;
        if (!ValidMouXiahouShi(f) || outcome.RecipientSeat != seat || outcome.RequestedCount != count ||
            q is not null && (q.Stage != MouXiahouShiQiaoshiStage.Drawing || q.Draw is not null) ||
            y is not null && (y.Stage != MouXiahouShiYanyuStage.Drawing || y.Draw is not null))
            throw new InvalidOperationException("Mou Xiahou Shi cannot repeat or redirect its issued native draw.");
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = y is null ? null : y with { Stage = MouXiahouShiYanyuStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter },
            MouXiahouShiQiaoshi = q is null ? null : q with { Stage = MouXiahouShiQiaoshiStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouXiahouShiDrawReturnedEvent(f.Id, seat, count, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        SealMouXiahouShiMovements(f.Id, outcome.SequenceBefore); ContinueMouXiahouShi(f.Id);
    }
    private void PublishMouXiahouShi(ProgramSkillFrame f, int chooser, IReadOnlyList<PromptChoice> choices, string text)
    {
        if (choices.Count == 0) throw new InvalidOperationException("Mou Xiahou Shi lost its actual legal choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    private void FinishMouXiahouShi(ProgramSkillFrame f)
    {
        var accepted = f.MouXiahouShiQiaoshi?.Branch == "recover-and-draw" || f.MouXiahouShiYanyu is { Payment: not null } or { TargetSeat: not null };
        AdvanceEventRulesAndQueueFact(new MouXiahouShiCompletedEvent(f.Id, f.SkillId, accepted));
        ReplaceRuntimeTop(f = f with { MouXiahouShiYanyu = null, MouXiahouShiQiaoshi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport)]
    private PromptChoice AiMouXiahouShiSupport(PendingDecision pending, ProgramSkillFrame f) => pending.Choices
        .OrderByDescending(c => _aiBrains[pending.PlayerSeat].ScoreProgramTarget(CreateSnapshot(pending.PlayerSeat), c.Targets.Single(), new(OwnerDraw: 0, OwnerRecovery: 0, OwnerHpLoss: 0,
            TargetDraw: checked(f.MouXiahouShiYanyu!.FrozenDiscardCount * 3), TargetRecovery: 0, TargetHpLoss: 0, GivesSelected: false, DiscardsSelected: false)))
        .ThenBy(c => c.Targets.Single()).First();
    [ProgramCapability] private sealed class MouXiahouShiCapability(GameEngine engine) : IMouXiahouShiHost
    {
        public SkillProgramStepOutcome Discard(ProgramSkillFrame f) => engine.BeginMouXiahouShiDiscard(f);
        public SkillProgramStepOutcome Support(ProgramSkillFrame f) => engine.BeginMouXiahouShiSupport(f);
        public SkillProgramStepOutcome Qiaoshi(ProgramSkillFrame f) => engine.BeginMouXiahouShiQiaoshi(f);
    }
}
