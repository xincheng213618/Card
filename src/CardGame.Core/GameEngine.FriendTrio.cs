using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<(int Seat, string Instance), FriendPredictionPolicy> _friendPredictions = [];
    private readonly Dictionary<(int Seat, string Instance), int> _friendPredictionCounts = [];
    private readonly HashSet<(int Seat, string Instance)> _friendFangqiuSpent = [];
    private readonly HashSet<int> _friendQihuiUnlimited = [];
    private IReadOnlyList<FriendPredictionView>? GetFriendPredictionViews(int? viewerSeat)
    {
        var values = _friendPredictions.Values.Where(p => p.Index < p.Predictions.Count && (p.Revealed || viewerSeat == p.OwnerSeat))
            .OrderBy(p => p.OwnerSeat).ThenBy(p => p.PredictionId).Select(p => new FriendPredictionView(p.OwnerSeat, p.PredictionId, p.Mode,
                p.Predictions.Select(FriendValueLabel).ToArray(), p.Index, p.Correct, p.Revealed)).ToArray();
        return values.Length == 0 ? null : Array.AsReadOnly(values);
    }
    private static readonly PlayerMarkerKind[] FriendCategoryMarkers = [PlayerMarkerKind.QihuiBasic, PlayerMarkerKind.QihuiTrick, PlayerMarkerKind.QihuiEquipment];
    private bool HasFriendTrioAlly(CharacterState owner, string id) => _modeDefinition.ModeKind == ContentModeKind.Team && owner.TeamId is { } team &&
        _players.Any(p => p.IsAlive && p.Seat != owner.Seat && p.TeamId == team && (p.General.Id == "friend:" + id || p.SecondaryGeneral?.Id == "friend:" + id));
    private bool ValidWholeHandSuitConversion(CharacterState owner, IReadOnlyList<Card> cards, CardConversionSource source)
    {
        if (cards.Count == 0 || cards.Any(c => _cardZones.GetLocation(c.Id) != CardLocation.Hand(owner.Seat))) return false;
        if (source.SkillId == "equipment:xuanjian" && HasRuntimeSkill(owner, "friend:gongli-xu-shu") && HasFriendTrioAlly(owner, "zhuge-liang")) return cards.Count == 1;
        var suit = EffectiveSuit(owner, cards[0]);
        return cards.All(c => EffectiveSuit(owner, c) == suit) && cards.Select(c => c.Id).Order().SequenceEqual(
            GetHand(owner).Where(c => EffectiveSuit(owner, c) == suit).Select(c => c.Id).Order());
    }
    private bool FriendXuanjianIgnoresDistance(CharacterState owner, CardConversionSource? source) => source?.SkillId == "equipment:xuanjian" &&
        HasRuntimeSkill(owner, "friend:gongli-xu-shu") && HasFriendTrioAlly(owner, "pang-tong");
    private static bool IsFriendTrioOperation(SkillProgramEffectOp op) => op is >= SkillProgramEffectOp.BeginFriendPrediction and <= SkillProgramEffectOp.GainFriendCategoryMark;
    private PlayerMarkerKind FriendCategoryMarker(CardKind kind) => EquipmentCatalog.IsEquipment(kind) ? PlayerMarkerKind.QihuiEquipment :
        MatchesProgramCardCategory(kind, [SkillProgramCardCategory.Basic]) ? PlayerMarkerKind.QihuiBasic : PlayerMarkerKind.QihuiTrick;
    private bool IsFriendActualCardUse(CardActionContext? action) => action is not null &&
        (action.Type == CardActionType.Use || action.Type == CardActionType.Response && action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null &&
         (action.EffectiveKind == CardKind.Nullification || IsProgramResponseCardUse(_players[action.ActorSeat], action.EffectiveKind)));
    private CardActionContext? FriendCardAction(ProgramSkillWindowContext context) => context.CardUse is { } use
        ? _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId)?.Action
          ?? CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().LastOrDefault(e => e.Action.ActionId == use.CardActionId)?.Action
        : null;
    private void BeginFriendXuanjianUse(ProgramSkillFrame f)
    {
        var card = GetAttackCard(f.FriendTrio!.Cards.Single()); var owner = _players[f.OwnerSeat];
        if (_cardZones.GetLocation(card.Id) != CardLocation.Hand(f.OwnerSeat) || owner.EquipmentSlotCapacity(EquipmentSlot.Weapon) == 0 ||
            IsCardUseForbidden(owner.Seat, card.Kind, CardActionType.Use) || IsSelfTargetForbiddenAction(owner, card.Kind, [owner.Seat]))
        { FinishFriendTrio(f); return; }
        ReplaceRuntimeTop(f = f with { FriendTrio = f.FriendTrio! with { Stage = FriendTrioStage.EquipmentUse, Before = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var id = BeginCardUse(card, f.OwnerSeat, [], conversionSource: source);
        var action = _resolutionStack.OfType<CardUseFrame>().Single(u => u.Id == id).Action!;
        ReplaceRuntimeFrame(f.Id, f = f with { FriendTrio = f.FriendTrio! with { IssuedUseFrameId = id, IssuedActionId = action.ActionId } });
        AdvanceEventRulesAndQueueFact(new FriendXuanjianUseIssuedEvent(f.Id, id, action.ActionId, card.Id));
        MoveCard(card, CardLocation.Hand(f.OwnerSeat), CardLocation.Processing, CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(owner, card, id)) CompleteEquipmentUse(owner, card, id);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 660)]
    private bool CanOfferFriendTrio(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var e] || !IsFriendTrioOperation(e.Op)) return true;
        var p = _players[candidate.OwnerSeat];
        return e.Op switch
        {
            SkillProgramEffectOp.BeginFriendPrediction => context.Window != SkillProgramTriggerWindow.RoundStarting || _roundNumber == 1,
            SkillProgramEffectOp.ObserveFriendPrediction => IsFriendActualCardUse(FriendCardAction(context)) &&
                _friendPredictions.TryGetValue((p.Seat, candidate.SkillInstanceId), out var policy) && policy.Index < policy.Predictions.Count &&
                !CompleteProgramEventHistory().OfType<FriendPredictionObservedEvent>().Any(x => x.PredictionId == policy.PredictionId && x.ActionId == FriendCardAction(context)!.ActionId),
            SkillProgramEffectOp.ReclaimFriendXuanjian => FriendCategoryMarkers.Sum(m => p.Markers.GetValueOrDefault(m)) >= 2 && context.MovementBatch is { } b &&
                b.Movements.Any(m => m.To == CardLocation.DiscardPile && GetAttackCard(m.CardId).Kind == CardKind.Xuanjian && _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile),
            SkillProgramEffectOp.GainFriendCategoryMark => IsFriendActualCardUse(FriendCardAction(context)) && !p.Markers.ContainsKey(FriendCategoryMarker(FriendCardAction(context)!.EffectiveKind)),
            _ => true
        };
    }
    private SkillProgramStepOutcome ExecuteFriendTrio(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.FriendTrio is not null || f.WindowContext is not { } c ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Friend skill lost its owning instruction and native opportunity.");
        if (op == SkillProgramEffectOp.ExchangeFriendGainForDiscard) return BeginFriendManjuan(f);
        if (op == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd) return BeginFriendYangming(f);
        var r = new FriendTrioReceipt(f.InstructionIndex, op, FriendTrioStage.Branch, [], []);
        ReplaceRuntimeTop(f = f with { FriendTrio = r });
        if (op == SkillProgramEffectOp.BeginFriendPrediction) { PublishFriendTrioChoice(f); return SkillProgramStepOutcome.AwaitChoice; }
        if (op == SkillProgramEffectOp.ObserveFriendPrediction)
        {
            if (!IsFriendActualCardUse(FriendCardAction(c)) || !_friendPredictions.TryGetValue((f.OwnerSeat, f.SkillInstanceId), out var p))
                throw new InvalidOperationException("Prediction lost its issued private policy.");
            var action = FriendCardAction(c)!;
            var actual = p.Mode == "color" ? action.EffectiveIsRed == true ? "red" : action.EffectiveIsRed == false ? "black" : "none" : FriendCategoryValue(action.EffectiveKind);
            var correct = p.Predictions[p.Index] == actual || p.Index == 0 && HasRuntimeSkill(_players[f.OwnerSeat], "friend:gongli-zhuge-liang") && HasFriendTrioAlly(_players[f.OwnerSeat], "xu-shu");
            var draw = correct && p.Drawn < 5;
            var updated = p with { Index = p.Index + 1, Correct = p.Correct + (correct ? 1 : 0), Drawn = p.Drawn + (draw ? 1 : 0) };
            _friendPredictions[(f.OwnerSeat, f.SkillInstanceId)] = updated;
            ReplaceRuntimeTop(f = f with { FriendTrio = r with { Policy = updated, FullVerification = updated.Index == updated.Predictions.Count } });
            AdvanceEventRulesAndQueueFact(new FriendPredictionObservedEvent(p.PredictionId, action.ActionId, p.Index, correct, updated.Correct));
            var next = updated.Index == updated.Predictions.Count ? FriendTrioStage.Settle : FriendTrioStage.Complete;
            if (draw) FriendTrioMutation(f, next, () => DrawCards(_players[f.OwnerSeat], 1, true, new("friend.yance.correct")));
            else ContinueFriendTrio(f with { FriendTrio = f.FriendTrio! with { Stage = next } });
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.EquipFriendXuanjian)
        {
            if (c.Window != SkillProgramTriggerWindow.GameStarting) throw new InvalidOperationException("Xuanjian requires the game-start boundary.");
            var card = new Card(CreateCardZoneDiagnostics().Select(x => x.CardId).DefaultIfEmpty(0).Max() + 1, CardKind.Xuanjian, Suit.Spade, 9)
            { GrantedSkillIds = Array.AsReadOnly(new[] { "equipment:xuanjian" }) };
            _cardZones.AddGeneratedCard(card); _generatedPhysicalCardIds.Add(card.Id);
            ReplaceRuntimeTop(f = f with { FriendTrio = r with { Cards = [card.Id] } });
            AdvanceEventRulesAndQueueFact(new FriendXuanjianGeneratedEvent(f.Id, f.OwnerSeat, card.Id));
            // Obtain and use are separate movements; the real equipment use enters the committed-use window.
            FriendTrioMutation(f, FriendTrioStage.FullReward, () => MoveCard(card, CardLocation.OutsideGame, CardLocation.Hand(f.OwnerSeat), new("friend.xiaxing.obtain")));
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.ReclaimFriendXuanjian)
        {
            var id = c.MovementBatch!.Movements.Where(m => m.To == CardLocation.DiscardPile && GetAttackCard(m.CardId).Kind == CardKind.Xuanjian)
                .Select(m => m.CardId).FirstOrDefault(id => _cardZones.GetLocation(id) == CardLocation.DiscardPile);
            if (id == 0) { FinishFriendTrio(f); return SkillProgramStepOutcome.AwaitChild; }
            ReplaceRuntimeTop(f = f with { FriendTrio = r with { Stage = FriendTrioStage.RemoveMarks, Cards = [id], Count = 2 } });
            PublishFriendTrioChoice(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        if (op == SkillProgramEffectOp.GainFriendCategoryMark)
        {
            if (!IsFriendActualCardUse(FriendCardAction(c)) || FriendCardAction(c)!.ActorSeat != f.OwnerSeat) throw new InvalidOperationException("Qihui requires an actual own use.");
            var marker = FriendCategoryMarker(FriendCardAction(c)!.EffectiveKind);
            SetStrategicMarker(f.OwnerSeat, marker, f.OwnerSeat, 1, f);
            if (FriendCategoryMarkers.Sum(m => _players[f.OwnerSeat].Markers.GetValueOrDefault(m)) < 3)
            { FinishFriendTrio(f); return SkillProgramStepOutcome.AwaitChild; }
            ReplaceRuntimeTop(f = f with { FriendTrio = r with { Stage = FriendTrioStage.RemoveMarks, Count = 2 } });
            PublishFriendTrioChoice(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        throw new InvalidOperationException("Unknown friend skill operation.");
    }
    private string FriendCategoryValue(CardKind kind) => FriendCategoryMarker(kind) switch
    { PlayerMarkerKind.QihuiBasic => "basic", PlayerMarkerKind.QihuiEquipment => "equipment", _ => "trick" };
    private static string FriendValueLabel(string v) => v switch
    { "basic" => "基本牌", "trick" => "锦囊牌", "equipment" => "装备牌", "red" => "红色", "black" => "黑色", _ => v };
    private int FriendPredictionCount(ProgramSkillFrame f) => Math.Clamp(_friendPredictionCounts.GetValueOrDefault((f.OwnerSeat, f.SkillInstanceId), 3) +
        (HasRuntimeSkill(_players[f.OwnerSeat], "friend:gongli-zhuge-liang") && HasFriendTrioAlly(_players[f.OwnerSeat], "pang-tong") ? 1 : 0), 1, 7);
    private IReadOnlyList<PromptChoice> FriendTrioChoices(ProgramSkillFrame f)
    {
        var r = f.FriendTrio!; var result = new List<PromptChoice>();
        void Add(string value, string label, int[]? cards = null) => result.Add(PromptChoiceProjection.Freeze(new(new($"friend-trio.{f.Id}.{r.Stage}.{r.Values.Count}.{value}"), label, cards ?? [], [],
            new Dictionary<string,string> { ["program-action"] = "friend-trio", ["value"] = value })));
        switch (r.Stage)
        {
            case FriendTrioStage.Branch: Add("trick", "随机获得一张锦囊牌"); Add("predict", "执行卧龙演策"); break;
            case FriendTrioStage.Mode: Add("category", "预测牌的类别"); Add("color", "预测牌的颜色"); break;
            case FriendTrioStage.Prediction:
                foreach (var v in r.Mode == "color" ? new[] { "red", "black" } : new[] { "basic", "trick", "equipment" })
                    Add(v, $"第 {r.Values.Count + 1}/{r.Count} 张：{FriendValueLabel(v)}"); break;
            case FriendTrioStage.Reveal:
                Add("hidden", "保留秘密预测");
                if (HasRuntimeSkill(_players[f.OwnerSeat], "friend:fangqiu") && !_friendFangqiuSpent.Contains((f.OwnerSeat, GetRuntimeSkillInstanceId(_players[f.OwnerSeat], "friend:fangqiu"))))
                    Add("reveal", "发动方遒，公开预测"); break;
            case FriendTrioStage.Reward:
                foreach (var v in r.Policy!.Mode == "color" ? new[] { "red", "black" } : new[] { "basic", "trick", "equipment" }) Add(v, "获得" + FriendValueLabel(v)); break;
            case FriendTrioStage.Discard:
                foreach (var choice in BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard))
                    if (choice.Cards.Count == 1 && !r.Cards.Contains(choice.Cards[0])) Add(choice.Cards[0].ToString(CultureInfo.InvariantCulture), choice.Label, [choice.Cards[0]]); break;
            case FriendTrioStage.RemoveMarks:
                foreach (var m in FriendCategoryMarkers.Where(m => _players[f.OwnerSeat].Markers.GetValueOrDefault(m) > 0)) Add(m.ToString(), "移除" + PlayerMarkerCatalog.GetDisplayName(m)); break;
            case FriendTrioStage.Benefit:
                if (_players[f.OwnerSeat].Hp < _players[f.OwnerSeat].MaxHp) Add("recover", "回复1点体力");
                Add("draw", "摸两张牌"); Add("unlimited", "下一张使用的牌不计次数且无次数限制"); break;
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishFriendTrioChoice(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f); var choices = FriendTrioChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) { FinishFriendTrio(f); return; }
        var text = f.FriendTrio!.Stage switch
        {
            FriendTrioStage.Prediction => "依次预测接下来使用的牌。",
            FriendTrioStage.Reveal => "是否发动方遒公开预测？",
            FriendTrioStage.RemoveMarks => $"还需移除 {f.FriendTrio.Count} 个启诲标记。",
            FriendTrioStage.Discard => $"弃置 {f.FriendTrio.Count} 张牌，已选 {f.FriendTrio.Cards.Count} 张。",
            _ => skill.Name + "：请选择。"
        };
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("friend-trio")]
    private void ResolveFriendTrioChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertFriendTrio(f);
        var r = f.FriendTrio!;
        if (!AssistedChoicesEqual([choice], FriendTrioChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Friend choice changed its frozen draft.");
        ClearPendingDecision(); var value = choice.Parameters["value"];
        switch (r.Stage)
        {
            case FriendTrioStage.Branch:
                if (value == "trick")
                {
                    FriendTrioMutation(f, FriendTrioStage.Complete, () => GainFriendMatchingDeckCards(f, "category", "trick", 1)); return;
                }
                if (_friendPredictions.TryGetValue((f.OwnerSeat, f.SkillInstanceId), out var old))
                { ContinueFriendTrio(f with { FriendTrio = r with { Stage = FriendTrioStage.Settle, Policy = old, StartNewPrediction = true } }); return; }
                PublishFriendTrioChoice(f with { FriendTrio = r with { Stage = FriendTrioStage.Mode, Count = FriendPredictionCount(f) } }); return;
            case FriendTrioStage.Mode:
                PublishFriendTrioChoice(f with { FriendTrio = r with { Stage = FriendTrioStage.Prediction, Mode = value } }); return;
            case FriendTrioStage.Prediction:
                var values = r.Values.Append(value).ToArray();
                PublishFriendTrioChoice(f with { FriendTrio = r with { Values = values, Stage = values.Length == r.Count ? FriendTrioStage.Reveal : FriendTrioStage.Prediction } }); return;
            case FriendTrioStage.Reveal:
                var policy = new FriendPredictionPolicy(f.Id, f.OwnerSeat, f.SkillInstanceId, f.GameplayHash, r.Mode!, r.Values, Revealed: value == "reveal");
                _friendPredictions[(f.OwnerSeat, f.SkillInstanceId)] = policy;
                AdvanceEventRulesAndQueueFact(new FriendPredictionStartedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, r.Count));
                if (policy.Revealed)
                {
                    _friendFangqiuSpent.Add((f.OwnerSeat, GetRuntimeSkillInstanceId(_players[f.OwnerSeat], "friend:fangqiu")));
                    AdvanceEventRulesAndQueueFact(new FriendPredictionRevealedEvent(f.Id, f.OwnerSeat, Array.AsReadOnly(r.Values.Select(FriendValueLabel).ToArray())));
                    AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【方遒】：" + string.Join("、", r.Values.Select(FriendValueLabel)), f.OwnerSeat);
                }
                FinishFriendTrio(f); return;
            case FriendTrioStage.Reward:
                FriendTrioMutation(f, FriendTrioStage.FullReward, () => GainFriendMatchingDeckCards(f, r.Policy!.Mode, value, 1 + (r.FullVerification && r.Policy.Revealed ? 1 : 0))); return;
            case FriendTrioStage.Discard:
                var cards = r.Cards.Append(choice.Cards.Single()).ToArray();
                f = f with { FriendTrio = r with { Cards = cards } };
                if (cards.Length < r.Count && FriendTrioChoices(f).Count > 0) { PublishFriendTrioChoice(f); return; }
                FriendTrioMutation(f, FriendTrioStage.Complete, () => MoveProgramLogicalDiscard(f, f.OwnerSeat, cards, new("friend.yance.discard"))); return;
            case FriendTrioStage.RemoveMarks:
                var marker = Enum.Parse<PlayerMarkerKind>(value); SetStrategicMarker(f.OwnerSeat, marker, f.OwnerSeat, 0, f);
                if (r.Count > 1) { PublishFriendTrioChoice(f with { FriendTrio = r with { Count = r.Count - 1 } }); return; }
                if (r.Operation == SkillProgramEffectOp.ReclaimFriendXuanjian)
                { FriendTrioMutation(f, FriendTrioStage.Complete, () => { if (_cardZones.GetLocation(r.Cards.Single()) == CardLocation.DiscardPile) MoveCard(GetAttackCard(r.Cards.Single()), CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new("friend.xiaxing.reclaim")); }); return; }
                PublishFriendTrioChoice(f with { FriendTrio = r with { Stage = FriendTrioStage.Benefit } }); return;
            case FriendTrioStage.Benefit:
                if (value == "unlimited")
                { _friendQihuiUnlimited.Add(f.OwnerSeat); AdvanceEventRulesAndQueueFact(new FriendQihuiUnlimitedArmedEvent(f.Id, f.OwnerSeat)); FinishFriendTrio(f); return; }
                FriendTrioMutation(f, FriendTrioStage.Complete, () =>
                { if (value == "draw") DrawCards(_players[f.OwnerSeat], 2, true, new("friend.qihui.draw")); else new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null); }); return;
        }
        throw new InvalidOperationException("Friend skill choice has no matching stage.");
    }
    [ProgramAiChoice(
        SkillProgramEffectOp.BeginFriendPrediction,
        SkillProgramEffectOp.ObserveFriendPrediction,
        SkillProgramEffectOp.ReclaimFriendXuanjian,
        SkillProgramEffectOp.GainFriendCategoryMark)]
    private PromptChoice SelectAiFriendTrioChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var stage = frame.FriendTrio?.Stage;
        if (stage == FriendTrioStage.Benefit)
            return decision.Choices.First(c => c.Parameters["value"] == (_players[frame.OwnerSeat].Hp <= 1 ? "recover" : "draw"));
        if (stage == FriendTrioStage.Branch && GetHand(_players[frame.OwnerSeat]).Count(c => c.Kind == CardKind.DrawTwo || c.Kind == CardKind.Alcohol || EquipmentCatalog.IsEquipment(c.Kind)) >= 2)
            return decision.Choices.Single(c => c.Parameters["value"] == "predict");
        if (stage == FriendTrioStage.Prediction)
        {
            var hand = GetHand(_players[frame.OwnerSeat]); var index = frame.FriendTrio!.Values.Count;
            var value = hand.Count > index ? frame.FriendTrio.Mode == "color" ? IsRedSuit(hand[index].Suit) ? "red" : "black" : FriendCategoryValue(hand[index].Kind) : "basic";
            return decision.Choices.FirstOrDefault(c => c.Parameters["value"] == value) ?? decision.Choices[0];
        }
        return decision.Choices[0];
    }
    private void GainFriendMatchingDeckCards(ProgramSkillFrame f, string mode, string value, int count)
    {
        var cards = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => mode == "category" ? FriendCategoryValue(c.Kind) == value :
            value == "red" ? IsRedSuit(c.Suit) : !IsRedSuit(c.Suit) && c.Suit != Suit.None).ToList();
        var selected = new List<Card>();
        for (var i = 0; i < count && cards.Count > 0; i++) { var card = cards[_random.Next(cards.Count)]; cards.Remove(card); selected.Add(card); }
        if (selected.Count > 0) MoveProgramCardsFromMultipleSources(selected.Select(c => c.Id).ToArray(), CardLocation.Hand(f.OwnerSeat), new("friend.yance.reward"));
    }
    private void ContinueFriendTrio(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f); var r = f.FriendTrio!;
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishFriendTrio(f); return; }
        if (r.Stage == FriendTrioStage.Settle)
        {
            var p = r.Policy!; _friendPredictions.Remove((f.OwnerSeat, p.SkillInstanceId));
            var boost = r.FullVerification && p.Revealed ? 1 : 0;
            var oldCount = _friendPredictionCounts.GetValueOrDefault((f.OwnerSeat, p.SkillInstanceId), 3);
            var nextCount = Math.Clamp(oldCount + (p.Correct == 0 ? -(1 + boost) : p.Correct == p.Predictions.Count ? 1 + boost : 0), 1, 7);
            _friendPredictionCounts[(f.OwnerSeat, p.SkillInstanceId)] = nextCount;
            if (r.FullVerification && p.Revealed && p.Predictions.Count > 3 && p.Correct == p.Predictions.Count && HasRuntimeSkill(_players[f.OwnerSeat], "friend:fangqiu"))
                _friendFangqiuSpent.Remove((f.OwnerSeat, GetRuntimeSkillInstanceId(_players[f.OwnerSeat], "friend:fangqiu")));
            AdvanceEventRulesAndQueueFact(new FriendPredictionSettledEvent(p.PredictionId, f.OwnerSeat, p.Correct, p.Predictions.Count, p.Revealed, nextCount));
            if (p.Correct * 2 >= p.Predictions.Count) { PublishFriendTrioChoice(f with { FriendTrio = r with { Stage = FriendTrioStage.Reward } }); return; }
            var count = Math.Min(2 + boost, BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).Count(c => c.Cards.Count == 1));
            var next = count > 0 ? FriendTrioStage.Discard : FriendTrioStage.Complete;
            f = f with { FriendTrio = r with { Count = count, Cards = [], NextStage = next } };
            if (p.Correct == 0)
            {
                ReplaceRuntimeTop(f = f with { FriendTrio = f.FriendTrio! with { Stage = FriendTrioStage.HpLoss } });
                var outcome = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, 1 + boost);
                if (outcome == SkillProgramStepOutcome.Continue) ResumeFriendTrio(f.Id); return;
            }
            ContinueFriendTrio(f with { FriendTrio = f.FriendTrio! with { Stage = next } }); return;
        }
        if (r.Stage == FriendTrioStage.FullReward)
        {
            if (r.Operation == SkillProgramEffectOp.EquipFriendXuanjian)
            { BeginFriendXuanjianUse(f); return; }
            if (r.Policy is { } p && p.Correct == p.Predictions.Count)
            { FriendTrioMutation(f, FriendTrioStage.Complete, () => DrawCards(_players[f.OwnerSeat], 2 + (r.FullVerification && p.Revealed ? 1 : 0), true, new("friend.yance.all-correct"))); return; }
            ContinueFriendTrio(f with { FriendTrio = r with { Stage = FriendTrioStage.Complete } }); return;
        }
        if (r.Stage == FriendTrioStage.Complete)
        {
            if (r.StartNewPrediction) { PublishFriendTrioChoice(f with { FriendTrio = new(r.InstructionIndex, r.Operation, FriendTrioStage.Mode, [], [], FriendPredictionCount(f)) }); return; }
            FinishFriendTrio(f); return;
        }
        PublishFriendTrioChoice(f);
    }
    private void FriendTrioMutation(ProgramSkillFrame f, FriendTrioStage next, Action action)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { FriendTrio = f.FriendTrio! with { Stage = FriendTrioStage.Movement, NextStage = next, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        action(); f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { FriendTrio = f.FriendTrio! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new FriendTrioMovementPaidEvent(f.Id, f.FriendTrio.Operation, before, _movementSequence));
        ReturnFriendTrioMovement(f);
    }
    private bool DrainFriendTrioChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Movement, 10)]
    private bool ReturnFriendTrioMovement(ProgramSkillFrame f)
    {
        if (f.FriendTrio is not { Stage: FriendTrioStage.Movement } r) return false;
        AssertFriendTrio(f); if (DrainFriendTrioChildren(f)) return true;
        ContinueFriendTrio(f with { PendingMovementContinuation = null, FriendTrio = r with { Stage = r.NextStage } }); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 10)]
    private bool ResumeFriendTrio(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FriendTrio: { } r } f || f.Id != id || !IsFriendTrioOperation(r.Operation)) return false;
        AssertFriendTrio(f);
        if (r.Stage == FriendTrioStage.Movement) return ReturnFriendTrioMovement(f);
        if (r.Stage == FriendTrioStage.HpLoss)
        {
            if (ActiveDying is not null) return true;
            if (TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program)) return true;
            ContinueFriendTrio(f with { FriendTrio = r with { Stage = r.NextStage } }); return true;
        }
        if (r.Stage == FriendTrioStage.EquipmentUse)
            throw new InvalidOperationException("Xuanjian use must return through its typed native completion.");
        if (_pendingDecision is null) ContinueFriendTrio(f); return true;
    }
    private void FinishFriendTrio(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f with { FriendTrio = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 80)]
    private bool IsFriendTrioMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        e is not null && IsFriendTrioOperation(e.Op) && f.FriendTrio is { Stage: FriendTrioStage.Movement or FriendTrioStage.EquipmentUse } r && r.Operation == e.Op &&
        f.PendingMovementContinuation == pending && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 460)]
    private void AssertFriendTrio(ProgramSkillFrame f)
    {
        if (f.FriendTrio is not { } r || !IsFriendTrioOperation(r.Operation)) return;
        var e = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (f.InstructionIndex != r.InstructionIndex || e.Op != r.Operation || !Enum.IsDefined(r.Stage) || r.Cards.Distinct().Count() != r.Cards.Count ||
            r.Values is not System.Collections.IList { IsReadOnly: true } || r.Cards is not System.Collections.IList { IsReadOnly: true } ||
            r.Stage == FriendTrioStage.EquipmentUse && (r.Operation != SkillProgramEffectOp.EquipFriendXuanjian || r.IssuedUseFrameId is null || r.IssuedActionId is null ||
                !_resolutionStack.OfType<CardUseFrame>().Any(u => FriendXuanjianFrameRidesOn(u, f))) ||
            r.Stage == FriendTrioStage.Movement && (f.PendingMovementContinuation is null || r.Before > r.After || r.After > _movementSequence ||
                CompleteProgramEventHistory().OfType<FriendTrioMovementPaidEvent>().Count(x => x.ProgramFrameId == f.Id && x.Before == r.Before && x.After == r.After) != 1))
            throw new InvalidOperationException("Friend skill lost its immutable owning receipt or paid child return.");
    }
    private bool FriendXuanjianFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        child is CardUseFrame { Action: { } a } u && parent is ProgramSkillFrame { FriendTrio: { Stage: FriendTrioStage.EquipmentUse } r } f &&
        r.Operation == SkillProgramEffectOp.EquipFriendXuanjian && r.IssuedUseFrameId == u.Id && r.IssuedActionId == a.ActionId &&
        r.Cards is [var card] && u.CardId == card && a.ActorSeat == f.OwnerSeat && a.EffectiveKind == CardKind.Xuanjian &&
        a.ConversionChain.Any(s => s.OwnerSeat == f.OwnerSeat && s.SkillId == f.SkillId && s.BindingId == GetProgramBindingId(f) && s.SkillInstanceId == f.SkillInstanceId) &&
        CompleteProgramEventHistory().OfType<FriendXuanjianUseIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.UseFrameId == u.Id && e.ActionId == a.ActionId && e.CardId == card) == 1;
    [NativeUseReturn(140)]
    private void ReturnFriendXuanjianUse(CardUseFrame use)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FriendTrio: { Stage: FriendTrioStage.EquipmentUse } r } f || !FriendXuanjianFrameRidesOn(use, f)) return;
        if (!CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(e => e.ResolutionId == use.Id))
            throw new InvalidOperationException("Xuanjian must finish its native equipment use before returning.");
        AdvanceEventRulesAndQueueFact(new FriendXuanjianUseReturnedEvent(f.Id, use.Id, use.Action!.ActionId, use.CardId));
        ReplaceRuntimeTop(f = f with { FriendTrio = r with { Stage = FriendTrioStage.Movement, After = _movementSequence, NextStage = FriendTrioStage.Complete } });
        AdvanceEventRulesAndQueueFact(new FriendTrioMovementPaidEvent(f.Id, r.Operation, r.Before, _movementSequence));
        ReturnFriendTrioMovement(f);
    }
    [ProgramCapability]
    private sealed class FriendTrioProgramCapability(GameEngine engine) : IFriendTrioProgramHost
    { public SkillProgramStepOutcome ExecuteFriendTrio(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.ExecuteFriendTrio(f, op); }
}
