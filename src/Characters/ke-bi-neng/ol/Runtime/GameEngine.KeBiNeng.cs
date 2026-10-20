using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string PingduanRewardReason = "skill-program.ke-bi-neng.pingduan.reward", PingduanEquipmentReason = "skill-program.ke-bi-neng.pingduan.equipment";
    private bool HasKeBiNengCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment);
    private void ExpireKeBiNengPingduanAlcohol(int actualTurn, int turnOwner)
    {
        if (!HasKeBiNengCapability) return;
        var nativeIds = CompleteProgramEventHistory().OfType<KeBiNengPingduanNativeIssuedEvent>().Where(e => e.Origin.ActualTurn == actualTurn &&
            e.Origin.Source.OwnerSeat == turnOwner && e.Origin.CardKind == CardKind.Alcohol).Select(e => e.Origin.CardUseFrameId).ToHashSet();
        foreach (var seat in CompleteProgramEventHistory().OfType<AlcoholAppliedEvent>().Where(e => nativeIds.Contains(e.ResolutionId)).Select(e => e.SourceSeat).Distinct().ToArray())
            if (seat != turnOwner && IsValidPlayerSeat(seat) && _players[seat].HasAlcoholEffect)
            { _players[seat].HasAlcoholEffect = false; AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(seat)); }
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27040)]
    private void ObserveKeBiNengAlcoholExpiry(IGameEvent payload)
    { if (payload is TurnEndedEvent ended) ExpireKeBiNengPingduanAlcohol(ended.TurnNumber, ended.ActorSeat); }
    private static CardConversionSource KeBiNengSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ke_bi_neng_ol_KeBiNeng.KeBiNengSource(f);
    [ProgramCapability]
    private sealed class KeBiNengCapability(GameEngine engine) : IKeBiNengProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginKeBiNeng(frame); }
    private SkillProgramStepOutcome BeginKeBiNeng(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.KeBiNengPingduan is not null || f.SkillId != "ol:pingduan" || f.ActivationId != "ordered-three-items" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var actor] || !IsValidPlayerSeat(actor) || !_players[actor].IsAlive ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Pingduan requires its accepted actual Play activation and one original living recipient.");
        var r = new KeBiNengPingduanReceipt(KeBiNengSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _cardUseDebitPhaseInstanceId, actor);
        ReplaceRuntimeTop(f = f with { KeBiNengPingduan = r });
        AdvanceEventRulesAndQueueFact(new KeBiNengPingduanStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstance, actor));
        PrepareKeBiNengBasic(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private void PrepareKeBiNengBasic(ProgramSkillFrame f)
    {
        var actions = BuildKeBiNengPingduanBasicActions(f);
        ReplaceRuntimeTop(f = f with { KeBiNengPingduan = f.KeBiNengPingduan! with { BasicActions = actions } });
        var actor = f.KeBiNengPingduan!.ActorSeat;
        if (actions.Count == 0 && !HasTousuiProgramSlash(_players[actor], _players.Where(p => p.IsAlive && p.Seat != actor).Select(p => p.Seat)) && KenshangProgramSlashChoices(f, actor,
                _players.Where(p => p.IsAlive && p.Seat != actor).Select(p => p.Seat).ToArray()).Count == 0 && !HasWeijieGenericBasic(f) && !(HasZonghuGenericBasic(f) || HasHuanpeiGenericBasic(f)))
            FinishKeBiNeng(f, "no-legal-basic-use");
        else PublishKeBiNeng(f);
    }
    private IReadOnlyList<KeBiNengPingduanCard> KeBiNengRecastCards(ProgramSkillFrame f) => Array.AsReadOnly(GetHand(_players[f.KeBiNengPingduan!.ActorSeat]).Concat(GetEquipment(f.KeBiNengPingduan.ActorSeat))
        .Select(c => new KeBiNengPingduanCard(c.Id, _cardZones.GetLocation(c.Id), AdvancedEffectiveHandKind(_players[f.KeBiNengPingduan.ActorSeat], c)))
        .Where(c => CardCatalog.Get(c.EffectiveKind).CategoryName == "锦囊牌" && !IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, GetAttackCard(c.CardId))).OrderBy(c => c.CardId).ToArray());
    private IReadOnlyList<KeBiNengPingduanCard> KeBiNengEquipmentCards(ProgramSkillFrame f) => Array.AsReadOnly(GetEquipment(f.KeBiNengPingduan!.ActorSeat)
        .Where(c => !IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, c))
        .OrderBy(c => c.Id).Select(c => new KeBiNengPingduanCard(c.Id, _cardZones.GetLocation(c.Id), c.Kind)).ToArray());
    private static Dictionary<string, string> KeBiNengArgs(string option) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ke_bi_neng_ol_KeBiNeng.KeBiNengArgs(option);
    private static IReadOnlyList<int> KeBiNengMaterials(LegalAction a) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ke_bi_neng_ol_KeBiNeng.KeBiNengMaterials(a);
    private IReadOnlyList<PromptChoice> KeBiNengChoices(ProgramSkillFrame f)
    {
        var r = f.KeBiNengPingduan!; var choices = new List<PromptChoice>();
        if (r.Stage == KeBiNengPingduanStage.BasicChoice)
        {
            choices.AddRange(r.BasicActions.Select((a, i) => new PromptChoice(new($"pingduan.{f.Id}.basic.{i}"), a.Description, KeBiNengMaterials(a), a.TargetSeats, KeBiNengArgs(i.ToString(CultureInfo.InvariantCulture)))));
            choices.AddRange(KenshangProgramSlashChoices(f, r.ActorSeat, _players.Where(p => p.IsAlive && p.Seat != r.ActorSeat).Select(p => p.Seat).ToArray()));
            choices.AddRange(TousuiProgramSlashChoices(f, r.ActorSeat, _players.Where(p => p.IsAlive && p.Seat != r.ActorSeat).Select(p => p.Seat).ToArray()));
        }
        else if (r.Stage is KeBiNengPingduanStage.RecastChoice or KeBiNengPingduanStage.EquipmentChoice)
            choices.AddRange(r.Cards.Select(c => new PromptChoice(new($"pingduan.{f.Id}.{r.Stage}.{c.CardId}"), (r.Stage == KeBiNengPingduanStage.RecastChoice ? "重铸" : "交付") + "【" + CardCatalog.Get(c.EffectiveKind).DisplayName + "】", [c.CardId], [], KeBiNengArgs(c.CardId.ToString(CultureInfo.InvariantCulture)))));
        else throw new InvalidOperationException("Pingduan can publish only its current uncommitted item.");
        choices.Add(new(new($"pingduan.{f.Id}.{r.Stage}.decline"), "不执行此项，结束平端", [], [], KeBiNengArgs("decline")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishKeBiNeng(ProgramSkillFrame f)
    {
        AssertKeBiNeng(f); var r = f.KeBiNengPingduan!; var choices = KeBiNengChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.ActorSeat, r.Stage switch { KeBiNengPingduanStage.BasicChoice => "平端：使用一张合法基本牌。", KeBiNengPingduanStage.RecastChoice => "平端：重铸一张锦囊牌。", _ => "平端：可以令轲比能获得你装备区的一张牌。" },
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), TargetSeat = r.ActorSeat, IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[r.ActorSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ke-bi-neng-pingduan")]
    private void AnswerKeBiNeng(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { KeBiNengPingduan: { } r } f || _pendingDecision?.PlayerSeat != r.ActorSeat ||
            !AssistedChoicesEqual([choice], KeBiNengChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Pingduan requires its original recipient's exact published private choice.");
        AssertKeBiNeng(f); var option = choice.Parameters["option"];
        if (option == "decline") { ClearPendingDecision(); FinishKeBiNeng(f, "recipient-declined"); return; }
        if (r.Stage == KeBiNengPingduanStage.BasicChoice)
        {
            if (!int.TryParse(option, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= r.BasicActions.Count) throw new InvalidOperationException("Pingduan changed its frozen basic option index.");
            var selected = r.BasicActions[index];
            if (!BuildKeBiNengPingduanBasicActions(f).Any(a => SameKeBiNengAction(a, selected))) throw new InvalidOperationException("Pingduan basic use is no longer natively legal.");
            ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, KeBiNengPingduan = r with { Stage = KeBiNengPingduanStage.BasicIssuing, SelectedBasicAction = selected } });
            BeginKeBiNengPingduanBasicUse(f, selected); return;
        }
        ClearPendingDecision(); var card = r.Cards.Single(c => c.CardId == int.Parse(option, CultureInfo.InvariantCulture));
        if (r.Stage == KeBiNengPingduanStage.RecastChoice && !KeBiNengRecastCards(f).Contains(card) || r.Stage == KeBiNengPingduanStage.EquipmentChoice && !KeBiNengEquipmentCards(f).Contains(card))
            throw new InvalidOperationException("Pingduan changed its frozen native recast or equipment source entity.");
        PayKeBiNengCard(f, card, r.Stage == KeBiNengPingduanStage.RecastChoice); ContinueKeBiNeng(f.Id);
    }
    private void PayKeBiNengCard(ProgramSkillFrame f, KeBiNengPingduanCard card, bool recast)
    {
        var r = f.KeBiNengPingduan!;
        if (recast) f = BeginFrameNativeRecastInvocation(f, r.ActorSeat, [card.CardId]);
        var grain = card.From.Zone == CardZoneKind.Equipment && GetAttackCard(card.CardId).Kind == CardKind.WoodenOx ? GetWoodenOxGrain(_players[r.ActorSeat]).Select(c => c.Id).ToArray() : [];
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, KeBiNengPingduan = r with { SelectedCard = card, Cards = [], Stage = recast ? KeBiNengPingduanStage.RecastChildren : KeBiNengPingduanStage.EquipmentChildren,
            Before = _movementSequence, After = _movementSequence, WoodenOxGrainIds = grain } });
        MoveCard(GetAttackCard(card.CardId), card.From, recast ? CardLocation.DiscardPile : CardLocation.Hand(f.OwnerSeat), recast ? CardMoveReasons.RecastDiscard : new(PingduanEquipmentReason), movement =>
        {
            var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current = current with { KeBiNengPingduan = current.KeBiNengPingduan! with { After = movement.Sequence, CompletedItems = recast ? 2 : 3, PaidBatchId = _activeCardMovementBatchIds.Peek() } });
            AdvanceEventRulesAndQueueFact(new KeBiNengPingduanItemPaidEvent(f.Id, recast ? 2 : 3, r.ActorSeat, card.CardId, card.From, current.KeBiNengPingduan!.Before, movement.Sequence));
        });
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { KeBiNengPingduan = f.KeBiNengPingduan! with { After = _movementSequence } });
    }
    private void PayKeBiNengDraw(ProgramSkillFrame f, int item, bool recast)
    {
        var r = f.KeBiNengPingduan!; var stage = recast ? KeBiNengPingduanStage.RecastDrawChildren : item switch { 1 => KeBiNengPingduanStage.BasicRewardChildren, 2 => KeBiNengPingduanStage.RecastRewardChildren, _ => KeBiNengPingduanStage.EquipmentRewardChildren };
        ReplaceRuntimeTop(f = f with { KeBiNengPingduan = r with { Stage = stage, Before = _movementSequence, After = _movementSequence, WoodenOxGrainIds = [] } });
        var drawn = _winner == Winner.None && _players[r.ActorSeat].IsAlive ? DrawCards(_players[r.ActorSeat], 1, true, recast ? CardMoveReasons.RecastDraw : new(PingduanRewardReason)) : [];
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { KeBiNengPingduan = f.KeBiNengPingduan! with { DrawCount = drawn.Count, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new KeBiNengPingduanDrawPaidEvent(f.Id, item, recast, drawn.Count, f.KeBiNengPingduan!.Before, f.KeBiNengPingduan.After));
        if (recast) { AdvanceEventRulesAndQueueFact(new CardRecastEvent(r.ActorSeat, r.SelectedCard!.CardId, r.SelectedCard.EffectiveKind, drawn.Count)); CompleteFrameNativeRecastInvocation(GetActiveProgramFrame(f.Id), drawn.Count); }
    }
    private bool DrainKeBiNeng(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueKeBiNeng(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { KeBiNengPingduan: { } r } f && f.Id == id)
        {
            AssertKeBiNeng(f);
            if (r.Stage is KeBiNengPingduanStage.BasicChoice or KeBiNengPingduanStage.RecastChoice or KeBiNengPingduanStage.EquipmentChoice) { if (_pendingDecision is null) PublishKeBiNeng(f); return; }
            if (r.Stage is KeBiNengPingduanStage.BasicIssuing or KeBiNengPingduanStage.BasicNative) return;
            if (DrainKeBiNeng(id)) return;
            f = GetActiveProgramFrame(id); r = f.KeBiNengPingduan!;
            if (_winner != Winner.None || !_players[r.ActorSeat].IsAlive) { FinishKeBiNeng(f, "recipient-dead-or-game-ended"); return; }
            if (r.Stage == KeBiNengPingduanStage.BasicReturned) { PayKeBiNengDraw(f, 1, false); continue; }
            if (r.Stage == KeBiNengPingduanStage.RecastChildren) { PayKeBiNengDraw(f, 2, true); continue; }
            if (r.Stage == KeBiNengPingduanStage.RecastDrawChildren) { PayKeBiNengDraw(f, 2, false); continue; }
            if (r.Stage == KeBiNengPingduanStage.EquipmentChildren) { PayKeBiNengDraw(f, 3, false); continue; }
            if (r.Stage == KeBiNengPingduanStage.EquipmentRewardChildren) { FinishKeBiNeng(f, "three-items-completed"); return; }
            var recast = r.Stage == KeBiNengPingduanStage.BasicRewardChildren;
            if (!recast && !_players[f.OwnerSeat].IsAlive) { FinishKeBiNeng(f, "equipment-recipient-dead"); return; }
            var cards = recast ? KeBiNengRecastCards(f) : KeBiNengEquipmentCards(f);
            if (cards.Count == 0) { FinishKeBiNeng(f, recast ? "no-trick-to-recast" : "no-equipment-to-give"); return; }
            ReplaceRuntimeTop(f = f with { KeBiNengPingduan = r with { Stage = recast ? KeBiNengPingduanStage.RecastChoice : KeBiNengPingduanStage.EquipmentChoice, Cards = cards } }); PublishKeBiNeng(f); return;
        }
    }
    private void FinishKeBiNeng(ProgramSkillFrame f, string reason)
    { AdvanceEventRulesAndQueueFact(new KeBiNengPingduanCompletedEvent(f.Id, f.KeBiNengPingduan!.CompletedItems, reason)); ReplaceRuntimeTop(f = f with { KeBiNengPingduan = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27040)]
    private bool ResumeKeBiNeng(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { KeBiNengPingduan: not null } f || f.Id != id) return false; ContinueKeBiNeng(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27040)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27040)]
    private bool ContinuePaidKeBiNeng(ProgramSkillFrame f) => f.KeBiNengPingduan is not null && ValidKeBiNeng(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment)]
    private PromptChoice ChooseKeBiNengAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.KeBiNengPingduan!; var executable = p.Choices.Where(c => c.Parameters.GetValueOrDefault("option") != "decline").ToArray();
        if (executable.Length == 0) return p.Choices.Last();
        var view = CreateSnapshot(r.ActorSeat);
        double Hostility(int seat) => _aiBrains[r.ActorSeat].ScoreProgramTarget(view, seat, new(0, 0, 0, 0, 0, 1, false, false));
        if (r.Stage == KeBiNengPingduanStage.EquipmentChoice && f.OwnerSeat != r.ActorSeat && Hostility(f.OwnerSeat) > 1.5 &&
            !executable.Any(c => GetAttackCard(c.Cards.Single()).Kind == CardKind.SilverLion && _players[r.ActorSeat].Hp < _players[r.ActorSeat].MaxHp)) return p.Choices.Last();
        if (r.Stage == KeBiNengPingduanStage.BasicChoice)
            return executable.OrderByDescending(c => c.Parameters.TryGetValue("option", out var basicIndex) && r.BasicActions[int.Parse(basicIndex, CultureInfo.InvariantCulture)].Kind == LegalActionKind.Peach ? 5 : c.Targets.Any(t => t != r.ActorSeat && Hostility(t) > 1.5) ? 3 : 1).First();
        return executable.OrderBy(c => GetKeepValue(GetAttackCard(c.Cards.Single()), _players[r.ActorSeat])).First();
    }
}
