namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasShenCaoPiCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ApplyShenCaoPiYinzhi);
    private static CardConversionSource ShenCaoPiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int YinzhiOwnTurns(int seat) => ProgramEventHistory<TurnStartedEvent>().Count(e => e.ActorSeat == seat);
    private bool YinzhiAwaitingOwnTurn(YinzhiIssuedEvent e) => YinzhiOwnTurns(e.TargetSeat) == e.TargetOwnTurns &&
        !(_resolutionStack.FirstOrDefault() is BeiMiHuTurnPreludeFrame prelude && prelude.Proof.UpcomingOwnerSeat == e.TargetSeat && prelude.Proof.UpcomingTurnNumber > e.ActualTurn);
    private bool IsYinzhiSkillSuppressed(int seat) => HasShenCaoPiCapability && ProgramEventHistory<YinzhiIssuedEvent>().Any(e => e.TargetSeat == seat && YinzhiAwaitingOwnTurn(e));
    private bool IsYinzhiTurnRestricted(int actor) => HasShenCaoPiCapability && actor == _currentSeat &&
        ProgramEventHistory<YinzhiIssuedEvent>().Any(e => e.TargetSeat == actor && YinzhiOwnTurns(actor) == e.TargetOwnTurns + 1 &&
            !ProgramEventHistory<YinzhiTurnRestrictionReleasedEvent>().Any(r => r.IssueFrameId == e.FrameId));
    private bool YinzhiNewCardsForbidden(int actor, IEnumerable<int> ids)
    {
        if (!IsYinzhiTurnRestricted(actor)) return false;
        var materials = ids.Where(id => id > 0).ToArray();
        return materials.Length == 0 || materials.Any(id => !_cardMovements.Any(m => m.CardId == id && m.TurnNumber == _turnNumber && m.To == CardLocation.Hand(actor) &&
            ActualHandGainOriginalSource(m) is { } original && original != CardLocation.Hand(actor)));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 39700)] private void ObserveShenCaoPiRules(IGameEvent payload)
    {
        if (!HasShenCaoPiCapability) return;
        if (payload is TurnStartedEvent) _currentTurnSkillSuppressionRevision++;
        if (payload is DamageAppliedEvent { SourceLess: false, Amount: > 0 } damage)
        foreach (var issue in ProgramEventHistory<YinzhiIssuedEvent>().Where(e => e.TargetSeat == damage.SourceSeat && e.Source.OwnerSeat == damage.TargetSeat &&
                     damage.SourceSeat == _currentSeat && YinzhiOwnTurns(e.TargetSeat) == e.TargetOwnTurns + 1 &&
                     !ProgramEventHistory<YinzhiTurnRestrictionReleasedEvent>().Any(r => r.IssueFrameId == e.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new YinzhiTurnRestrictionReleasedEvent(issue.FrameId, _resolutionStack.OfType<DamageFrame>().Last(d => d.SourceSeat == damage.SourceSeat && d.TargetSeat == damage.TargetSeat).Id));
        ObserveKemingProducedFacts(payload);
    }
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 39700)] private void ValidateYinzhiNewUse(ProgramSubmittedUseQuery q)
    { if (YinzhiNewCardsForbidden(q.ActorSeat, q.CardIds)) throw new InvalidOperationException("Yinzhi permits only cards genuinely gained during this actual turn."); }
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 39700)] private bool YinzhiMaterialBan(ProgramCardMaterialQuery q) => YinzhiNewCardsForbidden(q.ActorSeat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 39700)] private bool YinzhiResponseBan(ProgramNativeResponseQuery q) => YinzhiNewCardsForbidden(q.Provider.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 39700)] private bool YinzhiActionBan(ProgramLegalActionQuery q) => q.EffectiveKind is not null &&
        YinzhiNewCardsForbidden(q.Actor.Seat, q.Action.CardId is > 0 and var id ? [id] : []);
    [ProgramCapability] private sealed class ShenCaoPiCapability(GameEngine engine) : IShenCaoPiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.BeginShenCaoPi(frame, op); }
    private CardActionContext? YinzhiAction(ProgramSkillWindowContext? c) => c is null ? null : _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId)?.Action;
    private YinzhiIssuedEvent? YinzhiRewardIssue(int owner, CardActionContext? a) => a is null || !a.PhysicalCards.Any(c => c.From == CardLocation.Hand(a.ActorSeat)) ? null :
        ProgramEventHistory<YinzhiIssuedEvent>().Where(e => e.Source.OwnerSeat == owner && e.TargetSeat == a.ActorSeat && YinzhiAwaitingOwnTurn(e) &&
            !ProgramEventHistory<YinzhiHandRewardRequestedEvent>().Any(r => r.IssueFrameId == e.FrameId && r.ActionId == a.ActionId)).OrderBy(e => e.FrameId).FirstOrDefault();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39700, SkillProgramEffectOp.DrawYinzhiHandAction, SkillProgramEffectOp.ResolveKemingDamagedSkill, SkillProgramEffectOp.ArmKemingSkillEcho)]
    private bool CanTriggerShenCaoPi(ProgramTriggerLegalityContext g) => GetProgramTrigger(g.Candidate).Effects[0].Op switch
    {
        SkillProgramEffectOp.DrawYinzhiHandAction => YinzhiRewardIssue(g.OwnerSeat, YinzhiAction(g.WindowContext)) is not null,
        SkillProgramEffectOp.ResolveKemingDamagedSkill => g.WindowContext is { SourceSeat: { } source, TargetSeat: { } target, Amount: > 0 } c && source != target && target == g.OwnerSeat &&
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } window && !GetDamageTriggerAttack(window).IsSourceLess && KemingBorrowOptions(g.OwnerSeat, source).Count > 0,
        SkillProgramEffectOp.ArmKemingSkillEcho => g.WindowContext is { SourceSeat: { } s, TargetSeat: { } t, Amount: > 0 } && s == g.OwnerSeat && s != t,
        _ => false
    };
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 39700)] private void CollectYinzhiIssuedReward(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasShenCaoPiCapability || window is not (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted)) return;
        var binding = window == SkillProgramTriggerWindow.CardUseCompleted ? "hand-use-reward" : "hand-response-reward";
        foreach (var owner in _players.Where(p => p.IsAlive))
        {
            var issue = YinzhiRewardIssue(owner.Seat, action); if (issue is null) continue;
            if (!result.Any(c => c.OwnerSeat == owner.Seat && c.SkillId == ShenCaoPiComposition.Yinzhi && c.TriggerId == binding && c.SkillInstanceId == issue.Source.SkillInstanceId))
                result.Add(new(owner.Seat, action.ActorSeat, ShenCaoPiComposition.Yinzhi, binding, issue.GameplayHash, issue.Source.SkillInstanceId));
        }
    }
    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 39700)] private bool IsIssuedShenCaoPiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (c.SkillId != ShenCaoPiComposition.Yinzhi || c.BindingId is not ("hand-use-reward" or "hand-response-reward") || !_players[c.OwnerSeat].IsAlive ||
            w.Window is not (SkillProgramTriggerWindow.CardUseCompleted or SkillProgramTriggerWindow.CardResponseCompleted) || YinzhiRewardIssue(c.OwnerSeat, YinzhiAction(w)) is not { } issue) return false;
        return issue.Source.SkillInstanceId == c.SkillInstanceId && issue.GameplayHash == c.GameplayHash && _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash == c.GameplayHash;
    }
    private SkillProgramStepOutcome BeginShenCaoPi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenCaoPi is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Shen Cao Pi requires his exact original unpaid instruction.");
        var target = op == SkillProgramEffectOp.ApplyShenCaoPiYinzhi ? f.SelectedTargetSeats.Single() : f.WindowContext?.TargetSeat ?? f.OwnerSeat;
        var r = new ShenCaoPiReceipt(ShenCaoPiSource(f), f.GameplayHash, op, f.InstructionIndex, f.WindowContext?.ParentFrameId ?? 0, target, ShenCaoPiStage.Complete);
        if (op == SkillProgramEffectOp.ApplyShenCaoPiYinzhi)
        {
            var count = Math.Min(3, ProgramEventHistory<YinzhiActivatedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.TargetSeat == target) + 1);
            r = r with { Count = count, Stage = ShenCaoPiStage.Drawing, IssuedId = f.Id };
            ReplaceRuntimeTop(f = f with { ShenCaoPi = r });
            AdvanceEventRulesAndQueueFact(new YinzhiActivatedEvent(f.Id, r.Source, r.GameplayHash, target, count, _turnNumber, YinzhiOwnTurns(target)));
            BeginOperationProgramDraw(f, f.OwnerSeat, count, new("skill-program.anniversary:yinzhi.initial-draw")); return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.DrawYinzhiHandAction)
        {
            var action = YinzhiAction(f.WindowContext) ?? throw new InvalidOperationException("Yinzhi lost its real completed hand action.");
            var issue = YinzhiRewardIssue(f.OwnerSeat, action) ?? throw new InvalidOperationException("Yinzhi lost its issued pre-turn hand reward.");
            r = r with { Count = issue.Count, IssuedId = issue.FrameId, ActionId = action.ActionId, Stage = ShenCaoPiStage.Drawing };
            ReplaceRuntimeTop(f = f with { ShenCaoPi = r });
            AdvanceEventRulesAndQueueFact(new YinzhiHandRewardRequestedEvent(f.Id, issue.FrameId, action.ActionId, issue.Count));
            BeginOperationProgramDraw(f, f.OwnerSeat, issue.Count, new("skill-program.anniversary:yinzhi.hand-reward")); return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.ResolveKemingDamagedSkill)
        {
            var options = KemingBorrowOptions(f.OwnerSeat, f.WindowContext!.SourceSeat!.Value);
            var fallback = KemingSourceBorrowOptions(f.WindowContext.SourceSeat.Value).Count == 0;
            var before = _random.State; var forced = fallback && options.Count > 0 ? options[_random.Next(options.Count)] : null;
            ReplaceRuntimeTop(f = f with { ShenCaoPi = r with { Options = options, ForcedBorrow = forced, BorrowRandomBefore = before, BorrowRandomAfter = _random.State, Stage = ShenCaoPiStage.BorrowChoice } });
            PublishKemingBorrow(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        ReplaceRuntimeTop(f = f with { ShenCaoPi = r });
        AdvanceEventRulesAndQueueFact(new KemingEchoArmedEvent(f.Id, r.Source, r.GameplayHash, target, ProgramEventHistory<SkillInvocationStartedEvent>().Select(e => e.FrameId).DefaultIfEmpty().Max()));
        FinishShenCaoPi(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainShenCaoPi(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 39700)] private bool ResumeShenCaoPi(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenCaoPi: { } r } f || f.Id != id) return false;
        if (r.Stage == ShenCaoPiStage.Drawing) throw new InvalidOperationException("Shen Cao Pi must await his exact native draw return.");
        if (DrainShenCaoPi(id)) return true;
        if (r.Operation == SkillProgramEffectOp.ApplyShenCaoPiYinzhi && r.Stage == ShenCaoPiStage.Children)
        {
            var activation = ProgramEventHistory<YinzhiActivatedEvent>().Single(e => e.FrameId == id);
            if (ProgramEventHistory<YinzhiIssuedEvent>().Any(e => e.FrameId == id)) throw new InvalidOperationException("Yinzhi cannot issue its post-draw suppression twice.");
            AdvanceEventRulesAndQueueFact(new YinzhiIssuedEvent(id, activation.Source, activation.GameplayHash, activation.TargetSeat, activation.Count, activation.ActualTurn, activation.TargetOwnTurns));
            _currentTurnSkillSuppressionRevision++;
        }
        f = GetActiveProgramFrame(id); if (r.Stage == ShenCaoPiStage.BorrowChoice && _players[f.OwnerSeat].IsAlive && _winner == Winner.None) PublishKemingBorrow(f); else FinishShenCaoPi(f);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39700)] private bool ReturnShenCaoPiMovement(ProgramSkillFrame f) => f.ShenCaoPi is not null && ResumeShenCaoPi(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ApplyShenCaoPiYinzhi, SkillProgramEffectOp.DrawYinzhiHandAction)] private void ReturnShenCaoPiDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        var r = f.ShenCaoPi ?? throw new InvalidOperationException("Shen Cao Pi's draw lost its original paid receipt.");
        if (r.Stage != ShenCaoPiStage.Drawing || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Count || o.SequenceAfter != _movementSequence || ProgramEventHistory<ShenCaoPiDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Shen Cao Pi cannot repeat or alter his native draw.");
        ReplaceRuntimeTop(f with { ShenCaoPi = r with { Stage = ShenCaoPiStage.Children, DrawOutcome = o } });
        AdvanceEventRulesAndQueueFact(new ShenCaoPiDrawReturnedEvent(f.Id, f.OwnerSeat, r.Count, o.ActualDrawIds.Count, o.SequenceBefore, o.SequenceAfter)); ResumeShenCaoPi(f.Id);
    }
    private void FinishShenCaoPi(ProgramSkillFrame f) { ReplaceRuntimeTop(f with { ShenCaoPi = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39700)] private bool RetainShenCaoPi(ProgramSkillFrame f) => f.ShenCaoPi is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39700)] private void AssertShenCaoPi(ProgramSkillFrame f)
    {
        if (f.ShenCaoPi is not { } r) return;
        if (r.Source != ShenCaoPiSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.InstructionIndex != 1 ||
            r.ParentId != (f.WindowContext?.ParentFrameId ?? 0) || !Enum.IsDefined(r.Stage) || !IsValidPlayerSeat(r.TargetSeat) || r.Count is < 0 or > 3 ||
            r.Options is not System.Collections.IList { IsReadOnly: true }) throw new InvalidOperationException("Shen Cao Pi's original-frame receipt changed its owner, binding or frozen request.");
        if (r.Operation == SkillProgramEffectOp.ResolveKemingDamagedSkill)
        {
            if (r.Stage != ShenCaoPiStage.BorrowChoice || r.Count != 0 || r.ActionId != 0 || r.IssuedId != 0 || r.DrawOutcome is not null ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: { } target, DamageFrameId: { } damageId, Amount: > 0 } ||
                target != f.OwnerSeat || source == target || r.TargetSeat != target ||
                _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.ParentId) is not { } window || window.ParentFrameId != damageId ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
                !r.Options.SequenceEqual(KemingBorrowOptions(f.OwnerSeat, source)) || r.Options.Count == 0 ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.BindingId == GetProgramBindingId(f) && e.OwnerSeat == f.OwnerSeat && e.SkillInstanceId == f.SkillInstanceId && e.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1)
                throw new InvalidOperationException("Keming's borrowed choices must retain the actual source general pool on its original positive damage window.");
            var fallback = KemingSourceBorrowOptions(source).Count == 0;
            var random = new DeterministicRandom(unchecked((int)r.BorrowRandomBefore));
            if (fallback ? r.ForcedBorrow != r.Options[random.Next(r.Options.Count)] || random.State != r.BorrowRandomAfter :
                r.ForcedBorrow is not null || r.BorrowRandomBefore != r.BorrowRandomAfter)
                throw new InvalidOperationException("Keming's private fallback result must retain its exact single original random sample.");
            return;
        }
        if (r.Operation is not (SkillProgramEffectOp.ApplyShenCaoPiYinzhi or SkillProgramEffectOp.DrawYinzhiHandAction) || r.Count < 1 ||
            r.Stage is not (ShenCaoPiStage.Drawing or ShenCaoPiStage.Children) || r.Options.Count != 0 || r.ForcedBorrow is not null)
            throw new InvalidOperationException("Shen Cao Pi retained an unrecognized operation receipt.");
        if (r.Operation == SkillProgramEffectOp.ApplyShenCaoPiYinzhi)
        {
            if (r.IssuedId != f.Id || r.ActionId != 0 || !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) ||
                ProgramEventHistory<YinzhiActivatedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.TargetSeat == r.TargetSeat && e.Count == r.Count) != 1 ||
                r.Count != Math.Min(3, ProgramEventHistory<YinzhiActivatedEvent>().Count(e => e.FrameId < f.Id && e.Source.OwnerSeat == f.OwnerSeat && e.TargetSeat == r.TargetSeat) + 1))
                throw new InvalidOperationException("Yinzhi's initial draw must retain its unique accepted target and actual game activation ordinal.");
        }
        else
        {
            var issue = ProgramEventHistory<YinzhiIssuedEvent>().SingleOrDefault(e => e.FrameId == r.IssuedId);
            if (issue is null || issue.Source.SkillId != r.Source.SkillId || issue.Source.OwnerSeat != r.Source.OwnerSeat || issue.Source.SkillInstanceId != r.Source.SkillInstanceId ||
                issue.Source.BindingId != "edict" || issue.GameplayHash != r.GameplayHash || issue.Count != r.Count ||
                YinzhiAction(f.WindowContext) is not { } action || action.ActionId != r.ActionId || action.ActorSeat != issue.TargetSeat ||
                !action.PhysicalCards.Any(c => c.From == CardLocation.Hand(action.ActorSeat)) ||
                ProgramEventHistory<YinzhiHandRewardRequestedEvent>().Count(e => e.FrameId == f.Id && e.IssueFrameId == issue.FrameId && e.ActionId == action.ActionId && e.Count == r.Count) != 1)
                throw new InvalidOperationException("Yinzhi's paid hand reward must retain its unique issued source and actual completed hand action.");
        }
        var reason = r.Operation == SkillProgramEffectOp.ApplyShenCaoPiYinzhi ? "skill-program.anniversary:yinzhi.initial-draw" : "skill-program.anniversary:yinzhi.hand-reward";
        if (r.Stage == ShenCaoPiStage.Drawing)
        {
            if (r.DrawOutcome is not null || f.NativeDraw is not { } draw || draw.Operation != r.Operation || draw.ReturnKind != NativeDrawAttemptReturnKind.ProgramOperation ||
                draw.RequestedCount != r.Count || draw.Reason.Value != reason || !draw.Recipients.SequenceEqual([f.OwnerSeat]) || ProgramEventHistory<ShenCaoPiDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("Yinzhi lost its exact unpaid native draw producer.");
        }
        else if (f.NativeDraw is not null || r.DrawOutcome is not { } o || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Count ||
            ProgramEventHistory<ShenCaoPiDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Requested == r.Count && e.Actual == o.ActualDrawIds.Count && e.Before == o.SequenceBefore && e.After == o.SequenceAfter) != 1 ||
            !_cardMovements.Where(m => m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == reason)
                .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(o.ActualDrawIds))
            throw new InvalidOperationException("Yinzhi's returned draw changed its real paid entity movement range.");
    }
}
