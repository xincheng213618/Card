namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShiScNigu = "mobile:shi-nigu", ShiScLulian = "mobile:shi-lulian";
    private static CardMoveReason ShiScDiscardReason => new("skill.mobile-shi-sun-chen.nigu.discard");
    private static CardMoveReason ShiScGiveReason => new("skill.mobile-shi-sun-chen.nigu.give");
    private const string ShiScDrawReason = "skill.mobile-shi-sun-chen.lulian.draw";
    private static CardConversionSource ShiScSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class ShiSunChenHost(GameEngine e) : IShiSunChenHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginShiSc(f, op); }
    [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 49100)]
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 49100)]
    private bool ShiScImplicitDrawTwo(ProgramTargetCardWindowQuery q) => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiSunChenLulian);
    private static SkillProgramCardCategory ShiScCategory(CardKind kind) => EquipmentCatalog.IsEquipment(kind) ? SkillProgramCardCategory.Equipment :
        MatchesProgramCardCategory(kind, [SkillProgramCardCategory.Trick]) ? SkillProgramCardCategory.Trick : SkillProgramCardCategory.Basic;
    private sealed record ShiScCompletedUse(long NativeId, CardActionContext Action, IReadOnlyList<int> Targets);
    private ShiScCompletedUse? ShiScCompleted(ProgramSkillWindowContext w, int owner)
    {
        if (w.Window != SkillProgramTriggerWindow.CardUseCompleted || w.OwnerSeat != owner || w.CardUse is not { } info ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame p || GetCardActionWindow(p) != SkillProgramTriggerWindow.CardUseCompleted ||
            p.Action.ActorSeat != owner || info.ActorSeat != owner || p.Action.ActionId != info.CardActionId || p.Action.EffectiveKind != info.EffectiveKind || info.ParentCardUseFrameId != p.ParentFrameId ||
            p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count) return null;
        var a = p.Action; var nativeId = p.ParentFrameId;
        if (a.Type == CardActionType.Use)
        {
            if (LifecycleCardUse(nativeId) is not { Step: ResolutionFrameStep.Completed, Action: { } original } use || original.ActionId != a.ActionId || use.SourceSeat != owner) return null;
        }
        else if (p.CompletedResponseReturn is not { IsCommitted: false } response || !IsCompletedResponseUse(a, response.Kind) || !IsCompletedResponseParentConsistent(p)) return null;
        if (a.PhysicalCards.Count == 0 || a.PhysicalCards.Any(c => c.From.Zone != CardZoneKind.Hand || c.From.OwnerSeat != owner &&
            !ProgramEventHistory<ShiLsMaterialAuthorizedEvent>().Any(e => e.Actor == owner && e.UseId == (a.Type == CardActionType.Use ? nativeId : 0) && e.ActionId == a.ActionId && e.CardId == c.CardId && e.From == c.From))) return null;
        var targets = a.Type == CardActionType.Use ? LifecycleCardUse(nativeId)!.Action!.EffectiveDesignatedTargetSeats : a.TargetSeats;
        return new(nativeId, a, targets.Distinct().Where(IsValidPlayerSeat).ToArray());
    }
    private bool ShiScSourceWindow(ProgramSkillFrame f) => f.SkillId == ShiScLulian && f.TriggerId == "completed-hand-category" && f.ActivationId == f.TriggerId && f.WindowContext is { } w &&
        _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is ProgramCardTriggerWindowFrame p && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count &&
        MountObserverCandidateMatches(f, ToSharedCandidate(p.Candidates[p.CandidateIndex]));
    private bool ShiScNoCategory(int owner, CardKind used) => !GetHand(_players[owner]).Any(c => ShiScCategory(c.Kind) == ShiScCategory(used));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 49100)]
    private bool CanOfferShiSc(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (c.SkillId != ShiScLulian) return true;
        if (ShiScCompleted(w, c.OwnerSeat) is not { } use || !ShiScNoCategory(c.OwnerSeat, use.Action.EffectiveKind)) return false;
        var targets = use.Targets.Where(s => _players[s].IsAlive).ToArray();
        return targets.Any(s => _players[s].Hp <= _players[c.OwnerSeat].Hp) || targets.Any(s => GetEquipment(_players[s]).Count <= GetEquipment(_players[c.OwnerSeat]).Count);
    }
    private SkillProgramStepOutcome BeginShiSc(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiSunChen is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) throw new InvalidOperationException("Shi Sun Chen lost his original unstarted instruction.");
        var r = new ShiScReceipt(ShiScSource(f), f.GameplayHash, op, _turnNumber, _turnProgression.OwnerSeat, _cardUseDebitPhaseInstanceId, f.WindowContext?.ParentFrameId, 0, 0, ShiScStage.Cards)
        { Before = _movementSequence, After = _movementSequence };
        if (op == SkillProgramEffectOp.ResolveMobileShiSunChenNigu)
        {
            if (f.SkillId != ShiScNigu || f.TriggerId is not null || f.WindowContext is not null || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play)
                throw new InvalidOperationException("Nigu requires the owner's actual once-Play activation.");
        }
        else
        {
            if (op != SkillProgramEffectOp.ResolveMobileShiSunChenLulian || !ShiScSourceWindow(f) || f.WindowContext is not { } w || ShiScCompleted(w, f.OwnerSeat) is not { } use || !ShiScNoCategory(f.OwnerSeat, use.Action.EffectiveKind))
                throw new InvalidOperationException("Lulian requires the original completed physical Hand use and current absence of its category.");
            var alive = use.Targets.Where(s => _players[s].IsAlive).ToArray();
            r = r with { UseId = use.NativeId, ActionId = use.Action.ActionId, Targets = use.Targets, Stage = ShiScStage.Chain,
                Chain = alive.Any(s => _players[s].Hp <= _players[f.OwnerSeat].Hp), Draw = alive.Any(s => GetEquipment(_players[s]).Count <= GetEquipment(_players[f.OwnerSeat]).Count) };
        }
        ReplaceRuntimeTop(f = f with { ShiSunChen = r });
        AdvanceEventRulesAndQueueFact(new ShiScStartedEvent(f.Id, r.Source, r.Hash, r.Op, r.Turn, r.TurnOwner, r.PhaseId, r.WindowId, r.UseId, r.ActionId));
        if (op == SkillProgramEffectOp.ResolveMobileShiSunChenLulian)
        {
            AdvanceEventRulesAndQueueFact(new ShiScLulianBoundEvent(f.Id, r.UseId, r.ActionId, r.Chain, r.Draw, r.Targets.Count));
            for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new ShiScTargetEvent(f.Id, i, r.Targets[i]));
            ContinueShiSc(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        PublishShiSc(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ShiScOwnedChoices(ProgramSkillFrame f, int owner, bool discard) => BuildOwnedCardPaymentChoices(f.Id, owner, owner,
        [CardZoneKind.Hand, CardZoneKind.Equipment], discard ? OwnedCardMoveIntent.Discard : OwnedCardMoveIntent.Transfer,
        canSelect: (zone, card) => !IsSelfHandCategoryDiscardForbidden(owner, card, new(zone, owner), discard ? OwnedCardMoveIntent.Discard : OwnedCardMoveIntent.Transfer));
    private int ShiScChooser(ProgramSkillFrame f) => f.ShiSunChen is { } r && r.Stage is ShiScStage.Votes or ShiScStage.GiveCard ? r.Participants[r.Stage == ShiScStage.Votes ? r.Votes.Count : r.Cursor] : f.OwnerSeat;
    private IReadOnlyList<int> ShiScMomentumTargets() { var live = _players.Where(p => p.IsAlive).ToArray(); return live.Length == 0 ? [] : live.Where(p => p.Hp > live.Min(x => x.Hp)).Select(p => p.Seat).ToArray(); }
    private IReadOnlyList<PromptChoice> ShiScChoices(ProgramSkillFrame f)
    {
        var r = f.ShiSunChen!;
        PromptChoice C(string branch, string text, int? target = null) => new(new($"shi-sun-chen.{f.Id}.{r.Stage}.{branch}.{target ?? -1}"), text, [], target is { } s ? [s] : [],
            new Dictionary<string, string> { ["program-action"] = "shi-sun-chen", ["branch"] = branch });
        if (r.Stage == ShiScStage.Cards)
        {
            var suits = r.Cards.Select(id => EffectiveSuit(_players[f.OwnerSeat], GetAdvancedCard(id))).ToHashSet();
            return ShiScOwnedChoices(f, f.OwnerSeat, true).Where(c => r.Cards.Contains(c.Cards.Single()) || !suits.Contains(EffectiveSuit(_players[f.OwnerSeat], GetAdvancedCard(c.Cards.Single())))).Select(c => c with {
                Id = new($"shi-sun-chen.{f.Id}.cost.{c.Cards.Single()}"), Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "shi-sun-chen", ["branch"] = r.Cards.Contains(c.Cards.Single()) ? "remove" : "add" }
            }).Concat([C("pay", $"确认弃置{r.Cards.Count}张牌（可为零张）")]).Concat(r.Cards.Count > 0 ? [C("reset", "重新选择弃牌")] : Array.Empty<PromptChoice>()).ToArray();
        }
        if (r.Stage == ShiScStage.Votes) return (ShiScOwnedChoices(f, ShiScChooser(f), false).Count > 0 ? new[] { C("give", "私下承诺交给其一张牌") } : []).Concat([C("refuse", "不交牌")]).ToArray();
        if (r.Stage == ShiScStage.GiveCard) return ShiScOwnedChoices(f, ShiScChooser(f), false).Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "shi-sun-chen", ["branch"] = "gift" } }).ToArray();
        return r.Stage == ShiScStage.Momentum ? ShiScMomentumTargets().Select(s => C("fire", "乘势：对 " + _players[s].Name + " 造成1点火焰伤害", s)).ToArray() : [];
    }
    private void PublishShiSc(ProgramSkillFrame f)
    {
        var r = f.ShiSunChen!; var choices = ShiScChoices(f); if (choices.Count == 0) { FinishShiSc(f); return; }
        var chooser = ShiScChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage switch { ShiScStage.Cards => "逆固：多选不同花色的手牌或装备牌后确认，可以零张。", ShiScStage.Votes => "逆固：独立私下承诺，全部答复后同时公开。", ShiScStage.GiveCard => "逆固：交给孙綝一张手牌或装备牌。", _ => "戮连乘势：必须选择一名体力值不为最小的角色（可含自己）。" },
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shi-sun-chen")] private void AnswerShiSc(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiSunChen: { } r } f || _pendingDecision?.PlayerSeat != ShiScChooser(f) || !ShiScChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Shi Sun Chen rejects an unpublished chooser, original material or nonminimal fire target.");
        RequireShiSc(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (r.Stage == ShiScStage.Cards)
        {
            if (branch is "add" or "remove" or "reset")
            { int[] selected = branch == "reset" ? [] : branch == "add" ? r.Cards.Append(choice.Cards.Single()).ToArray() : r.Cards.Where(id => id != choice.Cards.Single()).ToArray(); ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Cards = selected } }); PublishShiSc(f); return; }
            if (branch != "pay") throw new InvalidOperationException("Nigu requires the explicit whole cost confirmation.");
            ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.DiscardChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            if (r.Cards.Count > 0) MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Cards, ShiScDiscardReason);
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { ShiSunChen = f.ShiSunChen! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new ShiScCostPaidEvent(f.Id, r.Cards.Count, f.ShiSunChen!.Before, _movementSequence)); ContinueShiSc(f.Id); return;
        }
        if (r.Stage == ShiScStage.Votes)
        {
            var votes = r.Votes.Append(new ShiScVote(ShiScChooser(f), branch == "give")).ToArray(); ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Votes = votes } });
            if (votes.Length < r.Participants.Count) { PublishShiSc(f); return; }
            for (var i = 0; i < votes.Length; i++) AdvanceEventRulesAndQueueFact(new ShiScVoteRevealedEvent(f.Id, i, votes[i].Seat, votes[i].Give));
            ReplaceRuntimeTop(f = f with { ShiSunChen = f.ShiSunChen! with { Stage = ShiScStage.Gifts } }); ContinueShiSc(f.Id); return;
        }
        if (r.Stage == ShiScStage.GiveCard)
        {
            if (branch != "gift") throw new InvalidOperationException("Nigu requires its originally committed giver's real card.");
            var id = choice.Cards.Single(); var from = _cardZones.GetLocation(id); var material = new ShiScMaterial(id, GetAdvancedCard(id).Kind, from); var payer = ShiScChooser(f); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.GiftChildren, Material = material, Given = false, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(f.OwnerSeat), ShiScGiveReason, (batch, moves) => {
                var current = GetActiveProgramFrame(f.Id); var move = moves.Single(m => m.CardId == id);
                if (move.From != from || move.CardKind != material.Kind) throw new InvalidOperationException("Nigu changed its committed giver's original physical source.");
                ReplaceRuntimeFrame(f.Id, current with { ShiSunChen = current.ShiSunChen! with { After = move.Sequence, Given = move.To == CardLocation.Hand(f.OwnerSeat) } });
                AdvanceEventRulesAndQueueFact(new ShiScGiftPaidEvent(f.Id, r.Cursor, payer, id, from, move.To, move.Sequence));
                RecordNativeGivenCards(f.Id, payer, f.OwnerSeat, [id], before);
            }); ContinueShiSc(f.Id); return;
        }
        if (r.Stage != ShiScStage.Momentum || branch != "fire") throw new InvalidOperationException("Lulian lost its forced momentum target.");
        ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.DamageChildren, Target = choice.Targets.Single() } });
        AdvanceEventRulesAndQueueFact(new ShiScMomentumIssuedEvent(f.Id, choice.Targets.Single())); new ProgramSkillHost(this).Damage(f, choice.Targets.Single(), 1, nature: DamageNature.Fire);
    }
    private bool DrainShiSc(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 49100)] private bool ContinueShiSc(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ShiSunChen: { } r } f && f.Id == id && _pendingDecision is null)
        {
            RequireShiSc(f); if (f.NativeDraw is not null || f.AttackAttempt is not null || r.Stage == ShiScStage.Drawing) return true;
            if (DrainShiSc(id)) return true; f = GetActiveProgramFrame(id); r = f.ShiSunChen!;
            if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (_winner != Winner.None) { FinishShiSc(f); return true; }
            if (r.Stage == ShiScStage.DiscardChildren)
            {
                var participants = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && IsWithinAttackRange(f.OwnerSeat, p.Seat)).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).Select(p => p.Seat).ToArray();
                ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.Votes, Participants = participants } });
                for (var i = 0; i < participants.Length; i++) AdvanceEventRulesAndQueueFact(new ShiScParticipantEvent(id, i, participants[i]));
                if (participants.Length > 0) { PublishShiSc(f); return true; }
                ReplaceRuntimeTop(f = f with { ShiSunChen = f.ShiSunChen! with { Stage = ShiScStage.Gifts } }); continue;
            }
            if (r.Stage == ShiScStage.GiftChildren) { ReplaceRuntimeTop(f with { ShiSunChen = r with { Stage = ShiScStage.Gifts, Cursor = r.Cursor + 1, Ungiven = r.Ungiven + (r.Given ? 0 : 1), Material = null } }); continue; }
            if (r.Stage == ShiScStage.Gifts)
            {
                if (r.Cursor >= r.Participants.Count) { AdvanceEventRulesAndQueueFact(new ShiScArmedEvent(id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.Ungiven)); FinishShiSc(f); return true; }
                var seat = r.Participants[r.Cursor];
                if (!r.Votes[r.Cursor].Give || !_players[seat].IsAlive || !_players[f.OwnerSeat].IsAlive || ShiScOwnedChoices(f, seat, false).Count == 0)
                { ReplaceRuntimeTop(f with { ShiSunChen = r with { Cursor = r.Cursor + 1, Ungiven = r.Ungiven + 1 } }); continue; }
                ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.GiveCard } }); PublishShiSc(f); return true;
            }
            if (r.Stage == ShiScStage.ChainChildren) { ReplaceRuntimeTop(f with { ShiSunChen = r with { Stage = ShiScStage.Chain } }); continue; }
            if (r.Stage == ShiScStage.Chain)
            {
                if (r.Chain && r.Cursor < r.Targets.Count)
                {
                    var target = r.Targets[r.Cursor]; ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.ChainChildren, Cursor = r.Cursor + 1 } });
                    AdvanceEventRulesAndQueueFact(new ShiScChainIssuedEvent(id, r.Cursor, target)); if (_players[target].IsAlive) SetProgramChainedState(f, true, target); continue;
                }
                if (r.Draw && _players[f.OwnerSeat].IsAlive)
                { ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.Drawing } }); BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(ShiScDrawReason)); return true; }
                ReplaceRuntimeTop(f with { ShiSunChen = r with { Stage = ShiScStage.DrawChildren } }); continue;
            }
            if (r.Stage == ShiScStage.DrawChildren)
            {
                if (r.Chain && r.Draw && _players[f.OwnerSeat].IsAlive && ShiScMomentumTargets().Count > 0)
                { ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.Momentum } }); PublishShiSc(f); return true; }
                FinishShiSc(f); return true;
            }
            if (r.Stage == ShiScStage.DamageChildren) { FinishShiSc(f); return true; }
            PublishShiSc(f); return true;
        }
        return _resolutionStack.Any(f => f.Id == id && f is ProgramSkillFrame { ShiSunChen: not null });
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiSunChenLulian)] private void ReturnShiScDraw(ProgramSkillFrame input, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(input.Id); var r = f.ShiSunChen!;
        if (r.Stage != ShiScStage.Drawing || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1) throw new InvalidOperationException("Lulian lost its exact one-card native draw return.");
        ReplaceRuntimeTop(f = f with { ShiSunChen = r with { Stage = ShiScStage.DrawChildren, Before = checked((int)outcome.SequenceBefore), After = checked((int)outcome.SequenceAfter) }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiScDrawPaidEvent(f.Id, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter)); ContinueShiSc(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 49100)] private bool ReturnShiScMovement(ProgramSkillFrame f) => f.ShiSunChen is not null && ContinueShiSc(f.Id);
    private void FinishShiSc(ProgramSkillFrame f) { AdvanceEventRulesAndQueueFact(new ShiScCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { ShiSunChen = f.ShiSunChen! with { Stage = ShiScStage.Finished }, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    private PendingDecision? ProjectShiScCardSelection(PendingDecision decision, ProgramSkillFrame f) => f.ShiSunChen is { Stage: ShiScStage.Cards } r && decision.PlayerSeat == f.OwnerSeat ?
        NativeOwnedSelection(decision, f, "shi-sun-chen:cost", "branch", ["add"], ["pay"], r.Cards, 0, ShiScChoices(f).Where(c => c.Parameters.GetValueOrDefault("branch") == "add").Select(c => EffectiveSuit(_players[f.OwnerSeat], GetAdvancedCard(c.Cards.Single()))).Distinct().Count(), resetValues: ["reset"]) : null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiSunChenNigu, SkillProgramEffectOp.ResolveMobileShiSunChenLulian)] private PromptChoice ChooseShiSc(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.ShiSunChen!;
        if (r.Stage == ShiScStage.Cards) return p.Choices.Single(c => c.Parameters["branch"] == "pay");
        if (r.Stage == ShiScStage.Votes) return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == (AreProgramDistributionAllies(_players[p.PlayerSeat], _players[f.OwnerSeat]) ? "give" : "refuse")) ?? p.Choices.Last();
        if (r.Stage == ShiScStage.GiveCard) return p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat])).First();
        return p.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat), c.Targets.Single(), new(0, 0, 0, 1, 0, 0, false, false))).First();
    }
}
