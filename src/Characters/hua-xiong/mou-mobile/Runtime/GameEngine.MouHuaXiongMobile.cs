using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouYaowu = MouHuaXiongComposition.Yaowu, MouYangwei = MouHuaXiongComposition.Yangwei;
    private static CardConversionSource MouHuaXiongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool MouYangweiCooling(int owner) => ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Any(e => e.OwnerSeat == owner &&
        !ProgramEventHistory<MouHuaXiongCooldownRecoveredEvent>().Any(returned => returned.FrameId == e.FrameId));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 29100, SkillProgramEffectOp.ResolveMouHuaXiongYangwei)]
    private bool CanActivateMouYangwei(ProgramActivationLegalityContext c) => c.SkillId == MouYangwei && MouHuaXiongComposition.IsYangwei(c.Program) &&
        _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat && _turnNumber > 0 && c.Owner.IsAlive && !MouYangweiCooling(c.OwnerSeat);
    private bool MouYangweiRoot(ProgramSkillFrame f) => f.SkillId == MouYangwei && f.ActivationId == "draw-and-wei" && f.TriggerId is null &&
        f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
        _contentRegistry.GetSkill(f.SkillId).Program is { } p && p.GameplayHash == f.GameplayHash && MouHuaXiongComposition.IsYangwei(p);
    private SkillProgramStepOutcome ResolveMouYangwei(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouYangweiRoot(f)) throw new InvalidOperationException("Yangwei needs its exact complete zero-card Play activation.");
        if (f.InstructionIndex == 1)
        {
            if (f.MouHuaXiongYangwei is not null || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat || _turnNumber < 1 ||
                !_players[f.OwnerSeat].IsAlive || MouYangweiCooling(f.OwnerSeat) ||
                ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("Yangwei cannot repeat an issued phase activation or bypass its owner-turn cooldown.");
            var receipt = new MouHuaXiongYangweiReceipt(MouHuaXiongSource(f), f.GameplayHash, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId);
            ReplaceRuntimeTop(f with { MouHuaXiongYangwei = receipt });
            AdvanceEventRulesAndQueueFact(new MouHuaXiongYangweiReservedEvent(f.Id, f.OwnerSeat, receipt.ActualTurn, receipt.PlayPhaseId, receipt.Source, receipt.GameplayHash));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.InstructionIndex != 3 || f.MouHuaXiongYangwei is not { WeiResolved: false } r || !ValidMouYangweiReceipt(f) || f.NativeDraw is not null ||
            ProgramEventHistory<MouHuaXiongWeiGrantedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Yangwei's Wei grant must follow its once-paid Draw and all native children.");
        var expired = !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || _currentSeat != r.ActualOwner ||
            _turnNumber != r.ActualTurn || _cardUseDebitPhaseInstanceId != r.PlayPhaseId;
        ReplaceRuntimeTop(f with { MouHuaXiongYangwei = r with { WeiResolved = true } });
        AdvanceEventRulesAndQueueFact(new MouHuaXiongWeiGrantedEvent(f.Id, f.OwnerSeat, r.ActualTurn, r.PlayPhaseId, expired));
        return SkillProgramStepOutcome.Continue;
    }
    private bool ValidMouYangweiReceipt(ProgramSkillFrame f) => MouYangweiRoot(f) && f.MouHuaXiongYangwei is { } r &&
        r.Source == MouHuaXiongSource(f) && r.GameplayHash == f.GameplayHash && r.ActualOwner == f.OwnerSeat && r.ActualTurn > 0 && r.PlayPhaseId > 0 &&
        ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Count(e => e == new MouHuaXiongYangweiReservedEvent(f.Id, f.OwnerSeat,
            r.ActualTurn, r.PlayPhaseId, r.Source, r.GameplayHash)) == 1;
    // A paid public mark survives loss of the granting source. It is not an
    // attack-range increase and never substitutes for native target prohibitions.
    private bool HasMouHuaXiongWei(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive && _phase == TurnPhase.Play &&
        _currentSeat == owner && ProgramEventHistory<MouHuaXiongWeiGrantedEvent>().Any(e => e.OwnerSeat == owner && !e.ExpiredBeforeGrant &&
            e.ActualTurn == _turnNumber && e.PlayPhaseId == _cardUseDebitPhaseInstanceId &&
            ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Any(r => r.FrameId == e.FrameId && r.OwnerSeat == owner &&
                r.ActualTurn == e.ActualTurn && r.PlayPhaseId == e.PlayPhaseId));
    private IReadOnlyList<PlayerMarkerSnapshot>? AppendMouHuaXiongWei(int owner, IReadOnlyList<PlayerMarkerSnapshot>? ordinary) => HasMouHuaXiongWei(owner)
        ? Array.AsReadOnly((ordinary ?? []).Where(m => m.Kind != PlayerMarkerKind.MouHuaXiongWei)
            .Append(new PlayerMarkerSnapshot(PlayerMarkerKind.MouHuaXiongWei, "威", 1)).OrderBy(m => m.Kind).ToArray()) : ordinary;
    // Invoked at the actual native Ending entry, including an owner's extra
    // turn. A face-down whole-turn skip creates no Ending and does not recover.
    private void RecoverMouYangweiAtEnding(int owner)
    {
        if (_turnNumber < 1 || _currentSeat != owner || _turnProgression.OwnerSeat != owner || _turnProgression.TurnNumber != _turnNumber)
            throw new InvalidOperationException("Yangwei cooldown recovery requires the actual turn's native Ending entry.");
        foreach (var issued in ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Where(e => e.OwnerSeat == owner && e.ActualTurn < _turnNumber &&
            !ProgramEventHistory<MouHuaXiongCooldownRecoveredEvent>().Any(returned => returned.FrameId == e.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new MouHuaXiongCooldownRecoveredEvent(issued.FrameId, owner, issued.ActualTurn, _turnNumber));
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29101, SkillProgramEffectOp.ResolveMouHuaXiongYaowu)]
    private bool CanOfferMouYaowu(ProgramTriggerLegalityContext c) => c.Candidate.SkillId == MouYaowu && MouHuaXiongComposition.IsYaowu(c.Program) &&
        c.Context.Window == SkillProgramTriggerWindow.BeforeDamageApplied && c.Context.TargetSeat == c.OwnerSeat && c.Context.Amount > 0 &&
        c.Owner.IsAlive && CurrentDamageAttempt is { EffectiveCardKind: { } kind } && IsSlashCard(kind);
    private bool MouYaowuRoot(ProgramSkillFrame f)
    {
        if (f.SkillId != MouYaowu || f.TriggerId != "slash-damage-benefit" || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context || context.TargetSeat != f.OwnerSeat ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouHuaXiongComposition.IsYaowu(p) ||
            _resolutionStack.SingleOrDefault(item => item.Id == context.ParentFrameId) is not BeforeDamageProgramWindowFrame window ||
            window.TargetSeat != f.OwnerSeat || window.Amount <= 0 || window.Prevented || window.RedirectedTargetSeat is not null ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].Candidate)) return false;
        var i = _resolutionStack.FindIndex(item => item.Id == f.Id);
        return i > 0 && _resolutionStack[i - 1].Id == window.Id;
    }
    private SkillProgramStepOutcome ResolveMouYaowu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouYaowuRoot(f) || f.InstructionIndex != 1 || f.MouHuaXiongYaowu is not null || CurrentDamageAttempt is not { EffectiveCardKind: { } kind } attack ||
            !IsSlashCard(kind) || ProgramEventHistory<MouHuaXiongYaowuStartedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Yaowu needs its exact positive native Slash damage-recipient window.");
        var action = LifecycleCardUse(attack.ResolutionId)?.Action;
        var red = action is not null ? action.EffectiveIsRed ?? (action.EffectiveSuit is Suit.Heart or Suit.Diamond) : attack.Card?.Suit is Suit.Heart or Suit.Diamond;
        var source = attack.IsSourceLess ? (int?)null : f.WindowContext!.SourceSeat;
        var r = new MouHuaXiongYaowuReceipt(MouHuaXiongSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId,
            attack.ResolutionId, f.OwnerSeat, source, kind, red);
        ReplaceRuntimeTop(f = f with { MouHuaXiongYaowu = r });
        AdvanceEventRulesAndQueueFact(new MouHuaXiongYaowuStartedEvent(f.Id, r.WindowId, r.AttackId, f.OwnerSeat, source, kind, red));
        if (!red) { CommitMouYaowuChoice(f, "owner-draw", f.OwnerSeat); return SkillProgramStepOutcome.Continue; }
        if (source is not { } seat || !IsValidPlayerSeat(seat) || !_players[seat].IsAlive)
        { CommitMouYaowuChoice(f, "none", null); return SkillProgramStepOutcome.Continue; }
        var choices = MouYaowuChoices(f);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, "耀武：回复1点体力或摸一张牌。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouYaowuChoices(ProgramSkillFrame f)
    {
        var source = f.MouHuaXiongYaowu!.SourceSeat!.Value;
        var options = IsRuleWounded(_players[source]) ? new[] { "recover", "draw" } : ["draw"];
        return Array.AsReadOnly(options.Select(option => new PromptChoice(new($"mou-hua-xiong.yaowu.{f.Id}.{option}"),
            option == "recover" ? "回复1点体力" : "摸一张牌", [], [], new Dictionary<string,string>
            { ["program-action"] = "mou-hua-xiong-yaowu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option-id"] = option })).ToArray());
    }
    private void CommitMouYaowuChoice(ProgramSkillFrame f, string benefit, int? recipient)
    {
        var r = f.MouHuaXiongYaowu!;
        if (r.Benefit is not null || f.ChoiceBindings.Any(b => b.Name == "yaowu-benefit")) throw new InvalidOperationException("Yaowu cannot choose or issue its reward twice.");
        ReplaceRuntimeTop(f with { MouHuaXiongYaowu = r with { Benefit = benefit }, ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings
            .Append(new ProgramChoiceResultBinding("yaowu-benefit", benefit, recipient ?? f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MouHuaXiongYaowuBenefitChosenEvent(f.Id, recipient, benefit));
    }
    [ProgramAnswer("mou-hua-xiong-yaowu")]
    private void AnswerMouYaowu(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!MouYaowuRoot(f) || f.InstructionIndex != 1 || f.MouHuaXiongYaowu is not { IsRed: true, Benefit: null, SourceSeat: { } source } ||
            _pendingDecision is not { IsPrivate: true, Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != source ||
            !AssistedChoicesEqual([selected], MouYaowuChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Yaowu only accepts its actual damage source's canonical available reward.");
        ClearPendingDecision();
        if (!_players[source].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CommitMouYaowuChoice(f, "none", null); AdvanceRuntimeProgram(f.Id); return; }
        CommitMouYaowuChoice(f, selected.Parameters.GetValueOrDefault("option-id") == "recover" ? "source-recover" : "source-draw", source);
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuaXiongYaowu)]
    private PromptChoice SelectAiMouYaowu(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c =>
        c.Parameters.GetValueOrDefault("option-id") == "recover" && _players[f.MouHuaXiongYaowu!.SourceSeat!.Value].Hp <= 2) ??
        p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "draw");
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29100)]
    private bool KeepPaidMouHuaXiong(ProgramSkillFrame f)
    {
        if (f.MouHuaXiongYangwei is not null) return ValidMouYangweiReceipt(f);
        return f.MouHuaXiongYaowu is { Benefit: not null } && ValidMouYaowuReceipt(f);
    }
    private bool ValidMouYaowuReceipt(ProgramSkillFrame f) => MouYaowuRoot(f) && f.MouHuaXiongYaowu is { } r && r.Source == MouHuaXiongSource(f) &&
        r.GameplayHash == f.GameplayHash && r.OwnerSeat == f.OwnerSeat && r.WindowId == f.WindowContext!.ParentFrameId && IsSlashCard(r.EffectiveKind) &&
        ProgramEventHistory<MouHuaXiongYaowuStartedEvent>().Count(e => e == new MouHuaXiongYaowuStartedEvent(f.Id, r.WindowId, r.AttackId,
            r.OwnerSeat, r.SourceSeat, r.EffectiveKind, r.IsRed)) == 1 && (r.Benefit is null ||
            ProgramEventHistory<MouHuaXiongYaowuBenefitChosenEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var chosen] &&
            chosen.Benefit == r.Benefit && (r.Benefit switch
            { "owner-draw" => !r.IsRed && chosen.RecipientSeat == f.OwnerSeat,
              "source-draw" or "source-recover" => r.IsRed && r.SourceSeat is { } seat && chosen.RecipientSeat == seat,
              "none" => r.IsRed && chosen.RecipientSeat is null, _ => false }) &&
            f.ChoiceBindings.Count(b => b.Name == "yaowu-benefit" && b.OptionId == r.Benefit && b.ChooserSeat == (chosen.RecipientSeat ?? f.OwnerSeat)) == 1);
    private bool IsMouHuaXiongChoiceResult(ProgramSkillFrame f, ProgramChoiceResultBinding binding)
    {
        if (binding.Name != "yaowu-benefit" || f.InstructionIndex < 1 ||
            f.MouHuaXiongYaowu is not { Benefit: not null } r || binding.OptionId != r.Benefit ||
            !ValidMouYaowuReceipt(f)) return false;
        return ProgramEventHistory<MouHuaXiongYaowuBenefitChosenEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var chosen] &&
            chosen.Benefit == binding.OptionId && binding.ChooserSeat == (chosen.RecipientSeat ?? f.OwnerSeat);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29100)]
    private void AssertMouHuaXiong(ProgramSkillFrame f)
    {
        if (f.MouHuaXiongYangwei is not null && !ValidMouYangweiReceipt(f) || f.MouHuaXiongYaowu is not null && !ValidMouYaowuReceipt(f))
            throw new InvalidOperationException("Mobile Hua Xiong lost its exact issued owner, source, damage window or paid phase receipt.");
        if (f.MouHuaXiongYaowu is { Benefit: null, SourceSeat: { } source } && _resolutionStack.LastOrDefault()?.Id == f.Id &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != source ||
                !AssistedChoicesEqual(prompt.Choices, MouYaowuChoices(f))))
            throw new InvalidOperationException("Yaowu's unanswered benefit must retain its real source's private choice.");
    }
    private bool MouHuaXiongPaidRoot(ProgramSkillFrame f) => f.MouHuaXiongYangwei is not null ? ValidMouYangweiReceipt(f) :
        f.MouHuaXiongYaowu is { Benefit: not null } && ValidMouYaowuReceipt(f);
    private bool MouHuaXiongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouHuaXiongPaidRoot(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f) || RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id;
        if (child is not CardsMovedTriggerWindowFrame moved || moved.Batch.ParentFrameId != f.Id ||
            moved.ResumeProgramFrameId != f.Id && moved.Batch.AwaitingProgramFrameId != f.Id || moved.Batch.Movements.Count == 0) return false;
        var recipient = f.MouHuaXiongYangwei is not null ? f.OwnerSeat : f.MouHuaXiongYaowu!.Benefit == "source-draw" ? f.MouHuaXiongYaowu.SourceSeat : f.OwnerSeat;
        return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.From == CardLocation.DrawPile &&
            m.To == CardLocation.Hand(recipient!.Value) && m.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.Draw}");
    }
    private bool MouHuaXiongObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) =>
        g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) || g.IsYanPuDyingExitEdge(child) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame f && g.MouHuaXiongFirstChild(f, g._resolutionStack[child]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? MouHuaXiongObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouHuaXiongFirstChild(f, child),
        static (g, index) => g.MouHuaXiongObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1270)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1380)]
    private bool HasMouHuaXiongDamageObserver(long id) => MouHuaXiongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1410)]
    private bool HasMouHuaXiongBeforeDamageObserver(long id) => MouHuaXiongObserverRoot() is { } root &&
        (root.MouHuaXiongYaowu?.WindowId == id ||
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1240)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 800)]
    private bool IsMouHuaXiongDying() => ActiveDying is { } dying && MouHuaXiongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 60)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1830)]
    private bool AllowsMouHuaXiongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouHuaXiongObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 570)]
    private bool IsMouHuaXiongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!MouHuaXiongPaidRoot(f) || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouHuaXiongFirstChild(f, _resolutionStack[parent + 1]) &&
            MouHuaXiongObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29100)]
    private bool AdvanceMouHuaXiongChildren() => _pendingDecision is null && MouHuaXiongObserverRoot() is not null && TryAdvanceObserverSubtree(true);

    [ProgramCapability]
    private sealed class MouHuaXiongCapability(GameEngine engine) : IMouHuaXiongHost
    {
        public SkillProgramStepOutcome Yangwei(ProgramSkillFrame f) => engine.ResolveMouYangwei(f);
        public SkillProgramStepOutcome Yaowu(ProgramSkillFrame f) => engine.ResolveMouYaowu(f);
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 20)]
    private bool DistanceCardUseExemption20HasMouHuaXiongWei(ProgramCardDistanceQuery q) =>
        IsSlashCard(q.Kind) && HasMouHuaXiongWei(q.Actor.Seat);

}
