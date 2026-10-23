namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HappyTscDulie = "happy:dulie", HappyTscChongwei = "happy:chongwei",
        HappyTscPowei = "happy:powei", HappyTscShenzhu = "happy:shenzhu";
    private static bool IsShenTscPoweiFamily(string skill) => skill is ShenTscPowei or HappyTscChongwei;
    private int HappyTscFieldWei() => _players.Where(p => p.IsAlive).Sum(p => p.Markers.GetValueOrDefault(PlayerMarkerKind.ShenTaishiCiWei));
    private bool HappyTscAwakened(int owner) => ProgramEventHistory<HappyTaishiCiAwakenedEvent>().Any(e => e.Source.OwnerSeat == owner);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 41740)]
    private bool CanOfferHappyTaishiCi(ProgramTriggerLegalityContext g)
    {
        if (g.SkillId == HappyTscDulie)
            return g.Trigger.Id == "other-slash-heart"
                ? g.Context.CardUse is { } use && use.ActorSeat != g.OwnerSeat
                : HappyTscFieldWei() > 0 && ProgramEventHistory<PlayerDyingEvent>().Count(e => e.VictimSeat == g.OwnerSeat) == 1;
        if (g.SkillId == HappyTscChongwei)
            return g.Trigger.Id switch
            {
                "initialize" => true,
                "rotate" => _players.Any(p => p.IsAlive && ShenTscWei(p.Seat, g.OwnerSeat) > 0),
                "damage-removes-wei" => g.Context.TargetSeat is { } target && ShenTscWei(target, g.OwnerSeat) > 0,
                "marked-turn" => g.Context.SourceSeat is { } actor && actor != g.OwnerSeat && ShenTscWei(actor, g.OwnerSeat) > 0 &&
                    (ShenTscDiscardCards(g.OwnerSeat).Length > 0 || ShenTscMayTake(g.OwnerSeat, actor)),
                _ => false
            };
        if (g.SkillId == HappyTscPowei) return !HappyTscAwakened(g.OwnerSeat) && HappyTscFieldWei() == 0;
        if (g.SkillId == HappyTscShenzhu)
            return g.Trigger.Id == "start-slash" || g.Context.CardUse is { } completed && completed.ActorSeat == g.OwnerSeat &&
                LifecycleCardUse(completed.ParentCardUseFrameId)?.Action is { PhysicalCards.Count: 1, ConversionChain.Count: 0 };
        return true;
    }

    // Transfers also emit negative marker deltas. Only the native damage-removal
    // instruction is a removed Wei for Powei's current-round draw.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 41740)]
    private void CountHappyTaishiCiRemovedWei(PlayerMarkerChangedEvent fact)
    {
        if (fact.Marker != PlayerMarkerKind.ShenTaishiCiWei || fact.Delta >= 0 ||
            !_players.Any(p => HasProgramSkill(p, HappyTscPowei)) ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame { TriggerId: "damage-removes-wei" } f ||
            !IsShenTscPoweiFamily(f.SkillId) || f.Id != fact.ResolutionId || fact.SkillOwnerSeat != f.OwnerSeat ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, TargetSeat: { } target } || target != fact.PlayerSeat) return;
        AdvanceEventRulesAndQueueFact(new HappyTaishiCiWeiRemovedEvent(f.Id, ShenTscSource(f), target, -fact.Delta, _roundNumber));
    }

    [ProgramCapability] private sealed class HappyTaishiCiHost(GameEngine engine) : IHappyTaishiCiHost
    {
        public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveHappyTaishiCiChongwei => engine.ResolveShenTscPowei(f),
            SkillProgramEffectOp.RecoverHappyTaishiCiDulie => engine.RecoverHappyTsc(f),
            _ => engine.BeginHappyTsc(f, op)
        };
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHappyTaishiCiChongwei)]
    private PromptChoice ChooseHappyTaishiCiChongwei(PendingDecision p, ProgramSkillFrame f) => ChooseShenTscPowei(p, f);

    private SkillProgramStepOutcome RecoverHappyTsc(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var count = HappyTscFieldWei();
        if (f.SkillId != HappyTscDulie || f.TriggerId != "first-dying-recover" || f.WindowContext?.Window != SkillProgramTriggerWindow.DyingEntering ||
            ActiveDying is not { } dying || dying.VictimSeat != f.OwnerSeat ||
            ProgramEventHistory<PlayerDyingEvent>().Count(e => e.VictimSeat == f.OwnerSeat) != 1 || count <= 0 ||
            ProgramEventHistory<HappyTaishiCiDulieRecoveredEvent>().Any(e => e.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("Happy Dulie requires its first actual dying entry and existing field Wei.");
        AdvanceEventRulesAndQueueFact(new HappyTaishiCiDulieRecoveredEvent(f.Id, f.OwnerSeat, dying.FrameId, count));
        new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, Math.Max(0, Math.Min(count, _players[f.OwnerSeat].MaxHp) - _players[f.OwnerSeat].Hp), null, null);
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome BeginHappyTsc(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HappyTaishiCi is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context)
            throw new InvalidOperationException("Happy Taishi Ci requires its original unissued instruction.");
        var awake = op == SkillProgramEffectOp.AwakenHappyTaishiCiPowei;
        if (awake ? f.SkillId != HappyTscPowei || context.Window != SkillProgramTriggerWindow.AfterTurnEnded || HappyTscAwakened(f.OwnerSeat) || HappyTscFieldWei() != 0
            : op != SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash || f.SkillId != HappyTscShenzhu || context.Window != SkillProgramTriggerWindow.TurnBeforeStart)
            throw new InvalidOperationException("Happy Taishi Ci lost its actual awakening or own-turn start.");
        var count = awake ? ProgramEventHistory<HappyTaishiCiWeiRemovedEvent>().Where(e => e.Round == _roundNumber).Sum(e => e.Count) : 1;
        var r = new HappyTaishiCiReceipt(ShenTscSource(f), f.GameplayHash, context.ParentFrameId, _turnNumber, _roundNumber, op, count,
            awake ? HappyTaishiCiStage.GrantChildren : HappyTaishiCiStage.MovementChildren, _movementSequence, _movementSequence);
        ReplaceRuntimeTop(f = f with { HappyTaishiCi = r });
        AdvanceEventRulesAndQueueFact(new HappyTaishiCiStartedEvent(f.Id, r.Source, r.Hash, r.Parent, r.Turn, r.Round, r.Op, r.Count));
        if (awake)
        {
            AdvanceEventRulesAndQueueFact(new HappyTaishiCiAwakenedEvent(f.Id, r.Source, r.Round, r.Count));
            new ProgramSkillHost(this).GrantSkills(f, [HappyTscShenzhu]);
        }
        else
        {
            // The draw pile's first printed Slash is a real existing entity;
            // hand identity conversions do not change cards in this public source.
            var card = _cardZones.CardsAt(CardLocation.DrawPile).FirstOrDefault(c => IsSlashCard(c.Kind));
            if (card is not null)
            {
                ReplaceRuntimeTop(f = f with { HappyTaishiCi = r with { CardId = card.Id } });
                MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), HappyTscSlashReason, (_, _) =>
                {
                    var current = GetActiveProgramFrame(f.Id);
                    ReplaceRuntimeTop(current with { HappyTaishiCi = current.HappyTaishiCi! with { After = _movementSequence } });
                });
                if (AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat) == SkillProgramStepOutcome.AwaitChild) return SkillProgramStepOutcome.AwaitChild;
            }
        }
        ResumeHappyTsc(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private static CardMoveReason HappyTscSlashReason => new("skill.happy.shenzhu.start-slash");
    private static CardMoveReason HappyTscAwakenDrawReason => new("skill.happy.powei.round-removed");
    [ProgramContinuation(ProgramContinuationKind.Resume, 41740)]
    private bool ResumeHappyTsc(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyTaishiCi: { } r } f || f.Id != id || _pendingDecision is not null) return false;
        RequireHappyTsc(f);
        if (r.Stage == HappyTaishiCiStage.Drawing) return true;
        var movement = r.Stage == HappyTaishiCiStage.MovementChildren;
        if (TryBeginQueuedRecoveryReplacement(id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        f = GetActiveProgramFrame(id); r = f.HappyTaishiCi!;
        if (r.Stage == HappyTaishiCiStage.GrantChildren && r.Count > 0 && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        {
            ReplaceRuntimeTop(f = f with { HappyTaishiCi = r with { Stage = HappyTaishiCiStage.Drawing }, PendingMovementContinuation = null });
            BeginOperationProgramDraw(f, f.OwnerSeat, r.Count, HappyTscAwakenDrawReason); return true;
        }
        ReplaceRuntimeTop(f = f with { HappyTaishiCi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 41740)]
    private bool ReturnHappyTscMovement(ProgramSkillFrame f) => f.HappyTaishiCi is not null && ResumeHappyTsc(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.AwakenHappyTaishiCiPowei)]
    private void ReturnHappyTscDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        RequireHappyTsc(f); var r = f.HappyTaishiCi!;
        if (r.Stage != HappyTaishiCiStage.Drawing || r.Draw is not null || outcome.RecipientSeat != f.OwnerSeat ||
            outcome.RequestedCount != r.Count || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Happy Powei lost its once-frozen native round-count draw.");
        ReplaceRuntimeTop(f with { HappyTaishiCi = r with { Stage = HappyTaishiCiStage.MovementChildren, Draw = outcome }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new HappyTaishiCiDrawReturnedEvent(f.Id, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ResumeHappyTsc(f.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 41740)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 41740)]
    private bool RetainHappyTsc(ProgramSkillFrame f) => f.HappyTaishiCi is not null;
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 41740)]
    private bool ProveHappyTscMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation continuation) =>
        f.HappyTaishiCi is { Stage: HappyTaishiCiStage.MovementChildren } r && effect?.Op == r.Op &&
        continuation.SubjectSeat == f.OwnerSeat && continuation.BeforeCount == 0 && continuation.CoverageResultBind is null && ValidHappyTscMaterial(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 41740)]
    private void RequireHappyTsc(ProgramSkillFrame f)
    {
        if (f.HappyTaishiCi is not { } r) return;
        if (r.Source != ShenTscSource(f) || r.Hash != f.GameplayHash || r.Parent != f.WindowContext?.ParentFrameId || f.InstructionIndex != 1 ||
            r.Turn != _turnNumber || r.Round != _roundNumber || r.Round < 1 || r.After < r.Before || !Enum.IsDefined(r.Stage) ||
            (r.Op == SkillProgramEffectOp.AwakenHappyTaishiCiPowei
                ? f.SkillId != HappyTscPowei || f.TriggerId != "awaken" || f.WindowContext.Window != SkillProgramTriggerWindow.AfterTurnEnded
                : r.Op != SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash || f.SkillId != HappyTscShenzhu || f.TriggerId != "start-slash" || f.WindowContext.Window != SkillProgramTriggerWindow.TurnBeforeStart) ||
            ProgramEventHistory<HappyTaishiCiStartedEvent>().Count(e => e == new HappyTaishiCiStartedEvent(f.Id, r.Source, r.Hash, r.Parent, r.Turn, r.Round, r.Op, r.Count)) != 1 ||
            r.Stage == HappyTaishiCiStage.MovementChildren && !ValidHappyTscMaterial(f))
            throw new InvalidOperationException("Happy Taishi Ci lost its exact source, original trigger, round or once-issued material receipt.");
    }
    private bool ValidHappyTscMaterial(ProgramSkillFrame f)
    {
        var r = f.HappyTaishiCi!;
        if (r.Op == SkillProgramEffectOp.ObtainHappyTaishiCiStartSlash)
            return r.CardId is not { } card ? r.Before == r.After :
                _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason == HappyTscSlashReason).ToArray() is
                    [{ CardId: var actual, From: var from, To: var to, CardKind: var kind }] && actual == card && from == CardLocation.DrawPile &&
                    to == CardLocation.Hand(f.OwnerSeat) && IsSlashCard(kind);
        return r.Draw is { } draw && draw.RecipientSeat == f.OwnerSeat && draw.RequestedCount == r.Count && draw.SequenceAfter <= _movementSequence &&
            _cardMovements.Where(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason == HappyTscAwakenDrawReason).Select(m => m.CardId).SequenceEqual(draw.ActualDrawIds) &&
            ProgramEventHistory<HappyTaishiCiDrawReturnedEvent>().Count(e => e == new HappyTaishiCiDrawReturnedEvent(f.Id, draw.RequestedCount, draw.ActualDrawIds.Count,
                draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId)) == 1;
    }
}
