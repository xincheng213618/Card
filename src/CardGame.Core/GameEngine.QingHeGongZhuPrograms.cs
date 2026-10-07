namespace CardGame.Core;

internal interface IQingHeGongZhuProgramHost
{
    SkillProgramStepOutcome ChangjiDesignationDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZengouGiftMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZengouPunishRecipient(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 长姬 evidence: the committed draw is public and derived from the frozen
// designated-target list of the finished card action.
public sealed record ProgramChangjiDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TargetCount, int DrawCount) : IGameEvent;

// 谮构 evidence: the gift is public once the entities move; the marks and the
// pending punish pairs live in the engine maps below until the punish event.
public sealed record ProgramZengouGiftedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat, IReadOnlyList<int> CardIds) : IGameEvent;
public sealed record ProgramZengouPunishEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat, IReadOnlyList<int> RevealedCardIds,
    int LostHp, IReadOnlyList<int> RemovedMarkedCardIds) : IGameEvent;

public sealed partial class GameEngine
{
    // 谮构 marks and pending punish pairs survive across turns until each
    // recipient's next HP increase or card use; replay reconstructs both maps
    // from the accepted commands, exactly like the Bijing mark map.
    private readonly Dictionary<int, int> _zengouMarkedCardOwners = [];
    private readonly HashSet<(int OwnerSeat, int RecipientSeat)> _zengouPendingPairs = [];

