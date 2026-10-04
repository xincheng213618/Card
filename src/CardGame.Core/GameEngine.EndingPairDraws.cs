namespace CardGame.Core;
public sealed partial class GameEngine
{
    private long EndingPairSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private CardConversionSource EndingPairSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static string EndingPairRoundUsage(string state) => "ending-hand-divergence:" + state;
    private bool CanRunEndingPair(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        t.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal) is not { } e ||
        context.Window == SkillProgramTriggerWindow.TurnEnding && _roundNumber > 0 &&
        _skillRuntimeState.GetUsage(c.OwnerSeat, c.SkillId, EndingPairRoundUsage(e.StateId!), SkillUsageScope.Round) == 0;
    private bool ExactEndingPairParent(ProgramSkillFrame f, long? expected = null) =>
        f.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } context &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault() is { } ending &&
        ending.Id == context.ParentFrameId && (expected is null || ending.Id == expected) && ending.OwnerSeat == _currentSeat &&
        ending.TurnNumber == _turnNumber && context.SourceSeat == _currentSeat && context.TargetSeat == _currentSeat &&
        ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count && ending.Items[ending.ItemIndex].Candidate is { } candidate &&
        MountObserverCandidateMatches(f, candidate);
    private IReadOnlyList<int>? PublishedEndingPairActorForAi(CharacterState owner, IReadOnlyList<SkillProgramEffect> effects, ProgramSkillWindowContext? context)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal)) return null;
        return context is { Window: SkillProgramTriggerWindow.TurnEnding, SourceSeat: { } actor } && actor == _currentSeat &&
            IsValidPlayerSeat(actor) && _players[actor].IsAlive ? Array.AsReadOnly(new[] { actor }) : [];
    }
    private SkillProgramStepOutcome BeginEndingPairDraw(ProgramSkillFrame supplied, string state)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.EndingPairDraw is not null || f.InstructionIndex != 1 || _roundNumber < 1 || !ExactEndingPairParent(f) ||
            f.TriggerId is null || !_players[f.OwnerSeat].IsAlive || !_players[_currentSeat].IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, EndingPairRoundUsage(state), SkillUsageScope.Round) != 0)
            throw new InvalidOperationException("Ending pair drawing lost its original actual Ending/round/current actor or qualified source.");
        var r = new ProgramEndingPairDrawReceipt(f.InstructionIndex, state, EndingPairSource(f), f.GameplayHash,
            _turnNumber, _turnProgression.OwnerSeat, _roundNumber, f.WindowContext!.ParentFrameId, _currentSeat, 0, false);
        ReplaceRuntimeTop(f with { EndingPairDraw = r });
        AdvanceEventRulesAndQueueFact(new EndingPairDrawStartedEvent(f.Id, state, r.Source, r.GameplayHash, r.ActualTurnNumber,
            r.ActualTurnOwnerSeat, r.RoundNumber, r.EndingFrameId, r.CurrentActorSeat));
        ContinueEndingPairDraw(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinueEndingPairDraw(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.EndingPairDraw!;
        if (!ValidEndingPairDraw(f)) throw new InvalidOperationException("The issued Ending pair lost its original source/round/typed parent or actual draw invoice.");
        if (r.AwaitingMovement)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = null, EndingPairDraw = r with { Cursor = r.Cursor + 1, AwaitingMovement = false } });
            f = GetActiveProgramFrame(id); r = f.EndingPairDraw!;
        }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.CurrentActorSeat].IsAlive)
        { CancelProgramBindingAndCleanup(f, "原参与者死亡或游戏结束，尚未摸牌及比较取消，已发行账单保留。"); return; }
        // The accepted Ending issuance owns both benefits. A child removing the
        // source cannot silently withdraw the already issued second draw.
        if (r.Cursor < 2)
        {
            var seat = r.Cursor == 0 ? f.OwnerSeat : r.CurrentActorSeat; var before = EndingPairSequence;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null), EndingPairDraw = r with { AwaitingMovement = true } });
            var reason = new CardMoveReason($"skill-program.{f.SkillId}.ending-pair.{r.Cursor}");
            var count = DrawCards(_players[seat], 1, true, reason).Count;
            var invoice = new ProgramOneCardDrawInvoice(seat, before, EndingPairSequence, count); f = GetActiveProgramFrame(id);
            ReplaceRuntimeTop(f with { EndingPairDraw = r.Cursor == 0 ? f.EndingPairDraw! with { FirstDraw = invoice } : f.EndingPairDraw! with { SecondDraw = invoice } });
            AdvanceEventRulesAndQueueFact(new EndingPairOneDrawIssuedEvent(id, r.Cursor, seat, invoice.SequenceBefore, invoice.SequenceAfter, count));
            AdvanceRuntimeProgram(id); return;
        }
        var ownerCount = GetHand(_players[f.OwnerSeat]).Count; var actorCount = GetHand(_players[r.CurrentActorSeat]).Count;
        var blocked = ownerCount != actorCount;
        if (blocked && !_skillRuntimeState.TryConsumeUsage(f.OwnerSeat, f.SkillId, EndingPairRoundUsage(r.StateId), SkillUsageScope.Round, 1))
            throw new InvalidOperationException("One exact Ending pair comparison cannot consume the same Round block twice.");
        AdvanceEventRulesAndQueueFact(new EndingPairDrawComparedEvent(id, r.StateId, r.Source, r.RoundNumber, ownerCount, actorCount, blocked));
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { EndingPairDraw = null });
        // No unqualified later instruction exists in this exact one-node plan.
        FinishProgramSkill(GetActiveProgramFrame(id), completed: true);
    }
    private bool ValidEndingPairDraw(ProgramSkillFrame f)
    {
        if (f.EndingPairDraw is not { } r || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 || r.Source != EndingPairSource(f) ||
            string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) || string.IsNullOrWhiteSpace(r.Source.BindingId) || r.GameplayHash != f.GameplayHash ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _turnProgression.OwnerSeat || r.RoundNumber != _roundNumber ||
            r.CurrentActorSeat != _currentSeat || r.Cursor is < 0 or > 2 || !ExactEndingPairParent(f, r.EndingFrameId) ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal, StateId: var state }] || state != r.StateId ||
            CompleteProgramEventHistory().OfType<EndingPairDrawStartedEvent>().Count(e => e.ProgramFrameId == f.Id && e.StateId == state &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ActualTurnNumber == r.ActualTurnNumber && e.ActualTurnOwnerSeat == r.ActualTurnOwnerSeat &&
                e.RoundNumber == r.RoundNumber && e.EndingFrameId == r.EndingFrameId && e.CurrentActorSeat == r.CurrentActorSeat) != 1) return false;
        if (r.AwaitingMovement && (f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat)) return false;
        return ValidEndingPairInvoice(f, r.FirstDraw, 0, f.OwnerSeat, r.Cursor > 0 || r.AwaitingMovement && r.Cursor == 0) &&
            ValidEndingPairInvoice(f, r.SecondDraw, 1, r.CurrentActorSeat, r.Cursor > 1 || r.AwaitingMovement && r.Cursor == 1);
    }
    private bool ValidEndingPairInvoice(ProgramSkillFrame f, ProgramOneCardDrawInvoice? invoice, int cursor, int seat, bool required)
    {
        var facts = CompleteProgramEventHistory().OfType<EndingPairOneDrawIssuedEvent>().Where(e => e.ProgramFrameId == f.Id && e.Cursor == cursor).ToArray();
        if (invoice is null) return !required && facts.Length == 0;
        return required && invoice.RecipientSeat == seat && invoice.ActualCount is >= 0 and <= 1 && invoice.SequenceBefore >= 0 && invoice.SequenceAfter >= invoice.SequenceBefore &&
            facts is [var fact] && fact.RecipientSeat == seat && fact.SequenceBefore == invoice.SequenceBefore && fact.SequenceAfter == invoice.SequenceAfter && fact.ActualCount == invoice.ActualCount &&
            _cardMovements.Count(m => m.Sequence > invoice.SequenceBefore && m.Sequence <= invoice.SequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(seat) && m.Reason.Value == $"skill-program.{f.SkillId}.ending-pair.{cursor}") == invoice.ActualCount;
    }
}
