namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string WuJingRewardReason = "skill-program.wu-jing.heji.red", WuJingClaimReason = "skill-program.wu-jing.liubing.claim";
    private CardConversionSource WuJingSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class WuJingCapability(GameEngine engine) : IWuJingHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginWuJing(f, op); }
    private CardUseFrame? WuJingCompletedUse(ProgramSkillWindowContext context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } card }) return null;
        var use = LifecycleCardUse(card.ParentCardUseFrameId);
        return use is { Action: { Type: CardActionType.Use } a } && a.ActionId == card.CardActionId && a.ActorSeat == card.ActorSeat && a.EffectiveKind == use.CardKind ? use : null;
    }
    private bool WuJingHejiIncoming(CardUseFrame use) => use.Action is { Type: CardActionType.Use } a && use.TargetSeats is [var target] &&
        a.EffectiveDesignatedTargetSeats.SequenceEqual([target]) && _players[target].IsAlive &&
        (use.CardKind == CardKind.Duel || IsSlashCard(use.CardKind) && a.EffectiveIsRed == true);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27130)]
    private bool QualifyWuJing(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var e] || !WuJingComposition.Handles(e.Op)) return true;
        if (e.Op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash) return CanRunWuJingLiubingCandidate(c, context);
        if (e.Op == SkillProgramEffectOp.RewardHejiUnconvertedUse) return IsIssuedWuJingHejiReward(c, context);
        return _players[c.OwnerSeat].IsAlive && WuJingCompletedUse(context) is { } use && WuJingHejiIncoming(use) && use.TargetSeats[0] != c.OwnerSeat;
    }
    private SkillProgramStepOutcome BeginWuJing(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WuJing is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context)
            throw new InvalidOperationException("Wu Jing requires his exact unissued native trigger instruction.");
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (!QualifyWuJing(c, GetProgramTrigger(f), context)) throw new InvalidOperationException("Wu Jing lost his completed use or genuine discard invoice.");
        var use = op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash ? null : WuJingCompletedUse(context)!;
        var claim = op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash ? WuJingClaimFacts(context.MovementBatch!, c) : [];
        var entities = claim.Where(e => _cardZones.GetLocation(e.CardId) == CardLocation.DiscardPile && _cardMovements.Last(m => m.CardId == e.CardId).Sequence == e.MovementSequence)
            .Select(e => new WuJingMaterial(e.CardId, GetAdvancedCard(e.CardId).Kind, CardLocation.DiscardPile, e.MovementSequence)).ToArray();
        var r = new WuJingReceipt(WuJingSource(f), f.GameplayHash, op, context.ParentFrameId, _turnNumber,
            op == SkillProgramEffectOp.UseHejiAtCompletedSingleTarget ? WuJingStage.UseChoice : op == SkillProgramEffectOp.RewardHejiUnconvertedUse ? WuJingStage.RewardChildren : WuJingStage.ClaimChildren,
            use?.Id ?? claim[0].UseId, use?.Action!.ActionId ?? claim[0].ActionId, op == SkillProgramEffectOp.RewardHejiUnconvertedUse ? use!.WuJingHeji!.Origin.TargetSeat : use?.TargetSeats.Single() ?? context.SourceSeat!.Value,
            NativeOrigin: op == SkillProgramEffectOp.RewardHejiUnconvertedUse ? use!.WuJingHeji!.Origin : null, Before: _movementSequence, After: _movementSequence) { Entities = entities };
        ReplaceRuntimeTop(f = f with { WuJing = r });
        AdvanceEventRulesAndQueueFact(new WuJingStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.ActualTurn, r.UseId, r.ActionId, r.TargetSeat));
        if (op == SkillProgramEffectOp.UseHejiAtCompletedSingleTarget) { PublishWuJing(f); return SkillProgramStepOutcome.AwaitChoice; }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (op == SkillProgramEffectOp.RewardHejiUnconvertedUse)
        {
            UpdateLifecycleCardUse(r.UseId, u => u with { WuJingHeji = u.WuJingHeji! with { Rewarded = true } });
            var cards = _cardZones.CardsAt(CardLocation.DrawPile).Where(card => card.Suit is Suit.Heart or Suit.Diamond).ToArray();
            var red = cards.Length == 0 ? null : cards[_random.Next(cards.Length)];
            if (red is not null) MoveCard(red, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(WuJingRewardReason));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { WuJing = f.WuJing! with { RewardCardId = red?.Id, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new WuJingRewardIssuedEvent(f.Id, r.NativeOrigin!.ProgramFrameId, r.UseId, red?.Id, r.Before, _movementSequence));
        }
        else
        {
            foreach (var entity in entities) MoveCard(GetAdvancedCard(entity.CardId), CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new(WuJingClaimReason));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { WuJing = f.WuJing! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new WuJingClaimIssuedEvent(f.Id, r.UseId, r.ActionId, context.MovementBatch!.Id, entities.Length, r.Before, _movementSequence));
        }
        ContinueWuJing(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool WuJingCanUse(int owner, int target, Card card, CardKind kind, CardConversionSource? conversion, IReadOnlyList<int> ids)
    {
        var actor = _players[owner]; var victim = _players[target];
        if (_winner != Winner.None || !actor.IsAlive || !victim.IsAlive || owner == target || IsCardUseForbidden(owner, kind, CardActionType.Use) ||
            (IsSlashCard(kind) ? IsHandCategoryMaterialRestricted(ids) || WuJingLiubingUsedSuit(owner, card, kind, ids, conversion is null ? [] : [conversion], CaptureUsedCardSuit(owner, ids, card)) is { } usedSuit && IsTurnSuitUseForbidden(owner, usedSuit) : IsTurnPhysicalUseForbidden(owner, ids)) || HasTurnCardTargetRestriction(owner, SkillProgramCardTargetRestriction.SelfOnly) ||
            ids.Any(id => _cardZones.GetLocation(id) == CardLocation.Hand(owner) && IsTurnHandCardRestricted(actor, GetAdvancedCard(id)))) return false;
        if (IsSlashCard(kind)) return CanUseSlashTarget(actor, victim, card, conversion, kind, ignoreDistance: true, physicalCardIds: ids);
        var appearance = ApplyProgramUseAppearance(actor, card, conversion);
        return kind == CardKind.Duel && !IsDirectedCardTargetProhibited(owner, target, kind) && !IsCardTargetProhibited(victim, kind, appearance.Suit, SuitColor(EffectiveSuit(actor, appearance)));
    }
    private IReadOnlyList<PromptChoice> WuJingChoices(ProgramSkillFrame f)
    {
        if (f.WuJing is not { Stage: WuJingStage.UseChoice } r || !_players[f.OwnerSeat].IsAlive || !_players[r.TargetSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return [];
        var actor = _players[f.OwnerSeat]; var result = new List<PromptChoice>();
        Dictionary<string,string> P(CardKind kind) => new() { ["program-action"] = "wu-jing", ["kind"] = kind.ToString() };
        void Add(Card card, CardKind kind, CardConversionSource? source, IReadOnlyList<int> ids, bool zhangba = false)
        {
            if (!WuJingCanUse(f.OwnerSeat, r.TargetSeat, card, kind, source, ids)) return;
            var parameters = P(kind); if (source is not null) AddConversionParameters(parameters, source); if (zhangba) parameters["equipment"] = CardKind.ZhangbaSerpentSpear.ToString();
            result.Add(new(new($"wu-jing.{f.Id}.{kind}.{source?.SkillInstanceId}.{source?.BindingId}.{string.Join('-', ids)}.{zhangba}"), $"对{_players[r.TargetSeat].Name}使用【{CardCatalog.Get(kind).DisplayName}】", ids, [r.TargetSeat], parameters));
        }
        foreach (var card in GetPlayableCards(actor).Concat(GetEquipment(actor)).DistinctBy(c => c.Id))
        {
            foreach (var kind in SlashKinds.Append(CardKind.Duel))
                foreach (var conversion in GetProgramViewAsConversions(actor, card, kind, false)) Add(card, kind, conversion, [card.Id]);
            if (card.Kind == CardKind.Duel && !HasProgramCardIdentity(actor, card)) Add(card, CardKind.Duel, null, [card.Id]);
            foreach (var identity in GetProgramCardIdentityMatches(actor, card).Where(i => i.Identity.OutputKind == CardKind.Duel)) Add(card, CardKind.Duel, identity.Source, [card.Id]);
        }
        foreach (var card in GetSlashUseCards(actor))
        {
            var identity = GetProgramCardIdentityMatches(actor, card).FirstOrDefault(i => IsSlashCard(i.Identity.OutputKind));
            var baseKind = identity?.Identity.OutputKind ?? (IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash);
            foreach (var kind in GetSlashUseKinds(actor, baseKind))
            foreach (var variant in CreateConversionChoiceVariants(actor, card, kind, false, $"wu-jing.{f.Id}.base.{kind}.{card.Id}", $"使用【{CardCatalog.Get(kind).DisplayName}】", [card.Id], [r.TargetSeat], P(kind)))
            { TryReadConversionSource(variant.Parameters, out var conversion); if (WuJingCanUse(f.OwnerSeat, r.TargetSeat, card, kind, conversion, [card.Id])) result.Add(variant); }
        }
        foreach (var pair in GetZhangbaSlashPairs(actor)) Add(pair[0], CardKind.Slash, null, pair.Select(c => c.Id).ToArray(), true);
        foreach (var kind in SlashKinds.Append(CardKind.Duel))
        foreach (var selection in GetProgramMultiCardViewAsSelections(actor, kind, false).Where(s => ViewAsRule(s.Source)?.RoundDistinctBasicUse is null || s.OutputKind == kind)) Add(selection.Cards[0], kind, selection.Source, selection.Cards.Select(c => c.Id).ToArray());
        result.AddRange(KenshangProgramSlashChoices(f, f.OwnerSeat, [r.TargetSeat]));
        foreach (var (source, _) in RoundOnceCardlessRules(actor, CardKind.Slash).Where(entry => RoundOnceCardlessProgramTargetLegal(actor, r.TargetSeat, entry.Source, true)))
        {
            var p = new Dictionary<string,string> { ["program-action"] = "wu-jing", ["kind"] = "round-once" }; AddConversionParameters(p, source);
            result.Add(new(new($"wu-jing.{f.Id}.round-once.{source.SkillInstanceId}.{source.BindingId}"), "支付独立回合额度，视为对原目标使用杀", [], [r.TargetSeat], p));
        }
        if (GetFactionResponsePolicy(actor, CardKind.Slash) is not null && WuJingFactionTarget(f))
            result.Add(new(new($"wu-jing.{f.Id}.faction"), "请求同势力角色提供杀", [], [r.TargetSeat], new Dictionary<string,string> { ["program-action"] = "wu-jing", ["kind"] = "faction" }));
        result.Add(new(new($"wu-jing.{f.Id}.decline"), "不使用牌", [], [], new Dictionary<string,string> { ["program-action"] = "wu-jing", ["kind"] = "decline" }));
        return Array.AsReadOnly(result.DistinctBy(c => c.Id).ToArray());
    }
    private void PublishWuJing(ProgramSkillFrame f, bool keepCurrentRequest = false)
    {
        var choices = WuJingChoices(f); if (choices.Count == 0 || !keepCurrentRequest && choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("kind") == "decline") && !HasWuJingZeroSupply(f)) { FinishWuJing(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "合击：对原牌的唯一目标使用一张杀或决斗。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [f.WuJing!.TargetSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wu-jing")]
    private void AnswerWuJing(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuJing: { Stage: WuJingStage.UseChoice } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], WuJingChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Heji lost its exact private original-actor card choices.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["kind"] == "decline") { FinishWuJing(f, false); return; }
        if (choice.Parameters["kind"] == "round-once") { BeginWuJingRoundOnce(f, RequireConversionSource(choice)); return; }
        if (choice.Parameters["kind"] == "faction") { BeginWuJingFaction(f); return; }
        var kind = Enum.Parse<CardKind>(choice.Parameters["kind"]); TryReadConversionSource(choice.Parameters, out var source);
        var zhangba = choice.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString();
        var cards = choice.Cards.Count == 1 ? new[] { GetPlayableCards(_players[f.OwnerSeat]).Concat(GetEquipment(f.OwnerSeat)).Single(c => c.Id == choice.Cards[0]) } :
            zhangba ? GetZhangbaSlashPairs(_players[f.OwnerSeat]).Single(p => p.Select(c => c.Id).SequenceEqual(choice.Cards)).ToArray() :
            FindProgramMultiCardViewAsSelection(_players[f.OwnerSeat], choice.Cards, kind, false, source!)?.Cards.ToArray() ?? throw new InvalidOperationException("Heji lost its real multi-material conversion.");
        var selection = new WuJingUseSelection(kind, zhangba, kind == CardKind.FireSlash && cards[0].Kind == CardKind.Slash && HasZhuqueFan(_players[f.OwnerSeat])) { CardIds = choice.Cards, Conversions = source is null ? [] : [source] };
        ReplaceRuntimeTop(f = f with { WuJing = r with { Stage = WuJingStage.IssuingUse, Selection = selection, Before = _movementSequence, After = _movementSequence } });
        _selectedUseConversion = source; _hasSelectedUseConversionChoice = true;
        if (IsSlashCard(kind)) ResolveSlashCore(_players[f.OwnerSeat], _players[r.TargetSeat], cards[0], kind, f.OwnerSeat, physicalCards: cards, countsTowardSlashLimit: false, usesZhuqueFan: selection.UsesFan, conversionSource: source, programSkillCardUseFrameId: f.Id);
        else
        {
            var id = BeginCardUse(cards[0], f.OwnerSeat, [r.TargetSeat], CardKind.Duel, physicalCardIds: cards.Select(c => c.Id).ToArray(), conversionSource: source);
            PaySlashUseMaterials(cards, f.OwnerSeat, null); CompleteWuJingHejiPayment(id);
            BeginJizhiOrNullificationWindow(id, cards[0], f.OwnerSeat, [r.TargetSeat], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
        }
    }
    private bool DrainWuJing(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueWuJing(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuJing: { } r } f || f.Id != id) return;
        AssertWuJing(f); if (r.Stage is WuJingStage.IssuingUse or WuJingStage.NativeUse or WuJingStage.FactionRequest) return;
        if (r.Stage == WuJingStage.UseChoice) { if (_pendingDecision is null) PublishWuJing(f); return; }
        if (!DrainWuJing(f)) FinishWuJing(GetActiveProgramFrame(id), true);
    }
    private void FinishWuJing(ProgramSkillFrame f, bool applied) { AdvanceEventRulesAndQueueFact(new WuJingCompletedEvent(f.Id, applied)); ReplaceRuntimeTop(f = f with { WuJing = null, PendingMovementContinuation = null }); FinishProgramSkill(f, applied); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27130)] private bool ResumeWuJing(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuJing: not null } f || f.Id != id) return false; ContinueWuJing(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27130)] private bool ReturnWuJingMovement(ProgramSkillFrame f) { if (f.WuJing is null) return false; ContinueWuJing(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27130)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27130)]
    private bool ContinuePaidWuJing(ProgramSkillFrame f) => f.WuJing is { } r && (r.Stage != WuJingStage.UseChoice || WuJingHasPaidAdapter(f)) && ProgramEventHistory<WuJingStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash) == 1;
    [ProgramAiChoice(SkillProgramEffectOp.UseHejiAtCompletedSingleTarget, SkillProgramEffectOp.ClaimLiubingUndamagingSlash, SkillProgramEffectOp.RewardHejiUnconvertedUse)]
    private PromptChoice AiWuJing(PendingDecision p, ProgramSkillFrame f) => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[f.WuJing!.TargetSeat]) ? p.Choices.Single(c => c.Parameters.GetValueOrDefault("kind") == "decline") : p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("kind") != "decline") ?? p.Choices.First();
}
