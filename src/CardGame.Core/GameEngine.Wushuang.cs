namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WushuangGrantSource = "mode:wushuang-shop";
    private const string WushuangOriginalReplacementSource = "mode:wushuang-replaced-original";
    private readonly Dictionary<int, WushuangPlayerProgress> _wushuangPlayers = [];
    private int _wushuangElapsedMilliseconds;
    private bool _wushuangOpeningCompleted;
    private sealed class WushuangPlayerProgress
    {
        public int Tokens;
        public int RefreshNumber;
        public int FreeRerolls;
        public bool IsLocked;
        public readonly List<int> Tactics = [];
        public IReadOnlyList<WushuangShopOffer> Offers = Array.AsReadOnly(Array.Empty<WushuangShopOffer>());
        public IReadOnlyList<WushuangShopOffer> Purchased = Array.AsReadOnly(Array.Empty<WushuangShopOffer>());
    }
    public bool IsWushuangMode => _modeDefinition.Id == WushuangMode.ModeId;
    private bool IsWushuangSkillGrantQualified(SkillGrant grant) => !IsWushuangMode ||
        _contentRegistry.Skills.TryGetValue(grant.SkillId, out var skill) && WushuangCatalogue.IsSkillAllowed(skill);
    public bool IsWushuangClockRunning => IsWushuangMode && _started && _setupComplete && _winner == Winner.None;
    public int? WushuangRemainingMilliseconds => IsWushuangMode ? Math.Max(0, WushuangMode.TimeLimitMilliseconds - _wushuangElapsedMilliseconds) : null;
    private void InitializeWushuangAutomaticHealth()
    {
        if (!IsWushuangMode || _options.UseInteractiveSetup) return;
        foreach (var p in _players) { p.MaxHp = p.General.BaseHp; p.Hp = p.General.InitialHp ?? p.MaxHp; }
    }
    private WushuangIntermissionFrame? ActiveWushuangWindow(int seat) =>
        _resolutionStack.LastOrDefault() is WushuangIntermissionFrame f && f.SeatCursor == seat ? f : null;
    private bool HasWushuangIntermissionBoundary() => IsWushuangMode &&
        _resolutionStack.FirstOrDefault() is WushuangIntermissionFrame f &&
        f.SeatCursor >= 0 && f.SeatCursor <= _playerCount && IsValidPlayerSeat(f.EndedSeat) &&
        _resolutionStack.Count(frame => frame is WushuangIntermissionFrame) == 1;
    public WushuangSnapshot? CreateWushuangSnapshot(int viewerSeat)
    {
        if (!IsWushuangMode || !IsValidPlayerSeat(viewerSeat)) return null;
        var p = _wushuangPlayers.GetValueOrDefault(viewerSeat);
        var f = _resolutionStack.OfType<WushuangIntermissionFrame>().LastOrDefault(frame => frame.SeatCursor == viewerSeat);
        var extra = _players[viewerSeat].SkillGrants.Grants.Where(g => g.SourceId == WushuangGrantSource).Select(g => g.SkillId).ToArray();
        return new(viewerSeat, 1, 0, 0, p?.Tokens ?? 0, Array.AsReadOnly(extra),
            p?.Offers ?? Array.AsReadOnly(Array.Empty<WushuangShopOffer>()), CanUseWushuangShop(viewerSeat), p?.FreeRerolls > 0 ? 0 : 1, 0, WushuangRemainingMilliseconds!.Value, _revision)
        {
            FirstNormalSeat = ProgramEventHistory<MoLuBuOpeningStartedEvent>().FirstOrDefault()?.NormalSeat ??
                ProgramEventHistory<RoundStartedEvent>().FirstOrDefault()?.ActorSeat ?? _currentSeat,
            ExtraSkillCapacity = WushuangExtraSkillCapacity(viewerSeat), FreeRerolls = p?.FreeRerolls ?? 0,
            ReplaceableOriginalSkillIds = Array.AsReadOnly(WushuangReplaceableOriginalSkills(viewerSeat).ToArray()),
            Phase = f is null ? WushuangShopPhase.Closed : f.Opening ? WushuangShopPhase.OpeningTactic : WushuangShopPhase.Shopping,
            WindowId = f?.Id ?? 0, IsLocked = p?.IsLocked == true,
            PurchasedOffers = p?.Purchased ?? Array.AsReadOnly(Array.Empty<WushuangShopOffer>()),
            TacticIds = Array.AsReadOnly(p?.Tactics.ToArray() ?? [])
        };
    }
    private bool CanUseWushuangShop(int seat) => IsWushuangClockRunning && IsValidPlayerSeat(seat) && _players[seat].IsHuman &&
        _players[seat].IsAlive && ActiveWushuangWindow(seat) is not null &&
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p && p.PlayerSeat == seat && p.SkillPrompt?.SkillId == WushuangMode.ModeId;
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 61001)]
    private void InitializeWushuangShops(GameStartedEvent started)
    {
        if (!IsWushuangMode || _wushuangPlayers.Count != 0) return;
        foreach (var p in _players) _wushuangPlayers.Add(p.Seat, new());
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 61002)]
    private void ProgressWushuangEconomy(TurnStartedEvent started)
    {
        if (!IsWushuangMode || _winner != Winner.None || _turnProgression.Kind != ActualTurnKind.Normal) return;
        foreach (var p in _players.Where(p => p.IsAlive))
        {
            var tactics = _wushuangPlayers[p.Seat].Tactics;
            var income = 1 + (tactics.Contains(2286) ? 1 : 0) + (tactics.Contains(2285) && p.Seat == started.ActorSeat ? 1 : 0) +
                (tactics.Contains(2281) && ProgramEventHistory<WushuangTokensChangedEvent>().Any(e => e.Seat == p.Seat && e.Tokens >= 5) ? 1 : 0);
            ChangeWushuangTokens(p.Seat, tactics.Contains(2366) ? income * 2 : income, "normal-turn-start");
            if (tactics.Contains(2290) && _wushuangPlayers[p.Seat].FreeRerolls == 0) ChangeWushuangFreeRerolls(p.Seat, 1, "turn-start");
        }
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 61003)]
    private void ProgressWushuangRound(RoundStartedEvent started)
    {
        if (IsWushuangMode && _winner == Winner.None)
            foreach (var p in _players.Where(p => p.IsAlive)) ChangeWushuangTokens(p.Seat, 1, "round-start");
    }
    private bool TryBeginWushuangOpening()
    {
        if (!IsWushuangClockRunning || _wushuangOpeningCompleted || _wushuangPlayers.Count != _playerCount) return false;
        foreach (var p in _players) RefreshWushuangOffers(p.Seat, true);
        var f = new WushuangIntermissionFrame(++_resolutionSequence, true, _currentSeat, false);
        PushRuntimeFrame(f); ContinueWushuangIntermission(f.Id); return true;
    }
    private bool TryBeginWushuangShopping(CharacterState previous, bool skipped)
    {
        if (!IsWushuangClockRunning || _turnProgression.Kind != ActualTurnKind.Normal) return false;
        var f = new WushuangIntermissionFrame(++_resolutionSequence, false, previous.Seat, skipped);
        PushRuntimeFrame(f); ContinueWushuangIntermission(f.Id); return true;
    }
    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.BeforeOpening, 0, NativeFrameAutomaticPending.RequireNoneBeforeFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(WushuangIntermissionFrame), 0)]
    private void ContinueWushuangIntermission(long frameId)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not WushuangIntermissionFrame f || f.Id != frameId) return;
        if (f.SeatCursor >= _playerCount || _winner != Winner.None)
        {
            PopResolutionFrame(f.Id, ResolutionFrameKind.WushuangIntermission);
            if (f.Opening) _wushuangOpeningCompleted = true;
            if (_winner == Winner.None)
            {
                // The in-turn preview must be the exact next ending's page.
                foreach (var next in _players.Where(player => player.IsAlive))
                    if (!_wushuangPlayers[next.Seat].IsLocked) RefreshWushuangOffers(next.Seat);
                if (!f.Opening) ContinueAfterWushuangShopping(_players[f.EndedSeat], f.Skipped);
            }
            return;
        }
        var p = _players[f.SeatCursor]; var progress = _wushuangPlayers[p.Seat];
        if (!p.IsAlive) { AdvanceWushuangShopSeat(f); return; }
        if (!p.IsHuman)
        {
            if (f.Opening)
            {
                var offer = progress.Offers.OrderByDescending(o => WushuangTactics.ById[o.TacticId!.Value].Cost).First();
                BuyWushuangOffer(p.Seat, offer, null, true);
                if (TryBeginWushuangAcquisition(f.Id, offer)) return;
            }
            else if (RunWushuangAiShop(p, f.Id)) return;
            AdvanceWushuangShopSeat(f); return;
        }
        var choices = f.Opening ? progress.Offers.Select(o => new PromptChoice(new("wushuang.opening." + o.Id), o.Name, [], [],
            new Dictionary<string, string> { ["program-action"] = "wushuang-choice", ["offer-id"] = o.Id })).ToArray()
            : [new PromptChoice(new("wushuang.finish." + f.Id), "结束购买", [], [], new Dictionary<string, string> { ["program-action"] = "wushuang-choice" })];
        _pendingDecision = new(DecisionKind.ProgramTrigger, p.Seat, f.Opening ? "请选择一个初始战法" : "请选择购买商品", [], [])
        { PromptId = CreatePromptId(), Choices = Array.AsReadOnly(choices), IsPrivate = true,
            SkillPrompt = new(WushuangMode.ModeId, f.Opening ? "初始奖励" : "虎符商店", "单骑无双", "战法不占额外技能槽。") };
        _status = EngineStatus.AwaitingHumanResponse;
    }
    [ProgramAnswer("wushuang-choice")]
    private void AnswerWushuangChoice(PromptChoice choice)
    {
        if (_pendingDecision is not { } p || ActiveWushuangWindow(p.PlayerSeat) is not { } f)
            throw new InvalidOperationException("Wushuang choice lost its owning frame.");
        var offer = f.Opening ? _wushuangPlayers[p.PlayerSeat].Offers.Single(o => o.Id == choice.Parameters["offer-id"]) : null;
        ClearPendingDecision();
        if (offer is not null) BuyWushuangOffer(p.PlayerSeat, offer, null, true);
        if (offer is not null && TryBeginWushuangAcquisition(f.Id, offer)) return;
        AdvanceWushuangShopSeat(f);
    }
    private void AdvanceWushuangShopSeat(WushuangIntermissionFrame f)
    {
        // Commit the next seat before any movement children run: replay or recovery
        // must never pay this purchase or repeat this AI action a second time.
        ReplaceRuntimeFrame(f.Id, f with { SeatCursor = f.SeatCursor + 1 });
        AdvanceRulesAndPublishState();
        if (_resolutionStack.LastOrDefault()?.Id == f.Id) ContinueWushuangIntermission(f.Id);
    }
    private void ChangeWushuangTokens(int seat, int delta, string reason)
    {
        var p = _wushuangPlayers[seat]; p.Tokens = checked(p.Tokens + delta);
        if (p.Tokens < 0) throw new InvalidOperationException("Wushuang currency cannot be negative.");
        AdvanceEventRulesAndQueueFact(new WushuangTokensChangedEvent(seat, delta, p.Tokens, reason));
    }
    private void RefreshWushuangOffers(int seat, bool opening = false)
    {
        var p = _wushuangPlayers[seat]; p.RefreshNumber++;
        var random = new DeterministicRandom(unchecked(_options.Seed * 397 ^ seat * 7919 ^ p.RefreshNumber * 104729));
        var tactics = WushuangTactics.All.Where(t => t.IsImplemented && (opening || !t.StartOnly) && !p.Tactics.Contains(t.Id) &&
            (t.SkillId is null || _contentRegistry.Skills.TryGetValue(t.SkillId, out var definition) && definition.ImplementationStatus == SkillImplementationStatus.Complete)).ToList();
        random.Shuffle(tactics); var offers = new List<WushuangShopOffer>();
        // A removed entry must not reorder all surviving opening candidates.
        if (opening) foreach (var t in tactics.Where(t => t.IsAvailableInShop).Take(3)) offers.Add(TacticOffer(t, offers.Count, 0));
        else
        {
            var owned = EnabledContentSkillIds(_players[seat]).ToHashSet(StringComparer.Ordinal);
            var skills = WushuangCatalogue.Skills.Where(item => item.IsAvailableInShop && item.SkillId is { } id &&
                _contentRegistry.Skills.TryGetValue(id, out var s) && s.ImplementationStatus == SkillImplementationStatus.Complete &&
                WushuangCatalogue.IsSkillAllowed(s) && !owned.Contains(id)).ToList();
            var candidates = skills.Select(skill => new WushuangShopOffer("", skill.Name, skill.Description,
                skill.Cost, skill.SkillId, WushuangOfferKind.Skill, 1) { Quality = skill.Quality })
                .Concat(tactics.Where(t => t.IsAvailableInShop).Select(t => TacticOffer(t, 0, t.Cost)))
                .Concat(WushuangCatalogue.Cards.Where(card => card.IsAvailableInShop && card.Kind is { } kind &&
                    (!card.IsEquipment || _players[seat].EquipmentSlotCapacity(EquipmentCatalog.Get(kind).Slot) > 0))
                    .Select(card => new WushuangShopOffer("", card.Name,
                        card.IsEquipment ? card.Description + "\n购买后立即装备，离开装备区销毁。" : "获得这张手牌。",
                        card.Cost, null, WushuangOfferKind.Card, 1)
                        { CardKind = card.Kind, CardOfferId = card.Id, Quality = card.Quality })).ToList();
            // The server's weights are not public. Local sampling gives every
            // eligible catalogue entry equal weight, without fixed type slots.
            random.Shuffle(candidates);
            offers.AddRange(candidates.Take(p.Tactics.Contains(2289) ? 5 : 4)
                .Select((offer, slot) => offer with { Id = Id(slot) }));
        }
        p.Offers = Array.AsReadOnly(offers.ToArray()); p.Purchased = Array.AsReadOnly(Array.Empty<WushuangShopOffer>());
        AdvanceEventRulesAndQueueFact(new WushuangShopRefreshedEvent(seat, p.RefreshNumber));
        string Id(int slot) => $"wushuang:{seat}:{p.RefreshNumber}:{slot}:offer";
        WushuangShopOffer TacticOffer(WushuangTactic t, int slot, int cost) => new(Id(slot), t.Name, t.Description, cost, null, WushuangOfferKind.Tactic, 1) { TacticId = t.Id, Quality = t.Quality };
    }
    private CommandResult SubmitBuyWushuangOffer(BuyWushuangOfferCommand command)
    {
        if (!CanUseWushuangShop(command.ActorSeat)) return Reject(CommandErrorCode.IllegalAction, "当前不在虎符商店购买阶段。");
        var f = ActiveWushuangWindow(command.ActorSeat)!;
        var offer = _wushuangPlayers[command.ActorSeat].Offers.SingleOrDefault(o => o.Id == command.OfferId);
        var error = ValidateWushuangPurchase(command.ActorSeat, offer, command.ReplaceSkillId);
        if (error is not null) return Reject(CommandErrorCode.IllegalAction, error);
        return Accept(() => { ClearPendingDecision(); BuyWushuangOffer(command.ActorSeat, offer!, command.ReplaceSkillId, f.Opening);
            if (TryBeginWushuangAcquisition(f.Id, offer!)) return;
            if (f.Opening)
            {
                AdvanceWushuangShopSeat(f);
                if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
            }
            else CompleteWushuangShopAction(f.Id); });
    }
    private string? ValidateWushuangPurchase(int seat, WushuangShopOffer? offer, string? replacement)
    {
        if (offer is null || offer.Cost > _wushuangPlayers[seat].Tokens) return "商品已刷新或虎符不足。";
        if (offer.CardKind is { } unavailableCard && !WushuangCatalogue.IsCardAllowed(unavailableCard) ||
            offer.SkillId is { } unavailableSkill && !WushuangCatalogue.IsSkillAllowed(_contentRegistry.GetSkill(unavailableSkill)) ||
            offer.TacticId is { } unavailableTactic && !WushuangTactics.ById[unavailableTactic].IsAvailableInShop)
            return "此商品不适用于单骑无双。";
        if (offer.TacticId is { } t && _wushuangPlayers[seat].Tactics.Contains(t)) return "已拥有这个战法。";
        if (offer.CardKind is { } cardKind && EquipmentCatalog.IsEquipment(cardKind) &&
            _players[seat].EquipmentSlotCapacity(EquipmentCatalog.Get(cardKind).Slot) == 0) return "对应装备栏已废除。";
        var extra = _players[seat].SkillGrants.Grants.Where(g => g.SourceId == WushuangGrantSource).ToArray();
        if (offer.SkillId is null) return replacement is null ? null : "战法与卡牌不占额外技能槽。";
        if (EnabledContentSkillIds(_players[seat]).Contains(offer.SkillId, StringComparer.Ordinal)) return "已拥有这个技能。";
        if (replacement is not null && !extra.Any(g => g.SkillId == replacement) && !WushuangReplaceableOriginalSkills(seat).Contains(replacement))
            return "请选择可替换的额外技能；一忘皆空允许替换武将初始技能。";
        return extra.Length >= WushuangExtraSkillCapacity(seat) && replacement is null ? "额外技能槽已满，请选择要替换的技能。" : null;
    }
    private void BuyWushuangOffer(int seat, WushuangShopOffer offer, string? replacement, bool opening = false)
    {
        var p = _wushuangPlayers[seat]; var player = _players[seat];
        if (offer.Cost != 0) ChangeWushuangTokens(seat, -offer.Cost, "purchase");
        p.Offers = Array.AsReadOnly(p.Offers.Where(o => o.Id != offer.Id).ToArray()); p.Purchased = Array.AsReadOnly(p.Purchased.Append(offer).ToArray());
        if (offer.SkillId is { } skill)
        {
            var original = replacement is not null && WushuangReplaceableOriginalSkills(seat).Contains(replacement);
            if (replacement is not null)
                foreach (var g in player.SkillGrants.Grants.Where(g => g.SkillId == replacement &&
                    (g.SourceId == WushuangGrantSource || original && IsWushuangOriginalGrant(g))).ToArray())
                    player.SkillGrants.RemoveGrant(g.GrantId);
            Grant(skill, original ? WushuangOriginalReplacementSource : WushuangGrantSource);
        }
        else if (offer.TacticId is { } t)
        {
            p.Tactics.Add(t); if (WushuangTactics.ById[t].SkillId is { } passive) Grant(passive, "mode:wushuang-tactic");
            if (t is 2287 or 2288 or 2369) ChangeWushuangTokens(seat, t == 2287 ? 1 : t == 2288 ? 2 : 5, "tactic-acquired");
            if (t is 2291 or 2292) ChangeWushuangFreeRerolls(seat, t == 2291 ? 2 : 4, "tactic-acquired");
            if (t == 2290 && p.FreeRerolls == 0) ChangeWushuangFreeRerolls(seat, 1, "tactic-acquired");
            AdvanceEventRulesAndQueueFact(new WushuangTacticAcquiredEvent(seat, t, opening));
        }
        else if (offer.CardOfferId is { } cardOfferId) PlaceWushuangShopCard(seat, offer.Id, WushuangCatalogue.Cards.Single(c => c.Id == cardOfferId));
        else if (offer.CardKind is { } kind) PlaceShanhePhysicalCard(new(kind, kind == CardKind.Dodge ? Suit.Spade : Suit.Heart, kind == CardKind.Dodge ? 2 : 7), seat, equipment: false);
        AdvanceEventRulesAndQueueFact(new WushuangOfferBoughtEvent(seat, offer.Id, offer.SkillId, replacement, offer.Cost));
        var itemName = offer.CardKind is { } bought && !EquipmentCatalog.IsEquipment(bought) ? "一张手牌" : $"【{offer.Name}】";
        AddLog("WushuangPurchase", $"{player.Name}{(opening ? "选择初始战法" : $"花费{offer.Cost}虎符购买")}{itemName}。", seat);
        void Grant(string id, string source)
        { var g = $"{source}:{seat}:{offer.Id}"; player.SkillGrants.Grant(new(g, id, g, source)); _skillBindingIndex.Invalidate(seat); RegisterTaggedConversionSkill(player, id); }
    }
    private CommandResult SubmitRerollWushuangShop(RerollWushuangShopCommand c)
    {
        if (!CanUseWushuangShop(c.ActorSeat) || ActiveWushuangWindow(c.ActorSeat) is not { Opening: false } f ||
            _wushuangPlayers[c.ActorSeat] is { FreeRerolls: 0, Tokens: < 1 })
            return Reject(CommandErrorCode.IllegalAction, "当前不能刷新商店，或虎符不足。");
        return Accept(() => { ClearPendingDecision();
            if (_wushuangPlayers[c.ActorSeat].FreeRerolls > 0) ChangeWushuangFreeRerolls(c.ActorSeat, -1, "refresh");
            else ChangeWushuangTokens(c.ActorSeat, -1, "refresh");
            RefreshWushuangOffers(c.ActorSeat); CompleteWushuangShopAction(f.Id); });
    }
    private CommandResult SubmitUpgradeWushuangShop(UpgradeWushuangShopCommand c) => Reject(CommandErrorCode.IllegalAction, "单骑无双没有经验或商店升级。");
    private CommandResult SubmitFinishWushuangShop(FinishWushuangShopCommand c)
    {
        if (!CanUseWushuangShop(c.ActorSeat) || ActiveWushuangWindow(c.ActorSeat) is not { Opening: false } f)
            return Reject(CommandErrorCode.IllegalAction, "请先选择初始战法，或等待商店开放。");
        return Accept(() => { ClearPendingDecision(); AdvanceWushuangShopSeat(f); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); });
    }
    private CommandResult SubmitToggleWushuangShopLock(ToggleWushuangShopLockCommand c)
    {
        if (!CanUseWushuangShop(c.ActorSeat) || ActiveWushuangWindow(c.ActorSeat) is not { Opening: false }) return Reject(CommandErrorCode.IllegalAction, "只能在购买阶段锁定商品。");
        return Accept(() => { var p = _wushuangPlayers[c.ActorSeat]; p.IsLocked = !p.IsLocked; AdvanceEventRulesAndQueueFact(new WushuangShopLockChangedEvent(c.ActorSeat, p.IsLocked)); AdvanceRulesAndPublishState(); });
    }
    private void CompleteWushuangShopAction(long frameId)
    {
        AdvanceRulesAndPublishState(); if (_resolutionStack.LastOrDefault()?.Id == frameId) ContinueWushuangIntermission(frameId);
        if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
    }
    private bool RunWushuangAiShop(CharacterState player, long windowId)
    {
        var p = _wushuangPlayers[player.Seat]; var extras = player.SkillGrants.Grants.Count(g => g.SourceId == WushuangGrantSource);
        var offer = p.Offers.OrderBy(o => o.Kind == WushuangOfferKind.Tactic ? 0 : o.Kind == WushuangOfferKind.Skill ? 1 : 2)
            .FirstOrDefault(o => !(o.SkillId is not null && extras >= WushuangExtraSkillCapacity(player.Seat)) && ValidateWushuangPurchase(player.Seat, o, null) is null);
        if (offer is null) return false;
        BuyWushuangOffer(player.Seat, offer, null);
        return TryBeginWushuangAcquisition(windowId, offer);
    }
}
