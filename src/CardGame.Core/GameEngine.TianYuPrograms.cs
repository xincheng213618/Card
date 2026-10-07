namespace CardGame.Core;

// 扫狄 evidence: one scalar record per added target. The owning use and action
// are already public (their acceptance is committed before the finalized-target
// window), so the record only names the seat this skill appended.
public sealed record ProgramSaodiTargetsAddedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, long CardUseFrameId, long CardActionId, int AddedSeat) : IGameEvent;

// Shared directed distance reduction: the grant/revoke pair records one owner
// holding a distance reduction against one target. Any content skill can grant
// through the 追讨-shaped ops; EvaluateDistance applies active grants as additive
// outgoing-distance contributions, so the state is derived from committed
// history on cold recovery and no runtime map survives a checkpoint.
public sealed record ProgramDirectedDistanceGrantedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, int TargetSeat, int Amount) : IGameEvent;

public sealed record ProgramDirectedDistanceRevokedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, int TargetSeat) : IGameEvent;

public sealed partial class GameEngine
{
    // 扫狄 card kinds: 【杀】(all three faces) plus the ordinary tricks that can
    // designate a character target. Pure-benefit or no-character tricks
    // (无中生有、五谷丰登、无懈可击) and the all-others AOE tricks can never
    // satisfy "仅指定一名其他角色为目标" with a usable in-between set, so the
    // trigger does not list them.
    internal static readonly CardKind[] SaodiCardKinds =
    [
        CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
        CardKind.Duel, CardKind.Dismantlement, CardKind.Snatch,
        CardKind.FireAttack, CardKind.IronChain, CardKind.BorrowedSword
    ];

