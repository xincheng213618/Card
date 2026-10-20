namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ChenshuoRevealReason = "skill-program.zhou-qun.chenshuo.reveal", ChenshuoGainReason = "skill-program.zhou-qun.chenshuo.gain", TianhouExchangeReason = "skill-program.zhou-qun.tianhou.exchange", ZhouQunCleanupReason = "skill-program.zhou-qun.cleanup";
    private bool HasZhouQunCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveChenshuoRepeatedReveal) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveLieshuEndingLoss) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveZhouyuThunderNeighbors) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYanshuangEndingLoss);
    private static CardConversionSource ZhouQunSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static int ZhouQunNameLength(CardKind kind) => IsSlashCard(kind) ? 1 : kind == CardKind.BorrowedSword ? 2 : CardCatalog.Get(kind).DisplayName.Length;
    private int[] ZhouQunNeighbors(int seat)
    {
        // A just-dead damaged participant still anchors the two directions;
        // its former seat is not silently removed from the after-damage rule.
        var living = _players.Where(p => p.IsAlive || p.Seat == seat).Select(p => p.Seat).Order().ToArray();
        var index = Array.IndexOf(living, seat);
        return index < 0 || living.Length < 2 ? [] : new[] { living[(index + living.Length - 1) % living.Length], living[(index + 1) % living.Length] }.Distinct().OrderBy(s => (s - seat + _playerCount) % _playerCount).ToArray();
    }
    private bool ZhouQunParent(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.OwnerSeat != c.OwnerSeat) return false;
        var parent = _resolutionStack.SingleOrDefault(p => p.Id == context.ParentFrameId);
        if (parent is ProgramLifecycleTriggerWindowFrame lifecycle) return context.Window == lifecycle.Window && lifecycle.CandidateIndex >= 0 && lifecycle.CandidateIndex < lifecycle.Candidates.Count && lifecycle.Candidates[lifecycle.CandidateIndex] == c;
        if (parent is TurnEndingBoundaryFrame ending) return context.Window == SkillProgramTriggerWindow.TurnEnding && ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count && ending.Items[ending.ItemIndex].Candidate == c;
        if (parent is ProgramCardTriggerWindowFrame card) return context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && card.CandidateIndex >= 0 && card.CandidateIndex < card.Candidates.Count && card.Candidates[card.CandidateIndex] is { } candidate && candidate.OwnerSeat == c.OwnerSeat && candidate.SkillId == c.SkillId && candidate.TriggerId == c.BindingId && candidate.SkillInstanceId == c.SkillInstanceId && candidate.GameplayHash == c.GameplayHash && context.CardUse?.ParentCardUseFrameId == card.ParentFrameId;
        if (parent is DamageTriggerWindowFrame damage) return context.Window == damage.TriggerWindow && damage.CandidateIndex >= 0 && damage.CandidateIndex < damage.Candidates.Count && damage.Candidates[damage.CandidateIndex].ToProgramCandidate() == c;
        return parent is BeforeDamageProgramWindowFrame before && context.Window == SkillProgramTriggerWindow.BeforeDamageApplied && before.CandidateIndex >= 0 && before.CandidateIndex < before.Candidates.Count && before.Candidates[before.CandidateIndex].Candidate == c;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27572)]
    private bool CanOfferZhouQun(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == "ol:zhouyu" && trigger.Effects is [{ Op: SkillProgramEffectOp.PreventCurrentDamage }])
            return _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is BeforeDamageProgramWindowFrame { Nature: DamageNature.Fire, Prevented: false } before && IsValidPlayerSeat(before.SourceSeat) && before.SourceSeat != candidate.OwnerSeat && CurrentDamageAttempt?.IsSourceLess != true;
        if (trigger.Effects is not [var effect] || !ZhouQunComposition.Handles(effect.Op)) return true;
        if (effect.Op == SkillProgramEffectOp.ResolveChenshuoRepeatedReveal) return _players.Any(p => p.IsAlive && GetHand(p).Count > 0);
        if (effect.Op == SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment) return NingwuEligibleUse(candidate.OwnerSeat, context) is not null;
        if (effect.Op == SkillProgramEffectOp.ResolveZhouyuThunderNeighbors) return context.Amount > 0 && _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is DamageTriggerWindowFrame d && GetDamageNature(GetDamageTriggerAttack(d)) == DamageNature.Thunder;
        if (effect.Op == SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant) return true;
        var target = context.TargetSeat ?? context.SourceSeat;
        if (target is not { } seat || seat == candidate.OwnerSeat || !_players[seat].IsAlive) return false;
        var hp = _players.Where(p => p.IsAlive && p.HasHpValue).Select(p => p.Hp).ToArray();
        return hp.Length > 0 && _players[seat].HasHpValue && _players[seat].Hp == (effect.Op == SkillProgramEffectOp.ResolveLieshuEndingLoss ? hp.Max() : hp.Min());
    }
    [ProgramCapability]
    private sealed class ZhouQunCapability(GameEngine engine) : IZhouQunHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.BeginZhouQun(frame, op); }
    private SkillProgramStepOutcome BeginZhouQun(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ZhouQun is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !ZhouQunComposition.Handles(op)) throw new InvalidOperationException("Zhou Qun requires his original living native trigger.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (!ZhouQunParent(candidate, context) || !CanOfferZhouQun(candidate, GetProgramTrigger(f), context)) throw new InvalidOperationException("The current weather binding lost its exact native parent or qualification.");
        var stage = op == SkillProgramEffectOp.ResolveChenshuoRepeatedReveal ? ZhouQunStage.ChooseHandOwner : op == SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant ? ZhouQunStage.LeaseChildren : op == SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment ? ZhouQunStage.Judgment : ZhouQunStage.HpIssuing;
        var r = new ZhouQunReceipt(ZhouQunSource(f), f.GameplayHash, op, context.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat, stage) { Paid = true };
        if (stage == ZhouQunStage.HpIssuing) r = r with { Targets = op == SkillProgramEffectOp.ResolveZhouyuThunderNeighbors ? ZhouQunNeighbors(context.TargetSeat!.Value) : [context.TargetSeat ?? context.SourceSeat!.Value] };
        if (stage == ZhouQunStage.Judgment)
        {
            var use = NingwuEligibleUse(f.OwnerSeat, context)!;
            r = r with { UseId = use.Id, ActionId = use.Action!.ActionId, Peer = use.SourceSeat, Targets = use.Action.EffectiveDesignatedTargetSeats, SlashRank = use.Action.EffectiveRank, JudgmentId = _resolutionSequence + 1 };
        }
        ReplaceRuntimeTop(f = f with { ZhouQun = r });
        AdvanceEventRulesAndQueueFact(new ZhouQunStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.TurnOwner));
        for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new ZhouQunHpTargetEvent(f.Id, i, r.Targets[i]));
        if (stage == ZhouQunStage.LeaseChildren) { AdvanceEventRulesAndQueueFact(new TianhouInvokedEvent(f.Id, r.Source, r.GameplayHash)); ExpireTianhouWeather(f); }
        if (stage == ZhouQunStage.Judgment)
        {
            AdvanceEventRulesAndQueueFact(new NingwuJudgmentIssuedEvent(f.Id, r.JudgmentId, r.UseId, r.ActionId, r.Peer, r.Targets.Single(), r.SlashRank));
            _ = BeginJudgment(null, r.Peer, "ningwu", f.Id, context.CardUse!.EffectiveKind, JudgmentContinuationKind.ProgramSkill, sourceSeat: f.OwnerSeat, programResultBind: "ningwu-result", programResultVisibility: SkillProgramCardSetVisibility.Public);
        }
        else ContinueZhouQun(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private int ZhouQunChooser(ProgramSkillFrame f) => f.ZhouQun is { Stage: ZhouQunStage.ChooseShownHand, Peer: >= 0 } r ? r.Peer : f.OwnerSeat;
    private PromptChoice ZhouQunChoice(ProgramSkillFrame f, string branch, string label, int? card = null, int? target = null) => new(new($"zhou-qun.{f.Id}.{f.ZhouQun!.Step}.{f.ZhouQun.Stage}.{branch}.{card ?? target ?? -1}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = "zhou-qun", ["branch"] = branch });
    private IReadOnlyList<PromptChoice> ZhouQunChoices(ProgramSkillFrame f)
    {
        var r = f.ZhouQun!; var choices = new List<PromptChoice>();
        if (r.Stage == ZhouQunStage.ChooseHandOwner) choices.AddRange(_players.Where(p => p.IsAlive && GetHand(p).Count > 0).Select(p => ZhouQunChoice(f, "hand-owner", "令" + p.Name + "展示一张手牌", target: p.Seat)));
        else if (r.Stage == ZhouQunStage.ChooseShownHand) choices.AddRange(GetHand(_players[r.Peer]).Select(c => ZhouQunChoice(f, "show-hand", "展示【" + c.DisplayName + "】", c.Id)));
        else if (r.Stage == ZhouQunStage.ExchangeHand)
        {
            if (r.SelectedHand.Count < r.Viewed.Count) choices.AddRange(GetHand(_players[f.OwnerSeat]).Where(c => !r.SelectedHand.Contains(c.Id)).Select(c => ZhouQunChoice(f, "hand", "换入牌堆【" + c.DisplayName + "】", c.Id)));
            choices.AddRange(r.SelectedHand.Select(id => ZhouQunChoice(f, "unhand", "撤回【" + GetAdvancedCard(id).DisplayName + "】", id)));
            choices.Add(ZhouQunChoice(f, "hand-done", "确定等量交换（可不交换）"));
        }
        else if (r.Stage == ZhouQunStage.ExchangeTop) choices.AddRange(r.Viewed.Where(id => !r.SelectedTop.Contains(id)).Select(id => ZhouQunChoice(f, "top", "换取【" + GetAdvancedCard(id).DisplayName + "】", id)));
        else if (r.Stage == ZhouQunStage.ChooseWeatherCard) choices.AddRange(r.Viewed.Where(id => _cardZones.GetLocation(id) == CardLocation.DrawPile).Select(id => ZhouQunChoice(f, "weather-card", "展示【" + GetAdvancedCard(id).DisplayName + "】授予" + ZhouQunWeather(GetAdvancedCard(id).Suit).Name, id)));
        else if (r.Stage == ZhouQunStage.ChooseWeatherRecipient) choices.AddRange(_players.Where(p => p.IsAlive).Select(p => ZhouQunChoice(f, "weather-target", "令" + p.Name + "获得" + ZhouQunWeather(GetAdvancedCard(r.ChosenCard).Suit).Name, target: p.Seat)));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishZhouQun(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { CleanupAndFinishZhouQun(f); return; }
        if (!IsValidPlayerSeat(ZhouQunChooser(f)) || !_players[ZhouQunChooser(f)].IsAlive || ZhouQunChoices(f).Count == 0)
        {
            if (f.ZhouQun is { Op: SkillProgramEffectOp.ResolveChenshuoRepeatedReveal } r && r.Revealed.Count > 0) { ReplaceRuntimeTop(f with { ZhouQun = r with { Stage = ZhouQunStage.RevealChildren, Repeat = false } }); ContinueZhouQun(f.Id); }
            else CleanupAndFinishZhouQun(f);
            return;
        }
        var choices = ZhouQunChoices(f);
        var skill = _contentRegistry.GetSkill(f.SkillId); var chooser = ZhouQunChooser(f); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, skill.Name + "：请选择真实牌或角色。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("zhou-qun")]
    private void AnswerZhouQun(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhouQun: { } r } f || _pendingDecision?.PlayerSeat != ZhouQunChooser(f) || !ZhouQunChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Weather choices require the real current private chooser and exact entities.");
        AssertZhouQun(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (branch == "hand-owner") { ReplaceRuntimeTop(f = f with { ZhouQun = r with { Peer = choice.Targets.Single(), Stage = ZhouQunStage.ChooseShownHand } }); PublishZhouQun(f); return; }
        if (branch == "show-hand")
        {
            var card = GetAdvancedCard(choice.Cards.Single()); var kind = AdvancedEffectiveHandKind(_players[r.Peer], card); var shown = new ZhouQunShownCard(card.Id, r.Peer, kind, GetProgramEffectiveSuit(_players[r.Peer], card), card.Rank, ZhouQunNameLength(kind));
            ReplaceRuntimeTop(f = f with { ZhouQun = r with { Shown = shown, ShownCards = r.ShownCards.Append(shown).ToArray(), Stage = ZhouQunStage.RevealChildren, Repeat = true } });
            AdvanceEventRulesAndQueueFact(new ChenshuoHandShownEvent(f.Id, r.Revealed.Count, shown)); AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId!, r.Peer, "chenshuo-hand", [ToSnapshot(card)]));
            ContinueZhouQun(f.Id); return;
        }
        if (branch == "hand") r = r with { SelectedHand = r.SelectedHand.Append(choice.Cards.Single()).ToArray() };
        else if (branch == "unhand") r = r with { SelectedHand = r.SelectedHand.Where(id => id != choice.Cards.Single()).ToArray() };
        else if (branch == "hand-done") { r = r with { Stage = ZhouQunStage.ExchangeTop }; if (r.SelectedHand.Count == 0) { ReplaceRuntimeTop(f = f with { ZhouQun = r }); PayTianhouExchange(f); return; } }
        else if (branch == "top")
        {
            r = r with { SelectedTop = r.SelectedTop.Append(choice.Cards.Single()).ToArray() };
            if (r.SelectedTop.Count == r.SelectedHand.Count) { ReplaceRuntimeTop(f = f with { ZhouQun = r }); PayTianhouExchange(f); return; }
        }
        else if (branch == "weather-card")
        {
            var id = choice.Cards.Single(); r = r with { ChosenCard = id, Stage = ZhouQunStage.ChooseWeatherRecipient };
            ReplaceRuntimeTop(f = f with { ZhouQun = r }); AdvanceEventRulesAndQueueFact(new TianhouCardShownEvent(f.Id, f.OwnerSeat, id, GetAdvancedCard(id).Suit));
            AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId!, f.OwnerSeat, "tianhou-weather", [ToSnapshot(GetAdvancedCard(id))])); PublishZhouQun(f); return;
        }
        else if (branch == "weather-target") { GrantTianhouWeather(f, choice.Targets.Single()); ContinueZhouQun(f.Id); return; }
        else throw new InvalidOperationException("Unknown Zhou Qun choice.");
        ReplaceRuntimeTop(f = f with { ZhouQun = r }); PublishZhouQun(f);
    }
    private void BeginZhouQunMovement(ref ProgramSkillFrame f, ZhouQunStage stage)
    {
        foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
        ReplaceRuntimeTop(f = f with { ZhouQun = f.ZhouQun! with { Stage = stage, Step = f.ZhouQun.Step + 1, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
    }
    private void CompleteZhouQunMovement(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.ZhouQun!;
        ReplaceRuntimeTop(f = f with { ZhouQun = r with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ZhouQunMoveIssuedEvent(id, r.Step, r.Stage, r.Before, _movementSequence));
    }
    private void RecordZhouQunMoves(ProgramSkillFrame f, long batch, IReadOnlyList<CardMovementRecord> records)
    { foreach (var m in records) AdvanceEventRulesAndQueueFact(new ZhouQunMovedEvent(f.Id, f.ZhouQun!.Step, batch, m.CardId, m.From, m.To, m.Reason, m.Sequence)); }
    private bool DrainZhouQun(ProgramSkillFrame f) => TryBeginAdvancedSkillsChanged(f.Id) || TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueZhouQun(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhouQun: { } r } f && f.Id == id)
        {
            AssertZhouQun(f);
            if (r.Stage == ZhouQunStage.Judgment) return;
            if (r.Stage is ZhouQunStage.ChooseHandOwner or ZhouQunStage.ChooseShownHand or ZhouQunStage.ExchangeHand or ZhouQunStage.ExchangeTop or ZhouQunStage.ChooseWeatherCard or ZhouQunStage.ChooseWeatherRecipient) { if (_pendingDecision is null) PublishZhouQun(f); return; }
            if (r.Stage == ZhouQunStage.HpIssuing)
            {
                if (r.Cursor == r.Targets.Count || _winner != Winner.None) { FinishZhouQun(f); return; }
                if (!_players[r.Targets[r.Cursor]].IsAlive) { ReplaceRuntimeTop(f with { ZhouQun = r with { Cursor = r.Cursor + 1 } }); continue; }
                new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, r.Targets[r.Cursor], 1);
                if (_resolutionStack.LastOrDefault()?.Id != id) return;
                f = GetActiveProgramFrame(id); if (f.ZhouQun!.Stage == ZhouQunStage.HpIssuing) return;
            }
            if (DrainZhouQun(f)) return;
            f = GetActiveProgramFrame(id); r = f.ZhouQun!; if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == ZhouQunStage.HpChildren) { ReplaceRuntimeTop(f with { ZhouQun = r with { Stage = ZhouQunStage.HpIssuing, Cursor = r.Cursor + 1 } }); continue; }
            if (r.Stage == ZhouQunStage.LeaseChildren)
            {
                if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishZhouQun(f); return; }
                BeginZhouQunMovement(ref f, ZhouQunStage.ViewChildren); _ = EnsureDrawPile();
                var viewed = _cardZones.CardsAt(CardLocation.DrawPile).TakeLast(3).Reverse().Select(c => c.Id).ToArray();
                f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { ZhouQun = f.ZhouQun! with { Viewed = viewed } }); for (var i = 0; i < viewed.Length; i++) AdvanceEventRulesAndQueueFact(new TianhouViewedEvent(id, i, viewed[i])); CompleteZhouQunMovement(id); continue;
            }
            if (r.Stage == ZhouQunStage.ViewChildren || r.Stage == ZhouQunStage.ExchangeChildren)
            { ReplaceRuntimeTop(f = f with { ZhouQun = r with { Viewed = r.Stage == ZhouQunStage.ExchangeChildren ? _cardZones.CardsAt(CardLocation.DrawPile).TakeLast(3).Reverse().Select(c => c.Id).ToArray() : r.Viewed, Stage = r.Stage == ZhouQunStage.ViewChildren ? ZhouQunStage.ExchangeHand : ZhouQunStage.ChooseWeatherCard } }); PublishZhouQun(f); return; }
            if (r.Stage == ZhouQunStage.RevealChildren)
            {
                if (_players[f.OwnerSeat].IsAlive && _winner == Winner.None && r.Repeat && r.Revealed.Count < 3)
                {
                    if (r.ShownCards.Count == r.Revealed.Count) { ReplaceRuntimeTop(f = f with { ZhouQun = r with { Stage = ZhouQunStage.ChooseHandOwner } }); PublishZhouQun(f); return; }
                    RevealNextChenshuo(f); continue;
                }
                BeginZhouQunMovement(ref f, ZhouQunStage.GainChildren); var ids = r.Revealed.Where(card => _cardZones.GetLocation(card) == CardLocation.Processing).ToArray();
                if (ids.Length > 0) MoveProgramCardsFromMultipleSources(ids, _players[f.OwnerSeat].IsAlive ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile, new(ChenshuoGainReason), (batch, records) => RecordZhouQunMoves(f, batch, records)); CompleteZhouQunMovement(id); continue;
            }
            if (r.Stage == ZhouQunStage.JudgmentChildren) { ApplyNingwuJudgment(f); FinishZhouQun(GetActiveProgramFrame(id)); return; }
            FinishZhouQun(f); return;
        }
    }
    private void CleanupAndFinishZhouQun(ProgramSkillFrame f)
    {
        var ids = f.ZhouQun!.Revealed.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
        if (ids.Length == 0) { FinishZhouQun(f); return; }
        BeginZhouQunMovement(ref f, ZhouQunStage.CleanupChildren); MoveProgramCardsFromMultipleSources(ids, CardLocation.DiscardPile, new(ZhouQunCleanupReason), (batch, records) => RecordZhouQunMoves(f, batch, records)); CompleteZhouQunMovement(f.Id); ContinueZhouQun(f.Id);
    }
    private void FinishZhouQun(ProgramSkillFrame f) { AdvanceEventRulesAndQueueFact(new ZhouQunCompletedEvent(f.Id, true)); ReplaceRuntimeTop(f = f with { ZhouQun = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27572)] private bool ResumeZhouQun(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhouQun: not null } f || f.Id != id) return false; ContinueZhouQun(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27572)] private bool ReturnZhouQunMovement(ProgramSkillFrame f) { if (f.ZhouQun is null) return false; ContinueZhouQun(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27572)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27572)] private bool ContinuePaidZhouQun(ProgramSkillFrame f) { if (f.ZhouQun is not { Paid: true }) return false; AssertZhouQun(f); return true; }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27572)]
    private void RecordZhouQunHpPayment(ProgramSkillHpLostEvent e)
    { if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { ZhouQun: { Stage: ZhouQunStage.HpIssuing } r } f && e.FrameId == f.Id && e.SkillId == f.SkillId && r.Cursor < r.Targets.Count && e.TargetSeat == r.Targets[r.Cursor]) { ReplaceRuntimeTop(f with { ZhouQun = r with { Stage = ZhouQunStage.HpChildren } }); AdvanceEventRulesAndQueueFact(new ZhouQunHpPaidEvent(f.Id, r.Cursor, e.TargetSeat, e.Amount, e.RemainingHp)); } }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveChenshuoRepeatedReveal, SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant)]
    private PromptChoice ChooseZhouQun(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "hand-done") ?? p.Choices.First();
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 80)]
    private IReadOnlyList<CardSnapshot> ZhouQunPrivatelyViewedTopCards(int viewerSeat) => Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewerSeat && f.ZhouQun is { Stage: ZhouQunStage.ExchangeHand or ZhouQunStage.ExchangeTop or ZhouQunStage.ChooseWeatherCard or ZhouQunStage.ChooseWeatherRecipient }).SelectMany(f => f.ZhouQun!.Viewed).Distinct().Select(id => ToSnapshot(GetAdvancedCard(id))).ToArray());
    [ProgramViewContributor(ProgramViewContribution.PublicCardsBeforeResolution, 30)]
    private IEnumerable<CardSnapshot> ProjectZhouQunPublicShownCards() => ZhouQunPublicShownCardIds().Select(id =>
        ToSnapshot(GetAdvancedCard(id)) with
        { PublicHandOwnerSeat = _cardZones.GetLocation(id).Zone == CardZoneKind.Hand ? _cardZones.GetLocation(id).OwnerSeat : null });

    private IEnumerable<int> ZhouQunPublicShownCardIds() => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.ZhouQun is not null).SelectMany(f => f.ZhouQun!.Revealed.Concat(f.ZhouQun.ShownCards.Select(c => c.CardId)).Concat(f.ZhouQun.ChosenCard > 0 ? [f.ZhouQun.ChosenCard] : [])).Distinct();
}
