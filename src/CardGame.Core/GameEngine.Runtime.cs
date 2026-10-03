namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The public Advance path, program child returns and rule-window continuations
    // meet here. Domain handlers retain their existing order and rules logic.
    // A child frame suspends its parent; it is never dispatched as that parent.
    private void AdvanceRuntimeTop<TFrame>() where TFrame : ResolutionFrame
    {
        if (_resolutionStack.LastOrDefault() is TFrame frame)
            AdvanceRuntimeFrame(frame.Id);
    }

    // A program effect may finish synchronously before its caller returns.
    // Preserve the executor's quiet return for a retired or suspended program;
    // concrete typed child receipts validate their owner before reaching here.
    private void AdvanceRuntimeProgram(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame frame && frame.Id == frameId)
            AdvanceRuntimeFrame(frameId);
    }

    private void AdvanceRuntimeFrame(long frameId)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0)
            throw new InvalidOperationException($"Runtime continuation {frameId} has no owner.");
        if (index != _resolutionStack.Count - 1) return;
        switch (_resolutionStack[index])
        {
            case NullificationWindowFrame { CounterspellPayment: not null }: ContinuePolicyCounterspellPayment(frameId); break;
            case CardUseFrame { ColorFireAttack.PaidCardId: not null }: ContinueColorFireAttackPayment(frameId); break;
            case CardEffectBeforeApplyFrame: ContinueCardEffectBeforeApply(); break;
            case CardDeclarationFrame: ContinueCardDeclaration(frameId); break;
            case CardDeclarationChallengeFrame: ContinueCardDeclarationChallenge(frameId); break;
            case ProgramSkillFrame:
                if (ResumeSharedSlashOffer(frameId)) return;
                if (ResumeGrantedEntityPhaseTrailer(frameId) || ResumeGrantedPhaseSlashClaim(frameId)) return;
                if (ResumeProvenanceClaim(frameId) || ResumeProvenanceAlcohol(frameId)) return;
                if (ResumeDamageJudgmentSuitPayment(frameId)) return;
                if (ResumeDualColorDuel(frameId)) return;
                if (ResumeLeastHandMarkerDraw(frameId)) return;
                if (ResumeAlternativePhaseCost(frameId)) return;
                if (ResumeAwaitedBlindHandTake(frameId)) return;
                if (ResumeParticipantHandPayment(frameId)) return;
                if (ResumeBoundDiscardSlashBenefits(frameId)) return;
                if (ResumeProgramBoundCardRecast(frameId)) return;
                if (ResumeHpDamageShieldPayment(frameId)) return;
                if (ResumeProgramEquipmentRecast(frameId)) return;
                if (ResumeCappedHandRefresh(frameId)) return;
                if (TryResumeFixedSlashAndDeclaredDeck(frameId)) return;
                if (ResumeDamageTargetObtain(frameId)) return;
                if (ResumeDamageTargetMount(frameId)) return;
                if (ResumeProgramSlashSuitDiscard(frameId)) return;
                if (ResumeDiamondDelayed(frameId)) return;
                if (ResumeDistinctFactionDiscards(frameId)) return;
                if (ResumePrivateTurnHold(frameId)) return;
                if(ResumePrivateGeneralLibrarySelection(frameId))return;
                if (ResumeNamedTurnFlow(frameId)) return;
                if (ResumeFinalTargetGift(frameId)) return;
                if (ResumeGameDomain(frameId)) return;
                if (TryResumeConvertingGift(frameId)) return;
                if (ResumePublicPileColorPayment(frameId)) return;
                if (ResumeAlternatingSuitTop(frameId)) return;
                if (TryResumeQuotaTop(frameId)) return;
                if (ResumeBudgetGift(frameId)) return;
                if (ResumeRedDiscardRecovery(frameId)) return;
                if (TryResumeResponseEntityExchange(frameId)) return;
                if (TryResumeDeckPrograms(frameId)) return;
                if (TryResumeProgramTargetCommit(frameId)) return;
                if (TryResumeNamedDefenseAndPublicDraft(frameId)) return;
                if (TryContinueFactionRecoveryDebts(frameId)) return;
                var host = new ProgramSkillHost(this);
                new SkillProgramExecutor().Run(frameId, host, host);
                break;
            case ProgramCardTriggerWindowFrame: ContinueProgramCardWindowCore(); break;
            case ProgramLifecycleTriggerWindowFrame: ContinueProgramLifecycleWindowCore(); break;
            case HpChangedTriggerWindowFrame: ContinueHpChangedProgramWindowCore(); break;
            case RecoveryReplacementFrame: ContinueRecoveryReplacement(); break;
            case EquipmentRecastFrame: ContinueEquipmentRecast(frameId); break;
            case DrawPhaseObligationFrame: ContinueDrawPhaseObligation(frameId); break;
            case CardsMovedTriggerWindowFrame: ContinueCardsMovedProgramWindowCore(); break;
            case BeforeDamageProgramWindowFrame: ContinueBeforeDamageProgramWindowCore(); break;
            case ProgramDeathTriggerWindowFrame: ContinueOwnerDiedProgramWindowCore(); break;
            case ProgramKillTriggerWindowFrame: ContinueKillDiedProgramWindowCore(); break;
            case ProgramJudgmentTriggerWindowFrame: ContinueProgramJudgmentWindowCore(); break;
            case TurnEndingBoundaryFrame: ContinueTurnEndingBoundaryCore(); break;
            case PlayPhaseStartingBoundaryFrame: ContinuePlayPhaseStartingBoundaryCore(); break;
            case DeferredTurnEndFrame: ContinueDeferredTurnEndCore(); break;
            default:
                throw new InvalidOperationException("The requested frame has no runtime continuation handler.");
        }
    }

    private void PushRuntimeFrame(ResolutionFrame frame)
    {
        // These child kinds have live owners. A zero judgment parent denotes the
        // normal turn judgment boundary. Program trigger contexts can also refer
        // to completed facts, so they are validated by their typed return path.
        var parentId = frame switch
        {
            CardDeclarationFrame child => child.Return.ParentFrameId,
            CardDeclarationChallengeFrame child => child.ParentFrameId,
            ResponseWindowFrame child => child.ParentFrameId,
            JudgmentFrame child => child.ParentFrameId,
            DamageFrame child => child.ParentFrameId,
            DamageTriggerWindowFrame child => child.ParentFrameId,
            RecoveryFrame child => child.ParentFrameId,
            RecoveryReplacementFrame child => child.ParentFrameId,
            DrawPhaseObligationFrame child => child.ParentFrameId ?? 0,
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
            IssueOriginalTargetAdditionPolicy(use.Id, action);
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
        if (_resolutionStack.LastOrDefault() is { PendingRecoveryAttempts.Count: > 0 })
            throw new InvalidOperationException("A producer cannot complete with unconsumed recovery attempts.");
        if (_resolutionStack.LastOrDefault()?.PaidFactionRequestCostRecovery is not null)
            throw new InvalidOperationException("A producer cannot complete with an unconsumed paid faction request cost.");
        var top = _resolutionStack.CompleteTop(frameId, expectedKind);
        if (top is RecoveryFrame recovery)
            RecordHpChange(recovery.ParentFrameId, recovery.SourceSeat, recovery.TargetSeat,
                recovery.HpBefore, _players[recovery.TargetSeat].Hp, HpChangeKind.Recovery);
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
        if (_pendingDecision is null && _resolutionStack.LastOrDefault() is NullificationWindowFrame { CounterspellPayment: not null } paidCounter)
        { AdvanceRuntimeFrame(paidCounter.Id); AdvanceRulesAndPublishState(); return; }
        if (_pendingDecision is null && _resolutionStack.LastOrDefault() is CardUseFrame { ColorFireAttack.PaidCardId: not null } paidFire)
        { AdvanceRuntimeFrame(paidFire.Id); AdvanceRulesAndPublishState(); return; }
        if (_resolutionStack.LastOrDefault() is DrawPhaseObligationFrame draw)
        { AdvanceRuntimeFrame(draw.Id); AdvanceRulesAndPublishState(); return; }
        if (_resolutionStack.LastOrDefault() is EquipmentRecastFrame recast)
        { AdvanceRuntimeFrame(recast.Id); AdvanceRulesAndPublishState(); return; }
        if (_resolutionStack.LastOrDefault() is RecoveryReplacementFrame recoveryReplacement)
        {
            if (_pendingDecision is { Kind: DecisionKind.RecoveryReplacement } decision)
            {
                if (!_players[decision.PlayerSeat].IsHuman) ResolveAiRecoveryReplacement();
            }
            else { AdvanceRuntimeFrame(recoveryReplacement.Id); AdvanceRulesAndPublishState(); }
            return;
        }
        if (_resolutionStack.LastOrDefault() is CardDeclarationChallengeFrame challenge)
        {
            if (_pendingDecision is null) ContinueCardDeclarationChallenge(challenge.Id);
            else if (!_players[_pendingDecision.PlayerSeat].IsHuman) AnswerCardDeclaration(_pendingDecision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("declaration-answer") == "pass"));
            AdvanceRulesAndPublishState(); return;
        }
        if (_resolutionStack.LastOrDefault() is CardDeclarationFrame declaration)
        { AdvanceRuntimeFrame(declaration.Id); AdvanceRulesAndPublishState(); return; }
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

        if (_pendingDecision?.SkillPrompt is not null)
        {
            if (IsAiPindianPending()) ResolvePendingAiPindian();
            return;
        }

        if (_resolutionStack.LastOrDefault() is ProgramJudgmentTriggerWindowFrame &&
            _pendingDecision is null)
        {
            AdvanceRuntimeTop<ProgramJudgmentTriggerWindowFrame>();
            return;
        }

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


        if (ActiveDying is { } paidProgramDying &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { WindowContext: { } paidContext } paidDyingProgram &&
            _resolutionStack.Count >= 2 && _resolutionStack[^2] is DyingFrame paidDyingParent && paidDyingParent.Id == paidProgramDying.Id &&
            paidContext.ParentFrameId == paidProgramDying.Id && paidContext.TargetSeat == paidProgramDying.VictimSeat &&
            paidContext.OwnerSeat == paidDyingProgram.OwnerSeat &&
            (paidContext.Window == SkillProgramTriggerWindow.DyingResponse && paidDyingProgram.OwnerSeat == paidProgramDying.ResponderSeat ||
             paidContext.Window == SkillProgramTriggerWindow.SelfDyingResponse && paidDyingProgram.OwnerSeat == paidProgramDying.VictimSeat) &&
            (IsPaidHandRepaymentProgramDying() || IsOwnedDamagePointJudgmentProgramDying() || IsPreventionDrawProgramDying() || IsDamageJudgmentSuitPaymentDying() || IsPaidCounterspellProgramDying()))
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
                GetDiscardEligibleHand(current).Count > GetHandLimit(current))
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
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            frame.Id, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId,
            frame.OwnerSeat, context.Window, Activated: true, Completed: completed));
        if (CompleteDeferredTurnEndBinding(frame, context)) return;
        switch (context.Window)
        {
            case SkillProgramTriggerWindow.ProgramTargetCommitted:
            case SkillProgramTriggerWindow.DyingEntering:
            case SkillProgramTriggerWindow.SkillsChanged:
            case SkillProgramTriggerWindow.JudgmentPhaseStarting:
            case SkillProgramTriggerWindow.DrawPhaseSkipped:
            case SkillProgramTriggerWindow.CharacterTurnedOver:
            case SkillProgramTriggerWindow.CharacterTurnedFaceUp:
            case SkillProgramTriggerWindow.CharacterEnteredChain:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame changed || changed.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The skill ownership change lost its lifecycle window.");
                AdvanceProgramLifecycleCursor(changed);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.TurnStartBeforeNormalFlow:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame lifecycle ||
                    lifecycle.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The turn-start program lost its parent window.");
                AdvanceProgramLifecycleCursor(lifecycle);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.DrawPhaseEnded:
                if (_phase != TurnPhase.Draw || _resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawEnded ||
                    drawEnded.Id != context.ParentFrameId || drawEnded.Continuation != ProgramLifecycleContinuation.CompleteDrawPhaseEnded)
                    throw new InvalidOperationException("The draw-phase-ended program lost its parent window.");
                AdvanceProgramLifecycleCursor(drawEnded);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.AfterNormalDraw:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame afterDraw ||
                    afterDraw.Id != context.ParentFrameId ||
                    afterDraw.Continuation != ProgramLifecycleContinuation.CompleteAfterNormalDraw)
                    throw new InvalidOperationException("The after-draw program lost its parent window.");
                AdvanceProgramLifecycleCursor(afterDraw);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.DrawPhaseStarting:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
                    drawPhase.Id != context.ParentFrameId ||
                    drawPhase.Continuation != ProgramLifecycleContinuation.CompleteDrawPhase)
                    throw new InvalidOperationException("The draw-phase program lost its parent window.");
                var drawTrigger = GetProgramTrigger(frame);
                if (completed && drawTrigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement)
                {
                    ReplaceRuntimeTop(drawPhase with
                    {
                        NormalDrawReplaced = true,
                        CandidateIndex = drawPhase.Candidates.Count
                    });
                }
                else
                {
                    if (context.ResumeCandidateIndex is { } resumeCandidateIndex)
                        ReplaceRuntimeTop(drawPhase with { CandidateIndex = resumeCandidateIndex });
                    else
                        AdvanceProgramLifecycleCursor(drawPhase);
                }
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.PlayEnding:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame playEnding ||
                    playEnding.Id != context.ParentFrameId ||
                    playEnding.Continuation != ProgramLifecycleContinuation.CompletePlayPhase)
                    throw new InvalidOperationException("The play-ending program lost its parent window.");
                AdvanceProgramLifecycleCursor(playEnding);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.DiscardPhaseStarting:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame discardPhase ||
                    discardPhase.Id != context.ParentFrameId ||
                    discardPhase.Continuation != ProgramLifecycleContinuation.CompleteDiscardPhase)
                    throw new InvalidOperationException("The discard-phase program lost its parent window.");
                AdvanceProgramLifecycleCursor(discardPhase);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.DiscardPhaseEnded:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame discardEnded ||
                    discardEnded.Id != context.ParentFrameId ||
                    discardEnded.Continuation != ProgramLifecycleContinuation.EndTurnAfterDiscardPhase)
                    throw new InvalidOperationException("The discard-phase-ended program lost its parent window.");
                AdvanceProgramLifecycleCursor(discardEnded);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.TurnEnding:
                if (_resolutionStack.LastOrDefault() is not TurnEndingBoundaryFrame turnEnding ||
                    turnEnding.Id != context.ParentFrameId ||
                    turnEnding.Items[turnEnding.ItemIndex].Candidate is not { } expected ||
                    expected != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        turnEnding.Items[turnEnding.ItemIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The turn-ending program lost its parent item.");
                AdvanceTurnEndingBoundaryCursor(turnEnding);
                AdvanceRuntimeTop<TurnEndingBoundaryFrame>();
                break;
            case SkillProgramTriggerWindow.PlayPhaseStarting:
                if (_resolutionStack.LastOrDefault() is not PlayPhaseStartingBoundaryFrame playStarting ||
                    playStarting.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The play-phase-starting program lost its parent item.");
                if (context.ResumeCandidateIndex is { } resumeItemIndex)
                {
                    if (resumeItemIndex > playStarting.Items.Count)
                        throw new InvalidOperationException("The play-phase-starting resume cursor left the boundary.");
                    ReplaceRuntimeTop(playStarting with { ItemIndex = resumeItemIndex });
                }
                else
                {
                    if (playStarting.ItemIndex >= playStarting.Items.Count ||
                        playStarting.Items[playStarting.ItemIndex].Candidate is not { } startingExpected ||
                        startingExpected != new ProgramTriggerCandidate(
                            frame.OwnerSeat,
                            frame.SkillId,
                            frame.TriggerId!,
                            frame.SkillInstanceId,
                            frame.GameplayHash,
                            playStarting.Items[playStarting.ItemIndex].Priority,
                            context.OccurrenceIndex))
                        throw new InvalidOperationException("The play-phase-starting program lost its parent item.");
                    AdvancePlayPhaseStartingCursor(playStarting);
                }
                AdvanceRuntimeTop<PlayPhaseStartingBoundaryFrame>();
                break;
            case SkillProgramTriggerWindow.GameStarting:
                if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame gameStarting ||
                    gameStarting.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The startup program lost its parent window.");
                AdvanceProgramLifecycleCursor(gameStarting);
                AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.SelfDyingResponse:
            case SkillProgramTriggerWindow.DyingResponse:
                CompleteDyingProgramBinding(frame, completed);
                break;
            case SkillProgramTriggerWindow.BeforeDamageApplied:
                if (_resolutionStack.LastOrDefault() is not BeforeDamageProgramWindowFrame beforeDamage ||
                    beforeDamage.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The before-damage program lost its parent window.");
                AdvanceBeforeDamageProgramCandidate(beforeDamage, activated: true, completed: completed);
                AdvanceRuntimeTop<BeforeDamageProgramWindowFrame>();
                break;
            case SkillProgramTriggerWindow.DamageAppliedBeforeDying:
            case SkillProgramTriggerWindow.AfterDamageApplied:
                if (ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The damage program lost its parent window.");
                AdvanceDamageTriggerCandidate(damage);
                break;
            case SkillProgramTriggerWindow.AfterHpLost:
            case SkillProgramTriggerWindow.AfterHpRecovered:
            case SkillProgramTriggerWindow.AfterHealthChanged:
                if (_resolutionStack.LastOrDefault() is not HpChangedTriggerWindowFrame hpChanged ||
                    hpChanged.Id != context.ParentFrameId || hpChanged.Contexts[hpChanged.CandidateIndex] != context)
                    throw new InvalidOperationException("The HP-change program lost its parent event cursor.");
                AdvanceHpChangedProgramCursor(hpChanged);
                AdvanceRuntimeTop<HpChangedTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.CardsGained:
            case SkillProgramTriggerWindow.CardsMoved:
            case SkillProgramTriggerWindow.DiscardPileReceived:
            case SkillProgramTriggerWindow.FirstGameDomainCrossing:
                if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame cardsMoved ||
                    cardsMoved.Id != context.ParentFrameId ||
                    cardsMoved.Candidates[cardsMoved.CandidateIndex] != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        cardsMoved.Candidates[cardsMoved.CandidateIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The cards-moved program lost its parent batch cursor.");
                AdvanceCardsMovedProgramCursor(cardsMoved);
                AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.OwnerDied:
                if (_resolutionStack.LastOrDefault() is not ProgramDeathTriggerWindowFrame deathWindow ||
                    deathWindow.Id != context.ParentFrameId ||
                    deathWindow.Candidates[deathWindow.CandidateIndex] != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        deathWindow.Candidates[deathWindow.CandidateIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The owner-death program lost its parent cursor.");
                AdvanceOwnerDiedProgramCursor(deathWindow);
                AdvanceRuntimeTop<ProgramDeathTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.CharacterDied:
                if (_resolutionStack.LastOrDefault() is not ProgramKillTriggerWindowFrame killWindow ||
                    killWindow.Id != context.ParentFrameId ||
                    killWindow.CandidateIndex >= killWindow.Candidates.Count ||
                    killWindow.Candidates[killWindow.CandidateIndex] != new ProgramTriggerCandidate(
                        frame.OwnerSeat,
                        frame.SkillId,
                        frame.TriggerId!,
                        frame.SkillInstanceId,
                        frame.GameplayHash,
                        killWindow.Candidates[killWindow.CandidateIndex].Priority,
                        context.OccurrenceIndex))
                    throw new InvalidOperationException("The killer-death program lost its parent cursor.");
                ReplaceRuntimeTop(killWindow with { CandidateIndex = killWindow.CandidateIndex + 1 });
                AdvanceRuntimeTop<ProgramKillTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.CardEffectBeforeApply:
                CompleteCardEffectCandidate(context);
                break;
            case SkillProgramTriggerWindow.CardUseCommitted:
            case SkillProgramTriggerWindow.CardUseBeforeTargetEffects:
            case SkillProgramTriggerWindow.CardUseTargetsFinalized:
            case SkillProgramTriggerWindow.CardResponseAccepted:
            case SkillProgramTriggerWindow.CardUseCompleted:
                if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame cardAction ||
                    cardAction.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The card-action program lost its parent cursor.");
                AdvanceProgramCardCandidate(cardAction);
                AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.SlashTargetRedirecting:
            case SkillProgramTriggerWindow.SlashBeforeResponse:
            case SkillProgramTriggerWindow.SlashFullyDodged:
                if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame slashStage ||
                    slashStage.Id != context.ParentFrameId)
                    throw new InvalidOperationException("The Slash program lost its parent cursor.");
                AdvanceProgramCardCandidate(slashStage);
                AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.JudgmentFinalized:
                if (_resolutionStack.LastOrDefault() is not ProgramJudgmentTriggerWindowFrame finalizedWindow ||
                    finalizedWindow.Id != context.ParentFrameId ||
                    finalizedWindow.CandidateIndex >= finalizedWindow.Candidates.Count)
                    throw new InvalidOperationException("The finalized judgment program lost its parent window.");
                AdvanceProgramJudgmentCandidate(finalizedWindow);
                AdvanceRuntimeTop<ProgramJudgmentTriggerWindowFrame>();
                break;
            case SkillProgramTriggerWindow.JudgmentReplacing:
                CompleteProgramJudgmentReplacementBinding(frame, completed);
                break;
            default:
                throw new InvalidOperationException("Unsupported lifecycle program continuation.");
        }
    }

    private void ReturnRuntimeProgramMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (ReturnDamageJudgmentSuitPaymentMovement(frame)) return;
        if (ReturnCappedHandRefreshMovement(frame)) return;
        if (frame.SelectedCardPayment is { } payment && frame.SelectedCardPaymentResult is null)
        {
            if (frame.PendingMovementContinuation is not null || !payment.MovementCommitted ||
                payment.ActiveChildFrameId is not null)
                throw new InvalidOperationException("A selected-card payment cannot return before its movement children finish.");
            var valid = _players[frame.OwnerSeat].IsAlive &&
                HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId);
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
        var pending = frame.PendingMovementContinuation ??
            throw new InvalidOperationException("The movement continuation is missing.");
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        if (!_players[frame.OwnerSeat].IsAlive || !_players[pending.SubjectSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
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
