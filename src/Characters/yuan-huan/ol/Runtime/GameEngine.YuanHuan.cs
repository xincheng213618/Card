using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DeruTakeReason = "skill-program.yuan-huan.deru.take", DeruFillReason = "skill-program.yuan-huan.deru.fill",
        DeruRecoverReason = "skill-program.yuan-huan.deru.recover", LinjieDiscardReason = "skill-program.yuan-huan.linjie.discard", LinjieDrawReason = "skill-program.yuan-huan.linjie.draw";
    private static CardConversionSource YuanHuanSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static int DeruNameBit(CardKind kind) => ProgramBasicCardName(kind) switch { CardKind.Slash => 1, CardKind.Dodge => 2, CardKind.Peach => 4, CardKind.Alcohol => 8, _ => 0 };
    private int DeruActualNames(int seat) => GetHand(_players[seat]).Aggregate(0, (mask, card) => mask | DeruNameBit(AdvancedEffectiveHandKind(_players[seat], card)));
    private IReadOnlyList<PromptChoice> LinjieCards(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.YuanHuan!.TargetSeat, f.YuanHuan.TargetSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard);
    [ProgramCapability]
    private sealed class YuanHuanCapability(GameEngine engine) : IYuanHuanHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginYuanHuan(f, op); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26920)]
    private bool QualifyYuanHuan(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveLinjieSourceHandDiscard)) return true;
        return context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: { } target, Amount: > 0 } &&
            CurrentDamageAttempt is { IsSourceLess: false, DamageWasApplied: true } attack && attack.SourceSeat == source && attack.TargetSeat == target && attack.DamageAmount == context.Amount &&
            target == c.OwnerSeat && IsValidPlayerSeat(source) && _players[source].IsAlive && GetHand(_players[source]).Any(card =>
                !IsSelfHandCategoryDiscardForbidden(source, card, CardLocation.Hand(source), OwnedCardMoveIntent.Discard));
    }
    private SkillProgramStepOutcome BeginYuanHuan(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.YuanHuan is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0)
            throw new InvalidOperationException("YuanHuan requires a new exact zero-card initial instruction.");
        var deru = op == SkillProgramEffectOp.ResolveDeruBasicNames;
        int target; long parent = 0; long? damage = null; var amount = 0;
        if (deru)
        {
            var a = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Activation;
            if (a is null || f.TriggerId is not null || f.WindowContext is not null || f.SelectedTargetSeats is not [var seat] || seat == f.OwnerSeat ||
                !IsValidPlayerSeat(seat) || !_players[seat].IsAlive || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
                _programPhaseUses.GetValueOrDefault((f.OwnerSeat, f.SkillId, a.UsageGroup)) != 1)
                throw new InvalidOperationException("Deru lost its actual once-per-Play activation or other living target.");
            YuanHuanComposition.Activation(f.SkillId, a); target = seat;
        }
        else
        {
            if (f.TriggerId is null || f.WindowContext is not { SourceSeat: { } source } context ||
                !QualifyYuanHuan(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), GetProgramTrigger(f), context))
                throw new InvalidOperationException("Linjie requires its actual damage source with a discardable hand card.");
            target = source; parent = context.ParentFrameId; damage = context.DamageFrameId; amount = context.Amount;
        }
        var r = new YuanHuanReceipt(YuanHuanSource(f), f.GameplayHash, op, f.InstructionIndex, _turnNumber, _cardUseDebitPhaseInstanceId,
            target, parent, damage, amount, deru ? YuanHuanStage.Guess : YuanHuanStage.SourceDiscard, Movements: []);
        ReplaceRuntimeTop(f = f with { YuanHuan = r });
        AdvanceEventRulesAndQueueFact(new YuanHuanStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ActualTurn, r.PhaseInstance, target, parent, damage, amount));
        PublishYuanHuanChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> YuanHuanChoices(ProgramSkillFrame f)
    {
        var r = f.YuanHuan!;
        if (r.Stage == YuanHuanStage.Guess)
        {
            var names = new[] { "杀", "闪", "桃", "酒" };
            return Array.AsReadOnly(Enumerable.Range(0, 16).Select(mask => new PromptChoice(new($"deru.{f.Id}.guess.{mask}"),
                "猜测有：" + (mask == 0 ? "均没有" : string.Join("、", names.Where((_, index) => (mask & (1 << index)) != 0))), [], [],
                new Dictionary<string,string> { ["program-action"] = "yuan-huan", ["guess-mask"] = mask.ToString(CultureInfo.InvariantCulture) })).ToArray());
        }
        if (r.Stage == YuanHuanStage.SourceDiscard)
            return Array.AsReadOnly(LinjieCards(f).Select(c => new PromptChoice(new($"linjie.{f.Id}.discard.{c.Cards.Single()}"), "临节：弃置【" + GetAdvancedCard(c.Cards.Single()).DisplayName + "】", c.Cards, [],
                new Dictionary<string,string> { ["program-action"] = "yuan-huan", ["discard"] = "true" })).ToArray());
        return [];
    }
    private void PublishYuanHuanChoice(ProgramSkillFrame f)
    {
        var r = f.YuanHuan!; var choices = YuanHuanChoices(f);
        if (choices.Count == 0) { FinishYuanHuan(f); return; }
        var chooser = r.Stage == YuanHuanStage.Guess ? f.OwnerSeat : r.TargetSeat; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == YuanHuanStage.Guess ? "德辱：分别猜测其手牌是否有杀、闪、桃、酒。未选的牌名表示猜测没有。" : "临节：弃置一张自己的手牌。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("yuan-huan")]
    private void AnswerYuanHuan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YuanHuan: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == YuanHuanStage.Guess ? f.OwnerSeat : r.TargetSeat) || !YuanHuanChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("YuanHuan requires the exact chooser's published private guess or owned hand discard.");
        AssertYuanHuan(f); ClearPendingDecision();
        if (r.Stage == YuanHuanStage.Guess)
        {
            var guess = int.Parse(choice.Parameters["guess-mask"], CultureInfo.InvariantCulture); var actual = DeruActualNames(r.TargetSeat);
            ReplaceRuntimeTop(f = f with { YuanHuan = r with { GuessMask = guess, ActualMask = actual, Stage = YuanHuanStage.RecoveryChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new DeruGuessCommittedEvent(f.Id)); var before = _movementSequence;
            if ((guess ^ actual) != 15 && _players[f.OwnerSeat].IsAlive) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
            RecordYuanHuanStep(f.Id, DeruRecoverReason, before, 0); ContinueYuanHuan(f.Id); return;
        }
        var card = GetAdvancedCard(choice.Cards.Single()); var sequence = _movementSequence;
        ReplaceRuntimeTop(f = f with { YuanHuan = r with { Stage = YuanHuanStage.DiscardChildren, DiscardedKind = AdvancedEffectiveHandKind(_players[r.TargetSeat], card), DiscardedPrintedKind = card.Kind }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, r.TargetSeat, card, CardLocation.Hand(r.TargetSeat), new(LinjieDiscardReason));
        RecordYuanHuanStep(f.Id, LinjieDiscardReason, sequence, 1, r.TargetSeat, card.Id, CardLocation.Hand(r.TargetSeat), CardLocation.DiscardPile);
        ContinueYuanHuan(f.Id);
    }
    private void RecordYuanHuanStep(long id, string reason, long before, int actual, int? subject = null, int? card = null, CardLocation? from = null, CardLocation? to = null)
    {
        var f = GetActiveProgramFrame(id); var r = f.YuanHuan!;
        var m = new YuanHuanMovement(r.Movements!.Count, subject ?? f.OwnerSeat, reason, before, _movementSequence, actual, card, from, to);
        ReplaceRuntimeTop(f with { YuanHuan = r with { Movements = r.Movements.Append(m).ToArray() } });
        AdvanceEventRulesAndQueueFact(new YuanHuanStepIssuedEvent(id, r.Stage, m.Index, m.SubjectSeat, reason, before, m.After, actual, card, from, to));
    }
    private void IssueDeruTake(ProgramSkillFrame f)
    {
        var r = f.YuanHuan!; var candidates = r.GuessMask != r.ActualMask && _players[r.TargetSeat].IsAlive ? GetHand(_players[r.TargetSeat]).Where(c => DeruNameBit(AdvancedEffectiveHandKind(_players[r.TargetSeat], c)) != 0).Select(c => c.Id).ToArray() : [];
        var index = candidates.Length == 0 ? -1 : _random.Next(candidates.Length); var card = index < 0 ? null : GetAdvancedCard(candidates[index]); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { YuanHuan = r with { Stage = YuanHuanStage.TakeChildren, BasicCandidates = candidates, RandomIndex = index,
            TakenKind = card is null ? null : AdvancedEffectiveHandKind(_players[r.TargetSeat], card), TakenPrintedKind = card?.Kind }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (card is not null) MoveCard(card, CardLocation.Hand(r.TargetSeat), CardLocation.Hand(f.OwnerSeat), new(DeruTakeReason));
        RecordYuanHuanStep(f.Id, DeruTakeReason, before, card is null ? 0 : 1, card: card?.Id,
            from: card is null ? null : CardLocation.Hand(r.TargetSeat), to: card is null ? null : CardLocation.Hand(f.OwnerSeat));
    }
    private void IssueYuanHuanDraw(ProgramSkillFrame f, bool linjie)
    {
        var r = f.YuanHuan!; var count = linjie ? (r.DiscardedKind is { } kind && IsSlashCard(kind) ? 1 : 0) : r.GuessMask == r.ActualMask ? Math.Max(0, _players[f.OwnerSeat].Hp - GetHand(_players[f.OwnerSeat]).Count) : 0;
        var reason = linjie ? LinjieDrawReason : DeruFillReason; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { YuanHuan = r with { Stage = linjie ? YuanHuanStage.LinjieDrawChildren : YuanHuanStage.FillChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var actual = count > 0 ? DrawCards(_players[f.OwnerSeat], count, true, new(reason)).Count : 0;
        RecordYuanHuanStep(f.Id, reason, before, actual);
    }
    private bool DrainYuanHuanChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueYuanHuan(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { YuanHuan: { } r } f && f.Id == id)
        {
            AssertYuanHuan(f);
            if (r.Stage is YuanHuanStage.Guess or YuanHuanStage.SourceDiscard) { if (_pendingDecision is null) PublishYuanHuanChoice(f); return; }
            if (DrainYuanHuanChildren(id)) return;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishYuanHuan(f); return; }
            if (r.Stage == YuanHuanStage.RecoveryChildren) IssueDeruTake(f);
            else if (r.Stage is YuanHuanStage.TakeChildren or YuanHuanStage.DiscardChildren) IssueYuanHuanDraw(f, r.Stage == YuanHuanStage.DiscardChildren);
            else { FinishYuanHuan(f); return; }
        }
    }
    private void FinishYuanHuan(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new YuanHuanCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { YuanHuan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26920)]
    private bool ResumeYuanHuan(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YuanHuan: not null } f || f.Id != id) return false; ContinueYuanHuan(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26920)]
    private bool ReturnYuanHuanMovement(ProgramSkillFrame f) { if (f.YuanHuan is null) return false; ContinueYuanHuan(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveDeruBasicNames, SkillProgramEffectOp.ResolveLinjieSourceHandDiscard)]
    private PromptChoice ChooseYuanHuan(PendingDecision p, ProgramSkillFrame f) => f.YuanHuan!.Stage == YuanHuanStage.Guess ?
        p.Choices.Single(c => c.Parameters["guess-mask"] == (CreateSnapshot(f.OwnerSeat).Players[f.YuanHuan.TargetSeat].HandCount == 0 ? "0" : "15")) :
        p.Choices.OrderBy(c => c.Cards.Single()).First();
}
