namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed class ProgramWindowReturnRuntime(
    IProgramRuntimeFrames frames, IProgramWindowCursors cursors,
    IProgramWindowReturnQueries queries, IProgramWindowNativeReturns nativeReturns)
{
    [ProgramWindowReturn(SkillProgramTriggerWindow.DyingExited)]
    internal void ReturnSurvivedDyingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.DyingExited, Continuation: ProgramLifecycleContinuation.ResumeYanPuDyingExit,
                  YanPuDyingExit: { } receipt } parent || parent.Id != context.ParentFrameId ||
            context.SourceSeat != receipt.VictimSeat || context.TargetSeat != receipt.VictimSeat ||
            !queries.IsCurrentYanPuDyingExitEdge() ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !queries.MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]) ||
            queries.GetProgramTrigger(frame).Effects is not [{ Op: SkillProgramEffectOp.DrawDyingExitRoundDistance or SkillProgramEffectOp.ResolveQingliuFactionChoice or SkillProgramEffectOp.ResolveMouJiangWeiFuzhan }])
            throw new InvalidOperationException("A survived-Dying binding lost its exact completed-Dying parent and current candidate.");
        cursors.AdvanceProgramLifecycleCursor(parent);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.PindianCardsRevealed)]
    internal void ReturnPindianCardsRevealedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompletePindianRevealBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.ActualSlashTargetPenalty)]
    internal void ReturnActualSlashTargetPenaltyProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompleteSlashTargetPenaltyBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.ActualSlashTargetBenefit,
        SkillProgramTriggerWindow.SlashDodgeCancelledBenefit)]
    internal void ReturnActualSlashTargetBenefitProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompleteSlashTargetBenefitBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.OtherActualUseTargeted)]
    internal void ReturnOtherActualUseTargetedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompleteActualUseTargetBinding(frame, completed);
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
    internal void ReturnProgramTargetCommittedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame changed || changed.Id != context.ParentFrameId)
            throw new InvalidOperationException("The skill ownership change lost its lifecycle window.");
        cursors.AdvanceProgramLifecycleCursor(changed);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.OtherActualTurnStarted)]
    internal void ReturnOtherActualTurnStartedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompleteForeignActualTurnStartBinding(frame);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.TurnBeforeStart,
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]
    internal void ReturnTurnBeforeStartProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame lifecycle ||
            lifecycle.Id != context.ParentFrameId)
            throw new InvalidOperationException("The turn-start program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(lifecycle);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.RoundStarting)]
    internal void ReturnRoundStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame roundStart ||
            roundStart.Id != context.ParentFrameId ||
            roundStart.Continuation != ProgramLifecycleContinuation.RoundProgramsTurnStart && !queries.ValidBeiMiHuPreludeLifecycleWindow(roundStart))
            throw new InvalidOperationException("The round-starting program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(roundStart);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DrawPhaseEnded)]
    internal void ReturnDrawPhaseEndedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (queries.Phase != TurnPhase.Draw || frames.Top is not ProgramLifecycleTriggerWindowFrame drawEnded ||
            drawEnded.Id != context.ParentFrameId || drawEnded.Continuation != ProgramLifecycleContinuation.CompleteDrawPhaseEnded)
            throw new InvalidOperationException("The draw-phase-ended program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(drawEnded);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.AfterNormalDraw)]
    internal void ReturnAfterNormalDrawProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame afterDraw ||
            afterDraw.Id != context.ParentFrameId ||
            afterDraw.Continuation != ProgramLifecycleContinuation.CompleteAfterNormalDraw)
            throw new InvalidOperationException("The after-draw program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(afterDraw);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DrawPhaseStarting)]
    internal void ReturnDrawPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame drawPhase ||
            drawPhase.Id != context.ParentFrameId ||
            drawPhase.Continuation != ProgramLifecycleContinuation.CompleteDrawPhase)
            throw new InvalidOperationException("The draw-phase program lost its parent window.");
        var drawTrigger = queries.GetProgramTrigger(frame);
        if (completed &&
            (drawTrigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement &&
             !drawTrigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GuessRanksForDrawReplacement) ||
             queries.IsMingchaNormalDrawReplacement(frame)))
        {
            frames.ReplaceTop(drawPhase with
            {
                NormalDrawReplaced = true,
                CandidateIndex = drawPhase.Candidates.Count
            });
        }
        else
        {
            if (context.ResumeCandidateIndex is { } resumeCandidateIndex)
                frames.ReplaceTop(drawPhase with { CandidateIndex = resumeCandidateIndex });
            else
                cursors.AdvanceProgramLifecycleCursor(drawPhase);
        }
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.PlayEnding)]
    internal void ReturnPlayEndingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame playEnding ||
            playEnding.Id != context.ParentFrameId ||
            playEnding.Continuation is not (ProgramLifecycleContinuation.CompletePlayPhase or
                ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd or ProgramLifecycleContinuation.CompleteWeiluForcedPlayEnd) ||
            playEnding.Continuation == ProgramLifecycleContinuation.CompletePhaseHandDebtForcedEnd &&
            (playEnding.OwnerSeat != frame.OwnerSeat || playEnding.Window != context.Window ||
             playEnding.CandidateIndex < 0 || playEnding.CandidateIndex >= playEnding.Candidates.Count ||
             !queries.IsPhaseHandDebtResolver(playEnding.Candidates[playEnding.CandidateIndex]) ||
             !queries.MountObserverCandidateMatches(frame, playEnding.Candidates[playEnding.CandidateIndex])) ||
            playEnding.Continuation == ProgramLifecycleContinuation.CompleteWeiluForcedPlayEnd &&
            (playEnding.OwnerSeat != frame.OwnerSeat || playEnding.Window != context.Window ||
             playEnding.CandidateIndex < 0 || playEnding.CandidateIndex >= playEnding.Candidates.Count ||
             !queries.MountObserverCandidateMatches(frame, playEnding.Candidates[playEnding.CandidateIndex]) ||
             playEnding.WeiluEndingPhase != context.WeiluPhase))
            throw new InvalidOperationException("The play-ending program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(playEnding);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DiscardPhaseStarting)]
    internal void ReturnDiscardPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame discardPhase ||
            discardPhase.Id != context.ParentFrameId ||
            discardPhase.Continuation != ProgramLifecycleContinuation.CompleteDiscardPhase)
            throw new InvalidOperationException("The discard-phase program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(discardPhase);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DiscardPhaseEnded)]
    internal void ReturnDiscardPhaseEndedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame discardEnded ||
            discardEnded.Id != context.ParentFrameId ||
            discardEnded.Continuation != ProgramLifecycleContinuation.EndTurnAfterDiscardPhase)
            throw new InvalidOperationException("The discard-phase-ended program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(discardEnded);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.TurnEnding)]
    internal void ReturnTurnEndingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not TurnEndingBoundaryFrame turnEnding ||
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
        cursors.AdvanceTurnEndingBoundaryCursor(turnEnding);
        frames.AdvanceTop<TurnEndingBoundaryFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.PlayPhaseStarting)]
    internal void ReturnPlayPhaseStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not PlayPhaseStartingBoundaryFrame playStarting ||
            playStarting.Id != context.ParentFrameId)
            throw new InvalidOperationException("The play-phase-starting program lost its parent item.");
        if (context.ResumeCandidateIndex is { } resumeItemIndex)
        {
            if (resumeItemIndex > playStarting.Items.Count)
                throw new InvalidOperationException("The play-phase-starting resume cursor left the boundary.");
            frames.ReplaceTop(playStarting with { ItemIndex = resumeItemIndex });
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
            cursors.AdvancePlayPhaseStartingCursor(playStarting);
        }
        frames.AdvanceTop<PlayPhaseStartingBoundaryFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.GameStarting)]
    internal void ReturnGameStartingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramLifecycleTriggerWindowFrame gameStarting ||
            gameStarting.Id != context.ParentFrameId)
            throw new InvalidOperationException("The startup program lost its parent window.");
        cursors.AdvanceProgramLifecycleCursor(gameStarting);
        frames.AdvanceTop<ProgramLifecycleTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.SelfDyingResponse,
        SkillProgramTriggerWindow.DyingResponse)]
    internal void ReturnSelfDyingResponseProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompleteDyingProgramBinding(frame, completed);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.BeforeDamageApplied)]
    internal void ReturnBeforeDamageAppliedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not BeforeDamageProgramWindowFrame beforeDamage ||
            beforeDamage.Id != context.ParentFrameId)
            throw new InvalidOperationException("The before-damage program lost its parent window.");
        cursors.AdvanceBeforeDamageProgramCandidate(beforeDamage, activated: true, completed: completed);
        frames.AdvanceTop<BeforeDamageProgramWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.DamageAppliedBeforeDying,
        SkillProgramTriggerWindow.AfterDamageApplied)]
    internal void ReturnDamageAppliedBeforeDyingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (queries.ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId)
            throw new InvalidOperationException("The damage program lost its parent window.");
        nativeReturns.AdvanceDamageTriggerCandidate(damage);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.AfterHpLost,
        SkillProgramTriggerWindow.AfterHpRecovered,
        SkillProgramTriggerWindow.AfterHealthChanged)]
    internal void ReturnAfterHpLostProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not HpChangedTriggerWindowFrame hpChanged ||
            hpChanged.Id != context.ParentFrameId || hpChanged.Contexts[hpChanged.CandidateIndex] != context)
            throw new InvalidOperationException("The HP-change program lost its parent event cursor.");
        cursors.AdvanceHpChangedProgramCursor(hpChanged);
        frames.AdvanceTop<HpChangedTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CardsGained,
        SkillProgramTriggerWindow.CardsMoved,
        SkillProgramTriggerWindow.DiscardPileReceived,
        SkillProgramTriggerWindow.FirstGameDomainCrossing)]
    internal void ReturnCardsGainedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not CardsMovedTriggerWindowFrame cardsMoved ||
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
        cursors.AdvanceCardsMovedProgramCursor(cardsMoved);
        frames.AdvanceTop<CardsMovedTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.OwnerDied)]
    internal void ReturnOwnerDiedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramDeathTriggerWindowFrame deathWindow ||
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
        cursors.AdvanceOwnerDiedProgramCursor(deathWindow);
        frames.AdvanceTop<ProgramDeathTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CharacterDied)]
    internal void ReturnCharacterDiedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramKillTriggerWindowFrame killWindow ||
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
        frames.ReplaceTop(killWindow with { CandidateIndex = killWindow.CandidateIndex + 1 });
        frames.AdvanceTop<ProgramKillTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CardEffectBeforeApply)]
    internal void ReturnCardEffectBeforeApplyProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        nativeReturns.CompleteCardEffectCandidate(context);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.CardUseCommitted,
        SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
        SkillProgramTriggerWindow.CardUseTargetsFinalized,
        SkillProgramTriggerWindow.CardResponseAccepted,
        SkillProgramTriggerWindow.CardResponseCompleted,
        SkillProgramTriggerWindow.CardSupplyCompleted,
        SkillProgramTriggerWindow.CardUseCompleted)]
    internal void ReturnCardUseCommittedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramCardTriggerWindowFrame cardAction ||
            cardAction.Id != context.ParentFrameId)
            throw new InvalidOperationException("The card-action program lost its parent cursor.");
        nativeReturns.RestoreCompletedUseAttackAfterIssuedChild(frame, cardAction);
        cursors.AdvanceProgramCardCandidate(cardAction);
        frames.AdvanceTop<ProgramCardTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.SlashTargetRedirecting,
        SkillProgramTriggerWindow.SlashBeforeResponse,
        SkillProgramTriggerWindow.SlashFullyDodged)]
    internal void ReturnSlashTargetRedirectingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramCardTriggerWindowFrame slashStage ||
            slashStage.Id != context.ParentFrameId)
            throw new InvalidOperationException("The Slash program lost its parent cursor.");
        cursors.AdvanceProgramCardCandidate(slashStage);
        frames.AdvanceTop<ProgramCardTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.JudgmentFinalized,
        SkillProgramTriggerWindow.JudgmentBeforeResult)]
    internal void ReturnJudgmentFinalizedProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext!;
        if (frames.Top is not ProgramJudgmentTriggerWindowFrame finalizedWindow ||
            (finalizedWindow.ZhenyiBeforeResult == true) != (context.Window == SkillProgramTriggerWindow.JudgmentBeforeResult) ||
            finalizedWindow.Id != context.ParentFrameId ||
            finalizedWindow.CandidateIndex >= finalizedWindow.Candidates.Count)
            throw new InvalidOperationException("The finalized judgment program lost its parent window.");
        cursors.AdvanceProgramJudgmentCandidate(finalizedWindow);
        frames.AdvanceTop<ProgramJudgmentTriggerWindowFrame>();
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.JudgmentReplacing)]
    internal void ReturnJudgmentReplacingProgramWindow(ProgramSkillFrame frame, bool completed)
    {
        nativeReturns.CompleteProgramJudgmentReplacementBinding(frame, completed);
    }
}
