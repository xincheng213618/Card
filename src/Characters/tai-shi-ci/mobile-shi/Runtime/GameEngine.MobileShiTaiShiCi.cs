using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiTzHanzhan = ShiTaiShiCiComposition.HanzhanSkill,
        ShiTzZhanlie = ShiTaiShiCiComposition.ZhanlieSkill, ShiTzZhenfeng = ShiTaiShiCiComposition.ZhenfengSkill;

    private int _shiTzLieScanSequence;

    private static CardConversionSource ShiTzSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasShiTzCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveShiTzHanzhan);
    private int ShiTzLieCount() => ProgramEventHistory<ShiTzLieGainedEvent>().Count();
    private int? ShiTzFixedX() => ProgramEventHistory<ShiTzZhenfengFixedEvent>().LastOrDefault()?.Value;
    private int ShiTzHanzhanHandCap(CharacterState player) => ShiTzFixedX() ?? player.MaxHp;
    private int ShiTzZhanlieRange() => ShiTzFixedX() ?? GetAttackRange(_currentSeat);
    private ShiTzSlashIssuedEvent? ShiTzIssuedSlash(long resolutionId)
    {
        if (LifecycleCardUse(resolutionId)?.Action?.ConversionChain is not [var source] || source.SkillId != ShiTzZhanlie) return null;
        var frame = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SkillId == ShiTzZhanlie && f.ShiTaiShiCi is { Operation: "zhanlie" });
        return frame is null ? null : ProgramEventHistory<ShiTzSlashIssuedEvent>().LastOrDefault(e =>
            e.Source.OwnerSeat == frame.OwnerSeat && e.Source.SkillInstanceId == frame.SkillInstanceId &&
            e.ActualTurn == _turnNumber);
    }
    private bool ShiTzGateRequired(long resolutionId) =>
        ShiTzIssuedSlash(resolutionId) is { Options: var options } && (options & ShiTaiShiCiComposition.OptionRespondCost) != 0 &&
        !ProgramEventHistory<ShiTzRespondGatePaidEvent>().Any(e => e.AttackResolutionId == resolutionId);

    [ProgramCapability]
    private sealed class ShiTaiShiCiCapability(GameEngine engine) : IShiTzProgramHost
    {
        public SkillProgramStepOutcome ResolveHanzhan(ProgramSkillFrame frame) => engine.BeginShiTz(frame);
        public SkillProgramStepOutcome ResolveRangeRecord(ProgramSkillFrame frame) => engine.BeginShiTz(frame);
        public SkillProgramStepOutcome ResolveZhanlie(ProgramSkillFrame frame) => engine.BeginShiTz(frame);
        public SkillProgramStepOutcome ResolveRespondGate(ProgramSkillFrame frame) => engine.BeginShiTz(frame);
        public SkillProgramStepOutcome ResolveZhenfeng(ProgramSkillFrame frame) => engine.BeginShiTz(frame);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67600)]
    private bool CanOfferShiTz(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != ShiTzZhanlie) return true;
        if (trigger.Id == ShiTaiShiCiComposition.ZhanlieGateTrigger)
            return ShiTzGateAttack(context) is { } attack && ShiTzGateRequired(attack.ResolutionId);
        if (trigger.Id == ShiTaiShiCiComposition.ZhanlieEndingTrigger)
            return ShiTzLieCount() > 0;
        return true;
    }

    private CardAttackHandle? ShiTzGateAttack(ProgramSkillWindowContext context)
    {
        if (_winner != Winner.None || _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
            .SingleOrDefault(item => item.Id == context.ParentFrameId) is not { AttackOwnerFrameId: { } ownerId })
            return null;
        var attack = new CardAttackHandle(this, ownerId);
        return context is { Window: SkillProgramTriggerWindow.SlashBeforeResponse, CardUse: { } action } &&
            context.TargetSeat == attack.TargetSeat && LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId == action.CardActionId &&
            IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) && SameAttackOwner(ActiveCardAttack, attack) ? attack : null;
    }

    private SkillProgramStepOutcome BeginShiTz(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiTaiShiCi is not null) throw new InvalidOperationException("Shi Tai Shi Ci restarted a running operation.");
        var r = f.ActivationId == ShiTaiShiCiComposition.HanzhanActivation && f.TriggerId is null && f.SkillId == ShiTzHanzhan
            ? StartShiTzHanzhan(f)
            : f.ActivationId == ShiTaiShiCiComposition.ZhenfengActivation && f.TriggerId is null && f.SkillId == ShiTzZhenfeng
                ? StartShiTzZhenfeng(f)
                : f.SkillId == ShiTzZhanlie && f.TriggerId == ShiTaiShiCiComposition.ZhanlieRangeTrigger ? StartShiTzRange(f)
                : f.SkillId == ShiTzZhanlie && f.TriggerId == ShiTaiShiCiComposition.ZhanlieEndingTrigger ? StartShiTzZhanlie(f)
                : f.SkillId == ShiTzZhanlie && f.TriggerId == ShiTaiShiCiComposition.ZhanlieGateTrigger ? StartShiTzGate(f)
                : throw new InvalidOperationException("Shi Tai Shi Ci instruction has an incompatible owning skill.");
        ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r });
        if (r.Stage == 30)
        {
            ContinueShiTz(f.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        ContinueShiTz(f.Id);
        return r.Operation == "hanzhan" ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.AwaitChoice;
    }

    private ShiTaiShiCiReceipt StartShiTzHanzhan(ProgramSkillFrame f)
    {
        if (f.SelectedTargetSeats is not [var owner, var counterpart] || owner != f.OwnerSeat || owner == counterpart ||
            !IsValidPlayerSeat(counterpart))
            throw new InvalidOperationException("Hanzhan needs itself and one other living participant as ordered rally pair.");
        var r = new ShiTaiShiCiReceipt("hanzhan", _turnNumber, CounterpartSeat: counterpart)
        { Source = ShiTzSource(f), GameplayHash = f.GameplayHash };
        AdvanceEventRulesAndQueueFact(new ShiTzHanzhanStartedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber, counterpart));
        return r;
    }

    private ShiTaiShiCiReceipt StartShiTzZhenfeng(ProgramSkillFrame f) =>
        new("zhenfeng", _turnNumber) { Source = ShiTzSource(f), GameplayHash = f.GameplayHash };

    private ShiTaiShiCiReceipt StartShiTzRange(ProgramSkillFrame f) =>
        new("range", _turnNumber) { Source = ShiTzSource(f), GameplayHash = f.GameplayHash };

    private ShiTaiShiCiReceipt StartShiTzZhanlie(ProgramSkillFrame f)
    {
        if (ShiTzLieCount() <= 0) throw new InvalidOperationException("Zhanlie ending requires at least one accumulated lie marker.");
        return new("zhanlie", _turnNumber) { Source = ShiTzSource(f), GameplayHash = f.GameplayHash };
    }

    private ShiTaiShiCiReceipt StartShiTzGate(ProgramSkillFrame f)
    {
        var attack = ShiTzGateAttack(f.WindowContext ??
            throw new InvalidOperationException("Zhanlie gate lost its native slash window.")) ??
            throw new InvalidOperationException("Zhanlie gate lost its current battle slash.");
        var target = attack.TargetSeat;
        if (GetHand(_players[target]).Count == 0)
        {
            // No hand card can buy response qualification; the Slash loses its effect on this target.
            AdvanceEventRulesAndQueueFact(new ShiTzRespondGatePaidEvent(f.Id, attack.ResolutionId, target, false));
            MarkCardEffectIneffective(attack.ResolutionId, target);
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(attack.ResolutionId, attack.CardUserSeat, target,
                attack.EffectiveCardKind ?? CardKind.Slash, CardEffectSkipReason.SkillNullified));
            return new("gate", _turnNumber, Stage: 30, TargetSeat: target)
            { Source = ShiTzSource(f), GameplayHash = f.GameplayHash, AttackResolutionId = attack.ResolutionId };
        }
        return new("gate", _turnNumber, TargetSeat: target)
        { Source = ShiTzSource(f), GameplayHash = f.GameplayHash, AttackResolutionId = attack.ResolutionId };
    }

    // ---- choice publication -------------------------------------------------

    private IReadOnlyList<PromptChoice> ShiTzChoices(ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!; var choices = new List<PromptChoice>();
        void Add(string branch, string label, int? target = null, IReadOnlyList<int>? cards = null) => choices.Add(new(
            new($"shi-tai-shi-ci.{f.Id}.{r.Stage}.{branch}.{target ?? -1}"), label, cards ?? [],
            target is { } seat ? [seat] : [], new Dictionary<string, string>
            {
                ["program-action"] = ShiTaiShiCiComposition.ZhanlieProgramAction,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch,
            }));
        if (r.Operation == "zhenfeng" && r.Stage == 0)
        {
            var owner = _players[f.OwnerSeat];
            Add("recover", "回复2点体力");
            Add("fix-hp", $"将“酣战”和“战烈”的X固定为当前体力值（{owner.Hp}）");
            Add("fix-lost", $"将“酣战”和“战烈”的X固定为已损失体力值（{owner.MaxHp - owner.Hp}）");
            Add("fix-alive", $"将“酣战”和“战烈”的X固定为存活角色数（{_players.Count(p => p.IsAlive)}）");
        }
        else if (r.Operation == "zhanlie" && r.Stage == 3)
        {
            var selected = r.TargetSeats!;
            var maximum = (r.Options & ShiTaiShiCiComposition.OptionExtraTarget) != 0 ? 2 : 1;
            foreach (var seat in GetProgramTargetSeats(f.OwnerSeat, SkillProgramTargetKind.OtherLivingSlashable))
                Add(selected.Contains(seat) ? "unpick" : "pick",
                    (selected.Contains(seat) ? "取消选择 " : "选择 ") + _players[seat].Name, seat);
            if (selected.Count > 0)
                Add("slash", $"视为使用无次数限制的【杀】（已选 {selected.Count}/{maximum} 名目标）");
        }
        else if (r.Operation == "gate" && r.Stage == 0)
        {
            foreach (var choice in BuildOwnedCardPaymentChoices(f.Id, r.TargetSeat, r.TargetSeat,
                [CardZoneKind.Hand], OwnedCardMoveIntent.Discard))
                choices.Add(choice with { Parameters = new Dictionary<string, string>(choice.Parameters)
                    { ["program-action"] = ShiTaiShiCiComposition.ZhanlieProgramAction, ["branch"] = "gate-pay" } });
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private IReadOnlyList<PromptChoice> ShiTzOptionChoices(ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!;
        var y = ShiTzLieCount() / 3;
        var chosen = ShiTzCountBits(r.Options);
        var choices = new List<PromptChoice>();
        void Add(string option, string label) => choices.Add(new(
            new($"shi-tai-shi-ci.{f.Id}.0.{option}"), label, [], [], new Dictionary<string, string>
            {
                ["program-action"] = ShiTaiShiCiComposition.ZhanlieProgramAction,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["branch"] = "option", ["option"] = option,
            }));
        Add("extra-target", "此杀目标数+1");
        Add("extra-damage", "此杀基础伤害+1");
        Add("respond-cost", "此杀需目标角色额外弃置一张手牌才能响应");
        Add("reward-draw", "此杀结算后你摸两张牌");
        var result = choices.Where(choice => (r.Options & ShiTzOptionFlag(choice.Parameters["option"])) == 0 && chosen < y).ToList();
        if (chosen > 0 || y == 0)
            result.Add(new(new($"shi-tai-shi-ci.{f.Id}.0.done"),
                y == 0 ? "移去全部“烈”，视为使用一张无次数限制的【杀】" : $"确认（已选 {chosen}/{y} 项）", [], [],
                new Dictionary<string, string>
                { ["program-action"] = ShiTaiShiCiComposition.ZhanlieProgramAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "done" }));
        return Array.AsReadOnly(result.ToArray());
    }

    private static int ShiTzOptionFlag(string option) => option switch
    {
        "extra-target" => ShiTaiShiCiComposition.OptionExtraTarget,
        "extra-damage" => ShiTaiShiCiComposition.OptionExtraDamage,
        "respond-cost" => ShiTaiShiCiComposition.OptionRespondCost,
        _ => ShiTaiShiCiComposition.OptionRewardDraw,
    };

    private static int ShiTzCountBits(int value)
    {
        var count = 0;
        while (value != 0) { count += value & 1; value >>= 1; }
        return count;
    }

    private void PublishShiTz(ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!;
        var choices = r.Operation == "zhanlie" && r.Stage == 0 ? ShiTzOptionChoices(f) : ShiTzChoices(f);
        if (choices.Count == 0 || _winner != Winner.None ||
            r.Operation != "gate" && !_players[f.OwnerSeat].IsAlive ||
            r.Operation == "gate" && !_players[r.TargetSeat].IsAlive)
        { FinishShiTzOperation(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        var chooser = r.Operation == "gate" ? r.TargetSeat : f.OwnerSeat;
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            r.Operation switch
            {
                "zhenfeng" => "振锋：选择一项。",
                "zhanlie" => r.Stage == 0 ? $"战烈：至多选择 {ShiTzLieCount() / 3} 项（当前 {ShiTzLieCount()} 枚“烈”）。" : "战烈：选择此杀的目标。",
                _ => "战烈：弃置一张手牌以获得响应此杀的资格。",
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

    [ProgramAnswer(ShiTaiShiCiComposition.ZhanlieProgramAction)]
    private void AnswerShiTz(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiTaiShiCi: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Operation == "gate" ? r.TargetSeat : f.OwnerSeat) ||
            !(r.Operation == "zhanlie" && r.Stage == 0 ? ShiTzOptionChoices(f) : ShiTzChoices(f))
                .Any(choice => AssistedChoicesEqual([choice], [selected])))
            throw new InvalidOperationException("Shi Tai Shi Ci choice lost its current frame, chooser or legal branch.");
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = selected.Parameters["branch"];
        switch (r.Operation, branch)
        {
            case ("zhenfeng", "recover"):
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Stage = 9 } });
                ContinueShiTz(f.Id);
                return;
            case ("zhenfeng", _):
            {
                var kind = branch switch { "fix-hp" => "hp", "fix-lost" => "lost", _ => "alive" };
                var value = kind switch
                {
                    "hp" => _players[f.OwnerSeat].Hp,
                    "lost" => _players[f.OwnerSeat].MaxHp - _players[f.OwnerSeat].Hp,
                    _ => _players.Count(p => p.IsAlive),
                };
                AdvanceEventRulesAndQueueFact(new ShiTzZhenfengFixedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber, kind, value));
                FinishShiTzOperation(GetActiveProgramFrame(f.Id));
                return;
            }
            case ("zhanlie", "option"):
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Options = r.Options | ShiTzOptionFlag(selected.Parameters["option"]) } });
                PublishShiTz(GetActiveProgramFrame(f.Id));
                return;
            case ("zhanlie", "done") when r.Stage == 0:
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Stage = 3, TargetSeats = Array.Empty<int>() } });
                PublishShiTz(GetActiveProgramFrame(f.Id));
                return;
            case ("zhanlie", "pick") when r.Stage == 3:
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { TargetSeats = r.TargetSeats!.Append(selected.Targets.Single()).ToArray() } });
                PublishShiTz(GetActiveProgramFrame(f.Id));
                return;
            case ("zhanlie", "unpick") when r.Stage == 3:
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { TargetSeats = r.TargetSeats!.Where(s => s != selected.Targets.Single()).ToArray() } });
                PublishShiTz(GetActiveProgramFrame(f.Id));
                return;
            case ("zhanlie", "slash") when r.Stage == 3:
            {
                var targets = r.TargetSeats ?? throw new InvalidOperationException("Zhanlie slash lost its chosen targets.");
                AdvanceEventRulesAndQueueFact(new ShiTzSlashIssuedEvent(f.Id, r.Source!, f.GameplayHash, _turnNumber,
                    targets.ToArray(), r.Options));
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Stage = 6, SlashCursor = 0 } });
                ContinueShiTz(f.Id);
                return;
            }
            case ("gate", "gate-pay"):
                AdvanceEventRulesAndQueueFact(new ShiTzRespondGatePaidEvent(f.Id, r.AttackResolutionId, r.TargetSeat, true));
                ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Stage = 18, Before = _movementSequence },
                    PendingMovementContinuation = new(r.TargetSeat, 0, null) });
                MoveProgramLogicalDiscard(f, r.TargetSeat, [selected.Cards.Single()], new(ShiTaiShiCiComposition.HanzhanGateMoveReason));
                return;
            default:
                throw new InvalidOperationException("Unknown Shi Tai Shi Ci choice branch.");
        }
    }

    // ---- continuations ------------------------------------------------------

    private void ContinueShiTz(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiTaiShiCi: { } r } f || f.Id != id) return;
        if (r.Stage is 6 or 8 or 12 or 14 or 18)
        {
            if (DrainShiTz(f)) return;
            f = GetActiveProgramFrame(id);
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, ShiTaiShiCi = f.ShiTaiShiCi! with { Draw = null } });
            r = f.ShiTaiShiCi!;
        }
        switch (r.Operation, r.Stage)
        {
            case ("hanzhan", 0) or ("hanzhan", 6) or ("hanzhan", 8):
                ContinueShiTzHanzhanRally(GetActiveProgramFrame(id));
                return;
            case ("range", 0):
            {
                AdvanceEventRulesAndQueueFact(new ShiTzRangeRecordedEvent(_turnNumber, _currentSeat, ShiTzZhanlieRange(), ShiTzFixedX()));
                FinishShiTzOperation(GetActiveProgramFrame(id));
                return;
            }
            case ("zhenfeng", 0) or ("zhanlie", 0) or ("zhanlie", 3) or ("gate", 0):
                PublishShiTz(GetActiveProgramFrame(id));
                return;
            case ("zhanlie", 6):
                ContinueShiTzZhanlieSlashes(GetActiveProgramFrame(id));
                return;
            case ("zhanlie", 16):
                ContinueShiTzZhanlieSlashes(GetActiveProgramFrame(id) with
                { ShiTaiShiCi = f.ShiTaiShiCi! with { SlashCursor = f.ShiTaiShiCi.SlashCursor + 1, Stage = 6 } });
                return;
            case ("zhanlie", 12) or ("zhanlie", 14):
                ContinueShiTzZhanlieReward(GetActiveProgramFrame(id));
                return;
            case ("gate", 18) or ("gate", 30):
                FinishShiTzOperation(GetActiveProgramFrame(id));
                return;
            case ("zhenfeng", 9):
                ContinueShiTzZhenfengRecovery(GetActiveProgramFrame(id));
                return;
            default:
                throw new InvalidOperationException("Shi Tai Shi Ci has an unknown continuation stage.");
        }
    }

    private void ContinueShiTzHanzhanRally(ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!;
        var participants = new[] { f.OwnerSeat, r.CounterpartSeat };
        for (var cursor = r.DrawCursor; cursor < participants.Length * 8; cursor++)
        {
            var seat = participants[cursor % participants.Length];
            if (!_players[seat].IsAlive) continue;
            var missing = ShiTzHanzhanHandCap(_players[seat]) - GetHand(_players[seat]).Count;
            if (missing <= 0) continue;
            ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { DrawCursor = cursor, Stage = 6 } });
            if (BeginOperationProgramDraw(GetActiveProgramFrame(f.Id), seat, Math.Min(3, missing),
                    new(ShiTaiShiCiComposition.HanzhanRallyDrawReason)) == SkillProgramStepOutcome.AwaitChild) return;
            ContinueShiTz(f.Id);
            return;
        }
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.CounterpartSeat].IsAlive || f.OwnerSeat == r.CounterpartSeat)
        { FinishShiTzOperation(GetActiveProgramFrame(f.Id)); return; }
        ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Stage = 9, DrawCursor = participants.Length * 8 } });
        FinishShiTzOperation(GetActiveProgramFrame(f.Id));
    }

    private void ContinueShiTzZhanlieSlashes(ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!;
        var targets = r.TargetSeats!;
        while (r.SlashCursor < targets.Count && !_players[targets[r.SlashCursor]].IsAlive) r = r with { SlashCursor = r.SlashCursor + 1 };
        if (r.SlashCursor >= targets.Count)
        {
            if ((r.Options & ShiTaiShiCiComposition.OptionRewardDraw) != 0 && _players[f.OwnerSeat].IsAlive)
            { ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { Stage = 12 } }); ContinueShiTz(f.Id); return; }
            FinishShiTzOperation(GetActiveProgramFrame(f.Id)); return;
        }
        var targetSeat = targets[r.SlashCursor];
        ReplaceRuntimeTop(f = f with { SelectedTargetSeats = Array.AsReadOnly(new[] { targetSeat }),
            ShiTaiShiCi = r with { Stage = 16 } });
        if (BeginProgramVirtualCardUse(GetActiveProgramFrame(f.Id), targetSeat, CardKind.Slash, false) == SkillProgramStepOutcome.AwaitChild) return;
        ContinueShiTz(f.Id);
    }

    private void ContinueShiTzZhanlieReward(ProgramSkillFrame f)
    {
        // The settlement draw is an ordinary completed draw; it needs no producer return.
        DrawProgramCards(f.Id, f.OwnerSeat, 2, null, null, SkillProgramCardSetVisibility.Private,
            new(ShiTaiShiCiComposition.ZhanlieRewardDrawReason));
        FinishShiTzOperation(GetActiveProgramFrame(f.Id));
    }

    private void ContinueShiTzZhenfengRecovery(ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!;
        if (!_players[f.OwnerSeat].IsAlive || _players[f.OwnerSeat].Hp >= _players[f.OwnerSeat].MaxHp)
        { FinishShiTzOperation(GetActiveProgramFrame(f.Id)); return; }
        BeginRecovery(f.Id, f.OwnerSeat, f.OwnerSeat, 2);
    }

    private bool DrainShiTz(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) ||
        TryBeginAdvancedSkillsChanged(f.Id);

    private void FinishShiTzOperation(ProgramSkillFrame f)
    {
        if (f.ShiTaiShiCi!.Operation is "hanzhan")
        {
            // The rally is the first of two instructions; advancing runs the virtual Duel.
            ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { ShiTaiShiCi = null });
            AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (f.ShiTaiShiCi.Operation is "zhanlie" or "zhenfeng")
        { FinishProgramSkill(f, true); return; }
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { ShiTaiShiCi = null });
        AdvanceRuntimeProgram(f.Id);
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShiTzHanzhan)]
    private void ReturnShiTzHanzhanDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.ShiTaiShiCi is not { Operation: "hanzhan", Stage: 6, Draw: null } r ||
            (outcome.RecipientSeat != f.OwnerSeat && outcome.RecipientSeat != r.CounterpartSeat) || outcome.RequestedCount is not (> 0 and <= 3))
            throw new InvalidOperationException("Hanzhan lost its original once-issued rally draw.");
        AdvanceEventRulesAndQueueFact(new ShiTzHanzhanRallyDrawnEvent(f.Id, outcome.RecipientSeat, outcome.RequestedCount,
            outcome.ActualDrawIds.Count + outcome.ReplacementGainedIds.Count));
        ReplaceRuntimeTop(f = f with { ShiTaiShiCi = r with { DrawCursor = r.DrawCursor + 1, Draw = outcome,
            Before = outcome.SequenceBefore, Stage = 8 }, PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        ContinueShiTz(f.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 67600)]
    private bool ResumeShiTz(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiTaiShiCi: not null } f || f.Id != id) return false;
        ContinueShiTz(id);
        return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 67600)]
    private bool ReturnShiTzMovement(ProgramSkillFrame f) => f.ShiTaiShiCi is not null && ResumeShiTz(f.Id);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67600)]
    private bool ContinuePaidShiTz(ProgramSkillFrame f) => f.ShiTaiShiCi is { Stage: > 0 };

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 67600)]
    private int ShiTzZhanlieDamageBonus(IDamageAttempt attack)
    {
        if (attack is not CardAttackHandle card || card.ProgramSkillCardUseFrameId is not { } frameId ||
            _resolutionStack.SingleOrDefault(f => f.Id == frameId) is not ProgramSkillFrame { ShiTaiShiCi: { Operation: "zhanlie" } r })
            return 0;
        return (r.Options & ShiTaiShiCiComposition.OptionExtraDamage) != 0 ? 1 : 0;
    }

    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 67600)]
    private bool ShiTzMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.ShiTaiShiCi is { Stage: 8 or 14 or 18 } r && p.CoverageResultBind is null && p.BeforeCount == 0 &&
        p.SubjectSeat == (r.Stage == 18 ? r.TargetSeat : f.OwnerSeat);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67600)]
    private void AssertShiTz(ProgramSkillFrame f)
    {
        if (f.ShiTaiShiCi is not { } r) return;
        if (r.ActualTurn <= 0 || r.Source?.OwnerSeat != f.OwnerSeat ||
            r.Operation is not ("hanzhan" or "zhanlie" or "range" or "gate" or "zhenfeng"))
            throw new InvalidOperationException("Shi Tai Shi Ci lost its original typed operation receipt.");
        if (r.Operation == "zhanlie" && r.Stage >= 6 && (r.TargetSeats is not { Count: >= 1 } targets ||
            targets.Distinct().Count() != targets.Count || targets.Any(seat => !IsValidPlayerSeat(seat)) ||
            !ProgramEventHistory<ShiTzSlashIssuedEvent>().Any(e => e.FrameId == f.Id && e.Source == ShiTzSource(f) &&
                e.TargetSeats.SequenceEqual(targets) && e.Options == r.Options)))
            throw new InvalidOperationException("Zhanlie lost its issued slash options or ordered targets.");
        if (r.Operation == "gate" && r.Stage >= 18 && !ProgramEventHistory<ShiTzRespondGatePaidEvent>()
            .Any(e => e.FrameId == f.Id && e.AttackResolutionId == r.AttackResolutionId))
            throw new InvalidOperationException("Zhanlie gate lost its paid response qualification receipt.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShiTzZhanlie, SkillProgramEffectOp.ResolveShiTzZhenfeng,
        SkillProgramEffectOp.ResolveShiTzRespondGate)]
    private PromptChoice ChooseShiTzAi(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.ShiTaiShiCi!;
        if (r.Operation == "zhenfeng")
        {
            var owner = _players[f.OwnerSeat];
            return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "recover" && owner.Hp < owner.MaxHp) ??
                decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"].StartsWith("fix")) ?? decision.Choices[0];
        }
        if (r.Operation == "zhanlie")
        {
            if (r.Stage == 0)
            {
                var y = ShiTzLieCount() / 3;
                return decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "option" && y > 0 &&
                    (ShiTzOptionFlag(choice.Parameters["option"]) & (ShiTaiShiCiComposition.OptionExtraDamage |
                        ShiTaiShiCiComposition.OptionRewardDraw)) != 0) ??
                    decision.Choices.Single(choice => choice.Parameters["branch"] == "done");
            }
            var maximum = (r.Options & ShiTaiShiCiComposition.OptionExtraTarget) != 0 ? 2 : 1;
            var pick = decision.Choices.FirstOrDefault(choice => choice.Parameters["branch"] == "pick" &&
                !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[choice.Targets.Single()]));
            return (r.TargetSeats?.Count ?? 0) < maximum && pick is not null ? pick :
                decision.Choices.Single(choice => choice.Parameters["branch"] == "slash");
        }
        return decision.Choices
            .OrderBy(choice => GetKeepValue(GetHand(_players[r.TargetSeat]).Single(card => card.Id == choice.Cards[0]), _players[r.TargetSeat]))
            .ThenBy(choice => choice.Cards[0]).First();
    }
}
