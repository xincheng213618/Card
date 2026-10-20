namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhangYiDrawReason = "skill-program.zhang-yi.kangrui.draw";
    private bool HasZhangYiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveKangruiFirstOwnTurnDamage);
    private CardConversionSource ZhangYiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class ZhangYiCapability(GameEngine engine) : IZhangYiProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginZhangYi(f, op); }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26400)]
    private void ObserveZhangYiActualDamage(IGameEvent payload)
    {
        if (!HasZhangYiCapability || _turnNumber <= 0 || payload is not DamageAppliedEvent { Amount: > 0 } e ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || CurrentDamageAttempt is not { } attack ||
            damage.ParentFrameId != attack.ResolutionId || damage.SourceSeat != e.SourceSeat || damage.TargetSeat != e.TargetSeat ||
            damage.Amount != e.Amount || attack.SourceSeat != e.SourceSeat || attack.TargetSeat != e.TargetSeat ||
            attack.IsSourceLess != e.SourceLess || !attack.DamageWasApplied) return;
        if (ProgramEventHistory<ZhangYiActualDamageFactEvent>().Any(x => x.DamageFrameId == damage.Id)) return;
        AdvanceEventRulesAndQueueFact(new ZhangYiActualDamageFactEvent(_turnNumber, _currentSeat, damage.Id,
            attack.ResolutionId, e.SourceSeat, e.TargetSeat, e.Amount, e.SourceLess));
        if (!e.SourceLess)
            foreach (var grant in ProgramEventHistory<ZhangYiKangruiArmedEvent>().Where(g => g.ActualTurnNumber == _turnNumber && g.RecipientSeat == e.SourceSeat).ToArray())
                if (!ProgramEventHistory<ZhangYiKangruiHandLimitSetEvent>().Any(x => x.GrantFrameId == grant.FrameId))
                    AdvanceEventRulesAndQueueFact(new ZhangYiKangruiHandLimitSetEvent(grant.FrameId, damage.Id, e.SourceSeat, _turnNumber));
    }

    private bool IsFirstZhangYiOwnTurnDamage(ProgramSkillWindowContext c)
    {
        if (c.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.TargetSeat != _currentSeat || c.Amount <= 0 ||
            c.DamageFrameId is not { } damageId || ActiveDamageTrigger?.Id != c.ParentFrameId) return false;
        var first = ProgramEventHistory<ZhangYiActualDamageFactEvent>().FirstOrDefault(e => e.ActualTurnNumber == _turnNumber && e.TargetSeat == _currentSeat);
        return first is not null && first.DamageFrameId == damageId && first.ActualTurnOwnerSeat == c.TargetSeat && first.Amount == c.Amount;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26400)]
    private bool CanOfferZhangYi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveKangruiFirstOwnTurnDamage)) return IsFirstZhangYiOwnTurnDamage(context);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDianjunActualTurnEnd))
            return context.Window == SkillProgramTriggerWindow.AfterTurnEnded && candidate.OwnerSeat == _currentSeat &&
                _phase == TurnPhase.Finished && context.TargetSeat == candidate.OwnerSeat;
        return true;
    }

    private SkillProgramStepOutcome BeginZhangYi(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id); var dianjun = op == SkillProgramEffectOp.ResolveDianjunActualTurnEnd;
        if (f.ZhangYi is not null || f.InstructionIndex != 1 || f.WindowContext is not { } c ||
            f.SkillId != (dianjun ? "ol:dianjun" : "ol:kangrui") ||
            !CanOfferZhangYi(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), c) ||
            c.TargetSeat is not { } target || !IsValidPlayerSeat(target))
            throw new InvalidOperationException("Zhang Yi requires its exact actual end or first own-turn positive damage.");
        var r = new ZhangYiReceipt(ZhangYiSource(f), f.GameplayHash, f.InstructionIndex, c.ParentFrameId,
            _turnNumber, target, dianjun ? ZhangYiStage.DianjunDamage : ZhangYiStage.KangruiDraw, c.DamageFrameId, _movementSequence, _movementSequence);
        ReplaceRuntimeTop(f = f with { ZhangYi = r });
        AdvanceEventRulesAndQueueFact(new ZhangYiResolutionStartedEvent(f.Id, r.Source, r.GameplayHash, r.ParentWindowId, r.ActualTurnNumber, target, r.DamageFrameId));
        if (dianjun) return BeginProgramSkillDamage(f, f.OwnerSeat, 1, sourceLess: true);
        AdvanceEventRulesAndQueueFact(new ZhangYiKangruiDrawIssuedEvent(f.Id, f.OwnerSeat, target, r.ActualTurnNumber, r.SequenceBefore));
        DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new(ZhangYiDrawReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ZhangYi = f.ZhangYi! with { SequenceAfter = _movementSequence } });
        ContinueZhangYi(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> ZhangYiChoices(ProgramSkillFrame f) => Array.AsReadOnly(new[] { "recover", "damage" }.Select(branch =>
        new PromptChoice(new($"zhang-yi.{f.Id}.{branch}"), branch == "recover" ? "令其回复1点体力" : "令其本回合下次造成的伤害+1", [], [f.ZhangYi!.RecipientSeat],
            new Dictionary<string, string> { ["program-action"] = "zhang-yi-kangrui", ["branch"] = branch })).ToArray());

    private void PublishZhangYi(ProgramSkillFrame f)
    {
        var r = f.ZhangYi!; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "亢锐：请选择对本次受伤角色的效果。", [], [r.RecipientSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.RecipientSeat, Choices = ZhangYiChoices(f),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("zhang-yi-kangrui")]
    private void AnswerZhangYi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangYi: { Stage: ZhangYiStage.KangruiChoice } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], ZhangYiChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Kangrui lost its paid draw, frozen injured actor or exact choice.");
        AssertZhangYi(f); ClearPendingDecision();
        var branch = choice.Parameters["branch"]; var recipient = _players[r.RecipientSeat];
        var recovery = branch == "recover" && recipient.IsAlive && recipient.HasHpValue ? Math.Min(1, Math.Max(0, recipient.MaxHp - recipient.Hp)) : 0;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect,
            ZhangYi = r with { Choice = branch, RecoveryRequested = recovery, Stage = ZhangYiStage.KangruiRecovery } });
        AdvanceEventRulesAndQueueFact(new ZhangYiKangruiChoiceEvent(f.Id, r.Source, r.RecipientSeat, r.ActualTurnNumber, branch, recovery));
        if (recovery > 0) new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, r.RecipientSeat, recovery, null, null);
        ContinueZhangYi(f.Id);
    }

    private bool DrainZhangYiChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);

    private void ContinueZhangYi(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhangYi: { } r } f && f.Id == id)
        {
            AssertZhangYi(f);
            if (f.AttackAttempt is not null) { CompleteDamageAttack(new ProgramAttackHandle(this, f.Id)); return; }
            if (DrainZhangYiChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.ZhangYi!;
            if (_winner != Winner.None) { FinishZhangYi(f); return; }
            if (r.Stage == ZhangYiStage.DianjunDamage)
            {
                if (!_players[f.OwnerSeat].IsAlive) { FinishZhangYi(f); return; }
                ReplaceRuntimeTop(f = f with { ZhangYi = r with { Stage = ZhangYiStage.DianjunExtraPlay } });
                if (!TryBeginZhangYiDianjunExtraPlay(f)) throw new InvalidOperationException("Dianjun lost its actual end-owned extra Play return.");
                return;
            }
            if (r.Stage == ZhangYiStage.DianjunExtraPlay) return;
            if (r.Stage == ZhangYiStage.DianjunReturned)
            {
                var before = _movementSequence;
                ReplaceRuntimeTop(f = f with { ZhangYi = r with { Stage = ZhangYiStage.DianjunCleanupChildren, CleanupBefore = before, CleanupAfter = before } });
                AdvanceEventRulesAndQueueFact(new ZhangYiDianjunCleanupStartedEvent(f.Id, f.OwnerSeat, r.ActualTurnNumber, before));
                ExpireEndOwnedPlayEffects(f);
                f = GetActiveProgramFrame(id);
                ReplaceRuntimeTop(f with { ZhangYi = f.ZhangYi! with { CleanupAfter = _movementSequence } });
                AdvanceEventRulesAndQueueFact(new ZhangYiDianjunCleanupIssuedEvent(f.Id, f.OwnerSeat, r.ActualTurnNumber, before, _movementSequence));
                continue;
            }
            if (r.Stage == ZhangYiStage.DianjunCleanupChildren) { FinishZhangYi(f); return; }
            if (r.Stage == ZhangYiStage.KangruiDraw)
            { ReplaceRuntimeTop(f with { ZhangYi = r with { Stage = ZhangYiStage.KangruiChoice } }); continue; }
            if (r.Stage == ZhangYiStage.KangruiChoice) { PublishZhangYi(f); return; }
            if (r.Stage == ZhangYiStage.KangruiRecovery)
            {
                AdvanceEventRulesAndQueueFact(new ZhangYiKangruiArmedEvent(f.Id, r.Source, r.RecipientSeat, r.ActualTurnNumber, r.Choice == "damage"));
                FinishZhangYi(GetActiveProgramFrame(id)); return;
            }
            throw new InvalidOperationException("Unknown Zhang Yi continuation stage.");
        }
    }

    // The central typed phase scheduler restores this exact program and its
    // original DeferredTurnEndFrame before calling this return.
    private void ResumeZhangYiDianjunExtraPlay(long programFrameId)
    {
        var f = GetActiveProgramFrame(programFrameId);
        if (f.ZhangYi is not { Stage: ZhangYiStage.DianjunExtraPlay } r || r.ActualTurnNumber != _turnNumber || _phase != TurnPhase.Finished)
            throw new InvalidOperationException("Dianjun received a foreign extra-phase return.");
        ReplaceRuntimeTop(f with { ZhangYi = r with { Stage = ZhangYiStage.DianjunReturned } }); AdvanceRuntimeProgram(programFrameId);
    }
    private void FinishZhangYi(ProgramSkillFrame f)
    {
        var completed = _winner == Winner.None && (f.SkillId == "ol:kangrui" || _players[f.OwnerSeat].IsAlive);
        ReplaceRuntimeTop(f = f with { ZhangYi = null }); FinishProgramSkill(f, completed);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26400)]
    private bool ResumeZhangYi(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangYi: not null } f || f.Id != id) return false; ContinueZhangYi(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26400)]
    private bool ContinuePaidZhangYi(ProgramSkillFrame f) => f.ZhangYi is { } r &&
        ProgramEventHistory<ZhangYiResolutionStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source &&
            e.GameplayHash == r.GameplayHash && e.ParentWindowId == r.ParentWindowId && e.ActualTurnNumber == r.ActualTurnNumber && e.RecipientSeat == r.RecipientSeat && e.DamageFrameId == r.DamageFrameId) == 1;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveKangruiFirstOwnTurnDamage)]
    private PromptChoice AiZhangYi(PendingDecision p, ProgramSkillFrame f) => p.Choices.First(c => c.Parameters["branch"] ==
        (_players[f.ZhangYi!.RecipientSeat].Hp < _players[f.ZhangYi.RecipientSeat].MaxHp ? "recover" : "damage"));

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 100)]
    private int ZhangYiKangruiDamageBonus(IDamageAttempt attack)
    {
        if (!HasZhangYiCapability || attack.IsSourceLess) return 0;
        var grants = ProgramEventHistory<ZhangYiKangruiArmedEvent>().Where(g => g.ActualTurnNumber == _turnNumber && g.RecipientSeat == attack.SourceSeat && g.DamageBonus &&
            !ProgramEventHistory<ZhangYiKangruiDamageConsumedEvent>().Any(e => e.GrantFrameId == g.FrameId)).ToArray();
        foreach (var grant in grants) AdvanceEventRulesAndQueueFact(new ZhangYiKangruiDamageConsumedEvent(grant.FrameId, attack.ResolutionId, attack.SourceSeat,
            attack.TargetSeat, _turnNumber, attack.IsChainPropagation, attack.IsChainPropagation ? ZhangYiChainCursor(attack) : 0));
        return grants.Length;
    }
    private int ZhangYiChainCursor(IDamageAttempt attack) => attack is ProgramAttackHandle
        ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
    private void SetZhangYiChainAmount(IDamageAttempt attack, int amount)
    {
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || !attack.IsChainPropagation || amount < 0)
            throw new InvalidOperationException("Kangrui can only restore its exact native chained damage instance.");
        if (attack is ProgramAttackHandle) UpdateProgramAttackState(attack.ResolutionId, s => s with { DamageAmount = amount });
        else UpdateCardAttackState(attack.ResolutionId, s => s! with { DamageAmount = amount });
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 60)]
    private void PrepareZhangYiChainAdvance(IDamageAttempt attack)
    {
        if (!HasZhangYiCapability || !attack.IsChainPropagation || attack.IsSourceLess) return;
        var cursor = ZhangYiChainCursor(attack);
        var remaining = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats : GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        if (remaining is null || !remaining.Skip(cursor).Any(seat => _players[seat].IsAlive)) return;
        var issued = ProgramEventHistory<ZhangYiChainInstanceIssuedEvent>().SingleOrDefault(e => e.AttackFrameId == attack.ResolutionId && e.ChainCursor == cursor);
        if (issued is null || !ProgramEventHistory<ZhangYiKangruiDamageConsumedEvent>().Any(e => e.AttackFrameId == attack.ResolutionId && e.ChainPropagation && e.ChainCursor == cursor)) return;
        if (ProgramEventHistory<ZhangYiChainInstanceRebasedEvent>().Any(e => e.AttackFrameId == attack.ResolutionId && e.ChainCursor == cursor)) return;
        SetZhangYiChainAmount(attack, issued.InheritedAmount);
        AdvanceEventRulesAndQueueFact(new ZhangYiChainInstanceRebasedEvent(attack.ResolutionId, cursor, attack.TargetSeat, issued.InheritedAmount));
    }
    [ProgramDamageHook(ProgramDamagePhase.AfterChainAdvance, 10)]
    private void ApplyZhangYiChainedDamageBonus(IDamageAttempt attack)
    {
        if (!HasZhangYiCapability || !attack.IsChainPropagation || attack.IsSourceLess ||
            !ProgramEventHistory<ZhangYiKangruiArmedEvent>().Any(g => g.ActualTurnNumber == _turnNumber && g.RecipientSeat == attack.SourceSeat && g.DamageBonus &&
                !ProgramEventHistory<ZhangYiKangruiDamageConsumedEvent>().Any(e => e.GrantFrameId == g.FrameId))) return;
        var cursor = ZhangYiChainCursor(attack);
        if (ProgramEventHistory<ZhangYiChainInstanceIssuedEvent>().Any(e => e.AttackFrameId == attack.ResolutionId && e.ChainCursor == cursor))
            throw new InvalidOperationException("A chained Kangrui instance cannot issue its increment twice.");
        AdvanceEventRulesAndQueueFact(new ZhangYiChainInstanceIssuedEvent(attack.ResolutionId, cursor, attack.TargetSeat, attack.DamageAmount));
        // Formal armor recomputes this recipient in the ordinary finalizer.
        // Legacy already-finalized propagation gets only the newly armed bonus.
        if (attack.DamageAmountFinalized) SetZhangYiChainAmount(attack, checked(attack.DamageAmount + ZhangYiKangruiDamageBonus(attack)));
    }
    private RuleQueryEvaluation ApplyZhangYiKangruiHandLimit(CharacterState player, RuleQueryEvaluation ordinary)
    {
        if (!HasZhangYiCapability || !ProgramEventHistory<ZhangYiKangruiHandLimitSetEvent>().Any(e => e.ActualTurnNumber == _turnNumber && e.RecipientSeat == player.Seat)) return ordinary;
        var set = new FiniteRuleQueryContribution($"turn:{_turnNumber}:zhang-yi:kangrui:{player.Seat}", SkillRuleOperation.Set, 0, 200);
        var frozen = Array.AsReadOnly(ordinary.Value.Contributions.Append(set).OrderBy(c => c.SourceId, StringComparer.Ordinal).ToArray());
        var stage = new RuleQueryStageSnapshot("kangrui-hand-limit-zero", ordinary.Value is FiniteRuleQueryValue finite ? finite.Value : 0, false, 0, Array.AsReadOnly<RuleQueryContribution>([set]));
        return new(new FiniteRuleQueryValue(0, frozen), ordinary.BaseTerms, Array.AsReadOnly(ordinary.Stages.Append(stage).ToArray()));
    }
}
