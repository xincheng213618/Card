namespace CardGame.Core;

// 同协 arm evidence: one public record per accepted arm. The member seats are
// public information (the skill text announces the 同协 roles), so the event
// carries them; membership expiry derives from the owner's own TurnStartedEvent
// facts already in history, so cold recovery rebuilds the same active arm.
public sealed record ProgramTongxieArmedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, IReadOnlyList<int> MemberSeats) : IGameEvent;

// 同协 follow-up evidence: one record per completed chain window. Used and
// declined responder seats are public; the used slash card ids are ordinary
// card-use movements and stay out of this scalar record.
public sealed record ProgramTongxieFollowUpResolvedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ActorSeat, int TargetSeat,
    IReadOnlyList<int> UsedBy, IReadOnlyList<int> DeclinedBy) : IGameEvent;

// 同协 guard evidence: one record per accepted prevention; the prevented amount
// mirrors the frozen before-damage window.
public sealed record ProgramTongxieGuardedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ProtectedSeat, int PreventerSeat, int PreventedAmount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TongxieFollowUpState? TongxieFollowUp { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TongxieGuardState? TongxieGuard { get; init; }
}

// One responder at a time: the pending list holds the remaining 同协 members in
// seat order after the slash user; used/declined evidence accumulates on the
// owning frame across slash children.
public sealed record TongxieFollowUpState(int ActorSeat, int TargetSeat, IReadOnlyList<int> PendingResponders,
    IReadOnlyList<int>? UsedBy = null, IReadOnlyList<int>? DeclinedBy = null)
{
    public IReadOnlyList<int> Used { get; init; } = UsedBy ?? [];
    public IReadOnlyList<int> Declined { get; init; } = DeclinedBy ?? [];
}

// One prevention candidate at a time; a nonzero accepted preventer marks the
// resume after that member's losing-hp dying child.
public sealed record TongxieGuardState(int ProtectedSeat, int PreventedAmount,
    IReadOnlyList<int> PendingPreventers, int? AcceptedPreventer = null);

public sealed partial class GameEngine
{
    // The active arm is the latest 同协 arm fact for this owner unless the
    // owner's own turn started later in history (直到你的下回合开始).
    private ProgramTongxieArmedEvent? ActiveTongxieArm(int ownerSeat)
    {
        foreach (var fact in CompleteProgramEventHistory().Reverse())
        {
            switch (fact)
            {
                case ProgramTongxieArmedEvent armed when armed.OwnerSeat == ownerSeat:
                    return armed;
                case TurnStartedEvent turn when turn.ActorSeat == ownerSeat:
                    return null;
            }
        }
        return null;
    }

    // 失去体力 evidence is only recorded by the program hp-loss pipeline, which
    // is the engine's single committed path for hp loss; damage is a separate
    // rules operation and does not count.
    private bool TongxieLostHpThisTurn(int seat)
    {
        foreach (var fact in CompleteProgramEventHistory().Reverse())
        {
            switch (fact)
            {
                case ProgramSkillHpLostEvent lost when lost.TargetSeat == seat:
                    return true;
                case TurnStartedEvent:
                    return false;
            }
        }
        return false;
    }

    private static bool IsTongxieMember(ProgramTongxieArmedEvent arm, int seat) =>
        arm.MemberSeats.Contains(seat);

    // Follow-up candidates: the other alive members with at least one hand
    // slash, in seat order after the slash user; the original target never
    // slashes itself.
    private IEnumerable<int> TongxieFollowUpResponders(IReadOnlyList<int> members, int actorSeat, int targetSeat)
    {
        var count = _players.Count;
        return members
            .Where(seat => seat != actorSeat && seat != targetSeat)
            .Where(seat => _players[seat].IsAlive)
            .Where(seat => GetHand(_players[seat]).Any(card => IsSlashCard(card.Kind)))
            .OrderBy(seat => (seat - actorSeat + count) % count)
            .ThenBy(seat => seat);
    }

    // Prevention candidates: the other alive members that have not lost hp this
    // turn, in seat order after the protected member.
    private IEnumerable<int> TongxieEligiblePreventers(IReadOnlyList<int> members, int protectedSeat)
    {
        var count = _players.Count;
        return members
            .Where(seat => seat != protectedSeat)
            .Where(seat => _players[seat].IsAlive)
            .Where(seat => !TongxieLostHpThisTurn(seat))
            .OrderBy(seat => (seat - protectedSeat + count) % count)
            .ThenBy(seat => seat);
    }

