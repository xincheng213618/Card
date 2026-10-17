namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ContinueGuanduShicai(ProgramSkillFrame f)
    {
        if (f.GuanduGeneral!.Stage == "begin")
        {
            if (!GuanduShicaiReady(f.OwnerSeat, f.SkillInstanceId) || _cardZones.Count(CardLocation.DrawPile) == 0)
            { FinishGuanduGeneral(f, false); return; }
            f = GuanduStage(f, "shicai-cost"); PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduOwnCardChoices(f, f.OwnerSeat, "discard", discard: true)); return;
        }
        if (f.GuanduGeneral.Stage == "shicai-cost")
        { PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduOwnCardChoices(f, f.OwnerSeat, "discard", discard: true)); return; }
        if (f.GuanduGeneral.Stage != "shicai-obtain") throw new InvalidOperationException("Shicai lost its paid discard.");
        var top = _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault();
        f = GuanduStage(f, "finish");
        if (top is not null && _players[f.OwnerSeat].IsAlive)
        {
            MoveCard(top, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new("activity.guandu.shicai.obtain"));
            AdvanceEventRulesAndQueueFact(new GuanduShicaiObtainedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, top.Id, _movementSequence));
        }
        RunGuanduGeneral(f.Id);
    }
    private void ContinueGuanduZezhu(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage == "begin")
            f = GuanduUpdate(f, r = r with { Stage = "zezhu-take-next", Seats = FreezeGuanduSeats(_players.Where(p => p.IsAlive && IsGuanduCommander(p.Seat)).Select(p => p.Seat)), Index = 0 });
        if (r.Stage == "zezhu-take") { PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduForeignGainChoices(f, r.Seats![r.Index])); return; }
        if (r.Stage == "zezhu-take-next")
        {
            if (r.Index == r.Seats!.Count) { f = GuanduUpdate(f, r with { Stage = "zezhu-give-next", Index = 0 }); RunGuanduGeneral(f.Id); return; }
            var target = r.Seats[r.Index];
            if (!_players[target].IsAlive) { GuanduUpdate(f, r with { Index = r.Index + 1 }); RunGuanduGeneral(f.Id); return; }
            if (GuanduOwnedCards(target, judgment: true).Length == 0)
            { f = GuanduUpdate(f, r with { Index = r.Index + 1 }); DrawGuanduGeneral(f, f.OwnerSeat, 1, "zezhu-take-next"); return; }
            f = GuanduStage(f, "zezhu-take"); PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduForeignGainChoices(f, target)); return;
        }
        if (r.Stage is "zezhu-give-next" or "zezhu-give")
        {
            while (r.Index < r.Seats!.Count && !_players[r.Seats[r.Index]].IsAlive)
                f = GuanduUpdate(f, r = r with { Index = r.Index + 1 });
            if (r.Index == r.Seats.Count || !_players[f.OwnerSeat].IsAlive || GuanduOwnedCards(f.OwnerSeat).Length == 0)
            { FinishGuanduGeneral(f); return; }
            f = GuanduStage(f, "zezhu-give"); PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduOwnCardChoices(f, f.OwnerSeat, "give", allowSkip: false)); return;
        }
        throw new InvalidOperationException("Zezhu changed its commander sequence.");
    }
    private void ContinueGuanduJieliang(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage == "begin")
        {
            var target = f.WindowContext?.SourceSeat ?? -1;
            if (!IsValidPlayerSeat(target) || target == f.OwnerSeat) throw new InvalidOperationException("Jieliang lost its actual Draw owner.");
            f = GuanduUpdate(f, r with { TargetSeat = target, Stage = "jieliang-cost" });
            PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduOwnCardChoices(f, f.OwnerSeat, "discard", discard: true)); return;
        }
        if (r.Stage == "jieliang-cost") { PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduOwnCardChoices(f, f.OwnerSeat, "discard", discard: true)); return; }
        if (r.Stage != "jieliang-paid") throw new InvalidOperationException("Jieliang lost its payment.");
        f = GuanduStage(f, "finish");
        AdjustProgramNormalDraw(f, -1);
        GuanduHandLimitPenalty(f, r.TargetSeat);
        AdvanceEventRulesAndQueueFact(new GuanduJieliangPaidEvent(f.Id, f.OwnerSeat, r.TargetSeat, _turnNumber));
        RunGuanduGeneral(f.Id);
    }
    private GuanduDiscardFactEvent[] GuanduClaimableDiscards(int target)
    {
        var phase = CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Discard, ActorSeat: var actor } current && actor == target ? current :
            ProgramEventHistory<ActualDiscardPhaseExecutionConfirmedEvent>().LastOrDefault(e => e.Phase.TurnNumber == _turnNumber && e.Phase.ActorSeat == target)?.Phase;
        if (phase is null) return [];
        return ProgramEventHistory<GuanduDiscardFactEvent>()
        .Where(e => e.ActualTurn == _turnNumber && e.PhaseActor == target && e.PhaseToken == phase.Token && _cardZones.GetLocation(e.CardId) == CardLocation.DiscardPile &&
            !_cardMovements.Any(m => m.CardId == e.CardId && m.Sequence > e.MovementSequence && m.From == CardLocation.DiscardPile))
        .DistinctBy(e => e.CardId).ToArray();
    }
    private void ContinueGuanduJieliangClaim(ProgramSkillFrame f)
    {
        var target = f.WindowContext?.SourceSeat ?? -1;
        f = GuanduUpdate(f, f.GuanduGeneral! with { TargetSeat = target, Stage = "jieliang-claim" });
        var choices = GuanduClaimableDiscards(target).Select(e => GuanduChoice(f, "claim", $"获得【{GetAdvancedCard(e.CardId).DisplayName}】", [e.CardId]))
            .Append(GuanduChoice(f, "skip", "不获得")).ToArray();
        PublishGuanduGeneralChoice(f, f.OwnerSeat, choices);
    }
    private void GuanduHandLimitPenalty(ProgramSkillFrame f, int target)
    {
        var grant = _turnCardUseEffects.GrantRuleModifier(_turnNumber, _currentSeat, f.Id, 0,
            CreateProgramTurnEffectSource(f), SkillRuleQuery.HandLimit, SkillRuleOperation.Add, -1, affectedSeat: target);
        AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(grant));
    }
    private void ContinueGuanduCangchu(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage == "begin")
        {
            var current = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.GuanduGrain);
            if (f.WindowContext!.Window == SkillProgramTriggerWindow.GameStarting)
            { SetStrategicMarker(f.OwnerSeat, PlayerMarkerKind.GuanduGrain, f.OwnerSeat, 3, f); GuanduStage(f, "finish"); RunGuanduGeneral(f.Id); return; }
            if (current == 0) { FinishGuanduGeneral(f); return; }
            SetStrategicMarker(f.OwnerSeat, PlayerMarkerKind.GuanduGrain, f.OwnerSeat, current - 1, f);
            if (current > 1 || !GuanduSkillEnabled(_players[f.OwnerSeat], "guandu:liangying"))
            { GuanduStage(f, "finish"); RunGuanduGeneral(f.Id); return; }
            f = GuanduUpdate(f, r with { Stage = "liangying-enemies", Index = 0,
                Seats = FreezeGuanduSeats(_players.Where(p => p.IsAlive && !GuanduAllies(f.OwnerSeat, p.Seat)).Select(p => p.Seat)) });
            ChangeProgramMaximumHp(f, -1, "guandu:liangying"); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage != "liangying-enemies") throw new InvalidOperationException("Liangying lost its once-paid collapse.");
        if (r.Index == r.Seats!.Count) { FinishGuanduGeneral(f); return; }
        var target = r.Seats[r.Index]; f = GuanduUpdate(f, r with { Index = r.Index + 1 });
        DrawGuanduGeneral(f, target, 2, "liangying-enemies");
    }
    private void ContinueGuanduSushou(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage == "begin")
        {
            f = GuanduUpdate(f, r with { Seats = FreezeGuanduSeats(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && GuanduAllies(f.OwnerSeat, p.Seat)).Select(p => p.Seat)) });
            DrawGuanduGeneral(f, f.OwnerSeat, _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.GuanduGrain) + 1, "sushou-give"); return;
        }
        var choices = r.Seats!.Where(s => _players[s].IsAlive).SelectMany(s => GuanduOwnedCards(f.OwnerSeat).Select(c =>
            GuanduChoice(f, "give", $"将【{c.DisplayName}】交给 {_players[s].Name}", [c.Id], [s])))
            .Append(GuanduChoice(f, "skip", "分配结束")).ToArray();
        PublishGuanduGeneralChoice(f, f.OwnerSeat, choices);
    }
    private void ContinueGuanduHengjiang(ProgramSkillFrame f)
    {
        var context = f.WindowContext!;
        if (context.DamageFrameId is not { } damage || _turnNumber < 1) { FinishGuanduGeneral(f, false); return; }
        GuanduStage(f, "finish"); GuanduHandLimitPenalty(f, _currentSeat);
        AdvanceEventRulesAndQueueFact(new GuanduHengjiangPaidEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, _currentSeat,
            _turnNumber, damage, context.OccurrenceIndex)); RunGuanduGeneral(f.Id);
    }
    private void ContinueGuanduHengjiangReward(ProgramSkillFrame f)
    {
        var count = ProgramEventHistory<GuanduHengjiangPaidEvent>().Count(e => e.OwnerSeat == f.OwnerSeat && e.ActualTurn == _turnNumber);
        if (count == 0 || GuanduTurnDiscarded(_currentSeat)) { FinishGuanduGeneral(f, false); return; }
        DrawGuanduGeneral(f, f.OwnerSeat, count, "finish");
    }
    private void ContinueGuanduXiying(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage is "begin" or "xiying-cost")
        { f = GuanduStage(f, "xiying-cost"); PublishGuanduGeneralChoice(f, f.OwnerSeat, GuanduOwnCardChoices(f, f.OwnerSeat, "discard", discard: true)); return; }
        if (r.Stage is not ("xiying-target" or "xiying-answer")) throw new InvalidOperationException("Xiying lost its paid target sequence.");
        while (r.Index < r.Seats!.Count && !_players[r.Seats[r.Index]].IsAlive)
            f = GuanduUpdate(f, r = r with { Index = r.Index + 1 });
        if (r.Index == r.Seats.Count) { FinishGuanduGeneral(f); return; }
        f = GuanduStage(f, "xiying-answer"); var target = r.Seats[r.Index];
        var choices = GuanduOwnCardChoices(f, target, "discard", discard: true, allowSkip: false)
            .Append(GuanduChoice(f, "ban", "本回合不能使用或打出任何牌")).ToArray();
        PublishGuanduGeneralChoice(f, target, choices);
    }
    private void ContinueGuanduBeizhan(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage is "begin" or "beizhan-target")
        {
            f = GuanduStage(f, "beizhan-target");
            PublishGuanduGeneralChoice(f, f.OwnerSeat, _players.Where(p => p.IsAlive).Select(p =>
                GuanduChoice(f, "target", $"令 {p.Name} 补至体力上限张手牌", targets: [p.Seat])).Append(GuanduChoice(f, "skip", "不执行")).ToArray()); return;
        }
        if (r.Stage != "beizhan-draw") throw new InvalidOperationException("Beizhan lost its target before drawing.");
        var target = _players[r.TargetSeat];
        AdvanceEventRulesAndQueueFact(new GuanduBeizhanArmedEvent(f.Id, f.OwnerSeat, r.TargetSeat, _turnNumber, CreateProgramTurnEffectSource(f)));
        DrawGuanduGeneral(f, target.Seat, Math.Max(0, target.MaxHp - GetHand(target).Count), "finish");
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44001)]
    private void ConsumeGuanduBeizhan(IGameEvent payload)
    {
        if (!IsGuanduActivity || payload is not TurnStartedEvent turn) return;
        var consumed = ProgramEventHistory<GuanduBeizhanConsumedEvent>().Select(e => e.ProducerFrameId).ToHashSet();
        foreach (var armed in ProgramEventHistory<GuanduBeizhanArmedEvent>().Where(e => e.TargetSeat == turn.ActorSeat &&
            e.CreatedTurn < _turnNumber && !consumed.Contains(e.FrameId)).ToArray())
        {
            var player = _players[turn.ActorSeat]; var restricted = player.IsAlive && _players.Where(p => p.IsAlive).All(p => GetHand(p).Count <= GetHand(player).Count);
            AdvanceEventRulesAndQueueFact(new GuanduBeizhanConsumedEvent(armed.FrameId, player.Seat, _turnNumber, restricted));
            if (!restricted) continue;
            var grant = _turnCardUseEffects.GrantTargetRestriction(_turnNumber, player.Seat, armed.FrameId, 0,
                armed.Source, SkillProgramCardTargetRestriction.SelfOnly, null, player.Seat == armed.OwnerSeat ? null : player.Seat);
            AdvanceEventRulesAndQueueFact(new CardTargetRestrictionGrantedEvent(grant));
        }
    }
    private void ContinueGuanduZhongjie(ProgramSkillFrame f)
    {
        var r = f.GuanduGeneral!;
        if (r.Stage is "begin" or "zhongjie-target")
        {
            f = GuanduStage(f, "zhongjie-target");
            PublishGuanduGeneralChoice(f, f.OwnerSeat, _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p =>
                GuanduChoice(f, "target", $"令 {p.Name} 增加体力上限、回复并摸一张牌", targets: [p.Seat])).Append(GuanduChoice(f, "skip", "不执行")).ToArray()); return;
        }
        if (r.Stage == "zhongjie-grow")
        {
            var target = _players[r.TargetSeat]; f = GuanduStage(f, "zhongjie-recover");
            target.MaxHp = checked(target.MaxHp + 1); AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(target.Seat, 1, target.MaxHp, f.SkillId)); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "zhongjie-recover")
        {
            f = GuanduStage(f, "zhongjie-draw"); new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, r.TargetSeat, 1, null, null); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage != "zhongjie-draw") throw new InvalidOperationException("Zhongjie changed its native ordered effects.");
        DrawGuanduGeneral(f, r.TargetSeat, 1, "finish");
    }
}
