namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenSunCeYingba = "mobile:yingba", ShenSunCeFuhai = "mobile:fuhai", ShenSunCeFenghe = "mobile:fenghe";
    private const string ShenSunCeGiftAction = "shen-sun-ce-gift", ShenSunCeGiftReason = "skill-program.mobile:fenghe.give";
    private static CardConversionSource ShenSunCeSource(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private int Pingding(int seat) => _players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.Pingding);
    [ProgramCapability] private sealed class ShenSunCeCapability(GameEngine engine) : IShenSunCeProgramHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginShenSunCe(f, op); }
    [ProgramCapability] private sealed class ShenSunCeSelectionQueries(GameEngine engine) : IShenSunCeSelectionQueries
    { public bool CanSubdue(int seat) => engine._players[seat].IsAlive && engine._players[seat].HasHpValue && engine._players[seat].MaxHp > 1; }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 38100,
        SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw, SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath, SkillProgramEffectOp.ResolveShenSunCeFenghe)]
    private bool ShenSunCeTriggerLegal(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        var op = t.Effects.Single().Op;
        if (op == SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw)
            return _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is { } use &&
                use.Action.Type == CardActionType.Use && use.Action.ActorSeat == c.OwnerSeat &&
                (use.Action.EffectiveKind == CardKind.BorrowedSword ? use.Action.TargetSeats.Where((_, index) => index % 2 == 0) : use.Action.EffectiveDesignatedTargetSeats).Any(s => Pingding(s) > 0) &&
                ProgramEventHistory<ShenSunCeDrawReturnedEvent>().Where(e => e.Source.OwnerSeat == c.OwnerSeat && e.Operation == op && e.ActualTurn == _turnNumber).Sum(e => e.Actual) < 2;
        if (op == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath)
            return w.TargetSeat is { } victim && Pingding(victim) > 0 &&
                _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is { } kill && kill.VictimSeat == victim &&
                _resolutionStack.OfType<DeathFrame>().SingleOrDefault(p => p.Id == kill.DeathFrameId) is { KillerProgramsResolved: false } death && death.VictimSeat == victim;
        return w is { SourceSeat: { } source, TargetSeat: { } target } && source != target && target == c.OwnerSeat &&
            _players[target].HasHpValue && _players[target].MaxHp > 1 && GetHand(_players[target]).Count > 0 &&
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is { Prevented: false, Amount: > 0 } damage &&
            !BeforeDamageHasNoSource(damage);
    }

    private SkillProgramStepOutcome BeginShenSunCe(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShenSunCe is not null || f.InstructionIndex != 1 || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Shen Sun Ce requires his original unpaid native program.");
        var subject = operation == SkillProgramEffectOp.ResolveShenSunCeYingba ? f.SelectedTargetSeats.Single() :
            operation == SkillProgramEffectOp.ResolveShenSunCeFenghe ? f.WindowContext!.SourceSeat!.Value : f.WindowContext!.TargetSeat ?? f.OwnerSeat;
        var amount = operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath ? Pingding(subject) : 1;
        var deathId = operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath ?
            _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().Single(w => w.Id == f.WindowContext!.ParentFrameId).DeathFrameId : 0;
        var r = new ShenSunCeReceipt(ShenSunCeSource(f), f.GameplayHash, operation, f.InstructionIndex, f.WindowContext?.ParentFrameId ?? 0,
            _turnNumber, subject, amount, operation == SkillProgramEffectOp.ResolveShenSunCeYingba ? ShenSunCeStage.TargetChildren : ShenSunCeStage.MaximumChildren)
            { DeathId = deathId, SequenceBefore = _movementSequence, SequenceAfter = _movementSequence };
        ReplaceRuntimeTop(f = f with { ShenSunCe = r });
        AdvanceEventRulesAndQueueFact(new ShenSunCeStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.ActualTurn, subject, amount, deathId));
        if (operation == SkillProgramEffectOp.ResolveShenSunCeYingba)
        {
            if (subject == f.OwnerSeat || !_players[subject].IsAlive || !_players[subject].HasHpValue || _players[subject].MaxHp <= 1)
                throw new InvalidOperationException("Yingba requires another actual living role with maximum HP above one.");
            ShenSunCeChangeMaximum(f, subject, -1); MutateParticipantMarker(f, subject, PlayerMarkerKind.Pingding, 1);
        }
        else if (operation == SkillProgramEffectOp.ResolveShenSunCeFenghe)
        { PreventProgramCurrentDamage(f); ShenSunCeChangeMaximum(f, f.OwnerSeat, -1); }
        else if (operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath) ShenSunCeChangeMaximum(f, f.OwnerSeat, amount);
        else { IssueShenSunCeDraw(f, 1); return SkillProgramStepOutcome.AwaitChild; }
        ContinueShenSunCe(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void ShenSunCeChangeMaximum(ProgramSkillFrame f, int seat, int delta)
    {
        var p = _players[seat]; var before = p.MaxHp; var hp = p.Hp;
        p.MaxHp = HasWushuangTactic(seat, 2020) ? 7 : Math.Max(0, checked(before + delta)); p.Hp = Math.Min(p.Hp, p.MaxHp);
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(seat, p.MaxHp - before, p.MaxHp, f.SkillId) { HpBeforeClamp = hp });
        AdvanceEventRulesAndQueueFact(new ShenSunCeMaximumPaidEvent(f.Id, seat, before, p.MaxHp, hp, p.Hp));
    }
    private bool DrainShenSunCe(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueShenSunCe(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShenSunCe: { } r } f && f.Id == id)
        {
            RequireShenSunCe(f);
            if (r.Stage == ShenSunCeStage.DrawAttempt) return;
            if (DrainShenSunCe(id)) return;
            f = GetActiveProgramFrame(id); r = f.ShenSunCe!;
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShenSunCe(f); return; }
            if (r.Stage == ShenSunCeStage.TargetChildren)
            {
                ReplaceRuntimeTop(f = f with { ShenSunCe = r with { Stage = ShenSunCeStage.OwnerChildren } });
                ShenSunCeChangeMaximum(f, f.OwnerSeat, -1);
                if (_players[f.OwnerSeat].MaxHp == 0)
                { BeginPlayerDeath(f.Id, _players[f.OwnerSeat], null, null, null, causingProgramSkillFrameId: f.Id); return; }
                continue;
            }
            if (r.Operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath && r.Stage == ShenSunCeStage.MaximumChildren)
            { IssueShenSunCeDraw(f, r.Amount); return; }
            if (r.Operation == SkillProgramEffectOp.ResolveShenSunCeFenghe && r.Stage == ShenSunCeStage.MaximumChildren)
            { ReplaceRuntimeTop(f = f with { ShenSunCe = r with { Stage = ShenSunCeStage.GiftChoice } }); PublishShenSunCeGift(f); return; }
            if (r.Stage == ShenSunCeStage.GiftChoice) { PublishShenSunCeGift(f); return; }
            if (r.Stage == ShenSunCeStage.GiftChildren)
            {
                ReplaceRuntimeTop(f = f with { ShenSunCe = r with { Stage = ShenSunCeStage.MarkerChildren }, PendingMovementContinuation = null });
                if (HasRuntimeSkill(_players[f.OwnerSeat], ShenSunCeYingba) && _players[r.SubjectSeat].IsAlive)
                    MutateParticipantMarker(f, r.SubjectSeat, PlayerMarkerKind.Pingding, 1);
                continue;
            }
            FinishShenSunCe(f); return;
        }
    }
    private void FinishShenSunCe(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { ShenSunCe = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    private void IssueShenSunCeDraw(ProgramSkillFrame f, int count)
    {
        ReplaceRuntimeTop(f = f with { ShenSunCe = f.ShenSunCe! with { Stage = ShenSunCeStage.DrawAttempt, RequestedDraw = count } });
        BeginOperationProgramDraw(f, f.OwnerSeat, count, new($"skill-program.mobile:fuhai.{(f.ShenSunCe.Operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath ? "death" : "target")}-draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath, SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw)]
    private void ReturnShenSunCeDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); RequireShenSunCe(f); var r = f.ShenSunCe!;
        if (r.Stage != ShenSunCeStage.DrawAttempt || r.DrawReturned || f.NativeDraw is not null || outcome.RecipientSeat != f.OwnerSeat ||
            outcome.RequestedCount != r.RequestedDraw || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Fuhai requires its exact typed native draw return.");
        ReplaceRuntimeTop(f = f with { ShenSunCe = r with { Stage = ShenSunCeStage.DrawChildren, DrawReturned = true,
            SequenceBefore = outcome.SequenceBefore, SequenceAfter = outcome.SequenceAfter } });
        AdvanceEventRulesAndQueueFact(new ShenSunCeDrawReturnedEvent(f.Id, r.Source, r.Operation, r.ActualTurn, outcome.RequestedCount,
            outcome.ActualDrawIds.Count, outcome.AttemptFrameId, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueShenSunCe(f.Id);
    }
    private IReadOnlyList<PromptChoice> ShenSunCeGiftChoices(ProgramSkillFrame f) => Array.AsReadOnly(GetHand(_players[f.OwnerSeat])
        .SelectMany(card => _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(target => new PromptChoice(
            new($"shen-sun-ce.{f.Id}.{card.Id}.{target.Seat}"), $"将【{card.DisplayName}】交给 {target.Name}", [card.Id], [target.Seat],
            new Dictionary<string, string> { ["program-action"] = ShenSunCeGiftAction }))).ToArray());
    private void PublishShenSunCeGift(ProgramSkillFrame f)
    {
        var choices = ShenSunCeGiftChoices(f);
        if (choices.Count == 0)
        { ReplaceRuntimeTop(f with { ShenSunCe = f.ShenSunCe! with { Stage = ShenSunCeStage.GiftChildren } }); ContinueShenSunCe(f.Id); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "冯河：将一张手牌交给一名其他角色。", choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true,
            SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(ShenSunCeGiftAction)] private void AnswerShenSunCeGift(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenSunCe: { Stage: ShenSunCeStage.GiftChoice } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShenSunCeGiftChoices(f).Any(c => AssistedChoicesEqual([choice], [c])))
            throw new InvalidOperationException("Fenghe requires its owner's exact current private hand gift and recipient.");
        RequireShenSunCe(f); ClearPendingDecision(); var card = choice.Cards.Single(); var target = choice.Targets.Single(); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, ShenSunCe = r with { Stage = ShenSunCeStage.GiftChildren,
            CardId = card, RecipientSeat = target, SequenceBefore = before, SequenceAfter = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([card], CardLocation.Hand(target), new(ShenSunCeGiftReason), NativeGiveCompletionRecords(f.Id, f.OwnerSeat, target, [card], before, null));
        f = GetActiveProgramFrame(f.Id); var movement = _cardMovements.Single(m => m.Sequence > before && m.CardId == card &&
            m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(target) && m.Reason.Value == ShenSunCeGiftReason);
        ReplaceRuntimeTop(f = f with { ShenSunCe = f.ShenSunCe! with { SequenceAfter = movement.Sequence } });
        AdvanceEventRulesAndQueueFact(new ShenSunCeGiftPaidEvent(f.Id, card, target, movement.Sequence)); ContinueShenSunCe(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 38100)] private bool ResumeShenSunCe(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenSunCe: not null } f || f.Id != id) return false; ContinueShenSunCe(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38100)] private bool ReturnShenSunCeMovement(ProgramSkillFrame f) => f.ShenSunCe is not null && ResumeShenSunCe(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 38100)]
    private bool IsShenSunCePaidMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.ShenSunCe is not { Operation: SkillProgramEffectOp.ResolveShenSunCeFenghe, Stage: ShenSunCeStage.GiftChildren, CardId: > 0 } r ||
            effect?.Op != r.Operation || f.PendingMovementContinuation != pending || pending.SubjectSeat != f.OwnerSeat ||
            pending.BeforeCount != 0 || pending.CoverageResultBind is not null || !IsValidPlayerSeat(r.RecipientSeat) || r.RecipientSeat == f.OwnerSeat ||
            r.SequenceAfter <= r.SequenceBefore) return false;
        RequireShenSunCe(f);
        return ProgramEventHistory<ShenSunCeGiftPaidEvent>().Count(e => e == new ShenSunCeGiftPaidEvent(f.Id, r.CardId, r.RecipientSeat, r.SequenceAfter)) == 1 &&
            _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence == r.SequenceAfter && m.CardId == r.CardId &&
                m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(r.RecipientSeat) && m.Reason.Value == ShenSunCeGiftReason) == 1;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 38100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 38100)] private bool KeepPaidShenSunCe(ProgramSkillFrame f) => f.ShenSunCe is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenSunCeFenghe)] private PromptChoice AiShenSunCeGift(PendingDecision d, ProgramSkillFrame f) =>
        d.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).First();

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 38100)] private void RequireShenSunCe(ProgramSkillFrame f)
    {
        if (f.ShenSunCe is not { } r) return;
        if (r.Source != ShenSunCeSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            r.ParentId != (f.WindowContext?.ParentFrameId ?? 0) || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) || r.Amount < 1 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            ProgramEventHistory<ShenSunCeStartedEvent>().Count(e => e == new ShenSunCeStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.ActualTurn, r.SubjectSeat, r.Amount, r.DeathId)) != 1)
            throw new InvalidOperationException("Shen Sun Ce lost the owning program, source, turn or original payment receipt.");
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (r.ParentId != 0 && (index < 1 || _resolutionStack[index - 1].Id != r.ParentId || f.WindowContext is not { } context ||
            _resolutionStack[index - 1] switch
            {
                ProgramCardTriggerWindowFrame w => w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, ToSharedCandidate(w.Candidates[w.CandidateIndex])),
                ProgramKillTriggerWindowFrame w => w.DeathFrameId != r.DeathId || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]),
                BeforeDamageProgramWindowFrame w => w.SourceSeat != r.SubjectSeat || w.TargetSeat != f.OwnerSeat || !w.Prevented ||
                    w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].Candidate),
                _ => true
            })) throw new InvalidOperationException("Shen Sun Ce lost his exact suspended native parent and candidate.");
        if (r.CardId > 0 && (r.Stage is ShenSunCeStage.GiftChildren or ShenSunCeStage.MarkerChildren) &&
            (!ProgramEventHistory<ShenSunCeGiftPaidEvent>().Any(e => e == new ShenSunCeGiftPaidEvent(f.Id, r.CardId, r.RecipientSeat, r.SequenceAfter)) ||
             !_cardMovements.Any(m => m.Sequence == r.SequenceAfter && m.CardId == r.CardId && m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(r.RecipientSeat) && m.Reason.Value == ShenSunCeGiftReason)))
            throw new InvalidOperationException("Fenghe lost its actual hand-to-recipient physical payment.");
        if (r.DrawReturned)
        {
            var paid = ProgramEventHistory<ShenSunCeDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            var reason = $"skill-program.mobile:fuhai.{(r.Operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath ? "death" : "target")}-draw";
            if (paid is not [var draw] || r.Stage != ShenSunCeStage.DrawChildren || draw.Source != r.Source || draw.Operation != r.Operation ||
                draw.ActualTurn != r.ActualTurn || draw.Requested != r.RequestedDraw || draw.SequenceBefore != r.SequenceBefore || draw.SequenceAfter != r.SequenceAfter ||
                draw.Actual < 0 || _cardMovements.Count(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == reason) != draw.Actual)
                throw new InvalidOperationException("Fuhai lost its typed native request and actual draw movement invoice.");
        }
    }

    private bool ShenSunCeCannotRespond(int source, int responder, CardKind kind, IReadOnlyList<int> targets) =>
        source != responder && (kind == CardKind.BorrowedSword ? targets.Where((_, index) => index % 2 == 0).Contains(responder) : targets.Contains(responder)) && _players[source].IsAlive && _players[responder].IsAlive &&
        HasRuntimeSkill(_players[source], ShenSunCeFuhai) && Pingding(responder) > 0;
    private bool ShenSunCeDistance(CharacterState actor, CharacterState target) => actor.IsAlive && target.IsAlive &&
        HasRuntimeSkill(actor, ShenSunCeYingba) && Pingding(target.Seat) > 0;
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 38100)] private bool ShenSunCeCardDistance(ProgramCardDistanceQuery q) => ShenSunCeDistance(q.Actor, q.Target);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 38100)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 38100)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 38100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 38100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 38100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 38100)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 38100)] private bool ShenSunCeSlashDistance(ProgramSlashDistanceQuery q) => ShenSunCeDistance(q.Actor, q.Target);
}
