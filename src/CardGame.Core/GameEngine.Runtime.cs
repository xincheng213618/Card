namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The public Advance path, program child returns and rule-window continuations
    // meet here. Domain handlers retain their existing order and rules logic.
    // A child frame suspends its parent; it is never dispatched as that parent.
    private void AdvanceRuntimeTop<TFrame>() where TFrame : ResolutionFrame =>
        GetRuntimeModule<NativeFrameRoutingRuntime>().AdvanceTop<TFrame>();

    private void AdvanceRuntimeProgram(long frameId) =>
        GetRuntimeModule<NativeFrameRoutingRuntime>().AdvanceProgram(frameId);

    private void AdvanceRuntimeFrame(long frameId) =>
        GetRuntimeModule<NativeFrameRoutingRuntime>().AdvanceFrame(frameId);

    private void PushRuntimeFrame(ResolutionFrame frame)
    {
        // These child kinds have live owners. A zero judgment parent denotes the
        // normal turn judgment boundary. Program trigger contexts can also refer
        // to completed facts, so they are validated by their typed return path.
        // Module-owned child frames expose their parent through the
        // IProgramChildResolutionFrame surface instead of a central arm here.
        var parentId = frame switch
        {
            NativeDrawAttemptFrame child => child.Context.ParentFrameId,
            IdentityAppearanceFrame child => child.Receipt.ParentFrameId ?? 0,
            RequestedDeckBasicFrame child => child.ParentFrameId,
            DrawFundedDistinctBasicFrame child => child.Payment.RequestFrameId ?? child.Payment.ParentFrameId ?? 0,
            ChainedStateBasicFrame child => child.Payment.RequestFrameId ?? child.Payment.ParentFrameId ?? 0,
            ActualUseTargetWindowFrame child => child.ParentFrameId,
            SlashTargetBenefitWindowFrame child => child.ParentFrameId,
            FirstSlashDuelLegacyWindowFrame child => child.ParentFrameId,
            IProgramChildResolutionFrame child => child.ParentFrameId,
            PindianRevealWindowFrame child => child.ParentFrameId,
            WuLuKangPindianFrame child => child.ParentFrameId,
            DouyuWindowFrame child => child.ParentFrameId,
            SlashTargetPenaltyWindowFrame child => child.ParentFrameId,
            CardDeclarationFrame child => child.Return.ParentFrameId,
            CardDeclarationChallengeFrame child => child.ParentFrameId,
            ResponseWindowFrame child => child.ParentFrameId,
            JudgmentFrame child => child.ParentFrameId,
            DamageFrame child => child.ParentFrameId,
            DamageTriggerWindowFrame child => child.ParentFrameId,
            RecoveryFrame child => child.ParentFrameId,
            RecoveryReplacementFrame child => child.ParentFrameId,
            DrawPhaseObligationFrame child => child.ParentFrameId ?? 0,
            ActualPhaseEndFrame child => child.ParentFrameId ?? 0,
            DyingFrame child => child.ParentFrameId,
            DeathFrame child => child.ParentFrameId,
            NullificationWindowFrame child => child.ParentFrameId,
            TargetCardSelectionFrame child => child.ParentFrameId,
            _ => 0
        };
        if (parentId > 0 && !_resolutionStack.Any(parent => parent.Id == parentId))
            throw new InvalidOperationException("A runtime child cannot outlive its owning frame.");
        _resolutionStack.Push(frame);
        if (frame is CardUseFrame use && use.Action is { Type: CardActionType.Use } action)
        {
            BindQinglongInsertedUse(use);
            IssueOriginalTargetAdditionPolicy(use.Id, action);
            IssueMaWanHunjiangUse(use.Id, action);
            IssueWeifuUse(use.Id, action);
        }
    }

    private void ReplaceRuntimeFrame(long expectedFrameId, ResolutionFrame next)
    {
        if (next.Id != expectedFrameId)
            throw new InvalidOperationException("A runtime write changed its selected owner identity.");
        _resolutionStack.Replace(next);
    }

    private void ReplaceRuntimeTop(ResolutionFrame next)
    {
        if (_resolutionStack.LastOrDefault()?.Id != next.Id)
            throw new InvalidOperationException("Only the active runtime frame may write its top continuation.");
        _resolutionStack.Replace(next);
    }

    private void PopResolutionFrame(long frameId, ResolutionFrameKind expectedKind)
    {
        if (_resolutionStack.LastOrDefault()?.NativeDraw is not null)
            throw new InvalidOperationException("A producer cannot complete with an unconsumed native draw request.");
        if (_resolutionStack.LastOrDefault() is { PendingRecoveryAttempts.Count: > 0 })
            throw new InvalidOperationException("A producer cannot complete with unconsumed recovery attempts.");
        if (_resolutionStack.LastOrDefault()?.PaidFactionRequestCostRecovery is not null)
            throw new InvalidOperationException("A producer cannot complete with an unconsumed paid faction request cost.");
        if (expectedKind == ResolutionFrameKind.Damage && _resolutionStack.LastOrDefault() is DamageFrame damage && damage.Id == frameId)
            CaptureLateFixedTargetDamageCapScope(damage);
        var top = _resolutionStack.CompleteTop(frameId, expectedKind);
        if (top is RecoveryFrame recovery)
        {
            var change = RecordHpChange(recovery.ParentFrameId, recovery.SourceSeat, recovery.TargetSeat,
                recovery.HpBefore, _players[recovery.TargetSeat].Hp, HpChangeKind.Recovery);
            if (change is not null) RecordProgramRecoveryApplication(recovery, change);
        }
    }

    /// <summary>
    /// Runs deterministic AI decisions until a human input boundary or game end.
    /// Calling this while input is already pending is harmless and returns immediately.
    /// </summary>

    private void AdvanceToHumanBoundary()
    {
        EnsureStarted();
        if (_status == EngineStatus.Completed || IsHumanDecisionPending())
        {
            return;
        }

        var guard = 0;
        while (_status != EngineStatus.Completed && !IsHumanDecisionPending())
        {
            if (_winner != Winner.None && _resolutionStack.Count == 0)
            {
                CompleteGame();
                break;
            }

            if (++guard > 20_000)
            {
                // Never cut an in-flight response in half. The current demo has a
                // single-step AI Dodge continuation, so finish it and evaluate the
                // guard again at the next stable boundary.
                if (IsAiResponsePending() ||
                    IsAiNullificationPending() ||
                    IsAiDyingResponsePending() ||
                    IsAiTargetCardSelectionPending() ||
                    IsAiHarvestPending() ||
                    IsAiFireAttackPending() ||
                    IsAiProgramJudgmentReplacementPending() ||
                    IsAiProgramJudgmentPending() ||
                    IsAiProgramTopReorderPending() ||
                    IsAiProgramRepeatJudgmentPending() ||
                    IsAiYingboPending() ||
                    IsAiSkipDiscardPolicyPending() ||
                    IsAiPindianPending())
                {
                    RunOneEngineStep();
                    continue;
                }

                EndAsDraw("规则循环超过安全上限");
                break;
            }

            RunOneEngineStep();
        }

    }

    /// <summary>
    /// Advances exactly one state-machine step (begin a turn, execute one AI play,
    /// request human input, or finish discard/end-turn) and then returns. This is
    /// useful for a UI that wants to animate or inspect each AI decision.
    /// </summary>

    private void AdvanceOneStepCore()
    {
        EnsureStarted();
        if (_status != EngineStatus.Completed &&
            _winner != Winner.None &&
            _resolutionStack.Count == 0)
        {
            CompleteGame();
        }
        else if (_status != EngineStatus.Completed && !IsHumanDecisionPending())
        {
            RunOneEngineStep();
            if (_winner != Winner.None &&
                _status != EngineStatus.Completed &&
                _pendingDecision is null &&
                _resolutionStack.Count == 0)
            {
                CompleteGame();
            }
        }
    }

    private void RunOneEngineStep()
    {
        if (GetRuntimeModule<NativeFrameAutomaticRuntime>().TryAdvance(NativeFrameAutomaticPhase.BeforeOpening)) return;
        if (_pendingDecision is null && _resolutionStack.Count == 0 && TryBeginWushuangOpening())
        { AdvanceRulesAndPublishState(); return; }
        if (GetRuntimeModule<NativeFrameAutomaticRuntime>().TryAdvance(NativeFrameAutomaticPhase.AfterOpening)) return;
        if (_resolutionStack.LastOrDefault() is RecoveryReplacementFrame recoveryReplacement)
        {
            if (_pendingDecision is { Kind: DecisionKind.RecoveryReplacement } decision)
            {
                if (!_players[decision.PlayerSeat].IsHuman) ResolveAiRecoveryReplacement();
            }
            else { AdvanceRuntimeFrame(recoveryReplacement.Id); AdvanceRulesAndPublishState(); }
            return;
        }
        if (TryAdvanceHuanpeiAi()) return;
        if (TryAdvanceZonghuAi()) return;
        if (TryAdvanceWeijieAi()) return;
        if (TryAdvanceRequestedDeckBasicAi()) return;
        if (TryAdvanceDrawFundedDistinctBasicAi()) return;
        if (TryAdvanceChainedStateBasicAi()) return;
        if (RuntimeHooks.TryAdvance(ProgramRuntimePhase.BeforeDeclaration)) return;
        if (_resolutionStack.LastOrDefault() is CardDeclarationChallengeFrame challenge)
        {
            if (_pendingDecision is null) ContinueCardDeclarationChallenge(challenge.Id);
            else if (!_players[_pendingDecision.PlayerSeat].IsHuman) AnswerCardDeclaration(_pendingDecision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("declaration-answer") == "pass"));
            AdvanceRulesAndPublishState(); return;
        }
        if (GetRuntimeModule<NativeFrameAutomaticRuntime>().TryAdvance(NativeFrameAutomaticPhase.AfterDeclarationChallenge)) return;
        if (_pendingDecision is { Kind: DecisionKind.ProgramTrigger } programTriggerDecision)
        {
            if (!_players[programTriggerDecision.PlayerSeat].IsHuman)
                ResolvePendingAiProgramTrigger();
            return;
        }

        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { TopReorder.Population: not null } &&
            IsAiProgramTopReorderPending())
        {
            ResolvePendingAiProgramTopReorder();
            return;
        }

        if (GetRuntimeModule<NativeFrameAutomaticRuntime>().TryAdvance(NativeFrameAutomaticPhase.BeforeJudgmentDecision)) return;

        if (_pendingDecision is
            {
                Kind: DecisionKind.ProgramJudgmentTrigger
            })
        {
            if (IsAiProgramJudgmentPending()) ResolvePendingAiProgramJudgment();
            return;
        }

        if (IsAiProgramTopReorderPending())
        {
            ResolvePendingAiProgramTopReorder();
            return;
        }

        if (IsAiProgramRepeatJudgmentPending())
        {
            ResolvePendingAiProgramRepeatJudgment();
            return;
        }

        if (IsAiProgramJudgmentReplacementPending())
        {
            ResolvePendingAiProgramJudgmentReplacement();
            return;
        }



        if (IsAiYingboPending())
        {
            ResolvePendingAiYingbo();
            return;
        }

        if (IsAiSkipDiscardPolicyPending())
        {
            ResolvePendingAiSkipDiscardPolicy();
            return;
        }

        if (_pendingDecision?.SkillPrompt is not null)
        {
            if (IsAiPindianPending()) ResolvePendingAiPindian();
            return;
        }


        if (RuntimeHooks.TryAdvance(ProgramRuntimePhase.BeforeResponseHealth)) return;
        if (_pendingDecision is null &&
            _resolutionStack.LastOrDefault() is (ProgramSkillFrame or HpChangedTriggerWindowFrame or CardsMovedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame) &&
            (ProjectTypedResponseCompletionHealthCursor() is { Owner: not null, IsMalformed: false } ||
             ProjectTypedCommittedSlashPaymentCursor() is { Owner: not null, IsMalformed: false }))
        {
            AdvanceRuntimeFrame(_resolutionStack.Last().Id);
            AdvanceRulesAndPublishState();
            return;
        }
        if (RuntimeHooks.TryAdvance(ProgramRuntimePhase.AfterResponseHealth)) return;

        if (IsCurrentWenhePoisonDying() && ActiveDying is { } poisonDying &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { WindowContext: { } poisonContext } poisonResponse &&
            poisonContext.ParentFrameId == poisonDying.Id && poisonContext.TargetSeat == poisonDying.VictimSeat &&
            (poisonContext.Window == SkillProgramTriggerWindow.DyingResponse && poisonResponse.OwnerSeat == poisonDying.ResponderSeat ||
             poisonContext.Window == SkillProgramTriggerWindow.SelfDyingResponse && poisonResponse.OwnerSeat == poisonDying.VictimSeat))
        {
            AdvanceRuntimeProgram(poisonResponse.Id);
            AdvanceRulesAndPublishState();
            return;
        }

        // The direct recovery family has already proved every producer/return
        // edge and the exact owned Dying. Advance its response without scanning
        // the capability-specific subtree predicates below.
        if (ActiveDying is { } recoveryDying &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame recoveryResponse &&
            recoveryResponse.WindowContext?.ParentFrameId == recoveryDying.Id && HasTypedRecoveryProgramDying())
        {
            AdvanceRuntimeProgram(recoveryResponse.Id);
            AdvanceRulesAndPublishState();
            return;
        }

        if (ActiveDying is { } paidProgramDying &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { WindowContext: { } paidContext } paidDyingProgram &&
            _resolutionStack.Count >= 2 && _resolutionStack[^2] is DyingFrame paidDyingParent && paidDyingParent.Id == paidProgramDying.Id &&
            paidContext.ParentFrameId == paidProgramDying.Id && paidContext.TargetSeat == paidProgramDying.VictimSeat &&
            paidContext.OwnerSeat == paidDyingProgram.OwnerSeat &&
            (paidContext.Window == SkillProgramTriggerWindow.DyingResponse && paidDyingProgram.OwnerSeat == paidProgramDying.ResponderSeat ||
             paidContext.Window == SkillProgramTriggerWindow.SelfDyingResponse && paidDyingProgram.OwnerSeat == paidProgramDying.VictimSeat) &&
            (RuntimeHooks.IsDying(ProgramDyingProofPhase.Runtime)))
        {
            AdvanceRuntimeProgram(paidDyingProgram.Id);
            AdvanceRulesAndPublishState();
            return;
        }

        if (ActiveDying is not null)
        {
            RunOneDyingStep();
            return;
        }

        if (IsAiTargetCardSelectionPending())
        {
            ResolvePendingAiTargetCardSelection();
            return;
        }

        if (IsAiFireAttackPending())
        {
            ResolvePendingAiFireAttack();
            return;
        }


        if (ActiveDamageTrigger is not null)
        {
            RunOneDamageTriggerStep();
            AdvanceRulesAndPublishState();
            return;
        }

        if (IsAiNullificationPending())
        {
            ResolvePendingAiNullification();
            return;
        }

        if (IsAiResponsePending())
        {
            ResolvePendingAiResponse();
            return;
        }

        if (IsAiHarvestPending())
        {
            ResolvePendingAiHarvest();
            return;
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.Recovery })
        {
            RunOneGroupRecoveryStep();
            return;
        }

        if (!_setupComplete)
        {
            RunOneSetupStep();
            return;
        }

        if (!_gameStartingProgramsResolved && _resolutionStack.Count == 0 && BeginGameStartingPrograms())
        {
            return;
        }
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame programFrame)
        {
            AdvanceRuntimeProgram(programFrame.Id);
            AdvanceRulesAndPublishState();
            return;
        }

        if (_phase is TurnPhase.NotStarted or TurnPhase.Finished)
        {
            BeginTurn();
            return;
        }

        if (_phase == TurnPhase.Play)
        {
            if (_strategicEndPlayTurn == _turnNumber)
            {
                _strategicEndPlayTurn = -1;
                CompleteCurrentPlayPhase();
                return;
            }
            var current = _players[_currentSeat];
            if (!current.IsAlive)
            {
                EndTurn();
                return;
            }

            if (current.IsHuman)
            {
                RequestHumanPlay();
                return;
            }

            RunOneAiPlayDecision(current);
            return;
        }

        if (_phase == TurnPhase.Discard)
        {
            var current = _players[_currentSeat];
            if (_options.UseInteractiveDiscard && current.IsHuman && current.IsAlive &&
                RequiredSelfHandLimitDiscard(current) > 0)
            {
                RequestHumanDiscard(current);
                return;
            }

            AutoDiscard(current);
            if (TryBeginDiscardPhaseEndedProgramWindow(current)) return;
            EndTurn();
            return;
        }

        throw new InvalidOperationException(
            $"No continuation for phase {_phase}; seed {_options.Seed}, turn {_turnNumber}, seat {_currentSeat}, " +
            $"general {_players[_currentSeat].General.Id}, winner {_winner}, " +
            $"frames [{string.Join(", ", _resolutionStack.Select(frame => $"{frame.Kind}:{frame.Id}"))}], " +
            $"decision {_pendingDecision?.Kind}.");
    }

    private void CompleteRuntimeProgramBinding(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext ??
            throw new InvalidOperationException("A trigger program frame lost its window context.");
        FinishPaidOwnTargetBeforeProgramCompletion(frame);
        FinishOwnedDeathBenefitReturn(frame, completed);
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            frame.Id, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId,
            frame.OwnerSeat, context.Window, Activated: true, Completed: completed));
        if (CompleteDeferredTurnEndBinding(frame, context)) return;
        if (_programPipelineHooks.ReturnCompletedProgramBinding(frame, completed)) return;
        if (_resolutionStack.LastOrDefault() is FirstSlashDuelLegacyWindowFrame)
        {
            CompleteFirstLegacyBenefitCandidate(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId,
                frame.GameplayHash, GetProgramTrigger(frame).Priority), context); return;
        }
        ProgramWindowReturns.Value.Return(this, frame, completed);
    }

    private void ReturnRuntimeProgramMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (ProgramContinuations.Value.TryReturnMovement(this, frame)) return;
        if (frame.SelectedCardPayment is { } payment && frame.SelectedCardPaymentResult is null)
        {
            if (frame.PendingMovementContinuation is not null || !payment.MovementCommitted ||
                payment.ActiveChildFrameId is not null)
                throw new InvalidOperationException("A selected-card payment cannot return before its movement children finish.");
            var valid = _players[frame.OwnerSeat].IsAlive &&
                (HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
                 CanContinueEquipmentSuitProgram(frame) || CanContinueHpConditionalProgram(frame) || CanContinuePhaseAlcohol(frame));
            ReplaceRuntimeTop(frame with
            {
                SelectedCardPaymentResult = new ProgramSelectedCardPaymentResult(
                    payment.InstructionIndex, payment.Operation, payment.LastCompletedChildFrameId, valid)
            });
            if (!valid)
            {
                CancelProgramBindingAndCleanup(GetActiveProgramFrame(frameId),
                    "移动响应后技能持有人、装备持有人或技能实例已失效。");
                return;
            }
            AdvanceRuntimeProgram(frameId);
            return;
        }
        if (frame.DamageTargetObtain is not null)
        {
            if (!IsValidDamageTargetObtain(frame)) throw new InvalidOperationException("The obtain movement lost its actual producer.");
            ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
            AdvanceRuntimeProgram(frameId);
            return;
        }
        if (frame.DamageTargetMount is { Receipt: not null })
        {
            if (!IsValidDamageTargetMount(frame)) throw new InvalidOperationException("The mount movement lost its actual payment.");
            ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
            AdvanceRuntimeProgram(frameId);
            return;
        }
        if (ProgramContinuations.Value.TryReturnMovement(this, frame, afterPayment: true)) return;
        var pending = frame.PendingMovementContinuation ??
            throw new InvalidOperationException("The movement continuation is missing.");
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        if (!_players[frame.OwnerSeat].IsAlive || !_players[pending.SubjectSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) &&
            !CanContinueEquipmentSuitProgram(frame) && !CanContinueHpConditionalProgram(frame) && !CanContinuePhaseAlcohol(frame))
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frameId),
                "移动响应后技能持有人、装备持有人或技能实例已失效。");
            return;
        }
        if (pending.CoverageResultBind is { } bind)
            SetProgramAttackRangeCoverage(frameId, bind, pending.SubjectSeat, pending.BeforeCount,
                CountLivingInAttackRange(pending.SubjectSeat));
        if (frame.RepeatedJudgment is { LastMatched: not null })
            ContinueProgramRepeatedJudgmentAfterMovement(frameId);
        else
            AdvanceRuntimeProgram(frameId);
    }
}
