using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BWeiyi = "wushuang:skill-3289", BZhendan = "wushuang:skill-3290",
        BLiantao = "wushuang:skill-3301", BBianyu = "wushuang:skill-3316", BYinglue = "wushuang:skill-3331",
        BXiaojie = "wushuang:skill-3333", BDingan = "wushuang:skill-3335", BChunlao = "wushuang:skill-3336";

    private bool HasWushuangBSkills => _contentRegistry.Skills.ContainsKey(BZhendan);
    private static CardKind BCanonicalName(CardKind kind) => IsSlashCard(kind) ? CardKind.Slash : kind;
    private static bool BBasic(CardKind kind) => IsSlashCard(kind) || kind is CardKind.Dodge or CardKind.Peach or CardKind.Alcohol;
    private static bool BOrdinaryTrick(CardKind kind) => MatchesSkillProgramCardCategory(kind, SkillProgramCardCategory.Trick) && !IsDelayedCard(kind);
    private CardConversionSource BSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool BSourceCurrent(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private bool BZhendanDisabled(int seat) => ProgramEventHistory<WushuangZhendanDisabledEvent>().Any(e => e.OwnerSeat == seat && e.RoundNumber == _roundNumber);

    private IEnumerable<ProgramTriggerCandidate> CollectWushuangBChunlaoLossCandidates(HpChangeContext change)
    {
        if (!HasWushuangBSkills || change.Kind != HpChangeKind.Loss) yield break;
        foreach (var player in _players.Where(p => p.IsAlive && _cardZones.Count(CardLocation.Chunlao(p.Seat)) > 0))
            foreach (var candidate in CollectProgramTriggerCandidates(player, SkillProgramTriggerWindow.AfterHpLost).Where(c => c.SkillId == BChunlao))
                yield return candidate;
    }
    private ProgramSkillWindowContext CreateWushuangBChunlaoLossContext(HpChangeContext change, ProgramTriggerCandidate c) =>
        new(SkillProgramTriggerWindow.AfterHpLost, change.Id, c.OwnerSeat, SourceSeat: change.SourceSeat, TargetSeat: change.TargetSeat,
            Amount: change.Amount, OccurrenceIndex: c.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]) with { HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore,
                HpAfterChange = change.HpAfter, HealthChangeKind = change.Kind }, HpChange: change);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 61200)]
    private void ObserveWushuangBSkills(IGameEvent payload)
    {
        if (!HasWushuangBSkills) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } native &&
            (native.Action is null || native.Action.Type == CardActionType.Use) &&
            !ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(e => e.OriginUseId == declared.ResolutionId && e.CardActionId == (native.Action?.ActionId ?? 0)))
            AdvanceEventRulesAndQueueFact(new WushuangCardUseNameRecordedEvent(_turnNumber, _roundNumber, declared.SourceSeat,
                BCanonicalName(declared.CardKind), native.Action?.ActionId ?? 0, declared.ResolutionId));
        if (payload is CardActionAcceptedEvent { Action: { Type: CardActionType.Response } a } &&
            a.ActorSeat == a.ProviderSeat && a.ResponderSeat == a.ActorSeat && a.RequesterSeat is null &&
            (a.EffectiveKind == CardKind.Nullification || a.EffectiveKind == CardKind.Dodge && ActiveCardAttack is { } defending &&
                defending.TargetSeat == a.ActorSeat && IsSlashCard(defending.EffectiveCardKind ?? CardKind.Slash) && ActiveGroupCard is null) &&
            !ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(e => e.CardActionId == a.ActionId))
            AdvanceEventRulesAndQueueFact(new WushuangCardUseNameRecordedEvent(_turnNumber, _roundNumber, a.ActorSeat,
                BCanonicalName(a.EffectiveKind), a.ActionId, 0));
        if (payload is ActualDiscardRecoveryPhaseStartedEvent { Phase: { Kind: ActualDiscardRecoveryPhaseKind.Draw or ActualDiscardRecoveryPhaseKind.Discard } } phase)
        {
            var kind = phase.Phase.Kind == ActualDiscardRecoveryPhaseKind.Draw ? TurnPhase.Draw : TurnPhase.Discard;
            var pending = ProgramEventHistory<WushuangYinglueArmedEvent>().Where(e => e.TargetSeat == phase.Phase.ActorSeat && e.Phase == kind &&
                !ProgramEventHistory<WushuangYinglueAppliedEvent>().Any(applied => applied.ProgramFrameId == e.ProgramFrameId)).ToArray();
            foreach (var grant in pending)
                AdvanceEventRulesAndQueueFact(new WushuangYinglueAppliedEvent(grant.ProgramFrameId, grant.TargetSeat, grant.Phase, grant.Amount, _turnNumber));
        }
        if (payload is DamageAppliedEvent damage)
        {
            foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.WushuangB is { Operation: SkillProgramEffectOp.ResolveWushuangLiantao, Stage: "using" }).ToArray())
            {
                var r = frame.WushuangB!;
                if (r.ChildUseId is { } useId && _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == useId) &&
                    damage.SourceSeat == frame.OwnerSeat && damage.TargetSeat == r.TargetSeat && !damage.SourceLess)
                    ReplaceRuntimeFrame(frame.Id, frame with { WushuangB = r with { Damage = r.Damage + damage.Amount } });
            }
        }
        if (payload is PlayerDyingEvent dying)
        {
            foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.WushuangB is { Operation: SkillProgramEffectOp.ResolveWushuangLiantao }).ToArray())
                if (dying.VictimSeat == frame.OwnerSeat || dying.VictimSeat == frame.WushuangB!.TargetSeat)
                    ReplaceRuntimeFrame(frame.Id, frame with { WushuangB = frame.WushuangB! with { EnteredDying = true } });
        }
    }

    // These issued next-phase adjustments survive the grantor losing the skill.
    private IEnumerable<RuleQueryContribution> WushuangSkillsBNumericContributions(CharacterState player, SkillRuleQuery query)
    {
        if (!HasWushuangBSkills || _currentSeat != player.Seat ||
            !((_phase == TurnPhase.Draw && query == SkillRuleQuery.DrawCount) || (_phase == TurnPhase.Discard && query == SkillRuleQuery.HandLimit))) yield break;
        foreach (var item in ProgramEventHistory<WushuangYinglueAppliedEvent>().Where(e => e.TargetSeat == player.Seat && e.TurnNumber == _turnNumber && e.Phase == _phase))
            yield return new FiniteRuleQueryContribution($"wushuang:yinglue:{item.ProgramFrameId}", SkillRuleOperation.Add, item.Amount);
    }

    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 61200)]
    private bool IsWushuangZhendanConversionForbidden(ProgramConfiguredViewAsQuery q) => q.Instance.SkillId == BZhendan &&
        (BZhendanDisabled(q.Owner.Seat) || ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(e => e.RoundNumber == _roundNumber &&
            e.ActorSeat == q.Owner.Seat && e.Name == BCanonicalName(q.Rule.OutputKind)));
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 61200)]
    private bool IsWushuangZhendanSubmittedConversionForbidden(ProgramViewAsLegalityQuery q) => q.Source.SkillId == BZhendan &&
        (BZhendanDisabled(q.Owner.Seat) || ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(e => e.RoundNumber == _roundNumber &&
            e.ActorSeat == q.Owner.Seat && e.Name == BCanonicalName(q.Kind)));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 61200)]
    private bool CanTriggerWushuangBSkill(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (c.SkillId == BWeiyi)
            return context.TargetSeat is { } target && IsValidPlayerSeat(target) && _players[target].IsAlive &&
                !ProgramEventHistory<WushuangWeiyiUsedEvent>().Any(e => e.OwnerSeat == c.OwnerSeat && e.SkillId == c.SkillId && e.TargetSeat == target);
        if (c.SkillId == BZhendan) return !BZhendanDisabled(c.OwnerSeat);
        if (c.SkillId == BXiaojie && trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveWushuangXiaojie))
            return context.CardUse is { } card && ProgramEventHistory<WushuangXiaojieAcceptedEvent>().Any(e =>
                e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId && e.IncomingUseId == card.ParentCardUseFrameId);
        if (c.SkillId == BDingan)
            return context.CardUse is { } use && BSecondTurnUse(use.CardActionId, use.EffectiveKind) && !BDyingThisTurn() &&
                BDinganDrawTargets(c.OwnerSeat, BTargets(use)).Any();
        return true;
    }

    private IEnumerable<IGameEvent> BTurnHistory() => EventsSinceLastBoundary(e => e is TurnStartedEvent);
    private IReadOnlyList<int> BTargets(ProgramCardUseContext use) => use.DesignatedTargetSeats ?? LifecycleCardUse(use.ParentCardUseFrameId)?.TargetSeats ?? [];
    private bool BDyingThisTurn() => BTurnHistory().OfType<PlayerDyingEvent>().Any();
    private bool BSecondTurnUse(long actionId, CardKind kind)
    {
        var declared = ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Where(e => e.TurnNumber == _turnNumber && e.Name == BCanonicalName(kind)).ToArray();
        return declared.Length == 2 && declared[^1].CardActionId == actionId;
    }
    private IEnumerable<int> BDinganDrawTargets(int owner, IReadOnlyList<int> targets) => _players.Where(p => p.IsAlive && !targets.Contains(p.Seat) &&
        !ProgramEventHistory<WushuangDinganDrawnEvent>().Any(e => e.OwnerSeat == owner && e.TargetSeat == p.Seat && e.TurnNumber == _turnNumber)).Select(p => p.Seat);

    [ProgramCapability]
    private sealed class WushuangSkillsBCapability(GameEngine engine) : IWushuangSkillsBHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginWushuangB(f, op); }

    private SkillProgramStepOutcome BeginWushuangB(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!BSourceCurrent(f)) return SkillProgramStepOutcome.Continue;
        if (f.WushuangB is not null) throw new InvalidOperationException("A Wushuang instruction cannot repeat its pending operation.");
        if (op == SkillProgramEffectOp.DrawWushuangZhendan)
        {
            if (BZhendanDisabled(f.OwnerSeat)) return SkillProgramStepOutcome.Continue;
            var count = Math.Min(5, EventsSinceLastBoundary(e => e is RoundStartedEvent).OfType<TurnStartedEvent>().Count());
            AdvanceEventRulesAndQueueFact(new WushuangZhendanDisabledEvent(f.OwnerSeat, f.SkillId, _roundNumber, f.Id));
            ReplaceRuntimeTop(f = f with { WushuangB = new(f.InstructionIndex, op, "complete") });
            DrawProgramCards(f.Id, f.OwnerSeat, count, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.zhendan.draw"));
            AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.ArmWushuangYinglue)
        {
            var target = f.SelectedTargetSeats.Single(); var draw = f.ActivationId == "lose-hp-next-draw";
            AdvanceEventRulesAndQueueFact(new WushuangYinglueArmedEvent(f.Id, f.OwnerSeat, target, draw ? TurnPhase.Draw : TurnPhase.Discard, draw ? 2 : -2));
            return SkillProgramStepOutcome.Continue;
        }
        if (op == SkillProgramEffectOp.ArmWushuangXiaojie)
        {
            var incoming = f.WindowContext?.CardUse?.ParentCardUseFrameId ?? throw new InvalidOperationException("Xiaojie lost its original incoming Slash.");
            if (ActiveCardAttack is not { } attack || attack.ResolutionId != incoming || attack.TargetSeat != f.OwnerSeat)
                throw new InvalidOperationException("Xiaojie requires its actual incoming Slash target.");
            attack.ProhibitDodgeBy(_contentRegistry.GetSkill(f.SkillId).Name);
            AdvanceEventRulesAndQueueFact(new WushuangXiaojieAcceptedEvent(f.Id, BSource(f), f.GameplayHash, incoming));
            return SkillProgramStepOutcome.Continue;
        }
        var targetSeat = op == SkillProgramEffectOp.ResolveWushuangGuzheng ? f.WindowContext?.SourceSeat ?? -1 : op is SkillProgramEffectOp.ResolveWushuangWeiyi or SkillProgramEffectOp.ResolveWushuangBianyu
            ? f.WindowContext?.TargetSeat ?? -1 : -1;
        var stage = op switch
        {
            SkillProgramEffectOp.ResolveWushuangWeiyi => "health",
            SkillProgramEffectOp.ResolveWushuangLiantao => "target",
            SkillProgramEffectOp.ResolveWushuangBianyu => "mark",
            SkillProgramEffectOp.ResolveWushuangXiaojie => "trick",
            SkillProgramEffectOp.ResolveWushuangDingan => "draw-target",
            SkillProgramEffectOp.RecoverWushuangChunlao => "chun",
            SkillProgramEffectOp.ResolveWushuangGuzheng => "guzheng-return",
            _ => throw new InvalidOperationException("Unknown Wushuang B instruction.")
        };
        var previous = op == SkillProgramEffectOp.ResolveWushuangXiaojie ? CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>()
            .LastOrDefault(e => e.SourceSeat == f.OwnerSeat && BOrdinaryTrick(e.CardKind))?.CardKind : null;
        ReplaceRuntimeTop(f = f with { WushuangB = new(f.InstructionIndex, op, stage, targetSeat, Before: _movementSequence, OutputKind: previous) });
        if (op == SkillProgramEffectOp.ResolveWushuangBianyu && (!IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive))
        { FinishWushuangB(f); return SkillProgramStepOutcome.AwaitChild; }
        if (op == SkillProgramEffectOp.ResolveWushuangXiaojie && previous is null) return DrawWushuangBFallback(f, 1);
        PublishWushuangBChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private PromptChoice BChoice(ProgramSkillFrame f, string step, string key, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        new(new($"wushuang-b.{f.Id}.{step}.{key}"), label, cards ?? [], targets ?? [], new Dictionary<string, string>
        { ["program-action"] = "wushuang-b", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["step"] = step, ["value"] = key });

    private IReadOnlyList<PromptChoice> WushuangBChoices(ProgramSkillFrame f)
    {
        var r = f.WushuangB!; var list = new List<PromptChoice>();
        if (r.Stage == "health")
        {
            if (_players[r.TargetSeat].Hp >= _players[f.OwnerSeat].Hp) list.Add(BChoice(f, r.Stage, "lose", "令其失去1点体力"));
            if (_players[r.TargetSeat].Hp <= _players[f.OwnerSeat].Hp) list.Add(BChoice(f, r.Stage, "recover", "令其回复1点体力"));
        }
        if (r.Stage == "target")
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)) list.Add(BChoice(f, r.Stage, p.Seat.ToString(CultureInfo.InvariantCulture), p.Name, targets: [p.Seat]));
        if (r.Stage == "color")
        { list.Add(BChoice(f, r.Stage, "red", "红色")); list.Add(BChoice(f, r.Stage, "black", "黑色")); }
        if (r.Stage == "mark")
        {
            var maximum = Math.Max(0, _players[r.TargetSeat].MaxHp - _players[r.TargetSeat].Hp);
            if (r.Cards.Count < maximum)
            {
                var hand = GetHand(_players[r.TargetSeat]);
                for (var i = 0; i < hand.Count; i++)
                    if (!r.Cards.Contains(hand[i].Id)) list.Add(BChoice(f, r.Stage, i.ToString(CultureInfo.InvariantCulture),
                        r.TargetSeat == f.OwnerSeat ? $"{hand[i].DisplayName} {hand[i].Suit}{hand[i].Rank}" : $"手牌位置 {i + 1}",
                        r.TargetSeat == f.OwnerSeat ? [hand[i].Id] : []));
            }
            list.Add(BChoice(f, r.Stage, "done", "完成选择"));
        }
        if (r.Stage == "chun")
        {
            if (r.Cards.Count < 2)
                foreach (var card in _cardZones.CardsAt(CardLocation.Chunlao(f.OwnerSeat)).Where(c => !r.Cards.Contains(c.Id)))
                    list.Add(BChoice(f, r.Stage, card.Id.ToString(CultureInfo.InvariantCulture), $"{card.DisplayName} {card.Suit}{card.Rank}", [card.Id]));
            list.Add(BChoice(f, r.Stage, "done", "将已选的醇置入手牌"));
        }
        if (r.Stage == "trick")
        {
            if (r.OutputKind is { } kind)
                foreach (var option in BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat], kind, Suit.None, enforceUsePermission: true,
                    actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []))
                    list.Add(BChoice(f, r.Stage, option.Id.Value, $"视为使用【{CardCatalog.Get(kind).DisplayName}】 {string.Join("、", option.TargetSeats.Select(s => _players[s].Name))}", targets: option.TargetSeats));
            list.Add(BChoice(f, r.Stage, "draw", "摸一张牌"));
        }
        if (r.Stage == "draw-target")
            foreach (var seat in BDinganDrawTargets(f.OwnerSeat, BTargets(f.WindowContext!.CardUse!)))
                list.Add(BChoice(f, r.Stage, seat.ToString(CultureInfo.InvariantCulture), $"与{_players[seat].Name}各摸一张牌", targets: [seat]));
        if (r.Stage == "punish")
        {
            var others = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).ToArray();
            var max = others.Select(p => GetHand(p).Count).DefaultIfEmpty().Max();
            foreach (var p in others.Where(p => GetHand(p).Count == max))
            {
                list.Add(BChoice(f, r.Stage, $"damage-{p.Seat}", $"对{p.Name}造成1点伤害", targets: [p.Seat]));
                list.Add(BChoice(f, r.Stage, $"discard-{p.Seat}", $"令{p.Name}弃置手牌中最多的同名牌", targets: [p.Seat]));
            }
        }
        if (r.Stage == "discard-name")
        {
            var groups = GetHand(_players[r.TargetSeat]).GroupBy(c => BCanonicalName(AdvancedEffectiveHandKind(_players[r.TargetSeat], c))).ToArray();
            var max = groups.Select(g => g.Count()).DefaultIfEmpty().Max();
            foreach (var group in groups.Where(g => g.Count() == max))
                list.Add(BChoice(f, r.Stage, group.Key.ToString(), $"弃置{group.Count()}张【{CardCatalog.Get(group.Key).DisplayName}】", group.Select(c => c.Id).ToArray()));
        }
        AddWushuangGuzhengChoices(f, list);
        return Array.AsReadOnly(list.ToArray());
    }

    private void PublishWushuangBChoice(ProgramSkillFrame f)
    {
        var r = f.WushuangB!; var chooser = r.Stage is "color" or "discard-name" ? r.TargetSeat : f.OwnerSeat;
        var choices = WushuangBChoices(f);
        if (choices.Count == 0) { FinishWushuangB(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"【{skill.Name}】请选择。", choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("wushuang-b")]
    private void AnswerWushuangB(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Wushuang choice lost its program.");
        var r = f.WushuangB ?? throw new InvalidOperationException("Wushuang choice lost its pending instruction.");
        if (_pendingDecision is not { } prompt || !WushuangBChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) &&
            c.Targets.SequenceEqual(selected.Targets) && c.Parameters.OrderBy(p => p.Key).SequenceEqual(selected.Parameters.OrderBy(p => p.Key))))
            throw new InvalidOperationException("Wushuang choice is not its current published option.");
        ClearPendingDecision();
        if (!BSourceCurrent(f)) { FinishWushuangB(f); return; }
        if (AnswerWushuangGuzheng(f, selected)) return;
        var value = selected.Parameters["value"];
        if (r.Stage == "health")
        {
            AdvanceEventRulesAndQueueFact(new WushuangWeiyiUsedEvent(f.OwnerSeat, f.SkillId, r.TargetSeat, f.Id));
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "complete" } });
            if (value == "lose") { var outcome = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, r.TargetSeat, 1); if (outcome == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id); }
            else { new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, r.TargetSeat, 1, null, null); AdvanceRuntimeProgram(f.Id); }
            return;
        }
        if (r.Stage == "target")
        { ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "color", TargetSeat = selected.Targets.Single() } }); PublishWushuangBChoice(f); return; }
        if (r.Stage == "color")
        {
            var hand = GetHand(_players[f.OwnerSeat]);
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, Array.AsReadOnly(hand.Select(ToSnapshot).ToArray())));
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "duels", Red = value == "red",
                Cards = hand.Where(c => SuitColor(EffectiveSuit(_players[f.OwnerSeat], c)) == (value == "red")).Select(c => c.Id).ToArray() } });
            AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage == "mark")
        {
            if (value != "done")
            {
                var card = GetHand(_players[r.TargetSeat])[int.Parse(value, CultureInfo.InvariantCulture)];
                ReplaceRuntimeTop(f = f with { WushuangB = r with { Cards = r.Cards.Append(card.Id).ToArray() } });
                PublishWushuangBChoice(f); return;
            }
            foreach (var id in r.Cards)
                AdvanceEventRulesAndQueueFact(new WushuangBianyuMarkedEvent(f.Id, BSource(f), f.GameplayHash, r.TargetSeat, id, _movementSequence, _cardActionSequence));
            var allSlash = new[] { f.OwnerSeat, r.TargetSeat }.Distinct().Any(seat => GetHand(_players[seat]).All(c => IsSlashCard(AdvancedEffectiveHandKind(_players[seat], c))));
            if (allSlash) { DrawWushuangBFallback(f, 2); return; }
            FinishWushuangB(f); return;
        }
        if (r.Stage == "trick")
        {
            if (value == "draw") { DrawWushuangBFallback(f, 1); return; }
            var option = BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat], r.OutputKind, Suit.None, enforceUsePermission: true,
                actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []).Single(o => o.Id.Value == value && o.TargetSeats.SequenceEqual(selected.Targets));
            IssueWushuangBTrick(f, option, null); return;
        }
        if (r.Stage == "chun")
        {
            if (value != "done")
            { ReplaceRuntimeTop(f = f with { WushuangB = r with { Cards = r.Cards.Append(selected.Cards.Single()).ToArray() } }); PublishWushuangBChoice(f); return; }
            if (r.Cards.Count == 0) { FinishWushuangB(f); return; }
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "complete", TargetSeat = f.OwnerSeat, Before = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCards(r.Cards.Select(GetAdvancedCard).ToArray(), CardLocation.Chunlao(f.OwnerSeat), CardLocation.Hand(f.OwnerSeat), new("skill-program.chunlao.return"));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { WushuangB = f.WushuangB! with { After = _movementSequence } });
            AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage == "draw-target")
        {
            var target = selected.Targets.Single();
            AdvanceEventRulesAndQueueFact(new WushuangDinganDrawnEvent(f.OwnerSeat, target, _turnNumber, f.Id));
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "draw-children", TargetSeat = target } });
            DrawProgramCards(f.Id, f.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.dingan.draw-owner"));
            DrawProgramCards(f.Id, target, 1, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.dingan.draw-target"));
            AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage == "punish")
        {
            var target = selected.Targets.Single();
            if (value.StartsWith("damage-", StringComparison.Ordinal))
            {
                ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "complete", TargetSeat = target } });
                new ProgramSkillHost(this).Damage(f, target, 1); return;
            }
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "discard-name", TargetSeat = target } }); PublishWushuangBChoice(f); return;
        }
        if (r.Stage == "discard-name")
        {
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "complete", Before = _movementSequence }, PendingMovementContinuation = new(r.TargetSeat, 0, null) });
            foreach (var id in selected.Cards)
                MoveLogicalDiscard(f.OwnerSeat, r.TargetSeat, GetAdvancedCard(id), CardLocation.Hand(r.TargetSeat), new("skill-program.dingan.discard"));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { WushuangB = f.WushuangB! with { After = _movementSequence } });
            AdvanceRuntimeProgram(f.Id); return;
        }
        throw new InvalidOperationException("Unsupported Wushuang pending choice.");
    }

    private SkillProgramStepOutcome DrawWushuangBFallback(ProgramSkillFrame f, int count)
    {
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { WushuangB = f.WushuangB! with { Stage = "complete" } });
        DrawProgramCards(f.Id, f.OwnerSeat, count, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.wushuang-b.draw"));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void FinishWushuangB(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { WushuangB = null, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 61200)]
    private bool ContinueWushuangB(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.WushuangB is not { } r) return false;
        if (_pendingDecision is not null) return true;
        if (r.Stage is "health" or "target" or "color" or "mark" or "chun" or "trick" or "draw-target" or "punish" or "discard-name" or "guzheng-return" or "guzheng-claim")
        { PublishWushuangBChoice(f); return true; }
        if (r.Stage == "using") return true;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id)) return true;
        f = GetActiveProgramFrame(id); r = f.WushuangB!;
        if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == "guzheng-returned")
        { ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "guzheng-claim" } }); PublishWushuangBChoice(f); return true; }
        if (r.Stage == "draw-children")
        { ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "punish" } }); PublishWushuangBChoice(f); return true; }
        if (r.Stage == "duels")
        {
            if (!BSourceCurrent(f) || !_players[r.TargetSeat].IsAlive || r.EnteredDying || r.Cards.Count == 0)
            { FinishWushuangLiantao(f); return true; }
            var next = r.Cards.First();
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Cards = r.Cards.Skip(1).ToArray() } });
            if (_cardZones.GetLocation(next) != CardLocation.Hand(f.OwnerSeat)) { AdvanceRuntimeProgram(id); return true; }
            var card = GetAdvancedCard(next);
            var option = BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat], CardKind.Duel, EffectiveSuit(_players[f.OwnerSeat], card),
                enforceUsePermission: true, physicalCardIds: [next]).FirstOrDefault(o => o.TargetSeats.SequenceEqual([r.TargetSeat]));
            if (option is null) { AdvanceRuntimeProgram(id); return true; }
            IssueWushuangBTrick(f, option, card); return true;
        }
        FinishWushuangB(f); return true;
    }

    private void FinishWushuangLiantao(ProgramSkillFrame f)
    {
        var r = f.WushuangB!;
        if (r.Damage == 0)
        {
            new ProgramSkillHost(this).GrantTurnRuleModifier(f, SkillRuleQuery.HandLimit, SkillRuleOperation.Add, 1, []);
            new ProgramSkillHost(this).GrantTurnCardActionProhibition(f, SlashKinds, [CardActionType.Use]);
        }
        DrawWushuangBFallback(f, r.Damage == 0 ? 1 : r.Damage);
    }

    private void IssueWushuangBTrick(ProgramSkillFrame f, ProgramOrdinaryTrickUseOption option, Card? physical)
    {
        var r = f.WushuangB!; var card = physical ?? new Card(0, option.EffectiveCardKind, Suit.None, 0);
        var nextId = _resolutionSequence + 1;
        ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "using", ChildUseId = nextId } });
        var useId = BeginCardUse(card, f.OwnerSeat, option.TargetSeats, option.EffectiveCardKind, physicalCardIds: physical is null ? [] : [physical.Id], conversionSource: physical is null ? null : BSource(f));
        var ret = new WushuangBNativeReturn(f.Id, f.InstructionIndex, BSource(f), f.GameplayHash, useId, option.EffectiveCardKind);
        UpdateLifecycleCardUse(useId, u => u with { WushuangBReturn = ret });
        AdvanceEventRulesAndQueueFact(new WushuangBNativeIssuedEvent(ret));
        if (physical is not null) MoveCard(physical, CardLocation.Hand(f.OwnerSeat), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(useId, card, f.OwnerSeat, option.TargetSeats, option.ActionKind, option.TargetCardId, option.RequiredCardKind, option.EffectiveCardKind);
    }

    private bool IsWushuangBZeroUse(long id) => LifecycleCardUse(id) is { WushuangBReturn: not null, CardId: 0, PhysicalCardIds.Count: 0 };
    [NativeUseReturn(61200)]
    private bool ReturnWushuangBNativeUse(CardUseFrame use)
    {
        if (use.WushuangBReturn is not { } ret) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != ret.ProgramFrameId ||
            f.WushuangB is not { Stage: "using" } r || r.ChildUseId != use.Id || ret.CardUseFrameId != use.Id ||
            ret.Source != BSource(f) || ret.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Wushuang native use lost its exact typed parent return.");
        AdvanceEventRulesAndQueueFact(new WushuangBNativeReturnedEvent(ret));
        ReplaceRuntimeTop(f with { WushuangB = r with { Stage = r.Operation == SkillProgramEffectOp.ResolveWushuangLiantao ? "duels" : "complete", ChildUseId = null } });
        // The native attack cleanup owns its response state until it returns.
        if (ActiveCardAttack is { } attack && attack.ResolutionId == use.Id) return true;
        AdvanceRuntimeProgram(f.Id); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 61200)]
    private bool ReturnWushuangBNativeAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WushuangB: { } r } f ||
            !ProgramEventHistory<WushuangBNativeReturnedEvent>().Any(e => e.Return.ProgramFrameId == f.Id && e.Return.CardUseFrameId == completion.ResolutionId)) return false;
        AdvanceRuntimeProgram(f.Id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 61200)]
    private bool ReturnWushuangBMovement(ProgramSkillFrame f)
    { if (f.WushuangB is null) return false; AdvanceRuntimeProgram(f.Id); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 61200)]
    private bool IsWushuangBMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.WushuangB is { Stage: "complete", TargetSeat: var seat } r &&
        r.Operation is SkillProgramEffectOp.ResolveWushuangDingan or SkillProgramEffectOp.RecoverWushuangChunlao &&
        effect?.Op == r.Operation && pending.SubjectSeat == seat;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 61200)]
    private void AssertWushuangBFrame(ProgramSkillFrame f)
    {
        if (f.WushuangB is not { } r) return;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        var expected = r.Operation switch
        {
            SkillProgramEffectOp.ResolveWushuangWeiyi => BWeiyi,
            SkillProgramEffectOp.DrawWushuangZhendan => BZhendan,
            SkillProgramEffectOp.ResolveWushuangLiantao => BLiantao,
            SkillProgramEffectOp.ResolveWushuangBianyu => BBianyu,
            SkillProgramEffectOp.ResolveWushuangXiaojie => BXiaojie,
            SkillProgramEffectOp.ResolveWushuangDingan => BDingan,
            SkillProgramEffectOp.RecoverWushuangChunlao => BChunlao,
            SkillProgramEffectOp.ResolveWushuangGuzheng => BGuzheng,
            _ => throw new InvalidOperationException("Unknown Wushuang pending operation.")
        };
        if (f.SkillId != expected || r.Operation != effect.Op || r.InstructionIndex != f.InstructionIndex ||
            r.Before < 0 || r.After < 0 || r.Damage < 0 || r.Cards.Any(id => id <= 0) || r.Cards.Distinct().Count() != r.Cards.Count ||
            r.Stage is not ("health" or "target" or "color" or "mark" or "chun" or "trick" or "draw-target" or "punish" or "discard-name" or "using" or "duels" or "complete" or "draw-children" or "guzheng-return" or "guzheng-returned" or "guzheng-claim") ||
            r.Stage is ("health" or "color" or "mark" or "punish" or "discard-name" or "duels" or "guzheng-return" or "guzheng-returned" or "guzheng-claim") && !IsValidPlayerSeat(r.TargetSeat))
            throw new InvalidOperationException("Wushuang pending receipt lost its exact owning instruction or participants.");
        if (r.Stage == "using" && (r.ChildUseId is not { } id || LifecycleCardUse(id) is not { WushuangBReturn: { } ret } use ||
            ret.ProgramFrameId != f.Id || ret.InstructionIndex != f.InstructionIndex || ret.Source != BSource(f) ||
            ret.GameplayHash != f.GameplayHash || ret.CardUseFrameId != use.Id || ret.OutputKind != use.CardKind ||
            ProgramEventHistory<WushuangBNativeIssuedEvent>().Count(e => e.Return == ret) != 1))
            throw new InvalidOperationException("Wushuang native child lost its exact issued source and parent receipt.");
    }

    private WushuangBianyuMarkedEvent? BianyuActiveMark(int seat, int id)
    {
        if (id <= 0 || !HasWushuangBSkills || _cardZones.GetLocation(id) != CardLocation.Hand(seat)) return null;
        var mark = ProgramEventHistory<WushuangBianyuMarkedEvent>().LastOrDefault(e => e.TargetSeat == seat && e.CardId == id);
        if (mark is null || _cardMovements.Any(m => m.Sequence > mark.MovementSequence && m.CardId == id && m.From == CardLocation.Hand(seat)) ||
            CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActionId > mark.ActionSequence && e.Action.ActorSeat == seat &&
                (e.Action.Type == CardActionType.Use || ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(use => use.CardActionId == e.Action.ActionId)) && !BBasic(e.Action.EffectiveKind))) return null;
        return mark;
    }
    private ProgramCardIdentityMatch? WushuangBianyuIdentity(CharacterState owner, Card card)
    {
        if (BianyuActiveMark(owner.Seat, card.Id) is not { } mark) return null;
        var program = _contentRegistry.GetSkill(mark.Source.SkillId).Program!;
        return new(program, new SkillProgramCardIdentity("bianyu-slash", [CardZoneKind.Hand], [], [], CardKind.Slash,
            new(SkillProgramConditionKind.Always, 0, [])), mark.Source with { BindingId = "bianyu-slash" });
    }
    private bool HasWushuangBianyuPotential(CharacterState owner) => HasWushuangBSkills && GetHand(owner).Any(c => BianyuActiveMark(owner.Seat, c.Id) is not null);
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 61200)]
    private bool WushuangBianyuQuota(ProgramSlashQuotaQuery q) => q.PhysicalCardIds is [var id] && BianyuActiveMark(q.Actor.Seat, id) is not null ||
        q.PhysicalCard is { } card && BianyuActiveMark(q.Actor.Seat, card.Id) is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWushuangWeiyi, SkillProgramEffectOp.ResolveWushuangLiantao,
        SkillProgramEffectOp.ResolveWushuangBianyu, SkillProgramEffectOp.ResolveWushuangXiaojie, SkillProgramEffectOp.ResolveWushuangDingan, SkillProgramEffectOp.RecoverWushuangChunlao, SkillProgramEffectOp.ResolveWushuangGuzheng)]
    private PromptChoice ChooseWushuangBAi(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("value") == "done" && (f.WushuangB!.Stage == "chun" ? f.WushuangB.Cards.Count >= 2 : f.WushuangB.Cards.Count >= Math.Max(0, _players[f.WushuangB.TargetSeat].MaxHp - _players[f.WushuangB.TargetSeat].Hp))) ?? p.Choices[0];
}


