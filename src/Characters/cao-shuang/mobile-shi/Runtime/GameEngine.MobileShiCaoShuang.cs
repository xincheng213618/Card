using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiCaoShuangSheQuanSkill = ShiCaoShuangComposition.SheQuanSkill,
        ShiCaoShuangDianYiSkill = ShiCaoShuangComposition.DianYiSkill,
        ShiCaoShuangJianZhuanSkill = ShiCaoShuangComposition.JianZhuanSkill,
        ShiCaoShuangNiZunSkill = ShiCaoShuangComposition.NiZunSkill;

    private static CardConversionSource ShiCaoShuangSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasShiCaoShuangCapability =>
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveShiCaoShuangNiZunUse);
    private bool ShiCaoShuangHasSkill(int seat, string skillId) =>
        _players[seat].SkillGrants.Grants.Any(grant => grant.IsEnabled && grant.SkillId == skillId) ||
        EnabledContentSkillIds(_players[seat]).Contains(skillId, StringComparer.Ordinal);
    private int? ShiCaoShuangOwnerSeat =>
        Enumerable.Range(0, _players.Count).Where(seat => _players[seat].IsAlive &&
            ShiCaoShuangHasSkill(seat, ShiCaoShuangNiZunSkill)).Cast<int?>().FirstOrDefault();

    [ProgramCapability]
    private sealed class ShiCaoShuangCapability(GameEngine engine) : IShiCaoShuangHost
    {
        public SkillProgramStepOutcome ResolveSheQuan(ProgramSkillFrame frame) => engine.BeginShiCaoShuang(frame);
        public SkillProgramStepOutcome ResolveDianYi(ProgramSkillFrame frame) => engine.BeginShiCaoShuang(frame);
        public SkillProgramStepOutcome ResolveJianZhuan(ProgramSkillFrame frame) => engine.BeginShiCaoShuang(frame);
        public SkillProgramStepOutcome ResolveNiZunStash(ProgramSkillFrame frame) => engine.BeginShiCaoShuang(frame);
        public SkillProgramStepOutcome ResolveNiZunUse(ProgramSkillFrame frame) => engine.BeginShiCaoShuang(frame);
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 67900, SkillProgramEffectOp.ResolveShiCaoShuangNiZunUse)]
    private bool CanStartShiCaoShuangNiZunUse(CharacterState owner, string skill) =>
        skill != ShiCaoShuangNiZunSkill || ShiCaoShuangStashedIds(owner.Seat).Count > 0;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67900)]
    private bool CanOfferShiCaoShuang(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!_players[candidate.OwnerSeat].IsAlive) return false;
        var windowKind = ShiCaoShuangComposition.WindowKind(trigger.Id);
        // The damage windows qualify any seat; the owner relation is checked here:
        // caused requires the owner as damage source, taken requires the owner as
        // the damage target, both with a positive amount.
        if (windowKind is "caused" or "taken")
        {
            var ownerRelated = windowKind == "caused"
                ? context.SourceSeat == candidate.OwnerSeat
                : context.TargetSeat == candidate.OwnerSeat;
            if (!ownerRelated || context.Amount <= 0) return false;
        }
        switch (candidate.SkillId)
        {
            case ShiCaoShuangSheQuanSkill:
                return ShiCaoShuangRoundTriggers(false, candidate.OwnerSeat, windowKind) <
                    1 + ShiCaoShuangTimings(ShiCaoShuangSheQuanSkill, windowKind);
            case ShiCaoShuangDianYiSkill:
                return ShiCaoShuangRoundTriggers(true, candidate.OwnerSeat, windowKind) <
                    1 + ShiCaoShuangTimings(ShiCaoShuangDianYiSkill, windowKind);
            case ShiCaoShuangJianZhuanSkill:
                return !ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>()
                    .Any(entry => entry.WindowKind == windowKind && entry.OwnerSeat == candidate.OwnerSeat) &&
                    ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>().Count < ShiCaoShuangComposition.TimingCapacity &&
                    !_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == ShiCaoShuangJianZhuanSkill &&
                        f.ShiCaoShuang is { Operation: "jianzhuan-timing" } r && r.WindowKind == windowKind);
            case ShiCaoShuangNiZunSkill when trigger.Id == ShiCaoShuangComposition.NiZunEndingTrigger:
                return ProgramEventHistory<ShiCaoShuangOwnerCardUsedEvent>().Any(entry => entry.TurnNumber == _turnNumber) &&
                    ShiCaoShuangStashableIds(candidate.OwnerSeat).Count > 0;
            default:
                return true;
        }
    }

    private SkillProgramStepOutcome BeginShiCaoShuang(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiCaoShuang is not null) throw new InvalidOperationException("Shi Cao Shuang restarted a running operation.");
        var r = f.SkillId switch
        {
            ShiCaoShuangSheQuanSkill when f.TriggerId is not null => StartShiCaoShuangSheQuan(f),
            ShiCaoShuangDianYiSkill when f.TriggerId is not null => StartShiCaoShuangDianYi(f),
            ShiCaoShuangJianZhuanSkill when f.TriggerId is not null => StartShiCaoShuangJianZhuan(f),
            ShiCaoShuangNiZunSkill when f.TriggerId == ShiCaoShuangComposition.NiZunEndingTrigger => StartShiCaoShuangNiZunStash(f),
            ShiCaoShuangNiZunSkill when f.TriggerId is null => StartShiCaoShuangNiZunUse(f),
            _ => throw new InvalidOperationException("Shi Cao Shuang instruction has an incompatible owning skill."),
        };
        ReplaceRuntimeTop(f = f with { ShiCaoShuang = r });
        ContinueShiCaoShuang(f.Id);
        return r.Operation switch
        {
            "shequan" or "jianzhuan" or "nizun-use" => SkillProgramStepOutcome.AwaitChoice,
            _ => SkillProgramStepOutcome.AwaitChild,
        };
    }

    private ShiCaoShuangReceipt StartShiCaoShuangSheQuan(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("SheQuan requires its living owner.");
        var windowKind = ShiCaoShuangComposition.WindowKind(f.TriggerId!);
        // A queued duplicate whose window already resolved this round settles silently.
        if (ShiCaoShuangRoundTriggers(false, f.OwnerSeat, windowKind) >=
            1 + ShiCaoShuangTimings(ShiCaoShuangSheQuanSkill, windowKind))
            return new("shequan", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, Skip = true };
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangSheQuanRoundTriggerEvent(_roundNumber, windowKind, f.OwnerSeat));
        var holders = Enumerable.Range(0, _players.Count).Where(seat => seat != f.OwnerSeat &&
            _players[seat].IsAlive && GetHand(_players[seat]).Count > 0).ToArray();
        return new("shequan", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash,
            Round = _roundNumber, Holders = Array.AsReadOnly(holders) };
    }

    private ShiCaoShuangReceipt StartShiCaoShuangDianYi(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("DianYi requires its living owner.");
        var windowKind = ShiCaoShuangComposition.WindowKind(f.TriggerId!);
        if (ShiCaoShuangRoundTriggers(true, f.OwnerSeat, windowKind) >=
            1 + ShiCaoShuangTimings(ShiCaoShuangDianYiSkill, windowKind))
            return new("dianyi", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, Skip = true };
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangDianYiRoundTriggerEvent(_roundNumber, windowKind, f.OwnerSeat));
        return new("dianyi", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, Round = _roundNumber };
    }

    private ShiCaoShuangReceipt StartShiCaoShuangJianZhuan(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("JianZhuan requires its living owner.");
        var windowKind = ShiCaoShuangComposition.WindowKind(f.TriggerId!);
        if (ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>().Count >= ShiCaoShuangComposition.TimingCapacity ||
            ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>()
                .Any(entry => entry.WindowKind == windowKind && entry.OwnerSeat == f.OwnerSeat))
            return new("jianzhuan-timing", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, Skip = true };
        return new("jianzhuan-timing", _turnNumber)
        { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, WindowKind = windowKind };
    }

    private ShiCaoShuangReceipt StartShiCaoShuangNiZunStash(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || ShiCaoShuangStashableIds(f.OwnerSeat).Count == 0)
            throw new InvalidOperationException("NiZun stash requires used cards and discard-pile marked cards.");
        return new("nizun-stash", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, Round = _roundNumber };
    }

    private ShiCaoShuangReceipt StartShiCaoShuangNiZunUse(ProgramSkillFrame f)
    {
        if (!_players[f.OwnerSeat].IsAlive || ShiCaoShuangStashedIds(f.OwnerSeat).Count == 0)
            throw new InvalidOperationException("NiZun use requires at least one stashed card.");
        return new("nizun-use", _turnNumber) { Source = ShiCaoShuangSource(f), GameplayHash = f.GameplayHash, Round = _roundNumber };
    }

    // ---- choice publication -------------------------------------------------

    private IReadOnlyList<PromptChoice> ShiCaoShuangChoices(ProgramSkillFrame f)
    {
        var r = f.ShiCaoShuang!;
        switch (r.Operation)
        {
            case "shequan":
                var holder = r.Holders[r.Cursor];
                return GetHand(_players[holder]).Select(card => new PromptChoice(
                    new($"shi-cao-shuang.{f.Id}.mark.{holder}.{card.Id}"),
                    $"将这张手牌（{DescribeShiCaoShuangCard(card)}）标记为“奢权”牌", [card.Id], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = ShiCaoShuangComposition.ProgramAction,
                        ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                        ["branch"] = "mark", ["card-id"] = card.Id.ToString(CultureInfo.InvariantCulture),
                    })).ToArray();
            case "jianzhuan-timing":
                return new[]
                {
                    MakeSkillChoice(f, "timing", ShiCaoShuangDianYiSkill, "将时机添加给“典易”"),
                    MakeSkillChoice(f, "timing", ShiCaoShuangSheQuanSkill, "将时机添加给“奢权”"),
                };
            case "jianzhuan-lose":
                return new[]
                {
                    MakeSkillChoice(f, "lose", ShiCaoShuangDianYiSkill, "失去“典易”并获得“溺尊”"),
                    MakeSkillChoice(f, "lose", ShiCaoShuangSheQuanSkill, "失去“奢权”并获得“溺尊”"),
                };
            case "nizun-use":
                return ShiCaoShuangStashedIds(f.OwnerSeat).Select(cardId => new PromptChoice(
                    new($"shi-cao-shuang.{f.Id}.nizun.{cardId}"),
                    $"如手牌般使用武将牌上的这张牌（{DescribeShiCaoShuangCard(_cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(card => card.Id == cardId))}）",
                    [cardId], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = ShiCaoShuangComposition.ProgramAction,
                        ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                        ["branch"] = "nizun", ["card-id"] = cardId.ToString(CultureInfo.InvariantCulture),
                    })).ToArray();
            default:
                return [];
        }
    }

    private PromptChoice MakeSkillChoice(ProgramSkillFrame f, string branch, string skillId, string label) =>
        new(new($"shi-cao-shuang.{f.Id}.{branch}.{skillId}"), label, [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = ShiCaoShuangComposition.ProgramAction,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["branch"] = branch, ["skill-id"] = skillId,
            });

    private static string DescribeShiCaoShuangCard(Card card) => card.Kind switch
    {
        CardKind.Slash => "杀", CardKind.Dodge => "闪", CardKind.Peach => "桃",
        _ => $"{card.Kind} {card.Suit}{card.Rank}",
    } + $"{card.Suit}{card.Rank}";

    private void PublishShiCaoShuang(ProgramSkillFrame f)
    {
        var r = f.ShiCaoShuang!;
        var choices = ShiCaoShuangChoices(f);
        var chooser = r.Operation switch
        {
            "shequan" => r.Holders[r.Cursor],
            _ => f.OwnerSeat,
        };
        if (choices.Count == 0 || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !_players[chooser].IsAlive)
        { FinishShiCaoShuangOperation(GetActiveProgramFrame(f.Id)); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            r.Operation switch
            {
                "shequan" => "奢权：请选择要标记为“奢权”牌的手牌。",
                "jianzhuan-timing" => "渐专：选择增加每轮限一次触发时机的技能。",
                "jianzhuan-lose" => "渐专：选择要失去的技能。",
                _ => "溺尊：选择要如手牌般使用的牌。",
            },
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(),
            choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), f.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer(ShiCaoShuangComposition.ProgramAction)]
    private void AnswerShiCaoShuang(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiCaoShuang: { } r } f ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } pending ||
            !ShiCaoShuangChoices(f).Any(choice => AssistedChoicesEqual([choice], [selected])))
            throw new InvalidOperationException("Shi Cao Shuang choice lost its current frame or legal branch.");
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        switch (r.Operation)
        {
            case "shequan" when selected.Parameters["branch"] == "mark" &&
                int.TryParse(selected.Parameters["card-id"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cardId):
                ResolveShiCaoShuangMark(f, cardId);
                return;
            case "jianzhuan-timing" when selected.Parameters["branch"] == "timing":
                ResolveShiCaoShuangTiming(f, selected.Parameters["skill-id"]);
                return;
            case "jianzhuan-lose" when selected.Parameters["branch"] == "lose":
                ResolveShiCaoShuangAwakening(f, selected.Parameters["skill-id"]);
                return;
            case "nizun-use" when selected.Parameters["branch"] == "nizun" &&
                int.TryParse(selected.Parameters["card-id"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var useId):
                ResolveShiCaoShuangNiZunCard(f, useId);
                return;
            default:
                throw new InvalidOperationException("Shi Cao Shuang answered an incompatible operation branch.");
        }
    }

    private void ResolveShiCaoShuangMark(ProgramSkillFrame f, int cardId)
    {
        var r = f.ShiCaoShuang!;
        var holder = r.Holders[r.Cursor];
        if (_cardZones.GetLocation(cardId).Zone != CardZoneKind.Hand ||
            _cardZones.GetLocation(cardId).OwnerSeat != holder)
            throw new InvalidOperationException("SheQuan marked a card outside the choosing holder's hand.");
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangSheQuanMarkedEvent(f.Id, cardId, holder, r.Round));
        SyncShiCaoShuangMarker(holder);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { ShiCaoShuang = f.ShiCaoShuang! with { Cursor = r.Cursor + 1 } });
        ContinueShiCaoShuang(f.Id);
    }

    private void ResolveShiCaoShuangTiming(ProgramSkillFrame f, string skillId)
    {
        if (skillId is not (ShiCaoShuangComposition.SheQuanSkill or ShiCaoShuangComposition.DianYiSkill))
            throw new InvalidOperationException("JianZhuan timing targeted an unsupported skill.");
        var r = f.ShiCaoShuang!;
        var ordinal = ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>().Count + 1;
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangJianZhuanTimingEvent(f.Id, f.OwnerSeat, skillId, r.WindowKind, ordinal));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 为{(skillId == ShiCaoShuangComposition.DianYiSkill ? "“典易”" : "“奢权”")}增加了一个每轮限一次的触发时机。", f.OwnerSeat);
        f = GetActiveProgramFrame(f.Id);
        if (ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>().Count >= ShiCaoShuangComposition.TimingCapacity)
        {
            ReplaceRuntimeTop(f = f with { ShiCaoShuang = new("jianzhuan-lose", r.ActualTurn, 6)
            { Source = r.Source, GameplayHash = r.GameplayHash } });
            ContinueShiCaoShuang(f.Id);
            return;
        }
        FinishShiCaoShuangOperation(GetActiveProgramFrame(f.Id));
    }

    private void ResolveShiCaoShuangAwakening(ProgramSkillFrame f, string lostSkillId)
    {
        if (lostSkillId is not (ShiCaoShuangComposition.SheQuanSkill or ShiCaoShuangComposition.DianYiSkill))
            throw new InvalidOperationException("JianZhuan awakening lost an unsupported skill.");
        if (ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>().Count < ShiCaoShuangComposition.TimingCapacity)
            throw new InvalidOperationException("JianZhuan awakening requires all three added timings.");
        var r = f.ShiCaoShuang!;
        var owner = _players[f.OwnerSeat];
        foreach (var grant in owner.SkillGrants.Grants.Where(grant => grant.IsEnabled && grant.SkillId == lostSkillId).ToArray())
            owner.SkillGrants.SetEnabled(grant.GrantId, false);
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        AdvanceEventRulesAndQueueFact(new CharacterSkillsLostEvent(f.OwnerSeat, f.OwnerSeat,
            Array.AsReadOnly(new[] { lostSkillId })));
        AddLog("SkillTriggered", $"{owner.Name} 失去技能{(lostSkillId == ShiCaoShuangComposition.DianYiSkill ? "“典易”" : "“奢权”")}。", f.OwnerSeat);
        if (!ShiCaoShuangHasSkill(f.OwnerSeat, ShiCaoShuangComposition.NiZunSkill))
            GrantProgramSkills(f, [ShiCaoShuangComposition.NiZunSkill]);
        var timings = ShiCaoShuangTimings(lostSkillId, null);
        owner.MaxHp = checked(owner.MaxHp + timings);
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(f.OwnerSeat, timings, owner.MaxHp, f.SkillId));
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangJianZhuanAwakenedEvent(f.Id, r.Source!, f.GameplayHash, r.ActualTurn,
            lostSkillId, timings, owner.MaxHp));
        if (timings > 0 && owner.IsAlive && owner.Hp < owner.MaxHp)
            RecoverProgramTargetTo(f.Id, f.OwnerSeat, f.OwnerSeat, SkillProgramNumberExpression.IntegerConstant,
                Math.Min(owner.Hp + timings, owner.MaxHp), true);
        SyncShiCaoShuangMarker(f.OwnerSeat);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { ShiCaoShuang = f.ShiCaoShuang! with { Stage = 12 } });
        ContinueShiCaoShuang(f.Id);
    }

    private void ResolveShiCaoShuangNiZunCard(ProgramSkillFrame f, int cardId)
    {
        var r = f.ShiCaoShuang!;
        var stashed = ShiCaoShuangStashedIds(f.OwnerSeat);
        if (!stashed.Contains(cardId)) throw new InvalidOperationException("NiZun use selected a card outside its stash.");
        var location = new CardLocation(CardZoneKind.PrivatePersistentPile, f.OwnerSeat, ShiCaoShuangComposition.NiZunPileId);
        _cardZones.EnsurePrivatePersistentPile(location);
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([cardId], CardLocation.Hand(f.OwnerSeat),
            new(ShiCaoShuangComposition.NiZunUseReason));
        var count = ShiCaoShuangNiZunUsesThisRound(f.OwnerSeat) + 1;
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangNiZunUsedEvent(f.Id, cardId, r.Round, count));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 将武将牌上的一张“奢权”牌如手牌般使用。", f.OwnerSeat);
        SyncShiCaoShuangMarker(f.OwnerSeat);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { ShiCaoShuang = f.ShiCaoShuang! with { Stage = 3 } });
        ContinueShiCaoShuang(f.Id);
    }

    // ---- continuations ------------------------------------------------------

    private void ContinueShiCaoShuang(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiCaoShuang: { } r } f || f.Id != id) return;
        if (r.Skip)
        { FinishShiCaoShuangOperation(GetActiveProgramFrame(id)); return; }
        switch (r.Operation, r.Stage)
        {
            case ("shequan", 0) when r.Cursor < r.Holders.Count:
                PublishShiCaoShuang(GetActiveProgramFrame(id));
                return;
            case ("shequan", 0):
                FinishShiCaoShuangOperation(GetActiveProgramFrame(id));
                return;
            case ("dianyi", 0):
                ContinueShiCaoShuangDianYi(GetActiveProgramFrame(id));
                return;
            case ("dianyi", 3):
                ContinueShiCaoShuangDianYiLoss(GetActiveProgramFrame(id));
                return;
            case ("dianyi", 6) or ("nizun-use", 3) or ("nizun-stash", 3) or ("jianzhuan-lose", 12):
                if (DrainShiCaoShuang(GetActiveProgramFrame(id))) return;
                FinishShiCaoShuangOperation(GetActiveProgramFrame(id));
                return;
            case ("jianzhuan-timing", 0) or ("jianzhuan-lose", 6) or ("nizun-use", 0):
                PublishShiCaoShuang(GetActiveProgramFrame(id));
                return;
            case ("nizun-stash", 0):
                ContinueShiCaoShuangStash(GetActiveProgramFrame(id));
                return;
            default:
                throw new InvalidOperationException("Shi Cao Shuang has an unknown continuation stage.");
        }
    }

    private void ContinueShiCaoShuangDianYi(ProgramSkillFrame f)
    {
        var r = f.ShiCaoShuang!;
        var owner = _players[f.OwnerSeat];
        var eligible = ShiCaoShuangMarkedInOtherHands(f.OwnerSeat);
        var amount = Math.Min(ShiCaoShuangDianYiUsesThisRound(f.OwnerSeat), eligible.Count);
        var taken = new List<int>();
        if (amount > 0)
        {
            var pool = new List<int>(eligible);
            for (var i = 0; i < amount; i++)
            {
                var pick = _random.Next(pool.Count);
                taken.Add(pool[pick]);
                pool.RemoveAt(pick);
            }
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources(taken, CardLocation.Hand(f.OwnerSeat),
                new(ShiCaoShuangComposition.DianYiGainReason));
        }
        var total = ShiCaoShuangDianYiGainsThisRound(f.OwnerSeat) + taken.Count;
        AdvanceEventRulesAndQueueFact(new ShiCaoShuangDianYiGainEvent(f.Id, r.Round, taken.Count, total));
        foreach (var seat in Enumerable.Range(0, _players.Count)) SyncShiCaoShuangMarker(seat);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { ShiCaoShuang = f.ShiCaoShuang! with { Stage = 3, SelectedIds = Array.AsReadOnly(taken.ToArray()) } });
        ContinueShiCaoShuang(f.Id);
    }

    private void ContinueShiCaoShuangDianYiLoss(ProgramSkillFrame f)
    {
        var r = f.ShiCaoShuang!;
        var owner = _players[f.OwnerSeat];
        var total = ShiCaoShuangDianYiGainsThisRound(f.OwnerSeat);
        if (r.SelectedIds.Count > 0 && total > owner.MaxHp && owner.IsAlive && !r.PendingHpLoss)
        {
            ReplaceRuntimeTop(f = f with { ShiCaoShuang = f.ShiCaoShuang! with { PendingHpLoss = true, Stage = 6 } });
            if (ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1) == SkillProgramStepOutcome.AwaitChild) return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { ShiCaoShuang = GetActiveProgramFrame(f.Id).ShiCaoShuang! with { Stage = 6 } });
        ContinueShiCaoShuang(f.Id);
    }

    private void ContinueShiCaoShuangStash(ProgramSkillFrame f)
    {
        var r = f.ShiCaoShuang!;
        var ids = ShiCaoShuangStashableIds(f.OwnerSeat);
        if (ids.Count > 0)
        {
            var location = new CardLocation(CardZoneKind.PrivatePersistentPile, f.OwnerSeat,
                ShiCaoShuangComposition.NiZunPileId);
            _cardZones.EnsurePrivatePersistentPile(location);
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources(ids, location, new(ShiCaoShuangComposition.NiZunStashReason));
            foreach (var cardId in ids)
                AdvanceEventRulesAndQueueFact(new ShiCaoShuangNiZunStashedEvent(f.Id, cardId, _turnNumber));
            foreach (var seat in Enumerable.Range(0, _players.Count)) SyncShiCaoShuangMarker(seat);
            AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 将本回合弃牌堆中的 {ids.Count} 张“奢权”牌置于武将牌上。", f.OwnerSeat);
        }
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { ShiCaoShuang = f.ShiCaoShuang! with { Stage = 3 } });
        ContinueShiCaoShuang(f.Id);
    }

    private void FinishShiCaoShuangOperation(ProgramSkillFrame f)
    {
        FinishProgramSkill(GetActiveProgramFrame(f.Id), true);
    }

    private bool DrainShiCaoShuang(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) ||
        TryBeginAdvancedSkillsChanged(f.Id);

    [ProgramContinuation(ProgramContinuationKind.Resume, 67900)]
    private bool ResumeShiCaoShuang(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiCaoShuang: not null } f || f.Id != id) return false;
        ContinueShiCaoShuang(id);
        return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 67900)]
    private bool ReturnShiCaoShuangMovement(ProgramSkillFrame f) => f.ShiCaoShuang is not null && ResumeShiCaoShuang(f.Id);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67900)]
    private bool ContinuePaidShiCaoShuang(ProgramSkillFrame f) => f.ShiCaoShuang is { Stage: > 0 };

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 67900)]
    private int ShiCaoShuangNiZunDamagePenalty(IDamageAttempt attack)
    {
        var target = attack.TargetSeat;
        if (!IsValidPlayerSeat(target) || !_players[target].IsAlive ||
            !ShiCaoShuangHasSkill(target, ShiCaoShuangNiZunSkill) ||
            !ShiCaoShuangNiZunDamagePenaltyPending(target)) return 0;
        return ShiCaoShuangNiZunUsesThisRound(target);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67900)]
    private void AssertShiCaoShuang(ProgramSkillFrame f)
    {
        if (f.ShiCaoShuang is not { } r) return;
        if (r.ActualTurn <= 0 || r.Source?.OwnerSeat != f.OwnerSeat ||
            r.Operation is not ("shequan" or "dianyi" or "jianzhuan-timing" or "jianzhuan-lose" or "nizun-stash" or "nizun-use"))
            throw new InvalidOperationException("Shi Cao Shuang lost its original typed operation receipt.");
        if (r.Operation == "jianzhuan-lose" && !ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>()
            .Any(entry => entry.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("JianZhuan lost its timing ledger before the awakening choice.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiCaoShuangSheQuan)]
    private PromptChoice ChooseShiCaoShuangSheQuanAi(PendingDecision decision, ProgramSkillFrame f) => decision.Choices[0];

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiCaoShuangJianZhuan)]
    private PromptChoice ChooseShiCaoShuangJianZhuanAi(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.ShiCaoShuang!;
        if (r.Operation == "jianzhuan-timing")
            return decision.Choices.OrderBy(choice => ShiCaoShuangTimings(choice.Parameters["skill-id"], null))
                .First();
        return decision.Choices.OrderByDescending(choice => ShiCaoShuangTimings(choice.Parameters["skill-id"], null)).First();
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiCaoShuangNiZunUse)]
    private PromptChoice ChooseShiCaoShuangNiZunAi(PendingDecision decision, ProgramSkillFrame f) => decision.Choices[0];
}
