namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MobileLbWuqian = "mobile-god:wuqian", MobileLbWushuang = "classic:wushuang", MobileLbGainReason = "skill.mobile-god.wuqian.damage-card";
    private bool HasMobileWuqianCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMobileShenLuBuWuqian);
    private IEnumerable<MobileLuBuWuqianIssuedEvent> ActiveMobileWuqian(int actor) => !HasMobileWuqianCapability ? [] :
        ProgramEventHistory<MobileLuBuWuqianIssuedEvent>().Where(e => e.Source.OwnerSeat == actor &&
            !ProgramEventHistory<MobileLuBuWuqianEndedEvent>().Any(end => end.FrameId == e.FrameId));
    private bool HasMobileWuqianArmorDisabled(int targetSeat) => HasMobileWuqianCapability && _players.Where(p => p.IsAlive).Any(p => ActiveMobileWuqian(p.Seat).Any(e => e.TargetSeat == targetSeat));
    private IEnumerable<RuleQueryContribution> MobileWuqianRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        if (query != SkillRuleQuery.SlashLimit || !player.IsAlive || !HasProgramSkill(player, MobileLbWuqian)) yield break;
        var count = ActiveMobileWuqian(player.Seat).Select(e => e.TargetSeat).Distinct().Count(seat => _players[seat].IsAlive);
        if (count > 0) yield return new FiniteRuleQueryContribution($"mobile-wuqian:{player.Seat}:victims", SkillRuleOperation.Add, count);
    }
    private bool MobileLbHasDamageHand(CharacterState owner) => GetHand(owner).Any(c => IsDamageCardKind(AdvancedEffectiveHandKind(owner, c)));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40325)]
    private bool AllowsMobileLbDamageGain(ProgramTriggerLegalityContext g) => g.SkillId != MobileLbWuqian ||
        g.Trigger.Effects.All(e => e.Op != SkillProgramEffectOp.GainMobileShenLuBuDamageCard) || !MobileLbHasDamageHand(g.Owner);
    [ProgramCapability] private sealed class MobileShenLuBuHost(GameEngine engine) : IMobileShenLuBuHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginMobileLb(f, op); }
    private SkillProgramStepOutcome BeginMobileLb(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MobileLuBu is not null || f.SkillId != MobileLbWuqian || f.InstructionIndex != 1) throw new InvalidOperationException("Mobile Wuqian requires its original paid program instruction.");
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var target = op == SkillProgramEffectOp.ResolveMobileShenLuBuWuqian ? f.SelectedTargetSeats.Single() : f.OwnerSeat;
        ReplaceRuntimeTop(f = f with { MobileLuBu = new(source, f.GameplayHash, f.InstructionIndex, op, target, _movementSequence, _movementSequence) });
        if (op == SkillProgramEffectOp.ResolveMobileShenLuBuWuqian)
        {
            var owner = _players[f.OwnerSeat];
            var grantSource = $"mobile-wuqian:{owner.Seat}:{f.Id}"; var grantId = grantSource + ":" + MobileLbWushuang;
            var instance = GetSkillBindingShard(owner).ActiveGrants.FirstOrDefault(g => g.SkillId == MobileLbWushuang)?.SkillInstanceId ?? $"mobile-wuqian-shared:{owner.Seat}";
            foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
            AdvanceEventRulesAndQueueFact(new MobileLuBuWuqianIssuedEvent(f.Id, source, target, grantId, instance, grantSource));
            if (!owner.SkillGrants.Grant(new SkillGrant(grantId, MobileLbWushuang, instance, grantSource))) throw new InvalidOperationException("One Wuqian issue cannot grant twice.");
            RegisterTaggedConversionSkill(owner, MobileLbWushuang);
            AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(owner.Seat, f.SkillId, Array.AsReadOnly(new[] { MobileLbWushuang })));
        }
        else
        {
            if (f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || f.OwnerSeat != _currentSeat) throw new InvalidOperationException("Wuqian's gain requires its actual ending stage.");
            if (!MobileLbHasDamageHand(_players[f.OwnerSeat]))
            {
                var candidates = _cardZones.CardsAt(CardLocation.DrawPile).Concat(_cardZones.CardsAt(CardLocation.DiscardPile)).Where(c => IsDamageCardKind(c.Kind)).ToArray();
                if (candidates.Length > 0)
                {
                    var chosen = candidates[_random.Next(candidates.Length)];
                    ReplaceRuntimeTop(f = f with { MobileLuBu = f.MobileLuBu! with { Cards = [chosen.Id] } });
                    MoveProgramCardsFromMultipleSources([chosen.Id], CardLocation.Hand(f.OwnerSeat), new(MobileLbGainReason), (_, _) =>
                    { var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { MobileLuBu = current.MobileLuBu! with { After = _movementSequence } }); });
                    if (AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat) == SkillProgramStepOutcome.AwaitChild) return SkillProgramStepOutcome.AwaitChild;
                }
            }
        }
        ContinueMobileLb(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinueMobileLb(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileLuBu: not null } f || f.Id != id) return;
        if (TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MobileLuBu = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40325)] private bool ResumeMobileLb(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileLuBu: not null } f || f.Id != id) return false; ContinueMobileLb(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40325)] private bool ReturnMobileLbMovement(ProgramSkillFrame f) => f.MobileLuBu is not null && ResumeMobileLb(f.Id);
    private bool ValidMobileLbGain(ProgramSkillFrame f) => f.MobileLuBu is { Op: SkillProgramEffectOp.GainMobileShenLuBuDamageCard, Cards: [var id] } r &&
        _cardMovements.Count(m => m.CardId == id && m.Sequence > r.Before && m.Sequence <= r.After && m.From.Zone is CardZoneKind.DrawPile or CardZoneKind.DiscardPile &&
            m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == MobileLbGainReason) == 1;
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 40325)]
    private bool ProveMobileLbGain(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.GainMobileShenLuBuDamageCard && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidMobileLbGain(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40325)] private void AssertMobileLb(ProgramSkillFrame f)
    {
        if (f.MobileLuBu is not { } r) return;
        if (r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            f.InstructionIndex != 1 || !IsValidPlayerSeat(r.TargetSeat) || r.After < r.Before || r.Cards.Count > 1 || r.Cards.Count > 0 && !ValidMobileLbGain(f))
            throw new InvalidOperationException("Mobile Wuqian lost its exact source, original paid instruction or once-moved damage-card gain.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40325)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40325)]
    private bool RetainMobileLb(ProgramSkillFrame f) => f.MobileLuBu is not null;
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 40325)] private void ObserveMobileWuqianUses(IGameEvent payload)
    {
        if (!HasMobileWuqianCapability) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use && IsDamageCardKind(use.CardKind))
        {
            var issued = ActiveMobileWuqian(use.SourceSeat).Select(e => e.FrameId).ToArray();
            if (issued.Length == 0) return;
            var r = new MobileLuBuUseReceipt(use.Id, use.SourceSeat, use.Action?.ActionId, use.CardKind) { IssuedFrames = issued };
            UpdateLifecycleCardUse(use.Id, current => current with { MobileLuBuUse = r });
            AdvanceEventRulesAndQueueFact(new MobileLuBuUseCapturedEvent(use.Id, r.ActorSeat, r.ActionId, r.Kind));
        }
        else if (payload is CardUseFinishedEvent finished && LifecycleCardUse(finished.ResolutionId) is { MobileLuBuUse: { } r, CausedDamage: false } completed)
        {
            ValidateMobileLbUse(completed, r);
            foreach (var issue in ProgramEventHistory<MobileLuBuWuqianIssuedEvent>().Where(e => r.IssuedFrames.Contains(e.FrameId) &&
                !ProgramEventHistory<MobileLuBuWuqianEndedEvent>().Any(end => end.FrameId == e.FrameId)).ToArray())
            {
                var owner = _players[issue.Source.OwnerSeat];
                foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
                owner.SkillGrants.RemoveGrant(issue.GrantId);
                AdvanceEventRulesAndQueueFact(new MobileLuBuWushuangRevokedEvent(owner.Seat, issue.GrantId, MobileLbWushuang, issue.SkillInstanceId, issue.SourceId, completed.Id));
                AdvanceEventRulesAndQueueFact(new MobileLuBuWuqianEndedEvent(issue.FrameId, completed.Id));
            }
        }
    }
    private void ValidateMobileLbUse(CardUseFrame use, MobileLuBuUseReceipt r)
    {
        if (r.UseFrameId != use.Id || r.ActionId != use.Action?.ActionId || !IsDamageCardKind(r.Kind) || r.IssuedFrames.Count == 0 || r.IssuedFrames.Distinct().Count() != r.IssuedFrames.Count ||
            ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.SourceSeat == r.ActorSeat && e.CardKind == r.Kind) != 1 ||
            ProgramEventHistory<MobileLuBuUseCapturedEvent>().Count(e => e.UseFrameId == use.Id && e.ActorSeat == r.ActorSeat && e.ActionId == r.ActionId && e.Kind == r.Kind) != 1 ||
            r.IssuedFrames.Any(id => ProgramEventHistory<MobileLuBuWuqianIssuedEvent>().Count(e => e.FrameId == id && e.Source.OwnerSeat == r.ActorSeat) != 1))
            throw new InvalidOperationException("Mobile Wuqian retirement lost the actual declared damage-card Use and its frozen original issues.");
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 40325)] private void AssertMobileLbUses()
    { foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.MobileLuBuUse is not null)) ValidateMobileLbUse(use, use.MobileLuBuUse!); }
}
