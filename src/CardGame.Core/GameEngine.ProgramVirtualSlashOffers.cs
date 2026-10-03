namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AssertProgramVirtualSlashOffer(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (effect.Op != SkillProgramEffectOp.OfferVirtualSlashOrDraw || !ReferenceEquals(frame, _resolutionStack.LastOrDefault())) return;
        if (frame.SelectedTargetSeats.Count != 1 || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision || decision.Choices.Count == 0)
            throw new InvalidOperationException("An assisted virtual card use lost its frozen selection or prompt.");
        var actorSeat = frame.SelectedTargetSeats[0];
        var targets = GetProgramVirtualSlashOfferTargets(actorSeat);
        var choosingTargets = decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-", StringComparison.Ordinal) == true);
        if (decision.PlayerSeat != (choosingTargets ? frame.OwnerSeat : actorSeat) || decision.Choices.Any(choice =>
            choice.Cards.Count != 0 || choice.Parameters.GetValueOrDefault("program-action") != "virtual-slash-offer" ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            (choosingTargets ? choice.Targets.Count != 1 || !targets.Contains(choice.Targets[0]) || choice.Parameters.GetValueOrDefault("offer-option") != $"target-{choice.Targets[0]}"
                : choice.Targets.Count != 0 || choice.Parameters.GetValueOrDefault("offer-option") is not ("slash" or "draw"))))
            throw new InvalidOperationException("An assisted virtual card prompt has an invalid chooser, option or target.");
    }

    private int[] GetProgramVirtualSlashOfferTargets(int actorSeat) => _players.Where(target =>
        _players[actorSeat].IsAlive && target.IsAlive && target.Seat != actorSeat &&
        IsWithinAttackRange(actorSeat, target.Seat) &&
        !IsCardUseForbidden(actorSeat, CardKind.Slash, CardActionType.Use) &&
        !IsDirectedCardTargetProhibited(actorSeat, target.Seat, CardKind.Slash) &&
        !IsSlashProhibited(target)).Select(target => target.Seat).ToArray();

    private SkillProgramStepOutcome OfferProgramVirtualSlashOrDraw(ProgramSkillFrame frame, int actorSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats.Count != 1 || active.SelectedTargetSeats[0] != actorSeat)
            throw new InvalidOperationException("A virtual Slash offer lost its selected actor.");
        if (!_players[actorSeat].IsAlive) return SkillProgramStepOutcome.Continue;
        if (GetProgramVirtualSlashOfferTargets(actorSeat).Length == 0)
        {
            DrawCards(_players[actorSeat], 1, log: true);
            return SkillProgramStepOutcome.Continue;
        }
        SetProgramVirtualSlashOfferPrompt(active, actorSeat, selectingTarget: false);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void SetProgramVirtualSlashOfferPrompt(ProgramSkillFrame frame, int actorSeat, bool selectingTarget)
    {
        PromptChoice Choice(string option, string label, IReadOnlyList<int> targets) => new(
            new ChoiceId($"program-virtual-slash.frame-{frame.Id}.{option}"), label, [], targets,
            new Dictionary<string, string> { ["program-action"] = "virtual-slash-offer", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["offer-option"] = option });
        var choices = selectingTarget
            ? GetProgramVirtualSlashOfferTargets(actorSeat).Select(seat => Choice($"target-{seat}", $"令 {_players[actorSeat].Name} 对 {_players[seat].Name} 使用【杀】", [seat])).ToArray()
            : new[] { Choice("slash", "视为使用一张【杀】", []), Choice("draw", "摸一张牌", []) };
        var chooserSeat = selectingTarget ? frame.OwnerSeat : actorSeat;
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, chooserSeat, selectingTarget ? "请选择【杀】的目标。" : "请选择使用【杀】或摸一张牌。", [], selectingTarget ? GetProgramVirtualSlashOfferTargets(actorSeat) : [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooserSeat, Choices = Array.AsReadOnly(choices), SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramVirtualSlashOfferChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A virtual Slash offer lost its frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var actorSeat = frame.SelectedTargetSeats.Single();
        var option = selected.Parameters.GetValueOrDefault("offer-option");
        var selectingTarget = option?.StartsWith("target-", StringComparison.Ordinal) == true;
        if (effect.Op != SkillProgramEffectOp.OfferVirtualSlashOrDraw || selected.Cards.Count != 0 || selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            _pendingDecision?.PlayerSeat != (selectingTarget ? frame.OwnerSeat : actorSeat))
            throw new InvalidOperationException("The virtual Slash answer changed its frozen actor or instruction.");
        ClearPendingDecision();
        if (!_players[actorSeat].IsAlive || !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "参与者或技能实例已失效。"); return; }
        var targets = GetProgramVirtualSlashOfferTargets(actorSeat);
        if (option == "draw" || targets.Length == 0)
        { DrawCards(_players[actorSeat], 1, log: true); AdvanceRuntimeProgram(frame.Id); return; }
        if (option == "slash" && selected.Targets.Count == 0)
        { SetProgramVirtualSlashOfferPrompt(frame, actorSeat, selectingTarget: true); return; }
        if (!selectingTarget || selected.Targets.Count != 1 || !targets.Contains(selected.Targets[0]) || option != $"target-{selected.Targets[0]}")
            throw new InvalidOperationException("The virtual Slash target is no longer legal.");
        BeginOfferedProgramVirtualSlash(frame, actorSeat, selected.Targets[0]);
    }

    private void BeginOfferedProgramVirtualSlash(ProgramSkillFrame frame, int actorSeat, int targetSeat)
    {
        if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("A virtual Slash cannot overwrite an attack.");
        var source = _players[actorSeat];
        var target = _players[targetSeat];
        var resolutionId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, actorSeat, actorSeat, null, null, null, CardKind.Slash, [targetSeat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(resolutionId, actorSeat, 0, CardKind.Slash, [targetSeat], PhysicalCardIds: []) { Action = action });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(resolutionId, 0, CardKind.Slash, actorSeat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(resolutionId, [targetSeat]));
        var attack = new CardAttackHandle(this, resolutionId, actorSeat, targetSeat, card: null,
            damageAmount: source.HasAlcoholEffect ? 2 : 1, playedCardKind: CardKind.Slash,
            ignoresArmor: HasCardArmorBypass(source, target, CardKind.Slash), programSkillCardUseFrameId: frame.Id);
        CaptureProgramAlcoholConsumption(resolutionId, source);
        source.HasAlcoholEffect = false;
        ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actorSeat, targetSeat));
        TryMarkProgramUseCommitted(resolutionId);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }

    private void GrantProgramTurnCardEffectImmunity(ProgramSkillFrame frame, int targetSeat, IReadOnlyList<CardKind> cardKinds)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (targetSeat != frame.OwnerSeat || cardKinds.Count == 0 || cardKinds.Any(kind => !GrantTurnCardEffectImmunityProgramOperationDescriptor.CardEffectImmunityKinds.Contains(kind)))
            throw new InvalidOperationException("The turn immunity grant has unsupported kinds or subject.");
        var granted = _turnCardUseEffects.GrantRuleModifier(_turnNumber, _currentSeat, frame.Id, frame.InstructionIndex - 1, CreateProgramTurnEffectSource(frame), SkillRuleQuery.CardEffectImmunity, SkillRuleOperation.Set, 1, cardKinds);
        AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(granted));
    }

    private sealed partial class ProgramSkillHost
    {
        public SkillProgramStepOutcome OfferVirtualSlashOrDraw(SkillProgramEffect effect, ProgramSkillFrame frame, int actorSeat) => engine.OfferProgramVirtualSlashOrDraw(frame, actorSeat);
        public void GrantTurnCardEffectImmunity(ProgramSkillFrame frame, int targetSeat, IReadOnlyList<CardKind> cardKinds) => engine.GrantProgramTurnCardEffectImmunity(frame, targetSeat, cardKinds);
    }
}
