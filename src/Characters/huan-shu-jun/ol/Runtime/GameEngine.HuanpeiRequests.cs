using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HuanpeiOpen = global::CardGame.Core.RuntimeSkills.RuntimeRules_huan_shu_jun_ol_HuanpeiRequests.HuanpeiOpen;
    private static readonly CardKind[] HuanpeiKinds = [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol];
    private HuanpeiNativeRequest? CaptureHuanpeiRequest(PendingDecision p, ResolutionFrame? suppliedTop = null)
    {
        var top = suppliedTop ?? _resolutionStack.LastOrDefault();
        if (top is null || top is ProgramSkillFrame { HuanShuJun: { Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse } }) return null;
        HuanpeiNativeRequest? request = null;
        if (top is ProgramSkillFrame f && p.Kind == DecisionKind.ProgramTrigger)
        {
            if (IsWenyiPeachRequest(f) && p.PlayerSeat == f.OwnerSeat)
                request = new(HuanpeiIntent.WenyiPeach, f.Id, f.Id, null, p.PlayerSeat, f.InstructionIndex, CardKind.Peach, f.HuanShuJun!.TargetSeat, p.PromptId, p.Revision);
            else if (f.KeBiNengPingduan is { Stage: KeBiNengPingduanStage.BasicChoice } ke && ke.ActorSeat == p.PlayerSeat)
                request = new(HuanpeiIntent.KeBiNengBasic, f.Id, f.Id, null, p.PlayerSeat, f.InstructionIndex, null, null, p.PromptId, p.Revision);
            else if (f.FixedTargetSlash is { Issued: false } fixedSlash && f.OwnerSeat == p.PlayerSeat &&
                (GetPausedPrivateOfferEffect(f)?.Op == SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner || IsAnniversaryLeiBoFixedSlash(f)))
                request = new(HuanpeiIntent.FixedSlash, f.Id, f.Id, null, p.PlayerSeat, f.InstructionIndex, CardKind.Slash, fixedSlash.TargetSeat, p.PromptId, p.Revision);
        }
        if (request is null && DrawFundedDistinctBasicContext(p, top) is { } n)
        {
            var intent = n.Intent switch { DrawFundedDistinctBasicIntent.OwnSlashDodge => HuanpeiIntent.OwnSlashDodge, DrawFundedDistinctBasicIntent.Dying => HuanpeiIntent.Dying, DrawFundedDistinctBasicIntent.BorrowedSword => HuanpeiIntent.BorrowedSword, DrawFundedDistinctBasicIntent.Qinglong => HuanpeiIntent.Qinglong, DrawFundedDistinctBasicIntent.ProgramSlash => HuanpeiIntent.ProgramSlash, DrawFundedDistinctBasicIntent.ProgramNearestSlash => HuanpeiIntent.ProgramNearestSlash, DrawFundedDistinctBasicIntent.NearestLegalSlash => HuanpeiIntent.NearestLegalSlash, _ => HuanpeiIntent.AssistedSlash };
            request = new(intent, n.ParentFrameId, n.RequestFrameId, n.ParentActionId, p.PlayerSeat, n.Cursor, intent == HuanpeiIntent.OwnSlashDodge ? CardKind.Dodge : intent == HuanpeiIntent.Dying ? null : CardKind.Slash, n.FixedTargetSeat, p.PromptId, p.Revision);
        }
        if (request is not null && request.ParentFrameId is { } parentId && _resolutionStack.SingleOrDefault(x => x.Id == parentId) is ProgramSkillFrame parent)
            request = request with { ParentSkillId = parent.SkillId, ParentSkillInstanceId = parent.SkillInstanceId, ParentGameplayHash = parent.GameplayHash, ParentOperation = GetPausedPrivateOfferEffect(parent)?.Op };
        return request;
    }
    private DrawFundedBasicNeed HuanpeiNativeNeed(HuanpeiNativeRequest n) => new(n.Intent switch
    {
        HuanpeiIntent.OwnSlashDodge => DrawFundedDistinctBasicIntent.OwnSlashDodge, HuanpeiIntent.Dying => DrawFundedDistinctBasicIntent.Dying,
        HuanpeiIntent.BorrowedSword => DrawFundedDistinctBasicIntent.BorrowedSword, HuanpeiIntent.Qinglong => DrawFundedDistinctBasicIntent.Qinglong,
        HuanpeiIntent.ProgramSlash => DrawFundedDistinctBasicIntent.ProgramSlash, HuanpeiIntent.ProgramNearestSlash => DrawFundedDistinctBasicIntent.ProgramNearestSlash,
        HuanpeiIntent.NearestLegalSlash => DrawFundedDistinctBasicIntent.NearestLegalSlash, _ => DrawFundedDistinctBasicIntent.AssistedSlash
    }, n.ParentFrameId, n.RequestFrameId, n.ParentActionId, n.Cursor, n.FixedTargetSeat);
    private bool HuanpeiRequestParentCurrent(HuanpeiNativeRequest n)
    {
        if (n.Intent == HuanpeiIntent.Play) return _phase == TurnPhase.Play && _currentSeat == n.ActorSeat;
        if (n.RequestFrameId is not { } id || _resolutionStack.SingleOrDefault(x => x.Id == id) is not { } request) return false;
        if (n.ParentSkillId is null) return true;
        return request is ProgramSkillFrame p && p.Id == n.ParentFrameId && p.InstructionIndex == n.Cursor && p.SkillId == n.ParentSkillId && p.SkillInstanceId == n.ParentSkillInstanceId && p.GameplayHash == n.ParentGameplayHash && GetPausedPrivateOfferEffect(p)?.Op == n.ParentOperation;
    }
    private bool HuanpeiNativeLegal(HuanpeiNativeRequest n, CardKind kind, int? target)
    {
        if (!HuanpeiRequestParentCurrent(n) || !HuanpeiKinds.Contains(kind) || IsCardUseForbidden(n.ActorSeat, kind, CardActionType.Use) || IsCuiYanNextUseForbidden(n.ActorSeat, [], null) || !_players[n.ActorSeat].IsAlive) return false;
        var actor = _players[n.ActorSeat];
        if (n.Intent == HuanpeiIntent.WenyiPeach) return kind == CardKind.Peach && _resolutionStack.SingleOrDefault(x => x.Id == n.ParentFrameId) is ProgramSkillFrame p && TryGetWenyiPeachRequest(p, out var a, out var t) && a == actor.Seat && t == target && WenyiTargetCanReceivePeach(p);
        if (n.Intent == HuanpeiIntent.FixedSlash) return IsSlashCard(kind) && target is { } seat && _resolutionStack.SingleOrDefault(x => x.Id == n.ParentFrameId) is ProgramSkillFrame { FixedTargetSlash: { } fs } && seat == fs.TargetSeat && CanUseSlashTarget(actor, _players[seat], new(0, kind, Suit.None, 0), effectiveKind: kind, physicalCardIds: [], ignoreDistance: fs.IgnoreDistance);
        if (n.Intent == HuanpeiIntent.KeBiNengBasic)
        {
            var source = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HuanShuJun?.Request == n)?.HuanShuJun?.Source ?? HuanpeiSources(actor).FirstOrDefault();
            return source is not null && HuanpeiNativePlayActions(actor, source, true).Any(a => a.PlayedCardKind == kind && (a.TargetSeats.Count == 0 ? target is null || target == actor.Seat : a.TargetSeats.Contains(target ?? -1)));
        }
        return DrawFundedDistinctBasicNativeResponseLegal(actor, HuanpeiNativeNeed(n), kind, target);
    }
    private IReadOnlyList<LegalAction> HuanpeiNativePlayActions(CharacterState actor, CardConversionSource source, bool requested = false)
    {
        if (!actor.IsAlive || _winner != Winner.None || !requested && (_phase != TurnPhase.Play || _currentSeat != actor.Seat) || IsCuiYanNextUseForbidden(actor.Seat, [], null)) return [];
        var result = new List<LegalAction>();
        foreach (var kind in HuanpeiKinds.Where(k => k != CardKind.Dodge && !IsCardUseForbidden(actor.Seat, k, CardActionType.Use)))
        {
            var card = new Card(0, kind, Suit.None, 0);
            if (IsSlashCard(kind))
            {
                var targets = _players.Where(p => p.IsAlive && CanUseSlashTarget(actor, p, card, source, kind, noEffectiveRank: true, physicalCardIds: [])).ToArray();
                result.AddRange(targets.Select(p => new LegalAction(LegalActionKind.Slash, 0, p.Seat, $"视为对{p.Name}使用【{card.DisplayName}】", kind) { ConversionSource = source }));
                AddProgramTargetCountSlashActions(result, actor, card, targets, card.DisplayName, kind, source, null);
            }
            else if (kind == CardKind.Peach && IsRuleWounded(actor) && !HasSelfCardTargetProhibition(actor.Seat) && !IsCardTargetProhibited(actor, kind, Suit.None) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None)) result.Add(new(LegalActionKind.Peach, 0, null, "视为使用【桃】", kind) { ConversionSource = source });
            else if (kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) && !IsCardTargetProhibited(actor, kind, Suit.None) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None) && (CanUseJianjiOrdinaryAlcohol(actor, null) || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) || HasNextUnlimitedCard(actor) || HasMouWujingAllowance(actor) || HasFriendQihuiAllowance(actor) || HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, kind))) result.Add(new(LegalActionKind.Alcohol, 0, null, "视为使用【酒】", kind) { ConversionSource = source });
        }
        AddNextCardTargetAdjustmentActions(result, actor); AddNextActualUseAdjustmentActions(result, actor);
        return Array.AsReadOnly(result.ToArray());
    }
    private IReadOnlyList<LegalAction> HuanpeiPaidPlayActions(ProgramSkillFrame f) => f.HuanShuJun is { Paid: true } r ? HuanpeiNativePlayActions(_players[f.OwnerSeat], r.Source, r.Request?.Intent == HuanpeiIntent.KeBiNengBasic) : [];
    private IReadOnlyList<PromptChoice> HuanpeiResponseChoices(PendingDecision p)
    {
        if (!_started || !HasHuanShuJunCapability || CaptureHuanpeiRequest(p) is not { } n || !CanAdjustHuanpeiHand(_players[p.PlayerSeat])) return [];
        var result = new List<PromptChoice>();
        foreach (var source in HuanpeiSources(_players[p.PlayerSeat]))
        {
            if (n.Intent == HuanpeiIntent.KeBiNengBasic)
            {
                if (HuanpeiNativePlayActions(_players[p.PlayerSeat], source, true).Count == 0) continue;
                var args = new Dictionary<string, string> { [HuanpeiOpen] = n.Intent.ToString() }; AddConversionParameters(args, source);
                result.Add(new(new($"huanpei.open.{p.PromptId.Value}.{source.SkillInstanceId}.basic"), "环佩：调整手牌，然后使用一张基本牌", [], [], args)); continue;
            }
            var kinds = n.Intent == HuanpeiIntent.OwnSlashDodge ? new[] { CardKind.Dodge } : n.Intent == HuanpeiIntent.Dying ? new[] { CardKind.Peach, CardKind.Alcohol } : n.Intent == HuanpeiIntent.WenyiPeach ? new[] { CardKind.Peach } : new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash };
            foreach (var kind in kinds)
            foreach (var target in n.FixedTargetSeat is { } fixedTarget ? new[] { fixedTarget } : p.ValidTargetSeats)
            {
                if (!HuanpeiNativeLegal(n, kind, target)) continue;
                var args = new Dictionary<string, string> { [HuanpeiOpen] = n.Intent.ToString(), ["output-kind"] = kind.ToString(), ["native-target-seat"] = target.ToString(CultureInfo.InvariantCulture) }; AddConversionParameters(args, source);
                IReadOnlyList<int> responseTargets = (n.Intent is HuanpeiIntent.OwnSlashDodge or HuanpeiIntent.Dying) ? Array.Empty<int>() : new[] { target };
                result.Add(new(new($"huanpei.open.{p.PromptId.Value}.{source.SkillInstanceId}.{kind}.{target}"), $"环佩：调整手牌，视为使用【{CardCatalog.Get(kind).DisplayName}】", [], responseTargets, args));
            }
        }
        return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private PendingDecision? AddHuanpeiRequestChoices(PendingDecision? p)
    {
        if (p is null || p.Choices.Any(c => c.Parameters.ContainsKey(HuanpeiOpen))) return p;
        var choices = HuanpeiResponseChoices(p); return choices.Count == 0 ? p : p with { Choices = Array.AsReadOnly(p.Choices.Concat(choices).ToArray()) };
    }
    private static PendingDecision HuanpeiNativeDecision(PendingDecision p) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_huan_shu_jun_ol_HuanpeiRequests.HuanpeiNativeDecision(p);
    private bool IsHuanpeiAnswer(ChoiceId id) => _pendingDecision?.Choices.Any(c => c.Id == id && c.Parameters.ContainsKey(HuanpeiOpen)) == true;
    private CommandResult SubmitHuanpeiAnswer(int actor, PromptId prompt, ChoiceId id)
    {
        if (_pendingDecision is not { } p || p.PlayerSeat != actor || p.PromptId != prompt) return Reject(CommandErrorCode.InvalidPrompt, "环佩必须回答当前真实请求。");
        var c = p.Choices.SingleOrDefault(c => c.Id == id && c.Parameters.ContainsKey(HuanpeiOpen));
        if (c is null) return Reject(CommandErrorCode.InvalidChoice, "环佩选择未发布。");
        return Accept(() => { TryResolveHuanpeiResponse(c, true); });
    }
    private bool TryResolveHuanpeiResponse(PromptChoice choice, bool advance)
    {
        if (!choice.Parameters.ContainsKey(HuanpeiOpen)) return false;
        var p = _pendingDecision ?? throw new InvalidOperationException("Huanpei lost its original native prompt.");
        if (!HuanpeiResponseChoices(HuanpeiNativeDecision(p)).Any(c => AssistedChoicesEqual([c], [choice])) || CaptureHuanpeiRequest(p) is not { } n) throw new InvalidOperationException("Huanpei changed its published native need.");
        var source = RequireConversionSource(choice); var definition = _contentRegistry.GetSkill(source.SkillId).Program!;
        var r = new HuanShuJunReceipt(source, definition.GameplayHash, SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, n.ParentFrameId ?? 0, _turnNumber, _turnProgression.OwnerSeat, HuanShuJunStage.AdjustHand)
        { Request = n, OriginalDecision = HuanpeiNativeDecision(p), Round = _roundNumber, TargetSeat = n.FixedTargetSeat ?? -1, Kind = choice.Parameters.TryGetValue("output-kind", out var raw) ? Enum.Parse<CardKind>(raw) : null, Targets = choice.Targets, FrozenHandCount = GetHand(_players[p.PlayerSeat]).Count, FrozenHp = _players[p.PlayerSeat].Hp };
        var f = new ProgramSkillFrame(++_resolutionSequence, p.PlayerSeat, source.SkillId, source.BindingId, definition.GameplayHash, 0, [], []) { SkillInstanceId = source.SkillInstanceId, HuanShuJun = r };
        ClearPendingDecision(); PushRuntimeFrame(f);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id, f.OwnerSeat, f.SkillId, f.ActivationId));
        AdvanceEventRulesAndQueueFact(new HuanShuJunStartedEvent(f.Id, source, definition.GameplayHash, r.Op, r.WindowId, r.ActualTurn, r.TurnOwner, r.TargetSeat, r.FrozenHandCount, r.FrozenHp));
        AdvanceEventRulesAndQueueFact(new HuanpeiRequestCapturedEvent(f.Id, n, HuanShuJunDecisionHash(r.OriginalDecision!)));
        var host = new ProgramSkillHost(this); new SkillProgramExecutor().Run(f.Id, host, host);
        AdvanceRulesAndPublishState(); if (advance && _options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); return true;
    }
    private void ContinueHuanpeiOriginalRequest(ProgramSkillFrame f)
    {
        var r = f.HuanShuJun!;
        if (r.Request!.Intent == HuanpeiIntent.KeBiNengBasic) { PublishHuanShuJun(f); return; }
        var targets = r.Request.FixedTargetSeat is { } target ? new[] { target } : r.Targets;
        if (r.Kind is not { } kind || !HuanpeiNativeLegal(r.Request, kind, targets.FirstOrDefault(-1))) { FinishHuanShuJun(f); return; }
        ChooseHuanpeiOutput(f, kind, targets);
    }
    private void ChooseHuanpeiOutput(ProgramSkillFrame f, CardKind kind, IReadOnlyList<int> targets)
    {
        var r = f.HuanShuJun!; if (!r.Paid || r.Stage != HuanShuJunStage.BasicChoice || r.Request is null || !_players[f.OwnerSeat].IsAlive) throw new InvalidOperationException("Huanpei cannot issue an unpaid or dead user's output.");
        if (r.Request.Intent is HuanpeiIntent.Play or HuanpeiIntent.KeBiNengBasic ? !HuanpeiPaidPlayActions(f).Any(a => a.PlayedCardKind == kind && a.TargetSeats.SequenceEqual(targets)) : !HuanpeiNativeLegal(r.Request, kind, targets.FirstOrDefault(-1))) throw new InvalidOperationException("Huanpei's paid output changed its native target or current use legality.");
        AdvanceEventRulesAndQueueFact(new HuanpeiOutputChosenEvent(f.Id, kind, targets.Count)); for (var i = 0; i < targets.Count; i++) AdvanceEventRulesAndQueueFact(new HuanpeiTargetChosenEvent(f.Id, i, targets[i]));
        var origin = new HuanpeiPaidOrigin(f.Id, r.Source, r.GameplayHash, r.Round, r.ActualTurn, r.TurnOwner, r.Request, r.FrozenHandCount, r.FrozenHp, Math.Abs(r.FrozenHandCount - r.FrozenHp), r.Before, r.After, kind) { Targets = targets };
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { HuanShuJun = null, PendingMovementContinuation = null }); AdvanceEventRulesAndQueueFact(new HuanShuJunCompletedEvent(f.Id, r.Op, true)); FinishProgramSkill(f, true); ExecuteHuanpeiPaid(origin);
    }
    private void RepublishHuanpeiOriginal(HuanShuJunReceipt r)
    {
        if (r.Request is null || r.OriginalDecision is null || !HuanpeiRequestParentCurrent(r.Request) || !_players[r.Source.OwnerSeat].IsAlive || _winner != Winner.None) return;
        _pendingDecision = r.OriginalDecision with { PromptId = CreatePromptId(), Revision = Revision }; _status = _players[r.Source.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    private bool TryAdvanceHuanpeiAi()
    {
        if (_pendingDecision is not { } p || _players[p.PlayerSeat].IsHuman) return false;
        var c = p.Choices.FirstOrDefault(c => c.Parameters.ContainsKey(HuanpeiOpen)); return c is not null && TryResolveHuanpeiResponse(c, false);
    }
    private bool HasHuanpeiRequiredBasic(CharacterState actor, CardKind kind) => HuanpeiSources(actor).Any() && CanAdjustHuanpeiHand(actor) && !IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) && !IsCuiYanNextUseForbidden(actor.Seat, [], null) && (kind != CardKind.Dodge || ActiveFactionDefense is null && IsProgramResponseCardUse(actor, kind));
    private bool HasHuanpeiDyingResponse(CharacterState actor) => ActiveDying is { } d && HuanpeiSources(actor).Any() && CanAdjustHuanpeiHand(actor) && new[] { CardKind.Peach, CardKind.Alcohol }.Any(kind => HuanpeiNativeLegal(new(HuanpeiIntent.Dying, d.FrameId, d.FrameId, null, actor.Seat, d.ResponderIndex, null, d.VictimSeat, new(0), Revision), kind, d.VictimSeat));
    private bool HasHuanpeiForcedSlash(CharacterState actor, CharacterState target, bool qinglong) => HuanpeiSources(actor).Any() && CanAdjustHuanpeiHand(actor) && new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }.Any(kind => !IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) && !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) && !IsCardTargetProhibited(target, kind, Suit.None) && (qinglong ? CanUseQinglongCrescentBladeTarget(actor, target, kind, null, false, Suit.None, 0) : IsLegalBorrowedSwordSlashTarget(actor, target, kind, Suit.None, false, null, 0, [])));
    private bool HasHuanpeiProgramSlash(CharacterState actor, IEnumerable<int> targets)
    {
        if (!HuanpeiSources(actor).Any() || !CanAdjustHuanpeiHand(actor) || _resolutionStack.LastOrDefault() is not ProgramSkillFrame parent) return false;
        var candidates = targets.ToArray(); var prompt = new PendingDecision(DecisionKind.ProgramTrigger, actor.Seat, "", [], candidates) { Choices = [] };
        var need = CaptureHuanpeiRequest(prompt, parent);
        var ignore = IsQigongRequest(parent) || IsWuJingHejiRequest(parent);
        return candidates.Any(t => IsValidPlayerSeat(t) && _players[t].IsAlive && HuanpeiKinds.Where(IsSlashCard).Any(kind =>
            need is not null ? HuanpeiNativeLegal(need, kind, t) : CanUseSlashTarget(actor, _players[t], new(0, kind, Suit.None, 0), effectiveKind: kind, noEffectiveRank: true, physicalCardIds: [], ignoreDistance: ignore)));
    }
}