    internal bool CanRunTongxieArm(int ownerSeat)
    {
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive)
            return false;
        return _players.Any(player => player.IsAlive && player.Seat != ownerSeat);
    }

    internal bool CanRunTongxieFollowUp(int ownerSeat, ProgramSkillWindowContext context)
    {
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive)
            return false;
        if (context.CardUse is not { } use || use.DesignatedTargetSeats is not { Count: 1 } designated)
            return false;
        if (IsTongxieProducedSlash(use.ParentCardUseFrameId))
            return false;
        var arm = ActiveTongxieArm(ownerSeat);
        if (arm is null || !IsTongxieMember(arm, use.ActorSeat) || !_players[use.ActorSeat].IsAlive)
            return false;
        var targetSeat = designated[0];
        if (!IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive)
            return false;
        return TongxieFollowUpResponders(arm.MemberSeats, use.ActorSeat, targetSeat).Any();
    }

    // 不因此技能使用: a slash produced by a 同协 follow-up itself never opens
    // another chain window; the producing program frame is identified by its
    // card-attack link, not by any concrete skill id.
    private bool IsTongxieProducedSlash(long parentCardUseFrameId)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>()
            .LastOrDefault(item => item.Id == parentCardUseFrameId);
        if (frame?.CardAttack?.ProgramSkillCardUseFrameId is not { } producerId)
            return false;
        return _resolutionStack.OfType<ProgramSkillFrame>().Any(item => item.Id == producerId &&
            _contentRegistry!.GetSkill(item.SkillId).Program!.Triggers.Any(trigger =>
                trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.TongxieFollowUp)));
    }

    internal bool CanRunTongxieGuard(int ownerSeat, ProgramSkillWindowContext context)
    {
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive)
            return false;
        if (context.Window != SkillProgramTriggerWindow.BeforeDamageApplied ||
            context.TargetSeat is not { } victim || context.Amount <= 0 || !_players[victim].IsAlive)
            return false;
        var arm = ActiveTongxieArm(ownerSeat);
        if (arm is null || !IsTongxieMember(arm, victim))
            return false;
        return TongxieEligiblePreventers(arm.MemberSeats, victim).Any();
    }

    // 同协 arm: the play-phase-start prompt offers every legal member set
    // (self only, one other, or a pair of others); the unique-least-hand draw
    // follows the accepted set.
    private SkillProgramStepOutcome TongxieProgramArm(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } ||
            active.OwnerSeat != _currentSeat)
            return SkillProgramStepOutcome.Continue;
        var others = _players.Where(player => player.IsAlive && player.Seat != owner.Seat)
            .Select(player => player.Seat).Order().ToArray();
        if (others.Length == 0) return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new List<PromptChoice>
        {
            new(new ChoiceId($"tongxie-arm.frame-{frame.Id}.self"),
                "仅自己称为“同协”角色。",
                [], [owner.Seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tongxie-arm",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["seats"] = ""
                })
        };
        foreach (var seat in others)
            choices.Add(new PromptChoice(
                new ChoiceId($"tongxie-arm.frame-{frame.Id}.single-{seat}"),
                $"与 {_players[seat].Name} 称为“同协”角色。",
                [], [seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tongxie-arm",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["seats"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        for (var first = 0; first < others.Length; first++)
        for (var second = first + 1; second < others.Length; second++)
            choices.Add(new PromptChoice(
                new ChoiceId($"tongxie-arm.frame-{frame.Id}.pair-{others[first]}-{others[second]}"),
                $"与 {_players[others[first]].Name}、{_players[others[second]].Name} 称为“同协”角色。",
                [], [others[first], others[second]],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tongxie-arm",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["seats"] = $"{others[first]},{others[second]}"
                }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择至多两名其他角色，与你一起称为“同协”角色（直到你的下回合开始）。",
            [], choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择同协角色", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveTongxieArmChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Tongxie arm choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.TongxieArm } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Tongxie arm choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Tongxie arm choice lost its chooser.");
        var chosen = ParseTongxieArmSeats(selected.Parameters.GetValueOrDefault("seats"));
        if (chosen.Length > 2 || chosen.Distinct().Count() != chosen.Length ||
            chosen.Any(seat => !IsValidPlayerSeat(seat) || !_players[seat].IsAlive || seat == owner.Seat))
            throw new InvalidOperationException("The Tongxie arm choice names an invalid member set.");
        ClearPendingDecision();
        var members = new[] { owner.Seat }.Concat(chosen).Order().ToArray();
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】：" +
            $"{DescribeTongxieMembers(members)}称为“同协”角色（直到{owner.Name}的下回合开始）。",
            active.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramTongxieArmedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _turnNumber, Array.AsReadOnly(members)));
        DrawForUniqueLeastTongxieHand(active, members);
        AdvanceRuntimeProgram(active.Id);
    }

    private static int[] ParseTongxieArmSeats(string? seats) =>
        string.IsNullOrEmpty(seats) ? [] : seats.Split(',').Select(seat =>
            int.Parse(seat, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private string DescribeTongxieMembers(IReadOnlyList<int> members) =>
        string.Join("、", members.Select(seat => _players[seat].Name));

    // 令其中手牌唯一最少的角色摸一张牌: the draw belongs to the arm; a tied
    // minimum draws nobody.
    private void DrawForUniqueLeastTongxieHand(ProgramSkillFrame active, IReadOnlyList<int> members)
    {
        var counts = members.Select(seat => (Seat: seat, Count: GetHand(_players[seat]).Count)).ToArray();
        var minimum = counts.Min(entry => entry.Count);
        var least = counts.Where(entry => entry.Count == minimum).ToArray();
        var drawer = least.Length == 1 ? _players[least[0].Seat] : null;
        if (drawer is null)
        {
            AddLog("SkillEffect",
                $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】：没有手牌唯一最少的“同协”角色，无人摸牌。",
                active.OwnerSeat);
            return;
        }
        DrawCards(drawer, 1, true, new($"skill-program.{active.SkillId}.tongxie-draw"));
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】：{drawer.Name} 手牌唯一最少，摸一张牌。",
            active.OwnerSeat, drawer.Seat);
    }

    // 同协 follow-up: one member responder at a time; an accepted slash
    // suspends as a child and the rewound cursor re-enters this instruction.
    private SkillProgramStepOutcome TongxieProgramFollowUp(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (frame.TongxieFollowUp is { } pending)
            return ContinueTongxieFollowUp(active, pending);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } use } ||
            use.DesignatedTargetSeats is not { Count: 1 } designated ||
            IsTongxieProducedSlash(use.ParentCardUseFrameId))
            return SkillProgramStepOutcome.Continue;
        var arm = ActiveTongxieArm(active.OwnerSeat);
        if (arm is null || !IsTongxieMember(arm, use.ActorSeat) || !_players[use.ActorSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var targetSeat = designated[0];
        if (!IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var responders = TongxieFollowUpResponders(arm.MemberSeats, use.ActorSeat, targetSeat).ToArray();
        if (responders.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            TongxieFollowUp = new TongxieFollowUpState(use.ActorSeat, targetSeat, Array.AsReadOnly(responders))
        });
        return PresentTongxieFollowUpPrompt(GetActiveProgramFrame(frame.Id));
    }

    private SkillProgramStepOutcome ContinueTongxieFollowUp(ProgramSkillFrame active, TongxieFollowUpState pending)
    {
        if (pending.PendingResponders.Count == 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { TongxieFollowUp = null });
            var used = DescribeTongxieActors("使用【杀】", pending.Used);
            var declined = DescribeTongxieActors("放弃追杀", pending.Declined);
            AddLog("SkillEffect",
                $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】追杀结算完成" +
                (used.Length + declined.Length > 0 ? $"：{used}{declined}。" : "。"),
                active.OwnerSeat, pending.TargetSeat);
            AdvanceEventRulesAndQueueFact(new ProgramTongxieFollowUpResolvedEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, pending.ActorSeat, pending.TargetSeat,
                Array.AsReadOnly(pending.Used.ToArray()), Array.AsReadOnly(pending.Declined.ToArray())));
            return SkillProgramStepOutcome.Continue;
        }
        return PresentTongxieFollowUpPrompt(GetActiveProgramFrame(active.Id));
    }

    private string DescribeTongxieActors(string verb, IReadOnlyList<int> seats) =>
        seats.Count == 0 ? "" : $"{(seats.Count > 0 ? "，" : "")}{verb}：{string.Join("、", seats.Select(seat => _players[seat].Name))}";

    private SkillProgramStepOutcome PresentTongxieFollowUpPrompt(ProgramSkillFrame frame)
    {
        var pending = frame.TongxieFollowUp ??
            throw new InvalidOperationException("The Tongxie follow-up lost its pending state.");
        var active = GetActiveProgramFrame(frame.Id);
        var responderSeat = pending.PendingResponders[0];
        var responder = _players[responderSeat];
        var target = _players[pending.TargetSeat];
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var slashes = GetHand(responder).Where(card => IsSlashCard(card.Kind))
            .OrderBy(card => card.Id).ToArray();
        if (slashes.Length == 0)
        {
            // The candidate list was frozen at window start; a lost slash skips
            // this responder without a prompt.
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                TongxieFollowUp = pending with
                {
                    PendingResponders = Array.AsReadOnly(pending.PendingResponders.Skip(1).ToArray())
                }
            });
            return ContinueTongxieFollowUp(GetActiveProgramFrame(frame.Id),
                GetActiveProgramFrame(frame.Id).TongxieFollowUp!);
        }
        var choices = slashes.Select(card => new PromptChoice(
            new ChoiceId($"tongxie-follow-up.frame-{frame.Id}.card-{card.Id}"),
            $"对 {target.Name} 使用【{card.DisplayName}】（无距离限制）。",
            [card.Id], [pending.TargetSeat],
            new Dictionary<string, string>
            {
                ["program-action"] = "tongxie-follow-up",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"tongxie-follow-up.frame-{frame.Id}.decline"),
            $"不对 {target.Name} 使用【杀】。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "tongxie-follow-up-decline",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, responderSeat,
            $"【{presentation.Name}】你可以对 {target.Name} 使用一张无距离限制的【杀】。",
            slashes.Select(card => card.Id).ToArray(), [pending.TargetSeat], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = responderSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 追杀", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = responder.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveTongxieFollowUpChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Tongxie follow-up choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.TongxieFollowUp } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Tongxie follow-up choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        var pending = active.TongxieFollowUp ??
            throw new InvalidOperationException("The Tongxie follow-up lost its pending state.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != pending.PendingResponders[0])
            throw new InvalidOperationException("The Tongxie follow-up responder changed while suspended.");
        ClearPendingDecision();
        var responderSeat = pending.PendingResponders[0];
        if (selected.Parameters.GetValueOrDefault("program-action") == "tongxie-follow-up-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                TongxieFollowUp = pending with
                {
                    PendingResponders = Array.AsReadOnly(pending.PendingResponders.Skip(1).ToArray()),
                    Declined = Array.AsReadOnly(pending.Declined.Append(responderSeat).ToArray())
                }
            });
            if (ContinueTongxieFollowUp(GetActiveProgramFrame(frame.Id),
                    GetActiveProgramFrame(frame.Id).TongxieFollowUp!) == SkillProgramStepOutcome.Continue)
                AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (selected.Parameters.GetValueOrDefault("program-action") != "tongxie-follow-up" ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            selected.Cards.Count != 1 || selected.Cards[0] != cardId ||
            selected.Targets.Count != 1 || selected.Targets[0] != pending.TargetSeat)
            throw new InvalidOperationException("The Tongxie follow-up answer is malformed.");
        var location = _cardZones.GetLocation(cardId);
        var card = location == CardLocation.Hand(responderSeat)
            ? _cardZones.CardsAt(location).SingleOrDefault(item => item.Id == cardId)
            : null;
        if (card is null || !IsSlashCard(card.Kind))
        {
            CancelProgramBindingAndCleanup(active, "所选【杀】已离开使用者手牌，同协追杀结算已取消。");
            return;
        }
        var usedBy = pending.Used.Append(responderSeat).ToArray();
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            // The rewound cursor re-enters this instruction after the slash
            // child completes; the used evidence stays on the frame.
            InstructionIndex = frame.InstructionIndex - 1,
            TongxieFollowUp = pending with
            {
                PendingResponders = Array.AsReadOnly(pending.PendingResponders.Skip(1).ToArray()),
                Used = Array.AsReadOnly(usedBy)
            }
        });
        ResolveSlashCore(_players[responderSeat], _players[pending.TargetSeat], card, card.Kind, responderSeat,
            physicalCards: [card], countsTowardSlashLimit: false,
            programSkillCardUseFrameId: active.Id);
    }

    // 同协 guard: one eligible member at a time; the first accepted prevention
    // marks the before-damage window and costs the preventer one hp.
    private SkillProgramStepOutcome TongxieProgramGuard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        if (frame.TongxieGuard is { } pending)
            return pending.AcceptedPreventer is not null
                ? FinalizeTongxieGuard(active, pending)
                : SkillProgramStepOutcome.Continue;
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                TargetSeat: { } victim, Amount: { } amount } || amount <= 0 || !_players[victim].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var arm = ActiveTongxieArm(active.OwnerSeat);
        if (arm is null || !IsTongxieMember(arm, victim))
            return SkillProgramStepOutcome.Continue;
        var preventers = TongxieEligiblePreventers(arm.MemberSeats, victim).ToArray();
        if (preventers.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        {
            TongxieGuard = new TongxieGuardState(victim, amount, Array.AsReadOnly(preventers))
        });
        return PresentTongxieGuardPrompt(GetActiveProgramFrame(frame.Id));
    }

    private SkillProgramStepOutcome PresentTongxieGuardPrompt(ProgramSkillFrame frame)
    {
        var pending = frame.TongxieGuard ??
            throw new InvalidOperationException("The Tongxie guard lost its pending state.");
        var active = GetActiveProgramFrame(frame.Id);
        var preventerSeat = pending.PendingPreventers[0];
        var preventer = _players[preventerSeat];
        if (!preventer.IsAlive || TongxieLostHpThisTurn(preventerSeat))
        {
            // The candidate list was frozen at window start; a dead or already
            // paid member skips without a prompt.
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                TongxieGuard = pending with
                {
                    PendingPreventers = Array.AsReadOnly(pending.PendingPreventers.Skip(1).ToArray())
                }
            });
            return ContinueTongxieGuard(GetActiveProgramFrame(frame.Id), GetActiveProgramFrame(frame.Id).TongxieGuard!);
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var protectedName = _players[pending.ProtectedSeat].Name;
        var choices = new List<PromptChoice>
        {
            new(new ChoiceId($"tongxie-guard.frame-{frame.Id}.prevent"),
                $"防止 {protectedName} 受到的 {pending.PreventedAmount} 点伤害，你失去1点体力。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tongxie-guard",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }),
            new(new ChoiceId($"tongxie-guard.frame-{frame.Id}.decline"),
                "不防止此伤害。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "tongxie-guard-decline",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })
        };
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, preventerSeat,
            $"【{presentation.Name}】你可以防止 {protectedName} 受到的 {pending.PreventedAmount} 点伤害并失去1点体力。",
            [], [pending.ProtectedSeat], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = preventerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 保护", presentation.Description),
            Choices = choices.AsReadOnly()
        };
        _status = preventer.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private SkillProgramStepOutcome ContinueTongxieGuard(ProgramSkillFrame active, TongxieGuardState pending)
    {
        if (pending.PendingPreventers.Count == 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { TongxieGuard = null });
            return SkillProgramStepOutcome.Continue;
        }
        return PresentTongxieGuardPrompt(GetActiveProgramFrame(active.Id));
    }

    private void ResolveTongxieGuardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Tongxie guard choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.TongxieGuard } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Tongxie guard choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        var pending = active.TongxieGuard ??
            throw new InvalidOperationException("The Tongxie guard lost its pending state.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != pending.PendingPreventers[0])
            throw new InvalidOperationException("The Tongxie guard responder changed while suspended.");
        ClearPendingDecision();
        var preventerSeat = pending.PendingPreventers[0];
        if (selected.Parameters.GetValueOrDefault("program-action") == "tongxie-guard-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                TongxieGuard = pending with
                {
                    PendingPreventers = Array.AsReadOnly(pending.PendingPreventers.Skip(1).ToArray())
                }
            });
            if (ContinueTongxieGuard(GetActiveProgramFrame(frame.Id),
                    GetActiveProgramFrame(frame.Id).TongxieGuard!) == SkillProgramStepOutcome.Continue)
                AdvanceRuntimeProgram(active.Id);
            return;
        }
        if (selected.Parameters.GetValueOrDefault("program-action") != "tongxie-guard" ||
            selected.Cards.Count != 0)
            throw new InvalidOperationException("The Tongxie guard answer is malformed.");
        // Prevent first, then the preventer's hp cost. A lethal cost starts a
        // dying child: the rewound cursor re-enters this instruction after the
        // child and the accepted state finalizes there.
        PreventProgramCurrentDamage(GetActiveProgramFrame(frame.Id));
        var accepted = pending with { AcceptedPreventer = preventerSeat };
        if (_players[preventerSeat].Hp == 1)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                InstructionIndex = frame.InstructionIndex - 1,
                TongxieGuard = accepted
            });
            new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, preventerSeat, 1);
            return;
        }
        new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, preventerSeat, 1);
        FinalizeTongxieGuard(GetActiveProgramFrame(frame.Id), accepted);
        AdvanceRuntimeProgram(active.Id);
    }

    private SkillProgramStepOutcome FinalizeTongxieGuard(ProgramSkillFrame active, TongxieGuardState pending)
    {
        if (pending.AcceptedPreventer is not { } preventerSeat)
            throw new InvalidOperationException("The Tongxie guard finalize lost its preventer.");
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { TongxieGuard = null });
        AddLog("SkillEffect",
            $"{_players[preventerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】保护生效：" +
            $"{_players[pending.ProtectedSeat].Name} 受到的 {pending.PreventedAmount} 点伤害被防止，{_players[preventerSeat].Name} 失去1点体力。",
            active.OwnerSeat, pending.ProtectedSeat);
        AdvanceEventRulesAndQueueFact(new ProgramTongxieGuardedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, pending.ProtectedSeat, preventerSeat,
            pending.PreventedAmount));
        return SkillProgramStepOutcome.Continue;
    }

    // 同协 arm: price every offered member set; each chosen member scores as a
    // support target and a guaranteed unique-least draw adds its card value.
    private PromptChoice SelectAiTongxieArmChoice(PendingDecision decision)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        return decision.Choices
            .Select(choice =>
            {
                var seats = ParseTongxieArmSeats(choice.Parameters.GetValueOrDefault("seats"));
                var members = new[] { decision.PlayerSeat }.Concat(seats);
                var support = seats.Sum(seat => brain.ScoreProgramTarget(view, seat,
                    new SkillProgramAiHint(0, 0, 0, 0, 0, 0, false, false)));
                return (Choice: choice, Score: support + TongxieArmDrawBonus(view, decision.PlayerSeat, seats));
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Choice.Parameters.GetValueOrDefault("seats", ""), StringComparer.Ordinal)
            .First().Choice;
    }

    private double TongxieArmDrawBonus(GameSnapshot view, int ownerSeat, int[] chosen)
    {
        var counts = new[] { ownerSeat }.Concat(chosen)
            .Select(seat => view.Players.Single(player => player.Seat == seat).HandCount)
            .ToArray();
        var minimum = counts.Min();
        return counts.Count(count => count == minimum) == 1 ? 7d : 0d;
    }

    // 同协 follow-up: use the cheapest slash when the target is hostile enough
    // to be worth the card; the answer stays deterministic in card-id order.
    private PromptChoice SelectAiTongxieFollowUpChoice(PendingDecision decision)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        var decline = decision.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "tongxie-follow-up-decline");
        var use = decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "tongxie-follow-up");
        if (use is null) return decline;
        var targetSeat = use.Targets.Count > 0 ? use.Targets[0] : -1;
        var value = brain.ScoreProgramTarget(view, targetSeat,
            new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false));
        return value > 0 ? use : decline;
    }

    // 同协 guard: protect a friend while the preventer keeps hp above one; the
    // prevention value scales with the prevented amount.
    private PromptChoice SelectAiTongxieGuardChoice(PendingDecision decision)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var brain = _aiBrains[decision.PlayerSeat];
        var decline = decision.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "tongxie-guard-decline");
        var prevent = decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "tongxie-guard");
        if (prevent is null) return decline;
        if (view.Players.Single(player => player.Seat == decision.PlayerSeat).Hp <= 1) return decline;
        var protectedSeat = prevent.Targets.Count > 0 ? prevent.Targets[0] : -1;
        var value = brain.ScoreProgramTarget(view, protectedSeat,
            new SkillProgramAiHint(0, 0, 0, 0, 1, 0, false, false));
        return value > 0 ? prevent : decline;
    }

    private sealed partial class ProgramSkillHost : IZhaoYanProgramHost
    {
        public SkillProgramStepOutcome TongxieArm(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TongxieProgramArm(frame, effect);
        public SkillProgramStepOutcome TongxieFollowUp(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TongxieProgramFollowUp(frame, effect);
        public SkillProgramStepOutcome TongxieGuard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TongxieProgramGuard(frame, effect);
    }
}
