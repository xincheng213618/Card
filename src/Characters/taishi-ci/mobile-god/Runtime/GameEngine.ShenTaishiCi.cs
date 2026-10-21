using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenTscPowei = "mobile-god:powei", ShenTscDulie = "mobile-god:dulie", ShenTscShenzhu = "mobile-god:shenzhu";
    private const string ShenTscDiscard = "skill-program.shen-taishi-ci.powei.discard", ShenTscTake = "skill-program.shen-taishi-ci.powei.take";
    private static CardConversionSource ShenTscSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool ShenTscMissionOver(int owner) => ProgramEventHistory<ShenTaishiCiPoweiMissionEvent>().Any(e => e.OwnerSeat == owner);
    private int ShenTscWei(int seat, int owner) => _players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.ShenTaishiCiWei, owner));
    private bool HasShenTaishiCiPoweiRange(int actor, int target) => _players[actor].IsAlive && _players[target].IsAlive &&
        ProgramEventHistory<ShenTaishiCiPoweiRangeEvent>().Any(e => e.ActualTurn == _turnNumber && e.AttackerSeat == actor && e.OwnerSeat == target) &&
        !ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == actor);

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 38200)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 38200)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 38200)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 38200)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 38200)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 38200)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 38200)]
    private bool ShenTscPoweiSlashRange(ProgramSlashDistanceQuery q) => HasShenTaishiCiPoweiRange(q.Actor.Seat, q.Target.Seat);

    [ProgramCapability]
    private sealed class ShenTaishiCiCapability(GameEngine e) : IShenTaishiCiHost
    {
        public SkillProgramStepOutcome Powei(ProgramSkillFrame f) => e.ResolveShenTscPowei(f);
        public void Dulie(ProgramSkillFrame f) => e.ResolveShenTscDulie(f);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 38200)]
    private bool CanOfferShenTaishiCi(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId == ShenTscDulie)
            return gate.Context.CardUse is { } card && card.ActorSeat != gate.OwnerSeat && _players[card.ActorSeat].HasHpValue &&
                gate.Owner.HasHpValue && _players[card.ActorSeat].Hp > gate.Owner.Hp;
        if (gate.SkillId == ShenTscShenzhu)
            return gate.Context.CardUse is { } completed && completed.ActorSeat == gate.OwnerSeat &&
                LifecycleCardUse(completed.ParentCardUseFrameId)?.Action is { PhysicalCards.Count: 1, ConversionChain.Count: 0 };
        if (gate.SkillId != ShenTscPowei) return true;
        if (ShenTscMissionOver(gate.OwnerSeat)) return false;
        return gate.Trigger.Id switch
        {
            "initialize" => true,
            "rotate" => _players.Any(p => p.IsAlive && ShenTscWei(p.Seat, gate.OwnerSeat) > 0),
            "success" => !_players.Any(p => p.IsAlive && ShenTscWei(p.Seat, gate.OwnerSeat) > 0),
            "damage-removes-wei" => gate.Context.TargetSeat is { } target && ShenTscWei(target, gate.OwnerSeat) > 0,
            "marked-turn" => gate.Context.SourceSeat is { } actor && actor != gate.OwnerSeat && ShenTscWei(actor, gate.OwnerSeat) > 0 &&
                (ShenTscDiscardCards(gate.OwnerSeat).Length > 0 || ShenTscMayTake(gate.OwnerSeat, actor)),
            "failure" => true,
            _ => false
        };
    }

    private int[] ShenTscDiscardCards(int owner) => GetHand(_players[owner]).Where(c =>
        !IsForeignEquipmentDiscardPrevented(owner, c, CardLocation.Hand(owner), OwnedCardMoveIntent.Discard) &&
        !IsSelfHandCategoryDiscardForbidden(owner, c, CardLocation.Hand(owner), OwnedCardMoveIntent.Discard)).Select(c => c.Id).ToArray();
    private bool ShenTscMayTake(int owner, int actor) => _players[owner].HasHpValue && _players[actor].HasHpValue &&
        _players[actor].Hp <= _players[owner].Hp && GetHand(_players[actor]).Any(c =>
            !IsForeignEquipmentDiscardPrevented(owner, c, CardLocation.Hand(actor), OwnedCardMoveIntent.Transfer));
    private void ShenTscSetWei(ProgramSkillFrame f, int seat, int count)
    {
        var p = _players[seat]; var key = (PlayerMarkerKind.ShenTaishiCiWei, f.OwnerSeat); var before = p.MarkerSourceCounts.GetValueOrDefault(key);
        if (before == count) return;
        if (count == 0) p.MarkerSourceCounts.Remove(key); else p.MarkerSourceCounts[key] = count;
        var total = p.Markers.GetValueOrDefault(PlayerMarkerKind.ShenTaishiCiWei) + count - before;
        if (total == 0) p.Markers.Remove(PlayerMarkerKind.ShenTaishiCiWei); else p.Markers[PlayerMarkerKind.ShenTaishiCiWei] = total;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, seat, PlayerMarkerKind.ShenTaishiCiWei, count - before, total, f.OwnerSeat, "program.shen-taishi-ci.powei"));
    }
    private SkillProgramStepOutcome ResolveShenTscPowei(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != ShenTscPowei || f.WindowContext is not { } c || f.ShenTaishiCiPowei is not null)
            throw new InvalidOperationException("Powei requires its original owning trigger.");
        if (f.TriggerId == "initialize")
        { foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)) ShenTscSetWei(f, p.Seat, 1); return SkillProgramStepOutcome.Continue; }
        if (f.TriggerId == "rotate")
        {
            // Freeze the entire simultaneous transfer before changing any marker.
            var living = CurrentTableOrder().Where(seat => _players[seat].IsAlive).ToArray();
            var transfers = living.Where(s => ShenTscWei(s, f.OwnerSeat) > 0).Select(s =>
            {
                var next = (Array.IndexOf(living, s) + 1) % living.Length;
                if (living[next] == f.OwnerSeat) next = (next + 1) % living.Length;
                return (From: s, To: living[next]);
            }).ToArray();
            foreach (var seat in living) ShenTscSetWei(f, seat, 0);
            foreach (var transfer in transfers) ShenTscSetWei(f, transfer.To, 1);
            return SkillProgramStepOutcome.Continue;
        }
        if (f.TriggerId == "damage-removes-wei")
        { ShenTscSetWei(f, c.TargetSeat!.Value, 0); return SkillProgramStepOutcome.Continue; }
        if (f.TriggerId is "success" or "failure")
        {
            if (f.InstructionIndex == 1) AdvanceEventRulesAndQueueFact(new ShenTaishiCiPoweiMissionEvent(f.Id, f.OwnerSeat, f.TriggerId == "success"));
            if (f.TriggerId == "failure" && f.InstructionIndex > 1) foreach (var p in _players) ShenTscSetWei(f, p.Seat, 0);
            return SkillProgramStepOutcome.Continue;
        }
        if (f.TriggerId != "marked-turn" || c.SourceSeat is not { } actor || actor == f.OwnerSeat || ShenTscWei(actor, f.OwnerSeat) == 0)
            throw new InvalidOperationException("Powei lost the marked character's actual turn.");
        var r = new ShenTaishiCiPoweiReceipt(ShenTscSource(f), f.GameplayHash, c.ParentFrameId, _turnNumber, actor, f.InstructionIndex, ShenTaishiCiPoweiStage.Choice);
        ReplaceRuntimeTop(f = f with { ShenTaishiCiPowei = r });
        AdvanceEventRulesAndQueueFact(new ShenTaishiCiPoweiStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.ActualTurn, actor, r.InstructionIndex));
        PublishShenTscPowei(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ShenTscChoices(ProgramSkillFrame f)
    {
        var r = f.ShenTaishiCiPowei!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Args(string branch) => new() { ["program-action"] = "shen-taishi-ci-powei", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        foreach (var id in ShenTscDiscardCards(f.OwnerSeat))
            choices.Add(new(new($"shen-taishi-ci.{f.Id}.discard.{id}"), $"弃置{GetAdvancedCard(id).DisplayName}，对{_players[r.TargetSeat].Name}造成1点伤害", [id], [r.TargetSeat], Args("damage")));
        if (ShenTscMayTake(f.OwnerSeat, r.TargetSeat))
            foreach (var old in BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.TargetSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Transfer,
                destination: SkillProgramCardDestination.OwnerHand, destinationSeat: f.OwnerSeat))
                choices.Add(old with { Parameters = new Dictionary<string, string>(old.Parameters) { ["program-action"] = "shen-taishi-ci-powei", ["branch"] = "take" } });
        choices.Add(new(new($"shen-taishi-ci.{f.Id}.cancel"), "取消破围", [], [], Args("cancel")));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishShenTscPowei(ProgramSkillFrame f)
    {
        var choices = ShenTscChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "破围：弃手牌造成伤害，或获得一张手牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [f.ShenTaishiCiPowei!.TargetSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.ShenTaishiCiPowei.TargetSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shen-taishi-ci-powei")]
    private void AnswerShenTscPowei(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenTaishiCiPowei: { Stage: ShenTaishiCiPoweiStage.Choice } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShenTscChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Powei requires its current chooser and private real card or opaque slot.");
        AssertShenTscPowei(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (branch == "cancel") { FinishShenTscPowei(f); return; }
        int id;
        if (branch == "damage") id = choice.Cards.Single();
        else
        {
            if (!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"), out var slot)) throw new InvalidOperationException("Powei lost its opaque hand slot.");
            id = GetHand(_players[r.TargetSeat])[slot].Id;
        }
        var from = _cardZones.GetLocation(id); var to = branch == "damage" ? CardLocation.DiscardPile : CardLocation.Hand(f.OwnerSeat);
        ReplaceRuntimeTop(f = f with { ShenTaishiCiPowei = r with { Stage = ShenTaishiCiPoweiStage.MovementChildren, Branch = branch, CardId = id, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        void Paid(long batch, IReadOnlyList<CardMovementRecord> moves)
        { foreach (var m in moves) AdvanceEventRulesAndQueueFact(new ShenTaishiCiPoweiPaidEvent(f.Id, branch, id, from, to, m.Sequence)); }
        if (branch == "damage") MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new(ShenTscDiscard), Paid);
        else MoveProgramCardsFromMultipleSources([id], to, new(ShenTscTake), Paid);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { ShenTaishiCiPowei = f.ShenTaishiCiPowei! with { After = _movementSequence } });
        ContinueShenTscPowei(f.Id);
    }
    private bool DrainShenTscPowei(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueShenTscPowei(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenTaishiCiPowei: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertShenTscPowei(f);
        if (r.Stage == ShenTaishiCiPoweiStage.Choice) { PublishShenTscPowei(f); return; }
        if (r.Stage == ShenTaishiCiPoweiStage.DamageChildren)
        { if (f.AttackAttempt is not null) return; GrantShenTscRange(f); FinishShenTscPowei(f); return; }
        if (DrainShenTscPowei(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        if (r.Branch == "damage" && _players[r.TargetSeat].IsAlive && _winner == Winner.None)
        { ReplaceRuntimeTop(f = f with { ShenTaishiCiPowei = r with { Stage = ShenTaishiCiPoweiStage.DamageChildren } }); BeginProgramSkillDamage(f, r.TargetSeat, 1); return; }
        GrantShenTscRange(f); FinishShenTscPowei(f);
    }
    private void GrantShenTscRange(ProgramSkillFrame f)
    { var r = f.ShenTaishiCiPowei!; AdvanceEventRulesAndQueueFact(new ShenTaishiCiPoweiRangeEvent(f.Id, f.OwnerSeat, r.TargetSeat, r.ActualTurn)); }
    private void FinishShenTscPowei(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { ShenTaishiCiPowei = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 38200)]
    private bool ResumeShenTscPowei(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenTaishiCiPowei: not null } f || f.Id != id) return false; ContinueShenTscPowei(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38200)]
    private bool ReturnShenTscPowei(ProgramSkillFrame f)
    { if (f.ShenTaishiCiPowei is null) return false; ContinueShenTscPowei(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 38200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 38200)]
    private bool ContinuePaidShenTscPowei(ProgramSkillFrame f) => f.ShenTaishiCiPowei is { Stage: not ShenTaishiCiPoweiStage.Choice } ||
        f.SkillId == ShenTscPowei && ProgramEventHistory<ShenTaishiCiPoweiMissionEvent>().Any(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 38200)]
    private bool ShenTscPoweiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.ResolveShenTaishiCiPowei && f.ShenTaishiCiPowei is { Stage: ShenTaishiCiPoweiStage.MovementChildren } &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 38200)]
    private void AssertShenTscPowei(ProgramSkillFrame f)
    {
        if (f.ShenTaishiCiPowei is not { } r) return;
        if (r.Source != ShenTscSource(f) || r.GameplayHash != f.GameplayHash || r.WindowId != f.WindowContext?.ParentFrameId || r.ActualTurn != _turnNumber || r.InstructionIndex != f.InstructionIndex ||
            f.SkillId != ShenTscPowei || f.TriggerId != "marked-turn" || f.WindowContext.SourceSeat != r.TargetSeat ||
            ProgramEventHistory<ShenTaishiCiPoweiStartedEvent>().Count(e => e == new ShenTaishiCiPoweiStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.ActualTurn, r.TargetSeat, r.InstructionIndex)) != 1)
            throw new InvalidOperationException("Powei lost its source, hash, original marked actual turn or owning instruction.");
        if (r.Stage == ShenTaishiCiPoweiStage.Choice) return;
        var paid = ProgramEventHistory<ShenTaishiCiPoweiPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Branch is not ("damage" or "take") || r.CardId is not { } card || paid is not [var invoice] || invoice.Branch != r.Branch || invoice.CardId != card ||
            invoice.Sequence <= r.Before || invoice.Sequence > r.After || !_cardMovements.Any(m => m.Sequence == invoice.Sequence && m.CardId == card && m.From == invoice.From && m.To == invoice.To &&
                m.Reason.Value == (r.Branch == "damage" ? ShenTscDiscard : ShenTscTake)))
            throw new InvalidOperationException("Powei cannot resume or repay an absent real hand-card movement.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenTaishiCiPowei)]
    private PromptChoice ChooseShenTscPowei(PendingDecision p, ProgramSkillFrame f)
    {
        var enemy = !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[f.ShenTaishiCiPowei!.TargetSeat]);
        return enemy ? p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "take") ?? p.Choices.First() : p.Choices.Last();
    }
    private void ResolveShenTscDulie(ProgramSkillFrame f)
    {
        if (f.SkillId != ShenTscDulie || f.WindowContext?.CardUse is not { } c || LifecycleCardUse(c.ParentCardUseFrameId) is not { } use ||
            use.Action?.ActionId != c.CardActionId || !IsSlashCard(use.CardKind)) throw new InvalidOperationException("Dulie lost its original incoming Slash.");
        if (f.JudgmentOutcomeBindings?.SingleOrDefault(b => b.Name == "dulie-outcome")?.Suit != Suit.Heart) return;
        if (!use.TargetSeats.Contains(f.OwnerSeat)) return;
        var targets = Array.AsReadOnly(use.TargetSeats.Where(s => s != f.OwnerSeat).ToArray());
        var action = CloneDesignatedExtraTargetAction(use.Action, targets);
        var prepared = use.PreparedTargetAttacks is { } old ? Array.AsReadOnly(old.Where(a => a.TargetSeat != f.OwnerSeat).ToArray()) : null;
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, TargetIndex = 0, Action = action, TargetsAdjusted = true,
            SlashTargetsCancelled = targets.Count == 0,
            CardAttack = prepared is { Count: > 0 } ? prepared[0] : use.CardAttack,
            PreparedTargetAttacks = prepared,
            Continuations = use.Continuations with { FangtianHalberd = use.Continuations.FangtianHalberd is { Active: true } multi
                ? multi with { TargetSeats = targets, CurrentAttackOwnerId = use.Id } : use.Continuations.FangtianHalberd } }, action);
        AdvanceEventRulesAndQueueFact(new ShenTaishiCiDulieCancelledEvent(f.Id, use.Id, c.CardActionId, f.OwnerSeat, targets.Count));
        for (var i = 0; i < targets.Count; i++) AdvanceEventRulesAndQueueFact(new ShenTaishiCiDulieRetainedTargetEvent(f.Id, use.Id, i, targets[i]));
    }

    private bool TryCompleteShenTaishiCiCancelledSlash(ProgramCardTriggerWindowFrame window)
    {
        if (GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            LifecycleCardUse(window.ParentFrameId) is not { Action: { } action } use || !IsSlashCard(use.CardKind) ||
            !ProgramEventHistory<ShenTaishiCiDulieCancelledEvent>().Any(e => e.UseId == use.Id && e.ActionId == action.ActionId)) return false;
        if (use.TargetSeats.Count > 0)
        {
            if (use.PreparedTargetAttacks is { Count: > 0 } prepared)
                UpdateCardAttackState(use.Id, _ => prepared[0]);
            return false;
        }
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || window.Action.ActionId != action.ActionId ||
            window.AttackOwnerFrameId != use.Id || ActiveCardAttack is not { } attack || attack.ResolutionId != use.Id)
            throw new InvalidOperationException("Dulie cancellation lost its exact native Slash return.");
        UpdateLifecycleCardUse(use.Id, u => u with { SlashTargetsCancelled = false });
        CompleteAttack(attack);
        return true;
    }
}
