namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 谮构: when a character within the owner's attack range uses a Dodge that
    // fully resolved against a single-target attack, the owner may discard one
    // non-basic card or lose 1 HP to nullify that Dodge and gain its entity.
    private SkillProgramStepOutcome ZengouProgramNullifyDodge(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            frame.WindowContext is not { CardUse: { } cardUse, TargetSeat: { } dodgerSeat } ||
            dodgerSeat == active.OwnerSeat || !_players[dodgerSeat].IsAlive ||
            !IsWithinAttackRange(active.OwnerSeat, dodgerSeat))
            return SkillProgramStepOutcome.Continue;
        var attack = _resolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(item => item.Id == cardUse.ParentCardUseFrameId);
        if (attack is null || attack.TargetSeats.Count != 1 ||
            (attack.Enhancements & CurrentCardEnhancement.Uncancelable) != 0)
            return SkillProgramStepOutcome.Continue;
        var dodge = LatestSettledDodgeEntity();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = new List<PromptChoice>();
        foreach (var card in NonBasicOwnedCards(active.OwnerSeat))
        {
            if (dodge is null) break;
            choices.Add(new PromptChoice(
                new ChoiceId($"zengou-cost.frame-{frame.Id}.card-{card.Id}"),
                $"弃置【{card.DisplayName}】。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zengou-cost",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["cost"] = "discard",
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        if (owner.Hp > 0)
            choices.Add(new PromptChoice(
                new ChoiceId($"zengou-cost.frame-{frame.Id}.lose-hp"),
                "失去1点体力。",
                [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zengou-cost",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["cost"] = "loseHp"
                }));
        if (choices.Count == 0) return SkillProgramStepOutcome.Continue;
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择支付方式，令此【闪】无效并获得之。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = dodgerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 令【闪】无效",
                $"{_players[dodgerSeat].Name} 使用的【闪】结算完成，你可以支付代价令其无效并获得之。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("zengou-cost")]
    private void ResolveZengouCostChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Zengou cost choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.ZengouNullifyDodge } ||
            selected.Parameters.GetValueOrDefault("program-action") != "zengou-cost" ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Zengou cost choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Zengou cost chooser changed while suspended.");
        ClearPendingDecision();
        if (active.WindowContext is not { CardUse: { } cardUse, TargetSeat: { } dodgerSeat } ||
            _winner != Winner.None || !_players[active.OwnerSeat].IsAlive ||
            !_players[dodgerSeat].IsAlive || !IsWithinAttackRange(active.OwnerSeat, dodgerSeat))
        {
            CancelProgramBindingAndCleanup(active, "谮构的目标已失效，剩余结算取消。");
            return;
        }
        var attack = _resolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(item => item.Id == cardUse.ParentCardUseFrameId);
        var dodge = LatestSettledDodgeEntity();
        if (attack is null || attack.TargetSeats.Count != 1 ||
            (attack.Enhancements & CurrentCardEnhancement.Uncancelable) != 0 || dodge is null)
        {
            CancelProgramBindingAndCleanup(active, "谮构的【闪】结算已变化，剩余结算取消。");
            return;
        }
        var owner = _players[active.OwnerSeat];
        var cost = selected.Parameters.GetValueOrDefault("cost");
        int? paidCardId = null;
        if (cost == "discard")
        {
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedCardId) ||
                NonBasicOwnedCards(active.OwnerSeat).All(card => card.Id != parsedCardId))
                throw new InvalidOperationException("The Zengou cost card is no longer available.");
            paidCardId = parsedCardId;
            var paidCard = NonBasicOwnedCards(active.OwnerSeat).Single(card => card.Id == parsedCardId);
            MoveLogicalDiscardCards(active.OwnerSeat, active.OwnerSeat, [paidCard], _cardZones.GetLocation(paidCard.Id),
                new($"skill-program.{active.SkillId}.zengou-cost"));
        }
        else if (cost == "loseHp")
        {
            var before = owner.Hp;
            owner.Hp = Math.Max(0, before - 1);
            RecordHpChange(active.Id, null, active.OwnerSeat, before, owner.Hp, HpChangeKind.Loss);
            AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(active.Id, active.SkillId,
                active.OwnerSeat, before - owner.Hp, owner.Hp));
        }
        else
        {
            throw new InvalidOperationException($"The Zengou cost '{cost}' is unsupported.");
        }
        ReplaceRuntimeFrame(attack.Id, attack with
        {
            Enhancements = attack.Enhancements | CurrentCardEnhancement.Uncancelable,
            EnhancementOwnerSeat = active.OwnerSeat
        });
        MoveCard(dodge, CardLocation.DiscardPile, CardLocation.Hand(active.OwnerSeat),
            new($"skill-program.{active.SkillId}.zengou-gain"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】：" +
            (cost == "discard" ? $"弃置一张非基本牌" : "失去1点体力") +
            $"，令 {_players[dodgerSeat].Name} 使用的【闪】无效，并获得之。",
            active.OwnerSeat, dodgerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramZengouNullifiedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, dodgerSeat, cost, paidCardId, dodge.Id));
        if (owner.Hp == 0)
        {
            BeginProgramSkillDying(active.Id, owner);
            return;
        }
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }
}
