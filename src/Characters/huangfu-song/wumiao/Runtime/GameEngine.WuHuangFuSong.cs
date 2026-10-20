using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuHfObtain = "skill-program.wumiao:chaozhen.obtain", WuHfBottom = "skill-program.wumiao:lianjie.bottom", WuHfDraw = "skill-program.wumiao:lianjie.draw";
    private bool HasWuHuangFuSongCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuHuangFuSongLianjie) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ArmWuHuangFuSongJiangxian);
    private WuHuangFuSongTurn WuHfTurn() => new(_turnNumber, _currentSeat);
    private bool WuHfTurnOpen(WuHuangFuSongTurn t) => t == WuHfTurn() && !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == t.Number && e.OwnerSeat == t.OwnerSeat);
    private static CardConversionSource WuHfSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private (Card Card, CardLocation From)[] WuHfMinimumPool()
    {
        var locations = _players.Where(p => p.IsAlive).SelectMany(p => new[] { CardLocation.Equipment(p.Seat), CardLocation.Judgment(p.Seat) }).Append(CardLocation.DrawPile);
        var all = locations.SelectMany(loc => _cardZones.CardsAt(loc).Where(c => c.Rank > 0).Select(c => (Card: c, From: loc))).ToArray();
        if (all.Length == 0) return [];
        var rank = all.Min(x => x.Card.Rank);
        return all.Where(x => x.Card.Rank == rank && x.From != CardLocation.DrawPile)
            .Concat(all.Where(x => x.Card.Rank == rank && x.From == CardLocation.DrawPile).TakeLast(1)).ToArray();
    }
    private bool WuHfChaozhenAvailable(int owner) => !ProgramEventHistory<WuHuangFuSongChaozhenDisabledEvent>().Any(e => e.OwnerSeat == owner && e.Turn == WuHfTurn()) && WuHfMinimumPool().Length > 0;
    private bool WuHfVictimAvailable(int owner, int target) => _players[target].IsAlive && GetHand(_players[target]).Count > 0 &&
        !ProgramEventHistory<WuHuangFuSongBottomPaidEvent>().Any(e => e.OwnerSeat == owner && e.TargetSeat == target && e.Turn == WuHfTurn());
    private CardUseFrame? WuHfLianjieUse(int owner, ProgramSkillWindowContext c)
    {
        if (c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || c.CardUse is not { } identity || identity.ActorSeat != owner ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { WuHuangFuSongUse: { HandUse: true, EffectiveRank: { } rank } r, Action: { } a } use ||
            use.Id != r.UseId || a.ActionId != identity.CardActionId || a.ActorSeat != owner || a.ProviderSeat != owner || !WuHfTurnOpen(r.Turn) ||
            a.EffectiveDesignatedTargetSeats.Count == 0 || !GetHand(_players[owner]).Where(card => !a.PhysicalCards.Any(cost => cost.CardId == card.Id)).All(card => rank <= EffectiveOwnedCardRank(_players[owner], card))) return null;
        return _players.Any(p => WuHfVictimAvailable(owner, p.Seat)) ? use : null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30000, SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen, SkillProgramEffectOp.ResolveWuHuangFuSongLianjie, SkillProgramEffectOp.SettleWuHuangFuSongJiangxian)]
    private bool CanOfferWuHuangFuSong(ProgramTriggerCandidate candidate, SkillProgramTrigger t, ProgramSkillWindowContext c)
    {
        if (t.Effects[0].Op == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie) return WuHfLianjieUse(candidate.OwnerSeat, c) is not null;
        if (t.Effects[0].Op == SkillProgramEffectOp.SettleWuHuangFuSongJiangxian) return WuHfOutstandingArm(candidate.OwnerSeat) is not null;
        return WuHfChaozhenAvailable(candidate.OwnerSeat) && (c.Window == SkillProgramTriggerWindow.DyingEntering && c.TargetSeat == candidate.OwnerSeat ||
            c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Preparation } phase && phase.ActorSeat == candidate.OwnerSeat);
    }
    [ProgramCapability] private sealed class WuHuangFuSongCapability(GameEngine g) : IWuHuangFuSongHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.BeginWuHuangFuSong(f, op); }
    private SkillProgramStepOutcome BeginWuHuangFuSong(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WuHuangFuSong is not null || f.InstructionIndex != 1 || GetPausedPrivateOfferEffect(f)?.Op != op) throw new InvalidOperationException("Wu Huangfu Song needs his original unissued instruction.");
        if (op == SkillProgramEffectOp.ArmWuHuangFuSongJiangxian)
        {
            if (f.SkillId != WuHuangFuSongComposition.Jiangxian || f.ActivationId != "arm-turn" || f.TriggerId is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play)
                throw new InvalidOperationException("Jiangxian must be the native once-per-game Play activation.");
            AdvanceEventRulesAndQueueFact(new WuHuangFuSongJiangxianArmedEvent(f.Id, WuHfSource(f), f.GameplayHash, WuHfTurn()));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.WindowContext is not { } context || f.TriggerId != f.ActivationId) throw new InvalidOperationException("Wu Huangfu Song lost his original native trigger window.");
        var use = op == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie ? WuHfLianjieUse(f.OwnerSeat, context) : null;
        if (op == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie && use is null || op == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen && !WuHfChaozhenAvailable(f.OwnerSeat)) return SkillProgramStepOutcome.Continue;
        var r = new WuHuangFuSongReceipt(WuHfSource(f), f.GameplayHash, op, WuHfTurn(), context.ParentFrameId, use?.Id ?? 0)
            { Stage = WuHuangFuSongStage.Choosing, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { WuHuangFuSong = r });
        AdvanceEventRulesAndQueueFact(new WuHuangFuSongStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.Turn, r.WindowId, r.UseId));
        var choices = WuHfChoices(f);
        if (choices.Count == 0) { FinishWuHf(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishWuHf(f, choices); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> WuHfChoices(ProgramSkillFrame f)
    {
        var r = f.WuHuangFuSong!;
        if (r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen)
            return Array.AsReadOnly(WuHfMinimumPool().Select(x => new PromptChoice(new($"wu-huangfu-song.{f.Id}.minimum.{(x.From == CardLocation.DrawPile ? "deck" : x.Card.Id.ToString(CultureInfo.InvariantCulture))}"),
                x.From == CardLocation.DrawPile ? "获得牌堆中点数最小的一张牌" : $"获得{_players[x.From.OwnerSeat!.Value].Name}场上的【{x.Card.DisplayName}】",
                x.From == CardLocation.DrawPile ? [] : [x.Card.Id], [], new Dictionary<string, string> { ["program-action"] = "wu-huangfu-song", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                    ["selection"] = x.From == CardLocation.DrawPile ? "deck" : "field", ["field-card"] = x.From == CardLocation.DrawPile ? "0" : x.Card.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
        if (r.Operation == SkillProgramEffectOp.SettleWuHuangFuSongJiangxian)
            return Array.AsReadOnly(new[] { WuHuangFuSongComposition.Lianjie, WuHuangFuSongComposition.Chaozhen }.Where(id => HasRuntimeSkill(_players[f.OwnerSeat], id))
                .Select(id => new PromptChoice(new($"wu-huangfu-song.{f.Id}.lose.{id}"), $"失去【{_contentRegistry.GetSkill(id).Name}】", [], [],
                    new Dictionary<string, string> { ["program-action"] = "wu-huangfu-song", ["lose-skill"] = id, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
        if (r.Stage == WuHuangFuSongStage.ChoosingMinimumHand)
        {
            var target = r.TargetSeat!.Value; var hand = GetHand(_players[target]); if (hand.Count == 0) return [];
            var min = hand.Min(card => EffectiveOwnedCardRank(_players[target], card));
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, target, [CardZoneKind.Hand], OwnedCardMoveIntent.Transfer,
                canSelect: (_, card) => EffectiveOwnedCardRank(_players[target], card) == min).Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "wu-huangfu-song" } }).ToArray());
        }
        return Array.AsReadOnly(_players.Where(p => WuHfVictimAvailable(f.OwnerSeat, p.Seat)).Select(p => new PromptChoice(new($"wu-huangfu-song.{f.Id}.target.{p.Seat}"),
            $"将{p.Name}一张最小点数手牌置底", [], [p.Seat], new Dictionary<string, string> { ["program-action"] = "wu-huangfu-song", ["target-seat"] = p.Seat.ToString(CultureInfo.InvariantCulture), ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }
    private void PublishWuHf(ProgramSkillFrame f, IReadOnlyList<PromptChoice> choices)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
            { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wu-huangfu-song")]
    private void AnswerWuHf(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.WuHuangFuSong!;
        if (!ValidWuHf(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([selected], WuHfChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Wu Huangfu Song requires his current published private choice.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !r.Paid && r.Operation != SkillProgramEffectOp.SettleWuHuangFuSongJiangxian && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishWuHf(f); return; }
        if (r.Operation == SkillProgramEffectOp.SettleWuHuangFuSongJiangxian)
        {
            var skill = selected.Parameters["lose-skill"];
            ReplaceRuntimeTop(f = f with { WuHuangFuSong = r with { Paid = true, Stage = WuHuangFuSongStage.LossChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var grant in _players[f.OwnerSeat].SkillGrants.Grants.Where(g => g.IsEnabled && g.SkillId == skill).ToArray())
            { _players[f.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId); AdvanceEventRulesAndQueueFact(new WuHuangFuSongSkillRemovedEvent(f.Id, f.OwnerSeat, skill, grant.GrantId)); }
            AdvanceEventRulesAndQueueFact(new WuHuangFuSongJiangxianSettledEvent(f.Id, f.OwnerSeat, skill, r.Turn)); ContinueWuHf(f.Id); return;
        }
        if (r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie && r.Stage == WuHuangFuSongStage.Choosing)
        { ReplaceRuntimeTop(f = f with { WuHuangFuSong = r with { TargetSeat = selected.Targets.Single(), Stage = WuHuangFuSongStage.ChoosingMinimumHand } }); PublishWuHf(f, WuHfChoices(f)); return; }
        Card card; CardLocation from; int rank;
        if (r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen)
        {
            var x = WuHfMinimumPool().Single(x => selected.Parameters["selection"] == "deck" ? x.From == CardLocation.DrawPile : x.Card.Id.ToString(CultureInfo.InvariantCulture) == selected.Parameters["field-card"]);
            card = x.Card; from = x.From; rank = card.Rank;
        }
        else
        {
            var target = r.TargetSeat!.Value;
            if (!WuHfVictimAvailable(f.OwnerSeat, target)) { FinishWuHf(f); return; }
            card = GetHand(_players[target])[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)]; from = CardLocation.Hand(target); rank = EffectiveOwnedCardRank(_players[target], card);
        }
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { WuHuangFuSong = r with { CardId = card.Id, From = from, Rank = rank, Stage = WuHuangFuSongStage.MovementChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var to = r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DrawPile;
        MoveCard(card, from, to, new(r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen ? WuHfObtain : WuHfBottom), m =>
        {
            var current = GetActiveProgramFrame(f.Id); var receipt = current.WuHuangFuSong!;
            ReplaceRuntimeFrame(f.Id, current with { WuHuangFuSong = receipt with { Paid = true, To = m.To, After = m.Sequence } });
            AdvanceEventRulesAndQueueFact(new WuHuangFuSongMovedEvent(f.Id, card.Id, rank, from, m.To, m.Sequence));
            if (r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie)
                AdvanceEventRulesAndQueueFact(new WuHuangFuSongBottomPaidEvent(f.Id, f.OwnerSeat, r.TargetSeat!.Value, card.Id, rank, m.Sequence, r.Turn));
            else if (rank == 1 && m.To == CardLocation.Hand(f.OwnerSeat))
                AdvanceEventRulesAndQueueFact(new WuHuangFuSongChaozhenDisabledEvent(f.Id, f.OwnerSeat, r.Turn));
        }, disclosure: from.Zone == CardZoneKind.Hand || from == CardLocation.DrawPile ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        if (to == CardLocation.DrawPile && _cardZones.GetLocation(card.Id) == to) _cardZones.PlaceDrawPileCardsAtBottom([card.Id]);
        SealWuHf(f.Id, before); ContinueWuHf(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen, SkillProgramEffectOp.ResolveWuHuangFuSongLianjie, SkillProgramEffectOp.SettleWuHuangFuSongJiangxian)]
    private PromptChoice AiWuHf(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.WuHuangFuSong?.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie && f.WuHuangFuSong.Stage == WuHuangFuSongStage.Choosing)
            return p.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat), c.Targets.Single(), new(0,0,0,0,0,1,false,false))).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        return p.Choices.First();
    }
    private void ContinueWuHf(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuHuangFuSong: { } r } f || f.Id != id) return;
        if (!ValidWuHf(f)) throw new InvalidOperationException("Wu Huangfu Song lost the owning paid receipt.");
        if (r.Stage is WuHuangFuSongStage.Choosing or WuHuangFuSongStage.ChoosingMinimumHand or WuHuangFuSongStage.Drawing) return;
        if (DrainWuHf(f)) return;
        f = GetActiveProgramFrame(id); r = f.WuHuangFuSong!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishWuHf(f); return; }
        if (r.Stage == WuHuangFuSongStage.MovementChildren && r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen && r.Rank == 1 && r.To == CardLocation.Hand(f.OwnerSeat))
        {
            ReplaceRuntimeTop(f = f with { WuHuangFuSong = r with { Stage = WuHuangFuSongStage.Recovering }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            new ProgramSkillHost(this).Recover(id, f.OwnerSeat, f.OwnerSeat, 1, null, null); SealWuHf(id, r.After);
            if (DrainWuHf(GetActiveProgramFrame(id))) return; FinishWuHf(GetActiveProgramFrame(id)); return;
        }
        if (r.Stage == WuHuangFuSongStage.MovementChildren && r.Operation == SkillProgramEffectOp.ResolveWuHuangFuSongLianjie)
        {
            var count = Math.Max(0, _players[f.OwnerSeat].MaxHp - GetHand(_players[f.OwnerSeat]).Count);
            if (count == 0) { FinishWuHf(f); return; }
            ReplaceRuntimeTop(f = f with { WuHuangFuSong = r with { Stage = WuHuangFuSongStage.Drawing, RequestedDraw = count }, PendingMovementContinuation = null });
            BeginOperationProgramDraw(f, f.OwnerSeat, count, new(WuHfDraw)); return;
        }
        FinishWuHf(f);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWuHuangFuSongLianjie)]
    private void ReturnWuHfDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        var r = f.WuHuangFuSong!;
        if (!ValidWuHf(f) || r.Stage != WuHuangFuSongStage.Drawing || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.RequestedDraw) throw new InvalidOperationException("Lianjie returned a different draw request.");
        ReplaceRuntimeTop(f = f with { WuHuangFuSong = r with { Draw = o, Stage = WuHuangFuSongStage.DrawChildren, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuHuangFuSongDrawReturnedEvent(f.Id, o.RecipientSeat, o.RequestedCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter));
        foreach (var id in o.ActualDrawIds)
        {
            var move = _cardMovements.Single(m => m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == WuHfDraw);
            AdvanceEventRulesAndQueueFact(new WuHuangFuSongLianjieEntityEvent(f.Id, f.OwnerSeat, id, move.Sequence, r.Turn));
        }
        SealWuHf(f.Id, o.SequenceBefore); ContinueWuHf(f.Id);
    }
    private void FinishWuHf(ProgramSkillFrame f)
    {
        var r = f.WuHuangFuSong!;
        if (r.Operation == SkillProgramEffectOp.SettleWuHuangFuSongJiangxian && !ProgramEventHistory<WuHuangFuSongJiangxianSettledEvent>().Any(e => e.OwnerSeat == f.OwnerSeat && e.Turn == r.Turn))
            AdvanceEventRulesAndQueueFact(new WuHuangFuSongJiangxianSettledEvent(f.Id, f.OwnerSeat, null, r.Turn));
        AdvanceEventRulesAndQueueFact(new WuHuangFuSongCompletedEvent(f.Id, r.Operation, r.Paid));
        ReplaceRuntimeTop(f = f with { WuHuangFuSong = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
}
