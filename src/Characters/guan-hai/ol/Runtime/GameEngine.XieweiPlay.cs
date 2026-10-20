namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XieweiPlayPrefix = "xiewei-basic-play:";
    [ProgramActionSource(ProgramActionSourcePhase.Cardless, 27578)]
    private IEnumerable<LegalAction> BuildXieweiPlayActions(CharacterState owner)
    {
        if (!HasGuanHaiCapability || !owner.IsAlive || _winner != Winner.None || (_phase != TurnPhase.Play || owner.Seat != _currentSeat) && !IsKeBiNengPingduanBasicQuery(owner)) yield break;
        foreach (var s in GetProgramMultiCardViewAsSelections(owner, CardKind.Slash, false).Where(IsXieweiSelection))
        {
            var targets = XieweiPlayTargets(owner, s); if (targets.Length == 0) continue;
            var ids = Array.AsReadOnly(s.Cards.Select(c => c.Id).Order().ToArray());
            yield return new(LegalActionKind.UseProgramSkill, null, null, "卸尾：将两张手牌置为饵，视为使用杀", CardKind.Slash, MinCardCount: 2, MaxCardCount: 2, MinTargetCount: 1, MaxTargetCount: Math.Min(targets.Length, XieweiTargetMaximum(owner)))
            { ProgramSkillId = s.Source.SkillId, ProgramActivationId = XieweiPlayPrefix + "Slash:" + string.Join('-', ids), ConversionSource = s.Source, SelectableCardIds = ids, SelectableTargetSeats = Array.AsReadOnly(targets), ProgramAiHint = new(0, 0, 0, 0, 0, 1, false, false) };
        }
    }
    private int[] XieweiPlayTargets(CharacterState actor, ProgramMultiCardViewAsSelection s) => _players.Where(p => CanUseVirtualSlashTarget(actor, p, CardKind.Slash, Suit.None, effectiveColor: null, physicalCardIds: s.Cards.Select(c => c.Id).ToArray())).Select(p => p.Seat).ToArray();
    private int XieweiTargetMaximum(CharacterState actor) => Math.Max(1, ConvertRuleValue(EvaluateCardTargetCount(actor, CardKind.Slash))) + (HasLegacyNextCardTargetAdjustment(actor) || HasNextActualUseAdjustment(actor) ? 1 : 0);
    private CommandError? ValidateXieweiPlay(CharacterState actor, LegalAction a, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (ValidateProgramSelection(a, ids, targets) is { } error) return error;
        if (a.ConversionSource is not { } source || !IsXieweiSource(source) || GetProgramMultiCardViewAsSelections(actor, CardKind.Slash, false).SingleOrDefault(s => s.Source == source && s.Cards.Select(c => c.Id).Order().SequenceEqual(ids.Order())) is not { } selection) return new(CommandErrorCode.InvalidCard, "卸尾必须支付当前两张真实手牌。");
        return targets.Count < 1 || targets.Count > XieweiTargetMaximum(actor) || targets.Distinct().Count() != targets.Count || targets.Any(t => !XieweiPlayTargets(actor, selection).Contains(t)) ? new(CommandErrorCode.InvalidTarget, "卸尾的杀须遵守原生目标、距离及次数限制。") : null;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Submit, 27578)]
    private bool SubmitXieweiPlay(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!; if (!command.ActivationId.StartsWith(XieweiPlayPrefix, StringComparison.Ordinal)) return false;
        if (ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction) is { } error) { result = Reject(error.Code, error.Message); return true; }
        var actor = _players[command.ActorSeat]; var a = BuildXieweiPlayActions(actor).SingleOrDefault(a => a.ProgramSkillId == command.SkillId && a.ProgramActivationId == command.ActivationId);
        if (a is null || command.SkillOwnerSeat is not null) { result = Reject(CommandErrorCode.IllegalAction, "卸尾真实使用菜单已失效。"); return true; }
        if (ValidateXieweiPlay(actor, a, command.CardIds, command.TargetSeats) is { } invalid) { result = Reject(invalid.Code, invalid.Message); return true; }
        result = Accept(() => { ClearPendingDecision(); ExecuteXieweiPlay(actor, a, command.CardIds, command.TargetSeats); AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); }); return true;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Execute, 27578)]
    private bool ExecuteXieweiPlay(CharacterState actor, LegalAction a, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (a.ProgramActivationId?.StartsWith(XieweiPlayPrefix, StringComparison.Ordinal) != true) return false;
        if (ValidateXieweiPlay(actor, a, ids, targets) is { } invalid) throw new InvalidOperationException(invalid.Message);
        var s = GetProgramMultiCardViewAsSelections(actor, CardKind.Slash, false).Single(s => s.Source == a.ConversionSource && s.Cards.Select(c => c.Id).Order().SequenceEqual(ids.Order()));
        if (targets.Count == 1) ResolveSlashCore(actor, _players[targets.Single()], ApplyProgramUseAppearance(actor, s.Cards[0], s.Source), CardKind.Slash, actor.Seat, physicalCards: s.Cards, conversionSource: s.Source);
        else BeginXieweiMultiTargetSlash(actor, s, targets);
        return true;
    }
    private void BeginXieweiMultiTargetSlash(CharacterState actor, ProgramMultiCardViewAsSelection s, IReadOnlyList<int> targets)
    {
        var card = ApplyProgramUseAppearance(actor, s.Cards[0], s.Source); var armor = HasArmorBypass(actor);
        var id = BeginCardUse(card, actor.Seat, targets, CardKind.Slash, ignoresArmor: armor, physicalCardIds: s.Cards.Select(c => c.Id).ToArray(), conversionSource: s.Source);
        var nuzhan = GetNuzhanModifiers(id, actor); PaySlashUseMaterials(s.Cards, actor.Seat, null);
        var use = LifecycleCardUse(id)!; if (!IsXieweiMultiTargetUse(use)) throw new InvalidOperationException("Xiewei's multiple targets lost their original native allowance and paid costs.");
        var counted = use.UnlimitedUse != true && !HasIssuedJudgedRankSlashQuota(id, actor.Seat) && !HasIssuedWuLuKangDixianUse(id, actor.Seat) && !HasIssuedRoundGainedBasicBonus(id, actor.Seat) && !HasIssuedHuiyunFollowup(id, actor.Seat) && !HasIssuedChanshuangSlash(id, actor.Seat) && !HasIssuedDongxinEntitySlashQuota(id, actor.Seat) && !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(actor, s.Source) && _phase == TurnPhase.Play && actor.Seat == _currentSeat;
        if (counted) RecordSlashUseDebit(id, actor.Seat); MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, CardKind.Slash);
        var damage = 1 + actor.AlcoholDamageBonus + nuzhan.DamageBonus; CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        var pending = new FangtianHalberdHandle(this, id, actor.Seat, card, CardKind.Slash, armor, damage, targets, false, counted, s.Source); ActiveFangtianHalberd = pending;
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, s.Source.SkillId, s.Source.BindingId, actor.Seat, Array.AsReadOnly(s.Cards.Select(c => c.Id).ToArray()), CardKind.Slash, true, targets));
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id, CardKind.Slash, actor.Seat, targets[0], IgnoresArmor: armor)); foreach (var t in targets) NotifyAiOfSlash(actor, _players[t]); BeginNextFangtianHalberdTarget(pending);
    }
    private XieweiNativePayment? IssuedXieweiPayment(CardActionContext action) => _resolutionStack.OfType<CardUseFrame>().SelectMany(u => u.XieweiPayments ?? []).SingleOrDefault(p => p.Paid && p.ActionId == action.ActionId && XieweiActionCosts(p, action) && ValidXieweiPayment(p));
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 6)]
    private void CollectIssuedXieweiCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> candidates)
    {
        if (window != SkillProgramTriggerWindow.CardUseCommitted || IssuedXieweiPayment(action) is not { } p || ProgramEventHistory<XieweiCommittedEvent>().Any(e => e.PaymentId == p.Id)) return;
        var t = _contentRegistry.GetSkill(p.Source.SkillId).Program!.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse }]);
        var context = CreateCardActionProgramContext(action, window, 0, p.OwnerSeat, -1, CaptureProgramTriggerFacts(_players[p.OwnerSeat], action));
        candidates.Add(new(p.OwnerSeat, -1, p.Source.SkillId, t.Id, p.GameplayHash, p.Source.SkillInstanceId, t.Priority, context));
    }
    private bool IsIssuedXieweiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.SkillId != "ol:xiewei" || context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.OwnerSeat != c.OwnerSeat || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || ToSharedCandidate(w.Candidates[w.CandidateIndex]) != c || IssuedXieweiPayment(w.Action) is not { } p || p.Source.SkillInstanceId != c.SkillInstanceId || p.OwnerSeat != c.OwnerSeat || p.GameplayHash != c.GameplayHash || context.CardUse?.CardActionId != p.ActionId || GetProgramTrigger(c).Effects is not [{ Op: SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse }]) return false;
        return !ProgramEventHistory<XieweiCommittedEvent>().Any(e => e.PaymentId == p.Id);
    }
}
