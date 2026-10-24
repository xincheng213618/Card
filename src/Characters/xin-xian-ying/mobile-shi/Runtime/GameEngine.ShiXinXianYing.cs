using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string ShiXxyJiejie = "mobile:shi-jiejie", ShiXxyQingshi = "mobile:shi-qingshi";
    private static readonly Suit[] ShiXxySuits = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];
    private static int ShiXxyMask(IEnumerable<Suit> suits) => suits.Aggregate(0, (n, s) => n | (s switch { Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0 }));
    private static int ShiXxySuitCount(int mask) => System.Numerics.BitOperations.PopCount((uint)mask);
    private static CardConversionSource ShiXxySource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int ShiXxyRoundMask(int owner, int actor) => ProgramEventHistory<ShiXinXianYingViewedEvent>().Where(e => e.Source.OwnerSeat == owner && e.Actor == actor && e.Round == _roundNumber).Aggregate(0, (n, e) => n | e.SuitMask);
    private int ShiXxyRoundUsed(int owner) => ProgramEventHistory<ShiXinXianYingQingshiIssuedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Round == _roundNumber && e.FromJiejie);
    private bool ShiXxySameCamp(int owner, int target)
    {
        if (owner == target) return true;
        var a = _players[owner]; var b = _players[target];
        if (_modeDefinition.ModeKind == ContentModeKind.Team) return a.TeamId is { } team && team == b.TeamId;
        if (_modeDefinition.ModeKind == ContentModeKind.NationalWarLite) return GetEffectiveFactionId(a) == GetEffectiveFactionId(b);
        if (_modeDefinition.ModeKind == ContentModeKind.Survival) return false;
        return a.Role is Role.Lord or Role.Loyalist && b.Role is Role.Lord or Role.Loyalist || a.Role == Role.Rebel && b.Role == Role.Rebel;
    }
    [ProgramCapability] private sealed class ShiXinXianYingHost(GameEngine e) : IShiXinXianYingHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op, int actor) => e.BeginShiXxy(f, op, actor); }
    private SkillProgramStepOutcome BeginShiXxy(ProgramSkillFrame supplied, SkillProgramEffectOp op, int actor)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ShiXinXianYing is not null || !_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("Shi Xin Xian Ying requires her original unpaid instruction.");
        var jiejie = op == SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie;
        if (jiejie ? f.SkillId != ShiXxyJiejie || f.TriggerId is not null || actor != _currentSeat || _phase != TurnPhase.Play || actor != (f.ActivatingSeat ?? f.OwnerSeat) :
            op != SkillProgramEffectOp.ResolveMobileShiXinXianYingQingshi || f.SkillId != ShiXxyQingshi || f.TriggerId != "damage-qingshi" || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } c || c.TargetSeat != f.OwnerSeat)
            throw new InvalidOperationException("Jiejie needs its real current-phase activator; Qingshi needs its actual damage victim.");
        var hand = jiejie ? GetHand(_players[actor]).Select(c => new ShiXinXianYingViewedCard(c.Id, EffectiveSuit(_players[actor], c))).ToArray() : [];
        var receipt = new ShiXinXianYingReceipt(ShiXxySource(f), f.GameplayHash, _turnNumber, _roundNumber, actor,
            jiejie ? ShiXinXianYingStage.Suit : ShiXinXianYingStage.Target) { Jiejie = jiejie, Viewed = hand };
        ReplaceRuntimeTop(f = f with { ShiXinXianYing = receipt });
        if (jiejie)
        {
            AdvanceEventRulesAndQueueFact(new ProgramPrivateHandViewedEvent(f.Id, f.SkillId, f.OwnerSeat, actor, hand.Length));
            AdvanceEventRulesAndQueueFact(new ShiXinXianYingViewedEvent(f.Id, receipt.Source, actor, _roundNumber, ShiXxyMask(hand.Select(c => c.Suit)), hand.Length));
        }
        PublishShiXxy(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ShiXxyChoices(ProgramSkillFrame f)
    {
        var r = f.ShiXinXianYing!;
        PromptChoice C(string branch, string label, int? target = null, int? card = null, CardZoneKind? zone = null, int? index = null) =>
            new(new($"shi-xin-xian-ying.{f.Id}.{r.Stage}.{branch}.{target ?? -1}.{zone}.{index ?? card ?? -1}"), label,
                card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> {
                    ["program-action"] = "shi-xin-xian-ying", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch,
                    ["zone"] = zone?.ToString() ?? "", ["index"] = (index ?? -1).ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == ShiXinXianYingStage.Suit) return ShiXxySuits.Select(s => C(s.ToString(), s switch { Suit.Spade => "选择黑桃", Suit.Heart => "选择红桃", Suit.Club => "选择梅花", _ => "选择方块" })).Append(C("cancel", "不选择花色，结束诫节")).ToArray();
        if (r.Stage == ShiXinXianYingStage.Target) return _players.Where(p => p.IsAlive).Select(p => C("target", "对 " + p.Name + " 发动清识", p.Seat)).ToArray();
        if (r.Stage is not (ShiXinXianYingStage.OwnerDiscard or ShiXinXianYingStage.OtherDiscard)) return [];
        var holder = r.Stage == ShiXinXianYingStage.OwnerDiscard ? f.OwnerSeat : r.Target;
        var result = new List<PromptChoice>();
        foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment })
        {
            var from = new CardLocation(zone, holder); var cards = _cardZones.CardsAt(from);
            for (var index = 0; index < cards.Count; index++)
            {
                var card = cards[index];
                if (IsForeignEquipmentDiscardPrevented(f.OwnerSeat, card, from, OwnedCardMoveIntent.Discard)) continue;
                var hidden = holder != f.OwnerSeat && zone == CardZoneKind.Hand;
                result.Add(C("discard", hidden ? $"弃置 {_players[holder].Name} 的第{index + 1}张暗手牌" : "弃置【" + card.DisplayName + "】",
                    holder, hidden ? null : card.Id, zone, index));
            }
        }
        return result;
    }
    private void PublishShiXxy(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ShiXinXianYing!; var choices = ShiXxyChoices(f);
        if (choices.Count == 0) { ContinueShiXxy(f.Id); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage switch {
            ShiXinXianYingStage.Suit => "诫节：私下观看当前角色的全部手牌，选择一种花色（也可以不选）。",
            ShiXinXianYingStage.Target => "清识：选择一名角色，按双方实际阵营结算。可以选择自己。",
            ShiXinXianYingStage.OwnerDiscard => "清识：选择弃置你的一张手牌或装备牌。",
            _ => "清识：选择弃置对方的一张手牌或装备牌。" },
            r.Stage == ShiXinXianYingStage.Suit ? r.Viewed.Select(c => c.CardId).ToArray() : choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, TargetSeat = r.Stage == ShiXinXianYingStage.Suit ? r.Actor : r.Target < 0 ? null : r.Target,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shi-xin-xian-ying")] private void AnswerShiXxy(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiXinXianYing: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiXxyChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Shi Xin Xian Ying rejects a stale choice or an invented foreign hand entity.");
        AssertShiXxyFrame(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == ShiXinXianYingStage.Suit)
        {
            if (branch == "cancel") { CompleteShiXxyJiejie(f); return; }
            var suit = Enum.Parse<Suit>(branch); var included = r.Viewed.Any(c => c.Suit == suit);
            AdvanceEventRulesAndQueueFact(new ShiXinXianYingSuitChosenEvent(f.Id, r.Actor, suit, included));
            ReplaceRuntimeTop(f = f with { ShiXinXianYing = r = r with { Stage = ShiXinXianYingStage.JiejieChildren, ChosenSuit = suit, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(r.Actor, 0, null) });
            if (included)
            {
                var ids = GetSelfDiscardableOwnedHand(_players[r.Actor]).Where(c => EffectiveSuit(_players[r.Actor], c) != suit).Select(c => c.Id).ToArray();
                if (ids.Length > 0) MoveProgramLogicalDiscard(f, r.Actor, ids, new("skill-program.mobile-shi-jiejie.discard"));
            }
            else
            {
                var pool = _cardZones.CardsAt(CardLocation.DrawPile).Concat(_cardZones.CardsAt(CardLocation.DiscardPile)).Where(c => c.Suit == suit).ToArray();
                if (pool.Length > 0)
                { var card = pool[_random.Next(pool.Length)]; MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.Hand(r.Actor), new("skill-program.mobile-shi-jiejie.obtain")); }
            }
            SealShiXxy(f.Id); ContinueShiXxy(f.Id); return;
        }
        if (r.Stage == ShiXinXianYingStage.Target)
        { IssueShiXxyQingshi(f, choice.Targets.Single()); return; }
        if (r.Stage is ShiXinXianYingStage.OwnerDiscard or ShiXinXianYingStage.OtherDiscard)
        {
            var holder = r.Stage == ShiXinXianYingStage.OwnerDiscard ? f.OwnerSeat : r.Target;
            var from = new CardLocation(Enum.Parse<CardZoneKind>(choice.Parameters["zone"]), holder);
            var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["index"], CultureInfo.InvariantCulture)];
            ReplaceRuntimeTop(f = f with { ShiXinXianYing = r with { Selected = r.Selected.Append(new(card.Id, from)).ToArray(), Stage = r.Stage == ShiXinXianYingStage.OwnerDiscard ? ShiXinXianYingStage.OtherDiscard : ShiXinXianYingStage.QingDiscardChildren } });
            ContinueShiXxy(f.Id);
        }
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 48200)]
    private IEnumerable<CardSnapshot> ShiXxyPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewer && f.ShiXinXianYing is { Stage: ShiXinXianYingStage.Suit })
        .SelectMany(f => f.ShiXinXianYing!.Viewed).Where(c => _cardZones.GetLocation(c.CardId).Zone == CardZoneKind.Hand).DistinctBy(c => c.CardId).Select(c => ToSnapshot(GetAdvancedCard(c.CardId))).ToArray();
}
