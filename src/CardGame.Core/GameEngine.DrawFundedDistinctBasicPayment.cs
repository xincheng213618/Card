namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsDrawFundedDistinctBasicAnswer(ChoiceId choice) => _pendingDecision?.Choices.Any(c => c.Id == choice && IsDrawFundedDistinctBasicAnswer(c)) == true;
    private CommandResult SubmitDrawFundedDistinctBasicAnswer(int actorSeat, PromptId promptId, ChoiceId choiceId)
    {
        if (!_started) return Reject(CommandErrorCode.NotStarted, "游戏尚未开始。");
        if (_winner != Winner.None || _status == EngineStatus.Completed) return Reject(CommandErrorCode.IllegalAction, "游戏已经结束。");
        if (_pendingDecision is not { } prompt || prompt.PlayerSeat != actorSeat || prompt.PromptId != promptId)
            return Reject(CommandErrorCode.InvalidPrompt, "摸牌转换必须回答当前精确提示。");
        var choice = prompt.Choices.SingleOrDefault(c => c.Id == choiceId && IsDrawFundedDistinctBasicAnswer(c));
        return choice is null ? Reject(CommandErrorCode.InvalidChoice, "该摸牌转换选择未发布。") : SubmitDrawFundedDistinctBasicAnswer(actorSeat, prompt, choice);
    }
    private CommandResult SubmitDrawFundedDistinctBasicAnswer(int actorSeat, PendingDecision prompt, PromptChoice choice)
    {
        if (prompt.PlayerSeat != actorSeat || _pendingDecision?.PromptId != prompt.PromptId ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [choice])) || choice.Cards.Count != 0 ||
            !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"), out var kind) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("native-target-seat"), out var target) ||
            DrawFundedDistinctBasicContext(prompt) is not { } need ||
            !DrawFundedDistinctBasicResponseChoices(prompt, need).Any(c => AssistedChoicesEqual([c], [choice])))
            return Reject(CommandErrorCode.IllegalAction, "The draw-funded answer changed its published original need, qualification, source, name or target.");
        var source = RequireConversionSource(choice);
        return Accept(() =>
        {
            BeginDrawFundedDistinctBasic(_players[actorSeat], source, kind,
                (need.Intent is DrawFundedDistinctBasicIntent.OwnSlashDodge or DrawFundedDistinctBasicIntent.Dying) ? [target] : choice.Targets, prompt, need);
            AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        });
    }

    private void BeginDrawFundedDistinctBasic(CharacterState actor, CardConversionSource source, CardKind kind,
        IReadOnlyList<int> targets, PendingDecision original, DrawFundedBasicNeed need)
    {
        var accepted = DrawFundedDistinctBasicRules(actor, kind, need.Intent == DrawFundedDistinctBasicIntent.OwnSlashDodge,
            need.Intent == DrawFundedDistinctBasicIntent.Dying).SingleOrDefault(r => r.Source == source);
        if (accepted.Rule is null || original.PlayerSeat != actor.Seat || _resolutionStack.OfType<DrawFundedDistinctBasicFrame>().Any(f => f.Payment.OriginalPromptId == original.PromptId))
            throw new InvalidOperationException("A draw-funded use cannot accept an unavailable source or pay the same original prompt twice.");
        var id = ++_resolutionSequence;
        var payment = new DrawFundedDistinctBasicPayment(id, source, accepted.Hash, accepted.Rule.DrawFundedDistinctBasic!.MethodLedgerId,
            _turnNumber, _turnProgression.OwnerSeat, need.Intent, kind, ProgramBasicCardName(kind), need.ParentFrameId, need.RequestFrameId,
            need.ParentActionId, targets.Count == 1 ? targets[0] : need.FixedTargetSeat, need.Cursor, original.PromptId, original.Revision, 0, 0, 0);
        ClearPendingDecision();
        PushRuntimeFrame(new DrawFundedDistinctBasicFrame(id, payment, original, targets));
        AdvanceEventRulesAndQueueFact(new DrawFundedDistinctBasicStartedEvent(id, actor.Seat, source, accepted.Hash, payment.MethodLedgerId,
            payment.ActualTurnNumber, payment.ActualTurnOwnerSeat, need.Intent, kind, need.ParentFrameId, need.RequestFrameId,
            need.ParentActionId, payment.TargetSeat, need.Cursor, original.PromptId, original.Revision));
        ContinueDrawFundedDistinctBasic(id);
    }

    private void ContinueDrawFundedDistinctBasic(long frameId)
    {
        while (_resolutionStack.LastOrDefault() is DrawFundedDistinctBasicFrame frame && frame.Id == frameId && _pendingDecision is null)
        {
            if (frame.ActiveChildFrameId is not null) throw new InvalidOperationException("A draw-funded producer resumed before its exact child returned.");
            if (frame.Stage == DrawFundedDistinctBasicStage.Drawing)
            {
                var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
                // Persist the completed-attempt direction before native draw/rule facts can run children.
                ReplaceRuntimeTop(frame with { Stage = DrawFundedDistinctBasicStage.DrawChildren, Payment = frame.Payment with { SequenceBefore = before, SequenceAfter = before } });
                var drawn = DrawCards(_players[frame.Payment.Source.OwnerSeat], 1, true, new(DrawFundedBasicDrawReason));
                var after = _cardMovements.LastOrDefault()?.Sequence ?? before;
                var current = (DrawFundedDistinctBasicFrame)_resolutionStack.Last();
                var paid = current.Payment with { SequenceAfter = after, ActualDrawCount = drawn.Count };
                ReplaceRuntimeTop(current with { Payment = paid });
                AdvanceEventRulesAndQueueFact(new DrawFundedDistinctBasicPaidEvent(frameId, paid.Source.OwnerSeat, before, after, drawn.Count));
                continue;
            }
            if (frame.Stage != DrawFundedDistinctBasicStage.DrawChildren || !ValidDrawFundedDistinctBasicPayment(frame.Payment))
                throw new InvalidOperationException("A draw-funded producer lost its unique native draw attempt.");
            if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.DrawFundedDistinctBasic) || TryBeginCardsMovedProgramWindow(frame.Id)) return;
            var legal = DrawFundedDistinctBasicOriginalNeedCurrent(frame);
            PopResolutionFrame(frame.Id, ResolutionFrameKind.DrawFundedDistinctBasic);
            if (!legal)
            { CancelDrawFundedDistinctBasicAttempt(frame); return; }
            // No new decision after payment: return only to the accepted use and its exact original native parent.
            ExecuteDrawFundedDistinctBasicPaid(frame); return;
        }
    }

    private void BeginDrawFundedDistinctBasicChild(long ownerFrameId, long childFrameId)
    {
        if (_resolutionStack.LastOrDefault() is not DrawFundedDistinctBasicFrame frame || frame.Id != ownerFrameId ||
            frame.Stage != DrawFundedDistinctBasicStage.DrawChildren || frame.ActiveChildFrameId is not null || !ValidDrawFundedDistinctBasicPayment(frame.Payment))
            throw new InvalidOperationException("A draw-funded cost cannot replace an active or unpaid child.");
        ReplaceRuntimeTop(frame with { ActiveChildFrameId = childFrameId });
    }
    private void ResumeDrawFundedDistinctBasicChild(long ownerFrameId, long childFrameId)
    {
        if (_resolutionStack.LastOrDefault() is not DrawFundedDistinctBasicFrame frame || frame.Id != ownerFrameId || frame.ActiveChildFrameId != childFrameId)
            throw new InvalidOperationException("A draw-funded child returned to another payment frame.");
        ReplaceRuntimeTop(frame with { ActiveChildFrameId = null }); ContinueDrawFundedDistinctBasic(frame.Id);
    }
    private void ResumeDrawFundedDistinctBasicMovement(CardsMovedTriggerWindowFrame completed)
    {
        if (completed.ResumeDrawFundedDistinctBasicFrameId is not { } id || completed.Batch.ParentFrameId != id ||
            completed.Batch.AwaitingProgramFrameId is not null || completed.ResumeProgramFrameId is not null || completed.Batch.Id != completed.Id)
            throw new InvalidOperationException("A draw-funded movement lost its exact native cost owner.");
        ResumeDrawFundedDistinctBasicChild(id, completed.Id);
    }
    private bool IsDrawFundedDistinctBasicHpParent(ResolutionFrame? parent, HpChangedTriggerWindowFrame child) =>
        parent is DrawFundedDistinctBasicFrame paid && child.Continuation == PostEventContinuation.DrawFundedDistinctBasic &&
        child.ResumeFrameId == paid.Id && child.Change.ParentFrameId == paid.Id && paid.ActiveChildFrameId == child.Id &&
        child.CardId is null && child.CardKind is null && ValidDrawFundedDistinctBasicPayment(paid.Payment);
    private void ResumeDrawFundedDistinctBasicHpChange(HpChangedTriggerWindowFrame completed)
    {
        if (!IsDrawFundedDistinctBasicHpParent(_resolutionStack.LastOrDefault(), completed))
            throw new InvalidOperationException("A draw-funded HP-change child lost its owning payment.");
        ResumeDrawFundedDistinctBasicChild(completed.ResumeFrameId!.Value, completed.Id);
    }
    private bool IsDrawFundedDistinctBasicRecoveryParent(ResolutionFrame? parent, RecoveryReplacementFrame child) =>
        parent is DrawFundedDistinctBasicFrame paid && child.ParentFrameId == paid.Id && child.Return.ResumeFrameId == paid.Id &&
        child.Return.Continuation == PostEventContinuation.DrawFundedDistinctBasic && paid.ActiveChildFrameId == child.Id &&
        child.Return.CardId is null && child.Return.CardKind is null && ValidDrawFundedDistinctBasicPayment(paid.Payment);
    private void ResumeDrawFundedDistinctBasicRecovery(RecoveryReplacementFrame completed)
    {
        if (!IsDrawFundedDistinctBasicRecoveryParent(_resolutionStack.LastOrDefault(), completed))
            throw new InvalidOperationException("A draw-funded recovery child lost its owning payment.");
        ResumeDrawFundedDistinctBasicChild(completed.ParentFrameId, completed.Id);
    }

    private bool ValidDrawFundedDistinctBasicPayment(DrawFundedDistinctBasicPayment p)
    {
        if (p.PaymentFrameId <= 0 || !IsValidPlayerSeat(p.Source.OwnerSeat) || string.IsNullOrWhiteSpace(p.Source.SkillInstanceId) ||
            p.NormalizedName != ProgramBasicCardName(p.EffectiveKind) || p.Cursor < 0 || p.ActualDrawCount is < 0 or > 1 || p.SequenceAfter < p.SequenceBefore ||
            _contentRegistry.Skills.GetValueOrDefault(p.Source.SkillId)?.Program is not { } program || program.GameplayHash != p.GameplayHash ||
            program.ViewAs.SingleOrDefault(r => r.Id == p.Source.BindingId) is not { DrawFundedDistinctBasic: { } policy } rule ||
            policy.MethodLedgerId != p.MethodLedgerId || rule.OutputKind != p.EffectiveKind || rule.InputCount != 0) return false;
        var started = CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicStartedEvent>().Where(e => e.PaymentFrameId == p.PaymentFrameId).ToArray();
        var paid = CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicPaidEvent>().Where(e => e.PaymentFrameId == p.PaymentFrameId).ToArray();
        return started is [var s] && s.ActorSeat == p.Source.OwnerSeat && s.Source == p.Source && s.GameplayHash == p.GameplayHash && s.MethodLedgerId == p.MethodLedgerId &&
            s.ActualTurnNumber == p.ActualTurnNumber && s.ActualTurnOwnerSeat == p.ActualTurnOwnerSeat && s.Intent == p.Intent && s.EffectiveKind == p.EffectiveKind &&
            s.ParentFrameId == p.ParentFrameId && s.RequestFrameId == p.RequestFrameId && s.ParentActionId == p.ParentActionId && s.TargetSeat == p.TargetSeat &&
            s.Cursor == p.Cursor && s.OriginalPromptId == p.OriginalPromptId && s.OriginalRevision == p.OriginalRevision &&
            paid is [var fact] && fact.ActorSeat == p.Source.OwnerSeat && fact.SequenceBefore == p.SequenceBefore && fact.SequenceAfter == p.SequenceAfter && fact.ActualDrawCount == p.ActualDrawCount &&
            _cardMovements.Count(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(p.Source.OwnerSeat) && m.Reason.Value == DrawFundedBasicDrawReason) == p.ActualDrawCount;
    }

    private bool DrawFundedDistinctBasicOriginalNeedCurrent(DrawFundedDistinctBasicFrame frame)
    {
        var p = frame.Payment; var actor = _players[p.Source.OwnerSeat];
        if (!actor.IsAlive || _winner != Winner.None || p.ActualTurnNumber != _turnNumber || p.ActualTurnOwnerSeat != _turnProgression.OwnerSeat) return false;
        if (p.Intent == DrawFundedDistinctBasicIntent.Play)
            return _resolutionStack.Count == 1 && _phase == TurnPhase.Play && _currentSeat == actor.Seat &&
                DrawFundedDistinctBasicNativePlayActions(actor, p.Source, p.EffectiveKind).Any(a => a.TargetSeats.SequenceEqual(frame.TargetSeats));
        var request = _resolutionStack.SingleOrDefault(f => f.Id == p.RequestFrameId);
        if (request is null || _resolutionStack.Count < 2 || _resolutionStack[_resolutionStack.Count - 2].Id != request.Id ||
            DrawFundedDistinctBasicContext(frame.OriginalDecision, request) is not { } current || current.Intent != p.Intent ||
            current.ParentFrameId != p.ParentFrameId || current.RequestFrameId != p.RequestFrameId || current.ParentActionId != p.ParentActionId || current.Cursor != p.Cursor)
            return false;
        if (request is ProgramSkillFrame program)
        {
            var effect = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!).GetPausedInstruction(program.InstructionIndex).Effect;
            if (!IsDrawFundedDistinctBasicProgramSelection(program, effect, frame)) return false;
        }
        return DrawFundedDistinctBasicNativeResponseLegal(actor, current, p.EffectiveKind, p.TargetSeat);
    }

    private void CancelDrawFundedDistinctBasicAttempt(DrawFundedDistinctBasicFrame frame)
    {
        var p = frame.Payment;
        AdvanceEventRulesAndQueueFact(new DrawFundedDistinctBasicCancelledEvent(p.PaymentFrameId, p.Source.OwnerSeat, p.Intent, p.EffectiveKind));
        ClearPendingDecision(); _status = EngineStatus.Running;
        if (_winner != Winner.None) return;
        if (p.Intent == DrawFundedDistinctBasicIntent.BorrowedSword && ActiveBorrowedSword is { } b && b.ResolutionId == p.ParentFrameId && b.ActiveAttack is null)
        { CompleteBorrowedSwordWithoutSlash(b, transferWeapon: _players[b.WeaponOwnerSeat].IsAlive && _players[b.SlashTargetSeat].IsAlive); return; }
        if (p.Intent == DrawFundedDistinctBasicIntent.Qinglong && ActiveQinglongCrescentBlade is { } q && q.Attack.ResolutionId == p.ParentFrameId)
        {
            _pendingDecision = DrawFundedDistinctBasicNativeDecision(frame.OriginalDecision);
            ResolveQinglongCrescentBladeChoice(frame.OriginalDecision.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip")); return;
        }
        if (p.Intent == DrawFundedDistinctBasicIntent.OwnSlashDodge && ActiveCardAttack is { } a && a.ResolutionId == p.ParentFrameId)
        {
            if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response && response.ParentFrameId == a.ResolutionId) PopResponseWindow(a.ResolutionId);
            SetCardUseStep(a.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            if (!_players[a.TargetSeat].IsAlive || !ApplyAttackDamage(a)) CompleteAttack(a); return;
        }
        if (p.Intent == DrawFundedDistinctBasicIntent.Dying && ActiveDying is { } d && d.FrameId == p.ParentFrameId && d.ResponderIndex == p.Cursor)
        { ApplyDyingResponse(_players[d.ResponderSeat], false, null, false, null); return; }
        if (p.Intent is DrawFundedDistinctBasicIntent.ProgramSlash or DrawFundedDistinctBasicIntent.ProgramNearestSlash or DrawFundedDistinctBasicIntent.AssistedSlash or DrawFundedDistinctBasicIntent.NearestLegalSlash &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame program && program.Id == p.ParentFrameId && program.InstructionIndex == p.Cursor)
            CancelProgramBindingAndCleanup(program, "原用牌对象或请求已失效，已支付摸牌尝试取消。");
    }
}
