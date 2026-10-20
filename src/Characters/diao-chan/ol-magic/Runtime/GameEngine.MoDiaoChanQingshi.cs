namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetRedirectSource, 10)]
    private bool MoDiaoChanSoleTargetRedirectSource(CardConversionSource source) =>
        HasMoDiaoChan && source.SkillId == MoDiaoChanComposition.Qingshi;

    private bool HasMoDcQingshiCard(int owner, int card) => card > 0 && ProgramEventHistory<MoDiaoChanCardGrantedEvent>().Any(e => e.Source.OwnerSeat == owner && e.CardId == card);
    private bool HasMoDcQingshiCard(int card) => HasMoDiaoChan && card > 0 && ProgramEventHistory<MoDiaoChanCardGrantedEvent>().Any(e => e.CardId == card);
    private bool MoDcRefreshReady(int owner)
    {
        var cohort = ProgramEventHistory<MoDiaoChanCohortStartedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner);
        return cohort is null || ProgramEventHistory<MoDiaoChanCardGrantedEvent>().Where(e => e.CohortId == cohort.CohortId)
            .All(e => _cardZones.GetLocation(e.CardId) != CardLocation.Hand(e.RecipientSeat));
    }
    private static bool IsMoDcSingleDamageKind(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_diao_chan_ol_magic_MoDiaoChanQingshi.IsMoDcSingleDamageKind(kind);
    private CardKind[] MoDcModeDamageKinds() => LeCaoZhiModeNamePool().Where(IsMoDcSingleDamageKind).ToArray();
    private void StartMoDcCohort(ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!; var recipients = _players.Where(p => p.IsAlive).OrderBy(p => (p.Seat - f.OwnerSeat + _playerCount) % _playerCount).Select(p => p.Seat).ToArray();
        var cohort = ProgramEventHistory<MoDiaoChanCohortStartedEvent>().Select(e => e.CohortId).DefaultIfEmpty().Max() + 1;
        ReplaceRuntimeTop(f = f with { MoDiaoChan = r with { Paid = true, Stage = MoDiaoChanStage.Obtaining, CohortId = cohort, Targets = recipients, Cursor = 0 } });
        AdvanceEventRulesAndQueueFact(new MoDiaoChanCohortStartedEvent(cohort, f.Id, r.Source, r.GameplayHash, _turnNumber, recipients.Length));
        ContinueMoDc(f.Id);
    }
    private void ObtainMoDcCohortCard(ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!; var target = r.Targets[r.Cursor]; var allowed = MoDcModeDamageKinds();
        var pool = _players[target].IsAlive ? _cardZones.CardsAt(CardLocation.DrawPile).Where(c => allowed.Contains(c.Kind)).ToArray() : [];
        var randomBefore = _random.State; var index = pool.Length == 0 ? -1 : _random.Next(pool.Length); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MoDiaoChan = r = r with { Stage = MoDiaoChanStage.GainChildren, Before = before, After = before, TargetSeat = target }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MoDiaoChanObtainChoiceEvent(f.Id, target, pool.Length, index, randomBefore, _random.State, index < 0 ? null : pool[index].Id));
        if (index >= 0) MoveCard(pool[index], CardLocation.DrawPile, CardLocation.Hand(target), new("skill-program.ol:mo-qingshi.obtain"), m =>
            AdvanceEventRulesAndQueueFact(new MoDiaoChanCardGrantedEvent(r.CohortId, f.Id, r.Source, r.GameplayHash, target, m.CardId, m.CardKind, m.Sequence)), disclosure: CardMovementDisclosure.FaceDown);
        SealMoDcMovement(f.Id, before);
    }
    private CardUseFrame? MoDcCurrentMarkedUse(int owner, ProgramSkillWindowContext c)
    {
        if (c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || c.CardUse is not { } identity ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            LifecycleCardUse(w.ParentFrameId) is not { Action: { Type: CardActionType.Use } a } use ||
            a.ActionId != identity.CardActionId || use.SourceSeat != a.ActorSeat || a.ActorSeat == owner || use.TargetSeats.Distinct().Count() != 1 ||
            !a.PhysicalCards.Any(m => HasMoDcQingshiCard(owner, m.CardId))) return null;
        return use;
    }
    private int[] MoDcRedirectTargets(CardUseFrame use) => _players.Where(p => p.IsAlive && !use.TargetSeats.Contains(p.Seat) &&
        (!HasTurnCardTargetRestriction(use.SourceSeat, SkillProgramCardTargetRestriction.SelfOnly) || p.Seat == use.SourceSeat) &&
        !IsDirectedCardTargetProhibited(use.SourceSeat, p.Seat, use.CardKind, use.Id) &&
        !HasBeneficiarySuitShield(use.SourceSeat, p.Seat, use.Action!.EffectiveSuit) &&
        !IsCardTargetProhibited(p, use.CardKind, use.Action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(use.Action)) &&
        (IsDelayedCard(use.CardKind) ? !p.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(p, use.CardKind) && !HasCardPolicy(p, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) :
            CanBeDistanceFreeDesignatedTarget(use, p))).Select(p => p.Seat).ToArray();
    private void ReplaceMoDcUniqueTarget(ProgramSkillFrame f, int target)
    {
        var r = f.MoDiaoChan!; var use = LifecycleCardUse(r.UseId);
        if (use?.Action is not { } a || a.ActionId != r.ActionId || a.ActorSeat != r.ActorSeat || a.ProviderSeat != r.ProviderSeat ||
            use.TargetSeats is not [var previous] || previous != r.PreviousTarget || !MoDcRedirectTargets(use).Contains(target))
        { FinishMoDc(f); return; }
        var targets = new[] { target }; var action = CloneDesignatedExtraTargetAction(a, targets); var attack = use.CardAttack;
        if (attack?.TargetSeat == previous) attack = attack with { TargetSeat = target, IgnoresArmor = HasCardArmorBypass(_players[use.SourceSeat], _players[target], use.CardKind) };
        var prepared = use.PreparedTargetAttacks is null ? null : Array.AsReadOnly(use.PreparedTargetAttacks.Select(x => x.TargetSeat != previous ? x :
            x with { TargetSeat = target, IgnoresArmor = HasCardArmorBypass(_players[use.SourceSeat], _players[target], use.CardKind) }).ToArray());
        AdvanceEventRulesAndQueueFact(new MoDiaoChanRetargetedEvent(f.Id, r.Source, r.GameplayHash, use.Id, a.ActionId, a.ActorSeat, a.ProviderSeat, previous, target));
        UpdateProgramRoleCardUse(use with { TargetSeats = Array.AsReadOnly(targets), Action = action, CardAttack = attack, PreparedTargetAttacks = prepared, TargetsAdjusted = true }, action);
        // This is the native sole-target mutation fact, with a distinct exact Qingshi producer proof.
        AdvanceEventRulesAndQueueFact(new SoleTargetReplacedEvent(use.Id, a.ActionId, r.Source, previous, target, Array.AsReadOnly(new[] { previous }), Array.AsReadOnly(targets)));
        if (IsSlashCard(use.CardKind))
        {
            var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == r.WindowId);
            ReplaceRuntimeFrame(window.Id, window with { SoleTargetRedirectPending = new(use.Id, a.ActionId, r.Source, target, 0) });
        }
        else { RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!, targets); RefreshSoleProtectionObservers(LifecycleCardUse(use.Id)!); }
        FinishMoDc(GetActiveProgramFrame(f.Id));
    }
    [ProgramTargetWindow(ProgramTargetWindowStage.SoleTargetReplacementFact, 10)]
    private bool IsMoDcSoleTargetFact(CardUseFrame use, SoleTargetReplacedEvent fact, IReadOnlyList<int> before) => HasMoDiaoChan &&
        fact.Source.SkillId == MoDiaoChanComposition.Qingshi && fact.Source.BindingId == "cohort-unique-target" && before is [var old] &&
        fact.BeforeTargets.SequenceEqual(before) && fact.AfterTargets is [var target] && old == fact.PreviousTargetSeat && target == fact.NewTargetSeat && target != old &&
        fact.CardUseFrameId == use.Id && fact.ActionId == use.Action?.ActionId &&
        ProgramEventHistory<MoDiaoChanRetargetedEvent>().Count(e => e.Source == fact.Source && e.UseId == use.Id && e.ActionId == fact.ActionId &&
            e.PreviousTarget == old && e.NewTarget == target && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash &&
            ProgramEventHistory<MoDiaoChanCostEvent>().Any(p => p.FrameId == e.FrameId && p.From.OwnerSeat == e.Source.OwnerSeat &&
                _cardMovements.Any(m => m.Sequence == p.Sequence && m.CardId == p.CardId && m.From == p.From && m.To == p.To))) == 1;
    private bool MoDcDamageEligible(int owner, ProgramSkillWindowContext c) => c.Window == SkillProgramTriggerWindow.AfterDamageApplied && c.Amount > 0 && CurrentDamageAttempt is { } attack &&
        attack.PhysicalCards.Any(card => HasMoDcQingshiCard(owner, card.Id)) &&
        !ProgramEventHistory<MoDiaoChanStartedEvent>().Any(e => e.Source.OwnerSeat == owner && e.Op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage && e.WindowId == c.ParentFrameId);

    private bool MoDcUsePaymentMovement(CardMovementRecord movement)
    {
        foreach (var p in ProgramEventHistory<MoDiaoChanUseMaterialEvent>().Where(p => p.CardId == movement.CardId))
        {
            var paid = _cardMovements.FirstOrDefault(m => m.CardId == p.CardId && m.Sequence > p.Before && m.From == p.From);
            if (paid is null) continue;
            if (paid.Sequence == movement.Sequence) return true;
            if (paid.To == CardLocation.Processing && movement.From == CardLocation.Processing && movement.To == CardLocation.DiscardPile &&
                _cardMovements.FirstOrDefault(m => m.CardId == p.CardId && m.Sequence > paid.Sequence && m.From == CardLocation.Processing)?.Sequence == movement.Sequence) return true;
        }
        return false;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 31401)]
    private void CaptureMoDcMaterialPayments(IGameEvent fact)
    {
        if (!HasMoDiaoChan) return;
        var use = fact is CardUseDeclaredEvent d ? LifecycleCardUse(d.ResolutionId) : null;
        var action = use?.Action ?? (fact is CardActionAcceptedEvent accepted ? accepted.Action : null);
        if (action is null || !IsMoLuBuTrueUse(action)) return;
        foreach (var cost in action.PhysicalCards)
        {
            if (ProgramEventHistory<MoDiaoChanUseMaterialEvent>().Any(e => e.ActionId == action.ActionId && e.CardId == cost.CardId)) continue;
            var before = _movementSequence;
            if (action.Type == CardActionType.Response || use?.AcceptedDeclarationPayment?.Cost.CardId == cost.CardId)
            {
                var paid = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId && m.From == cost.From && m.TurnNumber == _turnNumber);
                if (paid is not null) before = paid.Sequence - 1;
            }
            AdvanceEventRulesAndQueueFact(new MoDiaoChanUseMaterialEvent(use?.Id ?? action.ParentActionId ?? 0, action.ActionId, cost.CardId, cost.From, before));
        }
    }
    private int[] MoDcRecaptureIndexes(CardMovementBatchContext batch, int owner) => batch.Movements.Select((m, i) => (m, i)).Where(x =>
        x.m.To == CardLocation.DiscardPile && HasMoDcQingshiCard(owner, x.m.CardId) && _cardZones.GetLocation(x.m.CardId) == CardLocation.DiscardPile &&
        !MoDcUsePaymentMovement(x.m) && !ProgramEventHistory<MoDiaoChanDiscardClaimedEvent>().Any(e => e.Source.OwnerSeat == owner && e.CardId == x.m.CardId && e.Sequence == x.m.Sequence))
        .Select(x => x.i).ToArray();
    private CardMovementRecord[] MoDcRecaptureCards(int owner, ProgramSkillWindowContext c) => c.Window == SkillProgramTriggerWindow.DiscardPileReceived &&
        c.MovementBatch is { } batch && c.MovementIndex is { } index && MoDcRecaptureIndexes(batch, owner).Contains(index) ? [batch.Movements[index]] : [];
    private void RecaptureMoDcCards(ProgramSkillFrame f)
    {
        var r = f.MoDiaoChan!; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MoDiaoChan = r = r with { Paid = true, Stage = MoDiaoChanStage.GainChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (_players[f.OwnerSeat].IsAlive)
        foreach (var m in MoDcRecaptureCards(f.OwnerSeat, f.WindowContext!))
        {
            AdvanceEventRulesAndQueueFact(new MoDiaoChanDiscardClaimedEvent(f.Id, r.Source, m.CardId, m.Sequence));
            MoveCard(GetAdvancedCard(m.CardId), CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new("skill-program.ol:mo-qingshi.recapture"));
        }
        SealMoDcMovement(f.Id, before);
    }
    private bool IsMoDcQingshiRumoOrigin(OlMoRumoEnteredEvent e) => e.Origin.SkillId == MoDiaoChanComposition.Qingshi && e.Origin.BindingId == "prepare-enter-or-refresh" &&
        ProgramEventHistory<MoDiaoChanQingshiEnteredEvent>().Count(p => p.FrameId == e.EntryId && p.Source == e.Origin && p.GameplayHash == e.OriginGameplayHash && p.ActualTurn == e.ActualTurn && p.Round == e.EntryRound) == 1;
    private bool IsPaidMoDcQingshiOrigin(long id, CardConversionSource source, string hash) => source.SkillId == MoDiaoChanComposition.Qingshi && source.BindingId == "prepare-enter-or-refresh" &&
        _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash == hash && ProgramEventHistory<MoDiaoChanQingshiEnteredEvent>().Count(e => e.FrameId == id && e.Source == source && e.GameplayHash == hash) == 1;
    private bool IsPaidMoDcQingshiEntry(ProgramSkillFrame f) => f.MoDiaoChan is { Op: SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep, Paid: true } r && ValidMoDc(f) &&
        ProgramEventHistory<MoDiaoChanQingshiEnteredEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == f.GameplayHash) == 1;
}
