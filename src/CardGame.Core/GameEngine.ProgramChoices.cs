namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOption(ProgramSkillFrame frame, int chooserSeat,
        string resultBind, IReadOnlyList<SkillProgramChoiceOption> options)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A named program choice cannot be answered twice.");
        var chooser = _players[chooserSeat];
        var context = CreateSkillContext(chooser);
        var choices = options.Where(option => option.Condition.EvaluateOption(context,
            () => GetClaimableProgramDamageCards(active).Length > 0,
            bind => IsProgramAttackRangeCoverageDecreased(active, bind),
            hasOwnedCardCategory: (zones, categories) => HasOwnedProgramCardCategory(chooserSeat, zones, categories),
            boundCardCount: bind => CountChooserProgramBoundCards(active, bind, chooserSeat),
            activationCardCount: active.SelectedCardIds.Count,
            boundCardSuitMatchesChoice: (cardBind, choiceBind) =>
                DoesProgramFrozenSuitMatchChoice(active, cardBind, choiceBind))).Select(option =>
            new PromptChoice(new ChoiceId($"program-option.frame-{frame.Id}.{resultBind}.{option.Id}"),
                option.Label, [], [], new Dictionary<string, string>
                {
                    ["program-action"] = "choose-option",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = resultBind,
                    ["option-id"] = option.Id
                })).ToArray();
        if (!chooser.IsAlive || choices.Length == 0)
        {
            CancelProgramBindingAndCleanup(active, "没有可用的效果选项，技能剩余步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, chooserSeat,
            $"【{skill.Name}】请选择一项。", [], [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = chooserSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 选择效果", skill.Description),
            Choices = Array.AsReadOnly(choices)
        };
        _status = chooser.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramOptionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The named choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var chooserSeat = effect.ChooserRef is { } chooser
            ? ResolveProgramParticipant(frame, chooser)
            : ResolveProgramEffectTarget(frame, effect.Target);
        if (effect.Op != SkillProgramEffectOp.ChooseOption ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat || selected.Cards.Count != 0 || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
            frame.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind))
            throw new InvalidOperationException("The choice does not match its suspended instruction.");
        var option = effect.Options.SingleOrDefault(item => item.Id == selected.Parameters.GetValueOrDefault("option-id")) ??
            throw new InvalidOperationException("The selected program option is unavailable.");
        var stillAvailable = option.Condition.EvaluateOption(CreateSkillContext(_players[chooserSeat]),
            () => GetClaimableProgramDamageCards(frame).Length > 0,
            bind => IsProgramAttackRangeCoverageDecreased(frame, bind),
            hasOwnedCardCategory: (zones, categories) => HasOwnedProgramCardCategory(chooserSeat, zones, categories),
            boundCardCount: bind => CountChooserProgramBoundCards(frame, bind, chooserSeat),
            activationCardCount: frame.SelectedCardIds.Count,
            boundCardSuitMatchesChoice: (cardBind, choiceBind) =>
                DoesProgramFrozenSuitMatchChoice(frame, cardBind, choiceBind));
        if (!stillAvailable && (option.Condition.ContainsHasClaimableDamageCards() ||
            option.Condition.ContainsBoundCardCountAtLeast() || option.Condition.ContainsHasOwnedCardCategory()))
            throw new InvalidOperationException("The selected option's required cards are no longer available.");
        ClearPendingDecision();
        if (!_players[chooserSeat].IsAlive || !stillAvailable ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(frame, "效果选项或技能实例已失效，技能剩余步骤取消。");
            return;
        }
        _resolutionStack[^1] = frame with
        {
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(effect.ResultBind!, option.Id, chooserSeat)).ToArray())
        };
        QueueGameEvent(new ProgramOptionChosenEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame),
            frame.OwnerSeat, effect.ResultBind!, option.Id, chooserSeat, option.Label));
        ContinueProgramSkill(frame.Id);
    }

    private bool IsClaimableProgramOptionStillAvailable(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame)
            return true;
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseOption)
            return true;
        var option = effect.Options.SingleOrDefault(item => item.Id == selected.Parameters.GetValueOrDefault("option-id"));
        if (option is null) return true;
        if (!option.Condition.ContainsHasClaimableDamageCards() &&
            !option.Condition.ContainsBoundCardCountAtLeast() && !option.Condition.ContainsHasOwnedCardCategory()) return true;
        var chooserSeat = effect.ChooserRef is { } chooser
            ? ResolveProgramParticipant(frame, chooser)
            : ResolveProgramEffectTarget(frame, effect.Target);
        return option.Condition.EvaluateOption(CreateSkillContext(_players[chooserSeat]),
            () => GetClaimableProgramDamageCards(frame).Length > 0,
            bind => IsProgramAttackRangeCoverageDecreased(frame, bind),
            hasOwnedCardCategory: (zones, categories) => HasOwnedProgramCardCategory(chooserSeat, zones, categories),
            boundCardCount: bind => CountChooserProgramBoundCards(frame, bind, chooserSeat),
            activationCardCount: frame.SelectedCardIds.Count,
            boundCardSuitMatchesChoice: (cardBind, choiceBind) =>
                DoesProgramFrozenSuitMatchChoice(frame, cardBind, choiceBind));
    }

    private int CountChooserProgramBoundCards(ProgramSkillFrame frame, string bind, int chooserSeat)
    {
        var cards = GetProgramCardSet(frame, bind);
        if (cards.CardIds.Count != cards.SourceLocations.Count ||
            cards.SourceLocations.Any(location => location.OwnerSeat != chooserSeat ||
                location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("A chooser cannot inspect another player's private card set.");
        return cards.CardIds.Select((id, index) => (id, index))
            .Count(item => _cardZones.GetLocation(item.id) == cards.SourceLocations[item.index]);
    }

    private static int ResolveProgramEffectTarget(ProgramSkillFrame frame, SkillProgramEffectTarget target) => target switch
    {
        SkillProgramEffectTarget.Owner => frame.OwnerSeat,
        SkillProgramEffectTarget.SelectedTarget => frame.SelectedTargetSeats.Single(),
        SkillProgramEffectTarget.Actor => frame.WindowContext?.CardUse?.ActorSeat ??
            throw new InvalidOperationException("A program option lost its card-action actor."),
        _ => throw new InvalidOperationException("Unsupported program option chooser.")
    };

    private PromptChoice SelectAiProgramOption(PendingDecision decision, ProgramSkillFrame frame)
    {
        var program = _contentRegistry!.GetSkill(frame.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        var effect = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
        var owner = CreateSkillContext(_players[frame.OwnerSeat]);
        var chooser = CreateSkillContext(_players[decision.PlayerSeat]);
        var context = CreateProgramAiPublicContext(_players[frame.OwnerSeat]) with
        {
            SelectedTarget = frame.SelectedTargetSeats.Count == 1 ? CreateSkillContext(_players[frame.SelectedTargetSeats[0]]) : null,
            Actor = frame.WindowContext?.CardUse is { } card ? CreateSkillContext(_players[card.ActorSeat]) : null,
            BooleanState = stateId => GetProgramBooleanState(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId),
            PindianWon = bind => frame.PindianResultBindings.SingleOrDefault(item => item.Name == bind)?.SourceWon ?? false,
            HasClaimableDamageCards = effect.Options.Any(option => option.Condition.ContainsHasClaimableDamageCards()) &&
                GetClaimableProgramDamageCards(frame).Length > 0,
            HasOwnedCardCategory = (zones, categories) =>
                (zones.Contains(CardZoneKind.Equipment) &&
                 GetEquipment(_players[decision.PlayerSeat]).Any(card =>
                     MatchesProgramCardCategory(card.Kind, categories))) ||
                (zones.Contains(CardZoneKind.Hand) &&
                 GetHand(_players[decision.PlayerSeat]).Any(card =>
                     MatchesProgramCardCategory(card.Kind, categories)))
        };
        return decision.Choices.Select(choice =>
        {
            var facts = context with { ChoiceResult = bind => bind == choice.Parameters["result-bind"]
                ? choice.Parameters["option-id"] : frame.ChoiceBindings.SingleOrDefault(item => item.Name == bind)?.OptionId };
            var score = ProgramChoiceAi.Score(plan.Instructions.Skip(frame.InstructionIndex + 1), owner, chooser,
                facts, bind => TryCountChooserProgramBoundCards(frame, bind, decision.PlayerSeat));
            return (Choice: choice, Score: score);
        }).OrderByDescending(item => item.Score).ThenBy(item => item.Choice.Id.Value, StringComparer.Ordinal).First().Choice;
    }

    private int? TryCountChooserProgramBoundCards(ProgramSkillFrame frame, string bind, int chooserSeat)
    {
        var cards = frame.CardSetBindings.SingleOrDefault(item => item.Name == bind);
        return cards is not null && cards.SourceLocations.All(location => location.OwnerSeat == chooserSeat &&
            location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment)
            ? CountChooserProgramBoundCards(frame, bind, chooserSeat) : null;
    }
}
