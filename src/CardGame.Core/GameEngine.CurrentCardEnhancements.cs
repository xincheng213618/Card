namespace CardGame.Core;

public sealed partial class GameEngine
{

    // Capture before any per-target damage modifiers, reductions or armor apply.
    private void CaptureProgramAdjustedSlashBaseDamage(CardAttackHandle attack)
    {
        if (IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash))
            UpdateLifecycleCardUse(attack.ResolutionId, frame => frame with
            { ProgramAdjustedSlashBaseDamage = frame.ProgramAdjustedSlashBaseDamage ?? attack.DamageAmount });
    }

    private bool TryContinueEnhancedSlashTargets(CardAttackHandle attack)
    {
        if (!HasRemainingEnhancedSlashTargets(attack.ResolutionId) || IsForeignPublicPileSlashUse(attack.ResolutionId) ||
            ActiveFangtianHalberd?.ResolutionId == attack.ResolutionId) return false;
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == attack.ResolutionId);
        if (!ReferenceEquals(use, _resolutionStack.LastOrDefault()))
            throw new InvalidOperationException("An adjusted Slash must finish its child before advancing its target cursor.");
        if (use.ProgramAdjustedSlashBaseDamage is not { } baseDamage)
            throw new InvalidOperationException("An adjusted Slash lost its initial damage amount.");
        var next = use.TargetIndex + 1;
        while (next < use.TargetSeats.Count && !_players[use.TargetSeats[next]].IsAlive) next++;
        var targetSeat = use.TargetSeats[next];
        SetCardUseTargetIndex(use.Id, next); SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var continued = new CardAttackHandle(this, use.Id, use.SourceSeat, targetSeat, attack.Card, baseDamage, use.CardKind,
            ignoresArmor: use.IgnoresArmor || HasDirectedCardArmorBypass(use.Id, targetSeat) || HasCardArmorBypass(_players[use.SourceSeat], _players[targetSeat], use.CardKind),
            physicalCards: attack.PhysicalCards, conversionSource: attack.ConversionSource,
            programSkillCardUseFrameId: attack.ProgramSkillCardUseFrameId);
        if ((HasIssuedOriginalTargetAdditionTail(use) || HasSameTypeAidTargetTail(use)) && use.AdjustedSlashReturn is { } adjustedReturn && adjustedReturn.CardUseFrameId == use.Id)
            UpdateCardAttackState(use.Id, state => state! with { AdjustedSlashReturn = adjustedReturn, PhysicalCardIds = use.PhysicalCardIds! });
        continued.SetCardUseCausedDamage(attack.CardUseCausedDamage);
        ActiveCardAttack = continued; ActiveDuel = null; ClearPendingDecision();
        if (ActiveFactionCardRequest is { ActiveAttack: { } factionAttack } faction && SameAttackOwner(factionAttack, attack)) faction.ActiveAttack = continued;
        if (ActiveBorrowedSword is { ActiveAttack: { } borrowedAttack } borrowed && SameAttackOwner(borrowedAttack, attack)) borrowed.ActiveAttack = continued;
        BeginSlashTargetResolution(continued);
        return true;
    }
    private CardUseFrame EnhancementCardUse(ProgramSkillFrame frame)
    {
        if (frame.WindowContext?.CardUse is not { } context || context.ActorSeat != frame.OwnerSeat)
            throw new InvalidOperationException("An enhancement requires its owner's frozen card action.");
        return _resolutionStack.OfType<CardUseFrame>().Single(use => use.Id == context.ParentCardUseFrameId && use.Action?.ActionId == context.CardActionId);
    }

    private SkillProgramStepOutcome ApplyProgramCurrentCardEnhancements(ProgramSkillFrame frame, int maximum)
    {
        var use = EnhancementCardUse(frame);
        // The host's ordinary Draw Two path stores its implicit self target as
        // an empty input list. Normalize that actual card target before adding
        // a second target; recasting and targetless cards never enter this path.
        if (use.CardKind == CardKind.DrawTwo && use.Action!.EffectiveDesignatedTargetSeats.Count == 0)
        {
            var action = use.Action; var targets = new[] { use.SourceSeat };
            use = use with { TargetSeats = targets, Action = CaptureFactionAction(new CardActionContext(action.ActionId, action.ParentActionId, action.Type,
                action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind,
                targets, action.PhysicalCards, action.ConversionChain, targets, action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin)) };
            ReplaceRuntimeFrame(_resolutionStack[_resolutionStack.FindIndex(item => item.Id == use.Id)].Id, use);
        }
        if (_currentSeat != frame.OwnerSeat || use.Action!.EffectiveDesignatedTargetSeats.Count != 1 ||
            !IsSlashCard(use.CardKind) && CardUseCategoryCatalog.Get(use.CardKind) != CardUseCategories.InstantTrick ||
            _players.Any(other => other.IsAlive && other.Seat != frame.OwnerSeat && GetCombatDistance(frame.OwnerSeat, other.Seat) != 1))
            return SkillProgramStepOutcome.Continue;
        if (frame.CardEnhancementDraft is not null) throw new InvalidOperationException("A current-card enhancement was offered twice.");
        ReplaceRuntimeTop(frame = frame with { CardEnhancementDraft = new(use.Id, []) });
        PublishCardEnhancementPrompt(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private int[] CurrentCardExtraTargets(ProgramSkillFrame frame)
    {
        var use = EnhancementCardUse(frame); var actor = _players[frame.OwnerSeat];
        var card = use.Action!.PhysicalCards.Select(cost => _cardZones.CardsAt(_cardZones.GetLocation(cost.CardId)).Single(item => item.Id == cost.CardId)).FirstOrDefault();
        return _players.Where(target => target.IsAlive && !use.Action!.EffectiveDesignatedTargetSeats.Contains(target.Seat) &&
            !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, use.CardKind) &&
            !IsCardTargetProhibited(target, use.CardKind, card?.Suit ?? Suit.Spade, ActualTargetPolicyColor(use.Action!)) &&
            (IsSlashCard(use.CardKind) ? target.Seat != actor.Seat && (card is null ? !IsSlashProhibited(target) : CanUseSlashTarget(actor, target, card, effectiveKind: use.CardKind, ignoreDistance: true)) :
             use.CardKind switch
             {
                 CardKind.DrawTwo or CardKind.IronChain => true,
                 CardKind.BorrowedSword => GetEquipment(target).Any(item => EquipmentCatalog.Get(item.Kind).Slot == EquipmentSlot.Weapon) &&
                     _players.Any(victim => IsLegalBorrowedSwordSlashTarget(target, victim)),
                 CardKind.Duel => target.Seat != actor.Seat,
                 CardKind.FireAttack => GetHand(target).Count > 0,
                 CardKind.Dismantlement or CardKind.Snatch => target.Seat != actor.Seat && GetHand(target).Count + GetEquipment(target).Count + GetJudgment(target).Count > 0,
                 _ => false
             })).Select(target => target.Seat).ToArray();
    }

    private IReadOnlyList<PromptChoice> CardEnhancementChoices(ProgramSkillFrame frame)
    {
        var draft = frame.CardEnhancementDraft ?? throw new InvalidOperationException("Enhancement draft disappeared.");
        Dictionary<string, string> Parameters(string option) => new() { ["program-action"] = "current-card-enhancement", ["enhancement-option"] = option, ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (draft.ChoosingExtraTarget && EnhancementCardUse(frame).CardKind == CardKind.BorrowedSword)
            return CurrentCardExtraTargets(frame).SelectMany(holder => _players.Where(victim => IsLegalBorrowedSwordSlashTarget(_players[holder], victim))
                .Select(victim => new PromptChoice(new ChoiceId($"enhancement.{frame.Id}.pair.{holder}.{victim.Seat}"),
                    $"额外令 {_players[holder].Name} 对 {victim.Name} 使用杀", [], [holder, victim.Seat], Parameters("target")))).ToArray();
        if (draft.ChoosingExtraTarget)
            return CurrentCardExtraTargets(frame).Select(target => new PromptChoice(new ChoiceId($"enhancement.{frame.Id}.target.{target}"),
                $"额外指定 {_players[target].Name}", [], [target], Parameters("target"))).ToArray();
        var result = new List<PromptChoice>();
        foreach (var option in new[] { CurrentCardEnhancement.ExtraTarget, CurrentCardEnhancement.IgnoreArmor, CurrentCardEnhancement.Uncancelable, CurrentCardEnhancement.DrawAfterDamage }.Except(draft.Selected))
        {
            if (option == CurrentCardEnhancement.ExtraTarget && CurrentCardExtraTargets(frame).Length == 0) continue;
            var label = option switch { CurrentCardEnhancement.ExtraTarget => "目标数+1", CurrentCardEnhancement.IgnoreArmor => "无视防具", CurrentCardEnhancement.Uncancelable => "不能被抵消", _ => "造成伤害后摸一张牌" };
            result.Add(new(new ChoiceId($"enhancement.{frame.Id}.{option}"), label, [], [], Parameters(option.ToString())));
        }
        result.Add(new(new ChoiceId($"enhancement.{frame.Id}.finish"), "不再选择", [], [], Parameters("finish")));
        return result;
    }

    private void PublishCardEnhancementPrompt(ProgramSkillFrame frame)
    {
        var choices = CardEnhancementChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, frame.CardEnhancementDraft!.ChoosingExtraTarget ? "选择本牌的一个额外目标。" : "依次选择至多两项。",
            [], choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = false, TargetSeat = frame.OwnerSeat, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveCardEnhancementChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Enhancement frame disappeared.");
        selected = CardEnhancementChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id) ?? throw new InvalidOperationException("Enhancement option became illegal.");
        var draft = frame.CardEnhancementDraft!;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ApplyCurrentCardEnhancements || _pendingDecision?.PlayerSeat != frame.OwnerSeat)
            throw new InvalidOperationException("Enhancement lost its frozen instruction or chooser.");
        var option = selected.Parameters["enhancement-option"];
        ClearPendingDecision();
        if (option == "finish") { FinishProgramCardEnhancements(frame); return; }
        if (option == nameof(CurrentCardEnhancement.ExtraTarget))
        { ReplaceRuntimeTop(frame = frame with { CardEnhancementDraft = draft with { ChoosingExtraTarget = true } }); PublishCardEnhancementPrompt(frame); return; }
        var enhancement = option == "target" ? CurrentCardEnhancement.ExtraTarget : Enum.Parse<CurrentCardEnhancement>(option);
        var use = EnhancementCardUse(frame); var targets = option == "target" ? use.TargetSeats.Concat(selected.Targets).ToArray() : use.TargetSeats;
        var action = use.Action!;
        var updatedAction = CloneRoleAction(action, action.ActorSeat, targets);
        var index = _resolutionStack.FindIndex(item => item.Id == use.Id);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, use with { TargetSeats = targets, Action = updatedAction, Enhancements = use.Enhancements | enhancement,
            TargetsAdjusted = use.TargetsAdjusted || option == "target" && use.CardKind == CardKind.BorrowedSword,
            EnhancementOwnerSeat = frame.OwnerSeat,
            IgnoresArmor = use.IgnoresArmor || enhancement == CurrentCardEnhancement.IgnoreArmor });
        AdvanceEventRulesAndQueueFact(new CurrentCardEnhancedEvent(use.Id, action.ActionId, frame.OwnerSeat, enhancement, option == "target" ? selected.Targets[0] : null));
        ReplaceRuntimeTop(frame = frame with { CardEnhancementDraft = draft with { Selected = draft.Selected.Append(enhancement).ToArray(), ChoosingExtraTarget = false } });
        if (frame.CardEnhancementDraft.Selected.Count >= effect.Amount) { FinishProgramCardEnhancements(frame); return; }
        PublishCardEnhancementPrompt(frame);
    }

    private void FinishProgramCardEnhancements(ProgramSkillFrame frame)
    { ReplaceRuntimeTop(frame with { CardEnhancementDraft = null }); AdvanceRuntimeProgram(frame.Id); }

    private bool HasCurrentCardEnhancement(long frameId, CurrentCardEnhancement flag) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId) is { } use && (use.Enhancements & flag) != 0;

    private bool HasRemainingEnhancedSlashTargets(long frameId) => _winner == Winner.None &&
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId) is { } use &&
        IsSlashCard(use.CardKind) && use.TargetSeats.Count > 1 &&
        use.TargetSeats.Skip(use.TargetIndex + 1).Any(seat => _players[seat].IsAlive);

    private void AssertCurrentCardEnhancementDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.CardEnhancementDraft is not { } draft) return;
        var use = EnhancementCardUse(frame);
        var flags = draft.Selected.Aggregate(CurrentCardEnhancement.None, (value, item) => value | item);
        if (paused.Op != SkillProgramEffectOp.ApplyCurrentCardEnhancements || use.Id != draft.CardUseFrameId ||
            draft.Selected.Count >= paused.Amount || draft.Selected.Distinct().Count() != draft.Selected.Count ||
            draft.Selected.Any(flag => flag is not (CurrentCardEnhancement.ExtraTarget or CurrentCardEnhancement.IgnoreArmor or CurrentCardEnhancement.Uncancelable or CurrentCardEnhancement.DrawAfterDamage)) ||
            (use.Enhancements & flags) != flags || flags != CurrentCardEnhancement.None && use.EnhancementOwnerSeat != frame.OwnerSeat ||
            draft.ChoosingExtraTarget && draft.Selected.Contains(CurrentCardEnhancement.ExtraTarget))
            throw new InvalidOperationException("A current-card enhancement lost its paid action, unique option set or selection bounds.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
             !AssistedChoicesEqual(prompt.Choices, CardEnhancementChoices(frame))))
            throw new InvalidOperationException("A current-card enhancement prompt changed its frozen choices or targets.");
    }

    private sealed partial class ProgramSkillHost : ICurrentCardEnhancementProgramHost
    {
        public SkillProgramStepOutcome ApplyCurrentCardEnhancements(ProgramSkillFrame frame, int maximum) => engine.ApplyProgramCurrentCardEnhancements(frame, maximum);
    }
}
