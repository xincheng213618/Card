namespace CardGame.Core;

internal sealed partial class ProgramPaidCardRuntime
{
    internal SkillProgramStepOutcome ChooseProgramOption(ProgramSkillFrame frame, int chooserSeat,
        string resultBind, IReadOnlyList<SkillProgramChoiceOption> options)
    {
        var active = _frames.ActiveProgram(frame.Id);
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A named program choice cannot be answered twice.");
        var chooser = _facts.Seat(chooserSeat);
        var choices = options.Where(option => IsProgramOptionAvailable(active, chooserSeat, option)).Select(option =>
            new PromptChoice(new ChoiceId($"program-option.frame-{frame.Id}.{resultBind}.{option.Id}"),
                option.Label, [], [], new Dictionary<string, string>
                {
                    ["program-action"] = "choose-option",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = resultBind,
                    ["option-id"] = option.Id
                })).ToArray();
        if (!_facts.IsAlive(chooser) || choices.Length == 0)
        {
            _operations.Cancel(active, "没有可用的效果选项，技能剩余步骤取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var skill = _facts.Skill(frame.SkillId);
        _prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooserSeat,
            $"【{skill.Name}】请选择一项。", [], [], frame.OwnerSeat)
        {
            PromptId = _prompts.NextId(),
            IsPrivate = true,
            TargetSeat = chooserSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 选择效果", skill.Description),
            Choices = Array.AsReadOnly(choices)
        }, _facts.IsHuman(chooser));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAnswer("choose-option")]
    internal void ResolveProgramOptionChoice(PromptChoice selected)
    {
        var frame = _frames.Top as ProgramSkillFrame ??
            throw new InvalidOperationException("The named choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _facts.Skill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var chooserSeat = effect.ChooserRef is { } chooser
            ? ResolveProgramParticipant(frame, chooser)
            : ResolveProgramEffectTarget(frame, effect.Target);
        if (effect.Op != SkillProgramEffectOp.ChooseOption ||
            _prompts.Current is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat || selected.Cards.Count != 0 || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
            frame.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind))
            throw new InvalidOperationException("The choice does not match its suspended instruction.");
        var option = effect.Options.SingleOrDefault(item => item.Id == selected.Parameters.GetValueOrDefault("option-id")) ??
            throw new InvalidOperationException("The selected program option is unavailable.");
        var stillAvailable = IsProgramOptionAvailable(frame, chooserSeat, option);
        if (!stillAvailable && option.RequiredTargetKind is not null)
            throw new InvalidOperationException("The selected option's required targets are no longer available.");
        if (!stillAvailable && (option.Condition.ContainsHasClaimableDamageCards() ||
            option.Condition.ContainsBoundCardCountAtLeast() || option.Condition.ContainsHasOwnedCardCategory() ||
            option.Condition.ContainsBoundCardsMatchCategories()))
            throw new InvalidOperationException("The selected option's required cards are no longer available.");
        _prompts.Clear();
        if (!_facts.IsAlive(chooserSeat) || !stillAvailable ||
            !_facts.HasSkillInstance(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId))
        {
            _operations.Cancel(frame, "效果选项或技能实例已失效，技能剩余步骤取消。");
            return;
        }
        _frames.ReplaceTop(frame with
        {
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(effect.ResultBind!, option.Id, chooserSeat)).ToArray())
        });
        _events.AdvanceFact(new ProgramOptionChosenEvent(frame.Id, frame.SkillId, RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame),
            frame.OwnerSeat, effect.ResultBind!, option.Id, chooserSeat, option.Label));
        _frames.AdvanceProgram(frame.Id);
    }

    internal bool IsClaimableProgramOptionStillAvailable(PromptChoice selected)
    {
        if (_frames.Top is not ProgramSkillFrame frame)
            return true;
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _facts.Skill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseOption)
            return true;
        var option = effect.Options.SingleOrDefault(item => item.Id == selected.Parameters.GetValueOrDefault("option-id"));
        if (option is null) return true;
        if (option.RequiredTargetKind is null && !option.Condition.ContainsHasClaimableDamageCards() &&
            !option.Condition.ContainsBoundCardCountAtLeast() && !option.Condition.ContainsHasOwnedCardCategory() &&
            !option.Condition.ContainsBoundCardsMatchCategories()) return true;
        var chooserSeat = effect.ChooserRef is { } chooser
            ? ResolveProgramParticipant(frame, chooser)
            : ResolveProgramEffectTarget(frame, effect.Target);
        return IsProgramOptionAvailable(frame, chooserSeat, option);
    }

    internal bool IsProgramOptionAvailable(ProgramSkillFrame frame, int chooserSeat, SkillProgramChoiceOption option) =>
        (option.RequiredTargetKind is not { } requiredTargetKind ||
         chooserSeat == frame.OwnerSeat && _facts.TargetSeats(chooserSeat, requiredTargetKind, frame.WindowContext)
             .Any(seat => _facts.CanSelectPindianOpponent(frame, seat))) &&
        option.Condition.EvaluateOption(_facts.SkillContext(chooserSeat),
            () => _facts.ClaimableDamageCards(frame).Length > 0,
            bind => IsProgramAttackRangeCoverageDecreased(frame, bind),
            hasOwnedCardCategory: (zones, categories, kinds) =>
                HasOwnedProgramCardCategory(chooserSeat, zones, categories, kinds),
            boundCardCount: bind => CountChooserProgramBoundCards(frame, bind, chooserSeat),
            activationCardCount: frame.SelectedCardIds.Count,
            boundCardSuitMatchesChoice: (cardBind, choiceBind) =>
                _facts.FrozenSuitMatchesChoice(frame, cardBind, choiceBind),
            boundCardsMatchCategories: (bind, categories) =>
                _facts.BoundCardsMatchCategories(frame, bind, categories));

    internal int CountChooserProgramBoundCards(ProgramSkillFrame frame, string bind, int chooserSeat)
    {
        var cards = CardSet(frame, bind);
        if (cards.CardIds.Count != cards.SourceLocations.Count ||
            cards.SourceLocations.Any(location => location.OwnerSeat != chooserSeat ||
                location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("A chooser cannot inspect another player's private card set.");
        return cards.CardIds.Select((id, index) => (id, index))
            .Count(item => _facts.Location(item.id) == cards.SourceLocations[item.index]);
    }

    internal static int ResolveProgramEffectTarget(ProgramSkillFrame frame, SkillProgramEffectTarget target) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramChoices.ResolveProgramEffectTarget(frame,target);

    internal int? TryCountChooserProgramBoundCards(ProgramSkillFrame frame, string bind, int chooserSeat)
    {
        var cards = frame.CardSetBindings.SingleOrDefault(item => item.Name == bind);
        if (cards is { Visibility: SkillProgramCardSetVisibility.Public })
            return cards.CardIds.Count(cardId =>
                _facts.Location(cardId) is
                { OwnerSeat: var seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } &&
                seat == chooserSeat);
        return cards is not null && cards.SourceLocations.All(location => location.OwnerSeat == chooserSeat &&
            location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment)
            ? CountChooserProgramBoundCards(frame, bind, chooserSeat) : null;
    }
}
