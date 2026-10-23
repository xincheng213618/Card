namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMobileShenZhangLiao => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot);
    private static CardConversionSource MobileZhangLiaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool MobileDuoruiActive(MobileDuoruiIssuedEvent e) => !ProgramEventHistory<MobileDuoruiExpiredEvent>().Any(x => x.FrameId == e.FrameId);
    private IEnumerable<MobileDuoruiIssuedEvent> MobileDuoruiLeases => ProgramEventHistory<MobileDuoruiIssuedEvent>().Where(MobileDuoruiActive);
    private EquipmentSlot[] MobileDuoruiSlots(int seat) => new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.OffensiveHorse, EquipmentSlot.DefensiveHorse }
        .Where(slot => _players[seat].EquipmentSlotCapacity(slot) > 0).ToArray();
    private EquipmentSlot[] MobileZhitiSlots(int seat) => Enum.GetValues<EquipmentSlot>().Where(slot => _players[seat].EquipmentSlotCapacity(slot) == 0).ToArray();
    private static string MobileEquipmentSlotName(EquipmentSlot slot) => slot switch
    { EquipmentSlot.Weapon => "武器栏", EquipmentSlot.Armor => "防具栏", EquipmentSlot.OffensiveHorse => "进攻坐骑栏", EquipmentSlot.DefensiveHorse => "防御坐骑栏", EquipmentSlot.Treasure => "宝物栏", _ => throw new InvalidOperationException("Unknown equipment slot.") };
    private string[] MobileDuoruiSkills(int seat) => _contentRegistry.Generals[_players[seat].General.Id].SkillIds
        .Concat(_players[seat].SecondaryGeneral is { } second ? _contentRegistry.Generals[second.Id].SkillIds : []).Distinct(StringComparer.Ordinal)
        .Where(id => (_contentRegistry.GetSkill(id).Tags & (SkillTag.Limited | SkillTag.Awakening | SkillTag.Lord)) == 0 &&
            id is not ("mobile-god:powei" or "mou-mobile:jieyin")).Order(StringComparer.Ordinal).ToArray();
    private bool MobileZhitiOpponent(int owner, int opponent) => IsValidPlayerSeat(opponent) && owner != opponent && _players[opponent].IsAlive &&
        IsRuleWounded(_players[opponent]) && IsWithinAttackRange(owner, opponent);
    // Suppression is a qualification query, so another effect ending cannot
    // revive a still-stolen skill or overwrite the target's original grants.
    private bool IsMobileDuoruiGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (!HasMobileShenZhangLiao) return true;
        if (grant.SourceId.StartsWith("mobile-duorui:", StringComparison.Ordinal) && !MobileDuoruiLeases.Any(e => e.Source.OwnerSeat == owner.Seat &&
            e.BorrowGrantId == grant.GrantId && e.BorrowInstanceId == grant.SkillInstanceId && e.SelectedSkillId == grant.SkillId)) return false;
        return !MobileDuoruiLeases.Any(e => e.TargetSeat == owner.Seat && e.SelectedSkillId == grant.SkillId);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 39950)] private void ExpireMobileDuorui(IGameEvent fact)
    {
        if (!HasMobileShenZhangLiao) return;
        var leases = MobileDuoruiLeases.Where(e => fact is TurnEndedEvent turn && turn.ActorSeat == e.TargetSeat && turn.TurnNumber > e.ActualTurn ||
            fact is PlayerDiedEvent death && death.VictimSeat == e.TargetSeat).ToArray();
        foreach (var e in leases)
        {
            var owner = _players[e.Source.OwnerSeat];
            owner.SkillGrants.RemoveGrant(e.BorrowGrantId);
            AdvanceEventRulesAndQueueFact(new MobileDuoruiExpiredEvent(e.FrameId, e.TargetSeat, _turnNumber, fact is PlayerDiedEvent));
            _currentTurnSkillSuppressionRevision++;
        }
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 39950, SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill, SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot)]
    private bool MobileShenZhangLiaoEligible(ProgramTriggerLegalityContext g)
    {
        var op = GetProgramTrigger(g.Candidate).Effects[0].Op;
        if (op == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill)
            return g.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: { } target, Amount: > 0 } && source == g.OwnerSeat && source != target &&
                _players[target].IsAlive && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play, ActorSeat: var actor } && actor == source &&
                !MobileDuoruiLeases.Any(e => e.Source.OwnerSeat == source) && MobileDuoruiSlots(source).Length > 0 && MobileDuoruiSkills(target).Length > 0;
        if (g.WindowContext is not { } context || MobileZhitiSlots(g.OwnerSeat).Length == 0) return false;
        if (context.Window == SkillProgramTriggerWindow.AfterDamageApplied)
            return context is { TargetSeat: var target, SourceSeat: { } source, Amount: > 0 } && target == g.OwnerSeat && MobileZhitiOpponent(g.OwnerSeat, source);
        return MobileZhitiContestCandidateMatches(g.Candidate, context);
    }
    [ProgramCapability] private sealed class MobileShenZhangLiaoCapability(GameEngine engine) : IMobileShenZhangLiaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginMobileShenZhangLiao(f, op); }
    private SkillProgramStepOutcome BeginMobileShenZhangLiao(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.MobileShenZhangLiao is not null) throw new InvalidOperationException("Mobile Shen Zhang Liao requires his exact original unpaid operation.");
        var target = op == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill ? f.WindowContext!.TargetSeat!.Value : f.OwnerSeat;
        ReplaceRuntimeTop(f = f with { MobileShenZhangLiao = new(MobileZhangLiaoSource(f), f.GameplayHash, op, f.WindowContext!.ParentFrameId, target,
            op == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill ? MobileShenZhangLiaoStage.AbolishSlot : MobileShenZhangLiaoStage.RestoreSlot) });
        PublishMobileShenZhangLiao(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MobileShenZhangLiaoChoices(ProgramSkillFrame f)
    {
        if (f.MobileShenZhangLiao is not { } r) return [];
        var values = r.Stage == MobileShenZhangLiaoStage.ChooseSkill ? MobileDuoruiSkills(r.TargetSeat) :
            (r.Stage == MobileShenZhangLiaoStage.AbolishSlot ? MobileDuoruiSlots(f.OwnerSeat) : MobileZhitiSlots(f.OwnerSeat)).Select(s => s.ToString()).ToArray();
        return Array.AsReadOnly(values.Select(value => new PromptChoice(new($"mobile-shen-zhang-liao:{f.Id}:{r.Stage}:{value}"),
            r.Stage == MobileShenZhangLiaoStage.ChooseSkill ? "获得【" + _contentRegistry.GetSkill(value).Name + "】并令其失效" : (r.Stage == MobileShenZhangLiaoStage.AbolishSlot ? "废除" : "恢复") + MobileEquipmentSlotName(Enum.Parse<EquipmentSlot>(value)),
            [], [], new Dictionary<string, string> { ["program-action"] = "mobile-shen-zhang-liao", ["value"] = value })).ToArray());
    }
    private void PublishMobileShenZhangLiao(ProgramSkillFrame f)
    {
        var choices = MobileShenZhangLiaoChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !_players[f.MobileShenZhangLiao!.TargetSeat].IsAlive) { FinishMobileShenZhangLiao(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "【" + skill.Name + "】请选择。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mobile-shen-zhang-liao")] private void AnswerMobileShenZhangLiao(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShenZhangLiao: { } r } f ||
            !MobileShenZhangLiaoChoices(f).Any(c => c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) && c.Parameters.OrderBy(p => p.Key).SequenceEqual(choice.Parameters.OrderBy(p => p.Key))))
            throw new InvalidOperationException("Mobile Shen Zhang Liao rejects a stale slot or skill payment.");
        AssertMobileShenZhangLiao(f); var value = choice.Parameters["value"]; ClearPendingDecision();
        if (r.Stage == MobileShenZhangLiaoStage.ChooseSkill)
        {
            if (MobileDuoruiLeases.Any(e => e.Source.OwnerSeat == f.OwnerSeat) || r.PaidSlot is null) throw new InvalidOperationException("Duorui cannot borrow twice or before its real equipment payment.");
            var owner = _players[f.OwnerSeat]; var source = "mobile-duorui:" + f.Id; var grant = source + ":" + value;
            var instance = GetSkillBindingShard(owner).ActiveGrants.FirstOrDefault(g => g.SkillId == value)?.SkillInstanceId ?? source + ":instance:" + value;
            foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
            owner.SkillGrants.Grant(new(grant, value, instance, source)); RegisterTaggedConversionSkill(owner, value);
            AdvanceEventRulesAndQueueFact(new MobileDuoruiIssuedEvent(f.Id, r.Source, r.GameplayHash, r.TargetSeat, value, grant, instance, _turnNumber, r.PaidSlot.Value));
            _currentTurnSkillSuppressionRevision++;
            AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(f.OwnerSeat, f.SkillId, Array.AsReadOnly(new[] { value })));
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { MobileShenZhangLiao = r with { SelectedSkillId = value, Stage = MobileShenZhangLiaoStage.RestoreChildren } });
        }
        else
        {
            var slot = Enum.Parse<EquipmentSlot>(value); var before = _movementSequence;
            if (r.Stage == MobileShenZhangLiaoStage.AbolishSlot)
            {
                SetEquipmentSlotCapacity(_players[f.OwnerSeat], slot, 0);
                ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { MobileShenZhangLiao = r with { PaidSlot = slot, Before = before, After = _movementSequence, Stage = MobileShenZhangLiaoStage.AbolishChildren } });
                AdvanceEventRulesAndQueueFact(new MobileDuoruiSlotPaidEvent(f.Id, r.Source, slot, before, _movementSequence));
            }
            else
            {
                if (!RestoreEquipmentSlot(_players[f.OwnerSeat], slot)) throw new InvalidOperationException("Zhiti requires a genuinely disabled slot.");
                ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { MobileShenZhangLiao = r with { PaidSlot = slot, Before = before, After = _movementSequence, Stage = MobileShenZhangLiaoStage.RestoreChildren } });
                AdvanceEventRulesAndQueueFact(new MobileZhitiSlotRestoredEvent(f.Id, r.Source, slot));
            }
        }
        ResumeMobileShenZhangLiao(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill, SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot)] private PromptChoice ChooseMobileShenZhangLiao(PendingDecision p, ProgramSkillFrame f) => p.Choices[0];
    private bool DrainMobileShenZhangLiao(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 39950)] private bool ResumeMobileShenZhangLiao(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShenZhangLiao: { } r } f || f.Id != id) return false;
        if (r.Stage is MobileShenZhangLiaoStage.AbolishChildren or MobileShenZhangLiaoStage.RestoreChildren)
        {
            if (DrainMobileShenZhangLiao(id)) return true;
            f = GetActiveProgramFrame(id);
            if (r.Stage == MobileShenZhangLiaoStage.RestoreChildren) { FinishMobileShenZhangLiao(f); return true; }
            ReplaceRuntimeTop(f = f with { MobileShenZhangLiao = r with { Stage = MobileShenZhangLiaoStage.ChooseSkill } });
        }
        PublishMobileShenZhangLiao(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 39950)] private bool ReturnMobileShenZhangLiaoMovement(ProgramSkillFrame f) => f.MobileShenZhangLiao is not null && ResumeMobileShenZhangLiao(f.Id);
    private void FinishMobileShenZhangLiao(ProgramSkillFrame f) { ReplaceRuntimeTop(f with { MobileShenZhangLiao = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 39950)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 39950)] private bool RetainMobileShenZhangLiao(ProgramSkillFrame f) => f.MobileShenZhangLiao is { PaidSlot: not null };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 39950)] private void AssertMobileShenZhangLiao(ProgramSkillFrame f)
    {
        if (f.MobileShenZhangLiao is not { } r) return;
        if (r.Source != MobileZhangLiaoSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || f.WindowContext?.ParentFrameId != r.ParentFrameId ||
            !Enum.IsDefined(r.Stage) || !IsValidPlayerSeat(r.TargetSeat) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Operation == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill && r.PaidSlot is { } slot && ProgramEventHistory<MobileDuoruiSlotPaidEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Slot == slot && e.Before == r.Before && e.After == r.After) != 1 ||
            r.Operation == SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot && r.PaidSlot is { } restored && ProgramEventHistory<MobileZhitiSlotRestoredEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Slot == restored) != 1)
            throw new InvalidOperationException("Mobile Shen Zhang Liao lost his exact original source, stage or once-paid equipment slot.");
        var history = CompleteProgramEventHistory().ToArray();
        var starts = history.OfType<ProgramBindingStartedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var parent = _resolutionStack.SingleOrDefault(p => p.Id == r.ParentFrameId);
        var windowMatches = parent is DamageTriggerWindowFrame d && d.CandidateIndex < d.Candidates.Count &&
            d.Candidates[d.CandidateIndex].ToProgramCandidate() is { } candidate && MountObserverCandidateMatches(f, candidate) &&
            f.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } w && d.TriggerWindow == w.Window &&
            w.SourceSeat == d.SourceSeat && w.TargetSeat == d.TargetSeat &&
            r.TargetSeat == (r.Operation == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill ? d.TargetSeat : f.OwnerSeat) ||
            parent is not null && MobileZhitiProgramRidesOn(f, parent) && r.Operation == SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot && r.TargetSeat == f.OwnerSeat;
        var unpaid = r.Stage is MobileShenZhangLiaoStage.AbolishSlot or MobileShenZhangLiaoStage.RestoreSlot;
        if (starts is not [{ } started] || started.OwnerSeat != f.OwnerSeat || started.SkillId != f.SkillId || started.BindingId != GetProgramBindingId(f) ||
            started.SkillInstanceId != f.SkillInstanceId || started.Window != f.WindowContext?.Window || !windowMatches ||
            unpaid && (r.PaidSlot is not null || r.Before != 0 || r.After != 0 || r.SelectedSkillId is not null) ||
            !unpaid && r.PaidSlot is null ||
            r.Operation == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill && (r.Stage == MobileShenZhangLiaoStage.RestoreSlot || r.PaidSlot == EquipmentSlot.Treasure) ||
            r.Operation == SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot && (r.Stage is not (MobileShenZhangLiaoStage.RestoreSlot or MobileShenZhangLiaoStage.RestoreChildren) || r.SelectedSkillId is not null))
            throw new InvalidOperationException("Mobile Shen Zhang Liao requires the unique started binding and its exact original native trigger candidate.");
        if (r.PaidSlot is { } paid)
        {
            var startIndex = Array.FindIndex(history, e => e is ProgramBindingStartedEvent s && s.FrameId == f.Id);
            var paidIndex = Array.FindIndex(history, e => r.Operation == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill
                ? e is MobileDuoruiSlotPaidEvent p && p.FrameId == f.Id : e is MobileZhitiSlotRestoredEvent restoredEvent && restoredEvent.FrameId == f.Id);
            var capacity = r.Operation == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill ? 0 : 1;
            if (paidIndex <= startIndex || history.Skip(startIndex + 1).Take(paidIndex - startIndex - 1).OfType<EquipmentSlotCapacityChangedEvent>()
                    .Count(e => e.Seat == f.OwnerSeat && e.Slot == paid && e.Capacity == capacity) != 1 ||
                r.Operation == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill && CardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After)
                    .Any(m => m.From != CardLocation.Equipment(f.OwnerSeat) || m.To != CardLocation.DiscardPile || m.Reason.Value != "equipment.slot-abolished"))
                throw new InvalidOperationException("Mobile Shen Zhang Liao's paid slot must retain its real capacity mutation and exact equipment movements.");
        }
        if (r.SelectedSkillId is { } selected && (r.Stage != MobileShenZhangLiaoStage.RestoreChildren || ProgramEventHistory<MobileDuoruiIssuedEvent>()
            .Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.TargetSeat == r.TargetSeat && e.SelectedSkillId == selected && e.PaidSlot == r.PaidSlot) != 1))
            throw new InvalidOperationException("Mobile Duorui's acquired skill must retain its unique paid grant receipt.");
    }
}
