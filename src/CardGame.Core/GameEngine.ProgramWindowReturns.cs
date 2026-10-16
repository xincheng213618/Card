namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramWindowReturn(SkillProgramTriggerWindow.DyingExited)]
    private void ReturnSurvivedDyingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.DyingExited, Continuation: ProgramLifecycleContinuation.ResumeYanPuDyingExit,
                  YanPuDyingExit: { } receipt } parent || parent.Id != context.ParentFrameId ||
            context.SourceSeat != receipt.VictimSeat || context.TargetSeat != receipt.VictimSeat ||
            !IsYanPuDyingExitEdge(_resolutionStack.Count - 1) ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]) ||
            GetProgramTrigger(frame).Effects is not [{ Op: SkillProgramEffectOp.DrawDyingExitRoundDistance or SkillProgramEffectOp.ResolveQingliuFactionChoice }])
            throw new InvalidOperationException("A survived-Dying binding lost its exact completed-Dying parent and current candidate.");
        AdvanceProgramLifecycleCursor(parent);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.PindianCardsRevealed)]
    private void ReturnPindianCardsRevealedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompletePindianRevealBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.ActualSlashTargetPenalty)]
    private void ReturnActualSlashTargetPenaltyProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompleteSlashTargetPenaltyBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.ActualSlashTargetBenefit,
        SkillProgramTriggerWindow.SlashDodgeCancelledBenefit)]
    private void ReturnActualSlashTargetBenefitProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompleteSlashTargetBenefitBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.OtherActualUseTargeted)]
    private void ReturnOtherActualUseTargetedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompleteActualUseTargetBinding(frame, completed);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.ProgramTargetCommitted,
        SkillProgramTriggerWindow.DyingEntering,
        SkillProgramTriggerWindow.IdentityGeneralAppeared,
        SkillProgramTriggerWindow.SkillsChanged,
        SkillProgramTriggerWindow.JudgmentPhaseStarting,
        SkillProgramTriggerWindow.DrawPhaseSkipped,
        SkillProgramTriggerWindow.CharacterTurnedOver,
        SkillProgramTriggerWindow.CharacterTurnedFaceUp,
        SkillProgramTriggerWindow.CharacterEnteredChain)]
    private void ReturnProgramTargetCommittedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame changed || changed.Id != context.ParentFrameId)
            throw new InvalidOperationException("The skill ownership change lost its lifecycle window.");
        AdvanceProgramLifecycleCursor(changed);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.OtherActualTurnStarted)]
    private void ReturnOtherActualTurnStartedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompleteForeignActualTurnStartBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.TurnBeforeStart,
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]
    private void ReturnTurnBeforeStartProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame lifecycle ||
            lifecycle.Id != context.ParentFrameId)
            throw new InvalidOperationException("The turn-start program lost its parent window.");
        AdvanceProgramLifecycleCursor(lifecycle);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.RoundStarting)]
    private void ReturnRoundStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame roundStart ||
            roundStart.Id != context.ParentFrameId ||
            roundStart.Continuation != ProgramLifecycleContinuation.RoundProgramsTurnStart && !ValidBeiMiHuPreludeLifecycleWindow(roundStart))
            throw new InvalidOperationException("The round-starting program lost its parent window.");
        AdvanceProgramLifecycleCursor(roundStart);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DrawPhaseEnded)]
    private void ReturnDrawPhaseEndedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_phase != TurnPhase.Draw || _resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawEnded ||
            drawEnded.Id != context.ParentFrameId || drawEnded.Continuation != ProgramLifecycleContinuation.CompleteDrawPhaseEnded)
            throw new InvalidOperationException("The draw-phase-ended program lost its parent window.");
        AdvanceProgramLifecycleCursor(drawEnded);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.AfterNormalDraw)]
    private void ReturnAfterNormalDrawProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame afterDraw ||
            afterDraw.Id != context.ParentFrameId ||
            afterDraw.Continuation != ProgramLifecycleContinuation.CompleteAfterNormalDraw)
            throw new InvalidOperationException("The after-draw program lost its parent window.");
        AdvanceProgramLifecycleCursor(afterDraw);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DrawPhaseStarting)]
    private void ReturnDrawPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame drawPhase ||
            drawPhase.Id != context.ParentFrameId ||
            drawPhase.Continuation != ProgramLifecycleContinuation.CompleteDrawPhase)
            throw new InvalidOperationException("The draw-phase program lost its parent window.");
        var drawTrigger = GetProgramTrigger(frame);
        if (completed &&
            (drawTrigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement &&
             !drawTrigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GuessRanksForDrawReplacement) ||
             IsMingchaNormalDrawReplacement(frame)))
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
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.PlayEnding)]
    private void ReturnPlayEndingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame playEnding ||
            playEnding.Id != context.ParentFrameId ||
            playEnding.Continuation is not (ProgramLifecycleContinuation.CompletePlayPhase or
                ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd or ProgramLifecycleContinuation.CompleteWeiluForcedPlayEnd) ||
            playEnding.Continuation == ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd &&
            (playEnding.OwnerSeat != frame.OwnerSeat || playEnding.Window != context.Window ||
             playEnding.CandidateIndex < 0 || playEnding.CandidateIndex >= playEnding.Candidates.Count ||
             !IsPhaseHandDebtResolver(playEnding.Candidates[playEnding.CandidateIndex]) ||
             !MountObserverCandidateMatches(frame, playEnding.Candidates[playEnding.CandidateIndex])) ||
            playEnding.Continuation == ProgramLifecycleContinuation.CompleteWeiluForcedPlayEnd &&
            (playEnding.OwnerSeat != frame.OwnerSeat || playEnding.Window != context.Window ||
             playEnding.CandidateIndex < 0 || playEnding.CandidateIndex >= playEnding.Candidates.Count ||
             !MountObserverCandidateMatches(frame, playEnding.Candidates[playEnding.CandidateIndex]) ||
             playEnding.WeiluEndingPhase != context.WeiluPhase))
            throw new InvalidOperationException("The play-ending program lost its parent window.");
        AdvanceProgramLifecycleCursor(playEnding);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DiscardPhaseStarting)]
    private void ReturnDiscardPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame discardPhase ||
            discardPhase.Id != context.ParentFrameId ||
            discardPhase.Continuation != ProgramLifecycleContinuation.CompleteDiscardPhase)
            throw new InvalidOperationException("The discard-phase program lost its parent window.");
        AdvanceProgramLifecycleCursor(discardPhase);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DiscardPhaseEnded)]
    private void ReturnDiscardPhaseEndedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame discardEnded ||
            discardEnded.Id != context.ParentFrameId ||
            discardEnded.Continuation != ProgramLifecycleContinuation.EndTurnAfterDiscardPhase)
            throw new InvalidOperationException("The discard-phase-ended program lost its parent window.");
        AdvanceProgramLifecycleCursor(discardEnded);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.TurnEnding)]
    private void ReturnTurnEndingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
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
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.PlayPhaseStarting)]
    private void ReturnPlayPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
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
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.GameStarting)]
    private void ReturnGameStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame gameStarting ||
            gameStarting.Id != context.ParentFrameId)
            throw new InvalidOperationException("The startup program lost its parent window.");
        AdvanceProgramLifecycleCursor(gameStarting);
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.SelfDyingResponse,
        SkillProgramTriggerWindow.DyingResponse)]
    private void ReturnSelfDyingResponseProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompleteDyingProgramBinding(frame, completed);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.BeforeDamageApplied)]
    private void ReturnBeforeDamageAppliedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not BeforeDamageProgramWindowFrame beforeDamage ||
            beforeDamage.Id != context.ParentFrameId)
            throw new InvalidOperationException("The before-damage program lost its parent window.");
        AdvanceBeforeDamageProgramCandidate(beforeDamage, activated: true, completed: completed);
        AdvanceRuntimeTop<BeforeDamageProgramWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DamageAppliedBeforeDying,
        SkillProgramTriggerWindow.AfterDamageApplied)]
    private void ReturnDamageAppliedBeforeDyingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId)
            throw new InvalidOperationException("The damage program lost its parent window.");
        AdvanceDamageTriggerCandidate(damage);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.AfterHpLost,
        SkillProgramTriggerWindow.AfterHpRecovered,
        SkillProgramTriggerWindow.AfterHealthChanged)]
    private void ReturnAfterHpLostProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not HpChangedTriggerWindowFrame hpChanged ||
            hpChanged.Id != context.ParentFrameId || hpChanged.Contexts[hpChanged.CandidateIndex] != context)
            throw new InvalidOperationException("The HP-change program lost its parent event cursor.");
        AdvanceHpChangedProgramCursor(hpChanged);
        AdvanceRuntimeTop<HpChangedTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CardsGained,
        SkillProgramTriggerWindow.CardsMoved,
        SkillProgramTriggerWindow.DiscardPileReceived,
        SkillProgramTriggerWindow.FirstGameDomainCrossing)]
    private void ReturnCardsGainedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
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
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.OwnerDied)]
    private void ReturnOwnerDiedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
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
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CharacterDied)]
    private void ReturnCharacterDiedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
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
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CardEffectBeforeApply)]
    private void ReturnCardEffectBeforeApplyProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        CompleteCardEffectCandidate(context);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CardUseCommitted,
        SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
        SkillProgramTriggerWindow.CardUseTargetsFinalized,
        SkillProgramTriggerWindow.CardResponseAccepted,
        SkillProgramTriggerWindow.CardResponseCompleted,
        SkillProgramTriggerWindow.CardSupplyCompleted,
        SkillProgramTriggerWindow.CardUseCompleted)]
    private void ReturnCardUseCommittedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame cardAction ||
            cardAction.Id != context.ParentFrameId)
            throw new InvalidOperationException("The card-action program lost its parent cursor.");
        RestoreCompletedUseAttackAfterIssuedChild(frame, cardAction);
        AdvanceProgramCardCandidate(cardAction);
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.SlashTargetRedirecting,
        SkillProgramTriggerWindow.SlashBeforeResponse,
        SkillProgramTriggerWindow.SlashFullyDodged)]
    private void ReturnSlashTargetRedirectingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame slashStage ||
            slashStage.Id != context.ParentFrameId)
            throw new InvalidOperationException("The Slash program lost its parent cursor.");
        AdvanceProgramCardCandidate(slashStage);
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.JudgmentFinalized,
        SkillProgramTriggerWindow.JudgmentBeforeResult)]
    private void ReturnJudgmentFinalizedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (_resolutionStack.LastOrDefault() is not ProgramJudgmentTriggerWindowFrame finalizedWindow ||
            (finalizedWindow.ZhenyiBeforeResult == true) != (context.Window == SkillProgramTriggerWindow.JudgmentBeforeResult) ||
            finalizedWindow.Id != context.ParentFrameId ||
            finalizedWindow.CandidateIndex >= finalizedWindow.Candidates.Count)
            throw new InvalidOperationException("The finalized judgment program lost its parent window.");
        AdvanceProgramJudgmentCandidate(finalizedWindow);
        AdvanceRuntimeTop<ProgramJudgmentTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.JudgmentReplacing)]
    private void ReturnJudgmentReplacingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        CompleteProgramJudgmentReplacementBinding(frame, completed);
    }
}