    // 扫狄 resolve: the accepted optional trigger re-reads the finalized use,
    // turns every legal in-between character into an additional target and
    // commits one evidence record per added seat. With no usable in-between
    // character the invocation ends without a prompt or event.
    private SkillProgramStepOutcome SaodiProgramExpandTargets(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            active.WindowContext?.CardUse is null)
            return SkillProgramStepOutcome.Continue;
        var use = EnhancementCardUse(active);
        if (use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != active.OwnerSeat ||
            action.EffectiveDesignatedTargetSeats is not [var anchor] ||
            anchor == active.OwnerSeat || !IsValidPlayerSeat(anchor) || !_players[anchor].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var additions = SaodiBetweenSeats(active.OwnerSeat, anchor)
            .Where(seat => CanSaodiAddTarget(use, seat)).ToArray();
        if (additions.Length == 0)
            return SkillProgramStepOutcome.Continue;
        var targets = Array.AsReadOnly(use.TargetSeats.Concat(additions).ToArray());
        UpdateLifecycleCardUse(use.Id, old => old with
        {
            TargetSeats = targets,
            Action = CloneRoleAction(action, action.ActorSeat, targets),
            TargetsAdjusted = old.TargetsAdjusted || old.CardKind == CardKind.BorrowedSword
        });
        SyncIssuedTieredRoundZeroTrickTargetWindows(use.Id, action);
        var bindingId = GetProgramBindingId(active);
        foreach (var seat in additions)
            AdvanceEventRulesAndQueueFact(new ProgramSaodiTargetsAddedEvent(active.Id, active.SkillId,
                bindingId, active.SkillInstanceId, active.OwnerSeat, use.Id, action.ActionId, seat));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，" +
            string.Join("、", additions.Select(seat => _players[seat].Name)) + "也成为此牌的目标。",
            active.OwnerSeat);
        return SkillProgramStepOutcome.Continue;
    }

    // "你与其之间的角色": alive seats strictly between the two endpoints along
    // the shorter seating arc, counted the same way GetAliveSeatDistance counts
    // (dead seats are skipped, the target endpoint always counts). Equal arcs
    // contribute their union.
    private int[] SaodiBetweenSeats(int ownerSeat, int targetSeat)
    {
        var clockwise = CollectAliveSeatsOnArc(ownerSeat, targetSeat, step: 1);
        var counterClockwise = CollectAliveSeatsOnArc(ownerSeat, targetSeat, step: -1);
        int ArcLength(List<int> arc) => arc.Count + 1;
        if (ArcLength(clockwise) < ArcLength(counterClockwise))
            return clockwise.Order().ToArray();
        if (ArcLength(counterClockwise) < ArcLength(clockwise))
            return counterClockwise.Order().ToArray();
        return clockwise.Concat(counterClockwise).Distinct().Order().ToArray();
    }

    private List<int> CollectAliveSeatsOnArc(int ownerSeat, int targetSeat, int step)
    {
        var seats = new List<int>();
        var seat = ownerSeat;
        do
        {
            seat = (seat + step + _playerCount) % _playerCount;
            if (seat == targetSeat) break;
            if (_players[seat].IsAlive) seats.Add(seat);
        }
        while (seat != ownerSeat);
        return seats;
    }

    // One added target must be a legal recipient of the pending card, mirroring
    // the shared original-target checks. Distance never disqualifies an
    // in-between character: the OL text carries no reach requirement for them.
    private bool CanSaodiAddTarget(CardUseFrame use, int targetSeat)
    {
        if (use.Action is not { Type: CardActionType.Use } action ||
            !IsValidPlayerSeat(targetSeat) ||
            action.EffectiveDesignatedTargetSeats.Contains(targetSeat))
            return false;
        var actor = _players[action.ActorSeat];
        var target = _players[targetSeat];
        if (!target.IsAlive ||
            IsDirectedCardTargetProhibited(actor.Seat, targetSeat, use.CardKind) ||
            IsCardTargetProhibited(target, use.CardKind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) ||
            HasBeneficiarySuitShield(actor.Seat, targetSeat, action.EffectiveSuit))
            return false;
        if (IsSlashCard(use.CardKind))
            return CanUseSlashTarget(actor, target, new Card(-1, use.CardKind, action.EffectiveSuit ?? Suit.None, action.EffectiveRank ?? 0),
                effectiveKind: use.CardKind, ignoreDistance: true,
                existingUseFrameId: use.Id, specificEffectiveRank: action.EffectiveRank);
        return use.CardKind switch
        {
            CardKind.IronChain => true,
            CardKind.BorrowedSword => GetEquipment(target).Any(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon) &&
                _players.Any(victim => IsLegalBorrowedSwordSlashTarget(target, victim)),
            CardKind.Duel => targetSeat != actor.Seat,
            CardKind.FireAttack => GetHand(target).Count > 0,
            CardKind.Dismantlement or CardKind.Snatch => targetSeat != actor.Seat &&
                GetHand(target).Count + GetEquipment(target).Count + GetJudgment(target).Count > 0,
            _ => false
        };
    }

    // 追讨 mark: the accepted optional preparation trigger offers every other
    // living character whose distance the owner does not currently reduce this
    // way; the choice commits the directed distance grant.
    private SkillProgramStepOutcome ZhuitaoProgramMarkTarget(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var candidates = _players
            .Where(player => player.IsAlive && player.Seat != active.OwnerSeat &&
                ActiveDirectedDistanceGrants(active.OwnerSeat, player.Seat).Count == 0)
            .Select(player => player.Seat).Order().ToArray();
        if (candidates.Length == 0)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
                new ChoiceId($"zhuitao-mark.frame-{active.Id}.seat-{seat}"),
                $"令你与 {_players[seat].Name} 的距离-1。",
                [], [seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zhuitao-mark",
                    ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["target-seat"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择一名未以此法减少距离的其他角色，你与你的目标距离-1。",
            [], candidates, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择目标", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveZhuitaoMarkChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zhuitao choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZhuitaoMarkTarget } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Zhuitao choice does not match its suspended instruction.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat ||
            !_players[targetSeat].IsAlive ||
            ActiveDirectedDistanceGrants(frame.OwnerSeat, targetSeat).Count > 0)
            throw new InvalidOperationException("The Zhuitao target is no longer legal.");
        var active = GetActiveProgramFrame(frame.Id);
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new ProgramDirectedDistanceGrantedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.SkillInstanceId, active.OwnerSeat, targetSeat, -1));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，" +
            $"令自己与 {_players[targetSeat].Name} 的距离-1。",
            active.OwnerSeat, targetSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    // 追讨 revoke: the locked damage sweep drops every active reduction the
    // owner holds against the damaged character; damages to anyone else and
    // owner-less damage leave the ledger untouched.
    private SkillProgramStepOutcome ZhuitaoProgramRevoke(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            active.WindowContext?.TargetSeat is not { } damagedSeat ||
            damagedSeat == active.OwnerSeat)
            return SkillProgramStepOutcome.Continue;
        var grants = ActiveDirectedDistanceGrants(active.OwnerSeat, damagedSeat);
        if (grants.Count == 0)
            return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramDirectedDistanceRevokedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.SkillInstanceId, active.OwnerSeat, damagedSeat));
        AddLog("SkillEffect",
            $"{owner.Name} 对 {_players[damagedSeat].Name} 造成伤害，失去以此法对其减少的距离。",
            active.OwnerSeat, damagedSeat);
        return SkillProgramStepOutcome.Continue;
    }

    // Shared ledger: replays the committed grant/revoke history for one ordered
    // pair. A revoke erases every grant emitted before it; a later re-grant
    // starts a fresh entry, so "直到你对其造成伤害后" survives cold recovery.
    private IReadOnlyList<ProgramDirectedDistanceGrantedEvent> ActiveDirectedDistanceGrants(int ownerSeat, int targetSeat)
    {
        var active = new List<ProgramDirectedDistanceGrantedEvent>();
        foreach (var fact in CompleteProgramEventHistory())
        {
            switch (fact)
            {
                case ProgramDirectedDistanceGrantedEvent grant when grant.OwnerSeat == ownerSeat && grant.TargetSeat == targetSeat:
                    active.Add(grant);
                    break;
                case ProgramDirectedDistanceRevokedEvent revoke when revoke.OwnerSeat == ownerSeat && revoke.TargetSeat == targetSeat:
                    active.Clear();
                    break;
            }
        }
        return active;
    }

    // Shared consumption: every active directed grant against the queried pair
    // joins the outgoing distance as an additive contribution, bounded to the
    // game minimum of one, beneath the Set-style distance overlays.
    private RuleQueryEvaluation ApplyProgramDirectedDistance(CharacterState source, CharacterState target,
        RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ZhuitaoMarkTarget))
            return original;
        var grants = ActiveDirectedDistanceGrants(source.Seat, target.Seat);
        if (grants.Count == 0)
            return original;
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-program-directed", ConvertRuleValue(original))],
            grants.Select((grant, index) => (RuleQueryContribution)new FiniteRuleQueryContribution(
                $"program-directed:{grant.FrameId}:{index}", SkillRuleOperation.Add, grant.Amount)).ToArray());
    }

    // 追讨 mark: price every candidate through the shared target scoring; the
    // hint charges one point of prospective harm so a hostile target wins.
    private PromptChoice SelectAiZhuitaoMarkChoice(PendingDecision decision)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        return decision.Choices
            .Select(choice =>
            {
                var targetSeat = int.Parse(choice.Parameters.GetValueOrDefault("target-seat", "-1"),
                    System.Globalization.CultureInfo.InvariantCulture);
                var hint = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
                return (Choice: choice, Score: brain.ScoreProgramTarget(view, targetSeat, hint));
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Choice.Id.Value, StringComparer.Ordinal)
            .First().Choice;
    }

    private sealed partial class ProgramSkillHost : ITianYuProgramHost
    {
        public SkillProgramStepOutcome SaodiExpandTargets(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.SaodiProgramExpandTargets(frame, effect);
        public SkillProgramStepOutcome ZhuitaoMarkTarget(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhuitaoProgramMarkTarget(frame, effect);
        public SkillProgramStepOutcome ZhuitaoRevoke(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhuitaoProgramRevoke(frame, effect);
    }
}
