using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static readonly CardZoneKind[] DamageAcquisitionZones = [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment];
    private const string DamageAcquisitionReason = "skill-program.damage-zone-acquisition.obtain";
    private CardConversionSource DamageAcquisitionSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 820)]
    private bool CanOfferDamageZoneAndAdjacentTarget(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect]) return true;
        if (effect.Op == SkillProgramEffectOp.JoinAdjacentRedCardTarget)
            return TryGetAdjacentRedTargetUse(candidate.OwnerSeat, context, out var use, out _) && AdjacentRedTargetEligible(use, candidate.OwnerSeat) &&
                !CompleteProgramEventHistory().OfType<AdjacentRedTargetAddedEvent>().Any(e => e.CardUseFrameId == use.Id && e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId);
        if (effect.Op != SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly) return true;
        return _phase == TurnPhase.Play && _currentSeat == candidate.OwnerSeat && context.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
            context.Amount > 0 && context.DamageFrameId is not null && context.SourceSeat == candidate.OwnerSeat &&
            context.TargetSeat is { } victim && victim != candidate.OwnerSeat && IsValidPlayerSeat(victim) &&
            !CompleteProgramEventHistory().OfType<DamageZoneAcquisitionStartedEvent>().Any(e => e.Receipt.Source.OwnerSeat == candidate.OwnerSeat &&
                e.Receipt.Source.SkillId == candidate.SkillId && e.Receipt.ActualTurn == _turnNumber);
    }
    private bool ExactDamageAcquisitionParent(ProgramSkillFrame f)
    {
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied,
            Amount: > 0, SourceSeat: { } source, TargetSeat: { } target, DamageFrameId: { } damageId } c || source != f.OwnerSeat || target == source ||
            GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly }]) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return i > 0 && _resolutionStack[i - 1] is DamageTriggerWindowFrame w && w.Id == c.ParentFrameId && w.ParentFrameId == damageId &&
            w.TriggerWindow == c.Window && w.SourceSeat == source && w.TargetSeat == target && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
            MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) &&
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is { } damage &&
            damage.SourceSeat == source && damage.TargetSeat == target && damage.Amount == c.Amount && DamageAcquisitionHasExactOwningAttack(damage, w);
    }
    private bool DamageAcquisitionHasExactOwningAttack(DamageFrame damage, DamageTriggerWindowFrame window)
    {
        // A paid gain observer may issue its own damage and dying child. The
        // global current attack then belongs to that child; the original damage
        // remains suspended on its own typed frame and must be proved there.
        IDamageAttempt? attack = _resolutionStack.SingleOrDefault(x => x.Id == damage.ParentFrameId) switch
        {
            ProgramSkillFrame { AttackAttempt: not null } => new ProgramAttackHandle(this, damage.ParentFrameId),
            CardUseFrame { CardAttack: not null } or ProgramSkillFrame { CardAttack: not null } or JudgmentFrame { CardAttack: not null } =>
                new CardAttackHandle(this, damage.ParentFrameId),
            _ => null
        };
        return attack is { IsSourceLess: false, DamageWasApplied: true } && attack.SourceSeat == damage.SourceSeat &&
            attack.TargetSeat == damage.TargetSeat && attack.DamageAmount == damage.Amount && window.SourceCardId == attack.Card?.Id &&
            window.SourceCard == attack.EffectiveCardKind;
    }
    private SkillProgramStepOutcome BeginDamageZoneAcquisition(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (!ExactDamageAcquisitionParent(f) || f.DamageZoneAcquisition is not null)
            throw new InvalidOperationException("Damage-zone acquisition requires its exact actual damage candidate once.");
        var r = new DamageZoneAcquisitionReceipt { WindowId = f.WindowContext!.ParentFrameId, DamageId = f.WindowContext.DamageFrameId!.Value,
            Source = DamageAcquisitionSource(f), GameplayHash = f.GameplayHash, ActualTurn = _turnNumber,
            VictimSeat = f.WindowContext.TargetSeat!.Value, Stage = DamageZoneAcquisitionStage.Selecting };
        ReplaceRuntimeTop(f = f with { DamageZoneAcquisition = r });
        AdvanceEventRulesAndQueueFact(new DamageZoneAcquisitionStartedEvent(f.Id, r));
        PublishDamageAcquisitionChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> DamageAcquisitionChoices(ProgramSkillFrame f) => f.DamageZoneAcquisition is { NextZone: < 3 } r && _players[r.VictimSeat].IsAlive
        ? Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.VictimSeat, [DamageAcquisitionZones[r.NextZone]], OwnedCardMoveIntent.Obtain)
            .Select(c => PromptChoiceProjection.Freeze(c with { Parameters = new Dictionary<string, string>(c.Parameters)
                { ["program-action"] = "damage-zone-acquisition" } })).ToArray()) : [];
    private void PublishDamageAcquisitionChoice(ProgramSkillFrame f)
    {
        var r = f.DamageZoneAcquisition!;
        while (r.NextZone < 3)
        {
            var choices = DamageAcquisitionChoices(f);
            if (choices.Count > 0)
            {
                ReplaceRuntimeTop(f = f with { DamageZoneAcquisition = r with { Choices = choices } });
                var skill = _contentRegistry.GetSkill(f.SkillId);
                _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "雉盗：从此区域选择一张牌获得。",
                    choices.SelectMany(c => c.Cards).Distinct().ToArray(), [r.VictimSeat], f.OwnerSeat)
                { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.VictimSeat, Choices = choices,
                    SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
                _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
                AdvanceRulesAndPublishState(); return;
            }
            r = r with { NextZone = r.NextZone + 1 }; ReplaceRuntimeTop(f = f with { DamageZoneAcquisition = r });
        }
        IssueDamageZoneAcquisition(f);
    }
    [ProgramAnswer("damage-zone-acquisition")]
    private void ResolveDamageAcquisitionChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Damage acquisition lost its owning program.");
        RequireDamageAcquisitionReceipt(f); var r = f.DamageZoneAcquisition!;
        if (r.Stage != DamageZoneAcquisitionStage.Selecting || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], r.Choices.Where(c => c.Id == selected.Id).ToArray()) ||
            !AssistedChoicesEqual([selected], DamageAcquisitionChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Damage acquisition must retain its exact published opaque/public slot.");
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(f, "获得前技能来源已失效。"); return; }
        var from = new CardLocation(DamageAcquisitionZones[r.NextZone], r.VictimSeat);
        var cards = _cardZones.CardsAt(from); var slot = int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var card = cards[slot];
        if (from.Zone == CardZoneKind.Hand ? selected.Cards.Count != 0 : !selected.Cards.SequenceEqual([card.Id]))
            throw new InvalidOperationException("Damage acquisition cannot expose foreign hand entities or replace its public card.");
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { DamageZoneAcquisition = r with { Cards = r.Cards.Append(card.Id).ToArray(), From = r.From.Append(from).ToArray(),
            NextZone = r.NextZone + 1, Choices = [] } });
        PublishDamageAcquisitionChoice(f);
    }
    private void IssueDamageZoneAcquisition(ProgramSkillFrame f)
    {
        var r = f.DamageZoneAcquisition!;
        if (r.Stage != DamageZoneAcquisitionStage.Selecting || r.NextZone != 3 || r.Cards.Count != r.From.Count ||
            r.Cards.Select((id, i) => _cardZones.GetLocation(id) != r.From[i]).Any(x => x))
            throw new InvalidOperationException("Damage acquisition lost its selected one-per-zone physical invoice.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { DamageZoneAcquisition = r with { Stage = DamageZoneAcquisitionStage.MovementChildren, Choices = [], Before = before, After = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        foreach (var (id, i) in r.Cards.Select((id, i) => (id, i)))
        {
            var card = GetAttackCard(id); var to = card.IsGeneralWeapon && r.From[i].Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat);
            MoveCard(card, r.From[i], to, new(DamageAcquisitionReason));
        }
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { DamageZoneAcquisition = f.DamageZoneAcquisition! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new DamageZoneAcquisitionPaidEvent(f.Id, f.DamageZoneAcquisition!));
        if (!DrainDamageAcquisitionChildren(f)) ReturnDamageAcquisitionMovement(f);
    }
    private bool DrainDamageAcquisitionChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Movement, 21300)]
    private bool ReturnDamageAcquisitionMovement(ProgramSkillFrame f)
    {
        if (f.DamageZoneAcquisition is not { Stage: DamageZoneAcquisitionStage.MovementChildren } r || f.PendingMovementContinuation is null) return false;
        RequireDamageAcquisitionReceipt(f); if (DrainDamageAcquisitionChildren(f)) return true;
        GrantProgramTurnCardTargetRestriction(f, SkillProgramCardTargetRestriction.SelfOnly, f.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new DamageZoneAcquisitionCompletedEvent(f.Id, r.Source, r.ActualTurn));
        ReplaceRuntimeTop(f = f with { DamageZoneAcquisition = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21300)]
    private bool ResumeDamageAcquisition(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DamageZoneAcquisition: { } r } f || f.Id != id) return false;
        if (r.Stage == DamageZoneAcquisitionStage.Selecting) { if (_pendingDecision is null) PublishDamageAcquisitionChoice(f); return true; }
        return ReturnDamageAcquisitionMovement(f);
    }
    private void RequireDamageAcquisitionReceipt(ProgramSkillFrame f)
    {
        var r = f.DamageZoneAcquisition ?? throw new InvalidOperationException("Damage acquisition lost its receipt.");
        if (!ExactDamageAcquisitionParent(f) || r.WindowId != f.WindowContext!.ParentFrameId || r.DamageId != f.WindowContext.DamageFrameId ||
            r.Source != DamageAcquisitionSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber || r.VictimSeat != f.WindowContext.TargetSeat ||
            r.NextZone is < 0 or > 3 || r.Cards.Count != r.From.Count || r.Cards.Count > r.NextZone || r.Cards.Distinct().Count() != r.Cards.Count ||
            r.From.Select(l => l.Zone).Distinct().Count() != r.From.Count || r.From.Any(l => l.OwnerSeat != r.VictimSeat || !DamageAcquisitionZones.Contains(l.Zone)) ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            CompleteProgramEventHistory().OfType<DamageZoneAcquisitionStartedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Receipt.WindowId == r.WindowId && e.Receipt.DamageId == r.DamageId && e.Receipt.Source == r.Source && e.Receipt.GameplayHash == r.GameplayHash &&
                e.Receipt.ActualTurn == r.ActualTurn && e.Receipt.VictimSeat == r.VictimSeat) != 1)
            throw new InvalidOperationException("Damage acquisition lost its actual candidate, source or one-per-zone invoice.");
        if (r.Stage == DamageZoneAcquisitionStage.Selecting)
        {
            if (f.PendingMovementContinuation is not null || r.Before != 0 || r.After != 0 ||
                r.Cards.Select((id, i) => _cardZones.GetLocation(id) != r.From[i]).Any(x => x))
                throw new InvalidOperationException("Unpaid zone acquisition cannot retain a completed movement.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } prompt &&
                (prompt.Kind != DecisionKind.ProgramTrigger || prompt.PlayerSeat != f.OwnerSeat || !prompt.IsPrivate || prompt.TargetSeat != r.VictimSeat ||
                    !AssistedChoicesEqual(prompt.Choices, r.Choices)))
                throw new InvalidOperationException("Zone acquisition lost its chooser or foreign hand privacy.");
            return;
        }
        if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != f.OwnerSeat || r.NextZone != 3 ||
            CompleteProgramEventHistory().OfType<DamageZoneAcquisitionPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray() is not [var paid] ||
            paid.Receipt.Source != r.Source || paid.Receipt.Before != r.Before || paid.Receipt.After != r.After ||
            !paid.Receipt.Cards.SequenceEqual(r.Cards) || !paid.Receipt.From.SequenceEqual(r.From))
            throw new InvalidOperationException("Zone acquisition lost its once-issued movement parent.");
        foreach (var (id, i) in r.Cards.Select((id, i) => (id, i)))
            if (_cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == id && m.From == r.From[i] &&
                m.To == NormalizeDiouCarrierDestination(GetAdvancedCard(id), m.From, (m.CardKind == CardKind.GeneralWeapon && m.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat))) &&
                m.Reason.Value == DamageAcquisitionReason) != 1)
                throw new InvalidOperationException("Zone acquisition must move each selected physical entity exactly once.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 460)]
    private void AssertRegisteredDamageAcquisitionReceipt(ProgramSkillFrame frame)
    {
        if (frame.DamageZoneAcquisition is not null) RequireDamageAcquisitionReceipt(frame);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 50)]
    private bool IsDamageAcquisitionMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p)
    {
        if (f.DamageZoneAcquisition?.Stage != DamageZoneAcquisitionStage.MovementChildren || e?.Op != SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly ||
            p != f.PendingMovementContinuation) return false;
        RequireDamageAcquisitionReceipt(f); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 380)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 380)]
    private bool CanContinueDamageZoneAcquisition(ProgramSkillFrame f)
    {
        if (f.DamageZoneAcquisition?.Stage != DamageZoneAcquisitionStage.MovementChildren) return false;
        RequireDamageAcquisitionReceipt(f); return true;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ObtainEachDamageTargetZoneThenSelfOnly)]
    private PromptChoice SelectAiDamageAcquisition(PendingDecision p, ProgramSkillFrame f) => p.Choices
        .OrderByDescending(c => c.Cards is [var id] ? GetKeepValue(GetAttackCard(id), _players[f.OwnerSeat]) : 0).First();
    [ProgramCapability]
    private sealed class DamageZoneAndAdjacentTargetCapability(GameEngine engine) : IDamageZoneAndAdjacentTargetHost
    {
        public SkillProgramStepOutcome ObtainEachDamageTargetZoneThenSelfOnly(ProgramSkillFrame f) => engine.BeginDamageZoneAcquisition(f);
        public SkillProgramStepOutcome JoinAdjacentRedCardTarget(ProgramSkillFrame f) => engine.BeginAdjacentRedTarget(f);
    }
}