    // 长姬 draw: a finished use that designated more than one target including
    // the owner draws one card per other target. The resolution-order priority
    // ("此牌优先对你生效") is not expressible with current content nodes; the
    // draw therefore happens when the whole card finishes, and a portion that
    // was made ineffective against the owner draws nothing.
    private SkillProgramStepOutcome ChangjiProgramDesignationDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        var context = frame.WindowContext?.CardUse;
        var action = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault()?.Action ??
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(item => item.Action?.ActionId == context?.CardActionId)?.Action;
        if (action is null || action.ActionId != context?.CardActionId) return SkillProgramStepOutcome.Continue;
        var use = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(item => item.Action?.ActionId == action.ActionId);
        var designated = action.EffectiveDesignatedTargetSeats.Distinct().ToArray();
        if (designated.Length < 2 || !designated.Contains(owner.Seat) ||
            use?.IneffectiveTargetSeats is { } ineffective && ineffective.Contains(owner.Seat))
            return SkillProgramStepOutcome.Continue;
        var drawCount = designated.Length - 1;
        DrawCards(owner, drawCount, true, new($"skill-program.{active.SkillId}.changji-draw"));
        AddLog("SkillEffect",
            $"{owner.Name} 长姬：此牌指定 {designated.Length} 个目标且包含你，你摸其余目标数 {drawCount} 张牌。",
            owner.Seat);
        AdvanceEventRulesAndQueueFact(new ProgramChangjiDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, designated.Length, drawCount));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 谮构 gift: the selected hand cards move to the recipient, the owner draws
    // the same count, every moved entity is marked, and the (owner, recipient)
    // pair starts watching for the recipient's next HP increase or card use.
    private SkillProgramStepOutcome ZengouProgramGiftMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (active.SelectedTargetSeats is not [var recipient] || recipient == active.OwnerSeat ||
            !_players[recipient].IsAlive || !owner.IsAlive || _winner != Winner.None ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(active, "谮构的目标或技能实例已失效，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var binding = active.CardSetBindings.SingleOrDefault(item => item.Name == effect.SourceBind!);
        if (binding is null)
        {
            CancelProgramBindingAndCleanup(active, "谮构的选牌绑定未生成，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var cards = _cardZones.CardsAt(CardLocation.Hand(active.OwnerSeat))
            .Where(card => binding.CardIds.Contains(card.Id)).ToArray();
        if (cards.Length == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 谮构：没有交出任何牌，结算落空。", active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.Continue;
        }
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.zengou-gift");
        MoveCards(cards, CardLocation.Hand(active.OwnerSeat), CardLocation.Hand(recipient), reason);
        DrawCards(owner, cards.Length, true, new($"skill-program.{active.SkillId}.zengou-gift-draw"));
        foreach (var card in cards)
            _zengouMarkedCardOwners[card.Id] = active.OwnerSeat;
        _zengouPendingPairs.Add((active.OwnerSeat, recipient));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，将 {cards.Length} 张牌交给 {_players[recipient].Name} 并摸等量的牌；这些牌被标记为“谮构”牌。",
            active.OwnerSeat, recipient);
        AdvanceEventRulesAndQueueFact(new ProgramZengouGiftedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, recipient,
            Array.AsReadOnly(cards.Select(card => card.Id).ToArray())));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 谮构 punish: the pending pair is consumed by the recipient's next HP
    // increase (owner-side watcher candidates appended by the shared HP-change
    // window) or next card use (observer relation). The recipient reveals the
    // whole hand and loses one HP per marked card still in that hand; those
    // marks are removed.
    private SkillProgramStepOutcome ZengouProgramPunishRecipient(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        int? changedSeat = frame.WindowContext switch
        {
            { HpChange: { } hp } => hp.TargetSeat,
            { CardUse: { } use } => use.ActorSeat,
            _ => null
        };
        if (changedSeat is not { } seat || !_players[seat].IsAlive ||
            !_zengouPendingPairs.Contains((active.OwnerSeat, seat)))
            return SkillProgramStepOutcome.Continue;
        _zengouPendingPairs.Remove((active.OwnerSeat, seat));
        var recipient = _players[seat];
        var hand = GetHand(recipient).ToArray();
        var marked = hand.Where(card =>
            _zengouMarkedCardOwners.TryGetValue(card.Id, out var ownerSeat) && ownerSeat == active.OwnerSeat).ToArray();
        foreach (var card in marked)
            _zengouMarkedCardOwners.Remove(card.Id);
        AddLog("SkillEffect",
            $"{recipient.Name} 的“谮构”标记被结算：展示手牌 {string.Join('、', hand.Select(card => card.DisplayName))}，并失去 {marked.Length} 点体力。",
            active.OwnerSeat, seat);
        AdvanceEventRulesAndQueueFact(new ProgramZengouPunishEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, seat,
            Array.AsReadOnly(hand.Select(card => card.Id).ToArray()), marked.Length,
            Array.AsReadOnly(marked.Select(card => card.Id).ToArray())));
        if (marked.Length > 0)
        {
            var before = recipient.Hp;
            recipient.Hp = Math.Max(0, before - marked.Length);
            RecordHpChange(active.Id, active.OwnerSeat, seat, before, recipient.Hp, HpChangeKind.Loss);
            AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(active.Id, active.SkillId,
                seat, before - recipient.Hp, recipient.Hp));
        }
        if (recipient.Hp == 0)
        {
            BeginProgramSkillDying(active.Id, recipient);
            return SkillProgramStepOutcome.AwaitChild;
        }
        return SkillProgramStepOutcome.Continue;
    }

    // 谮构 watcher: HP-change windows only wake the changed player's own skill
    // instances, so the owner-side punish candidates are appended here behind a
    // content dependency. Fingerprints without the skill never reach this path.
    private IEnumerable<(ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)>
        ZengouWatcherBindings(HpChangeContext change, SkillProgramTriggerFacts facts)
    {
        if (change.HpAfter <= change.HpBefore ||
            _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ZengouPunishRecipient) != true)
            yield break;
        foreach (var ownerSeat in _players.Where(player => player.IsAlive &&
                _zengouPendingPairs.Contains((player.Seat, change.TargetSeat)))
            .Select(player => player.Seat).Order())
        foreach (var candidate in CollectProgramTriggerCandidates(_players[ownerSeat],
                SkillProgramTriggerWindow.AfterHealthChanged))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!trigger.Effects.Any(effectItem => effectItem.Op == SkillProgramEffectOp.ZengouPunishRecipient) ||
                !trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId))
                continue;
            yield return (candidate, new ProgramSkillWindowContext(
                SkillProgramTriggerWindow.AfterHealthChanged, change.Id, ownerSeat,
                SourceSeat: change.SourceSeat, TargetSeat: change.TargetSeat, Amount: change.Amount,
                Facts: facts, HpChange: change));
        }
    }

    private sealed partial class ProgramSkillHost : IQingHeGongZhuProgramHost
    {
        public SkillProgramStepOutcome ChangjiDesignationDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ChangjiProgramDesignationDraw(frame, effect);
        public SkillProgramStepOutcome ZengouGiftMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZengouProgramGiftMarkedCards(frame, effect);
        public SkillProgramStepOutcome ZengouPunishRecipient(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZengouProgramPunishRecipient(frame, effect);
    }
}
