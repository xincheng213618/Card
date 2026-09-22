namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SimpleProgramPindianResultBind = "__active-pindian-result";

    // The journal rebuilds these counters through accepted commands. They are
    // keyed by content identities, never by translated presentation text.
    private readonly Dictionary<(int Seat, string Skill, string Activation), int> _programUses = new();
    private readonly Dictionary<(int ProviderSeat, int SkillOwnerSeat, string Skill, string Contribution), int>
        _programContributionUses = new();

    // Compatibility inspection surface for existing diagnostics and tests. The
    // result is projected by the match-local index and does not scan registry values.
    private IReadOnlyList<SkillProgram> EnabledSkillPrograms(CharacterState player) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.Programs;

    private IReadOnlyList<SkillProgram> EnabledActivationPrograms(CharacterState player) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.ActivationPrograms;

    private IReadOnlyList<SkillProgram> EnabledContributionPrograms(CharacterState player) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.ContributionPrograms;

    private IReadOnlyList<SkillProgram> EnabledViewAsPrograms(CharacterState player) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.ViewAsPrograms;

    private IReadOnlyList<SkillProgram> EnabledCardIdentityPrograms(CharacterState player) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.CardIdentityPrograms;

    private IReadOnlyList<SkillProgram> EnabledPassiveRulePrograms(CharacterState player) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.PassiveRulePrograms;

    private IReadOnlyList<IndexedSkillProgramTrigger> EnabledUniqueProgramTriggers(
        CharacterState player,
        SkillProgramTriggerWindow window) =>
        _rulesVersion < 79 || _contentRegistry is null
            ? []
            : GetSkillBindingShard(player)!.GetUniqueTriggers(window);

    private SkillProgram GetEnabledSkillProgram(CharacterState player, string skillId) =>
        GetSkillBindingShard(player)?.GetProgram(skillId) ??
        throw new InvalidOperationException(
            $"Player {player.Seat} does not own enabled skill program '{skillId}'.");

    private IEnumerable<LegalAction> BuildProgramActions(CharacterState owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Play) yield break;
        var context = CreateSkillContext(owner);
        foreach (var program in EnabledActivationPrograms(owner))
        foreach (var activation in program.Activations)
        {
            if (!activation.Condition.Evaluate(context) ||
                activation.UsesPerTurn is { } limit &&
                _programUses.GetValueOrDefault((owner.Seat, program.Id, activation.Id)) >= limit)
                continue;
            if (activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartPindian) &&
                GetHand(owner).Count == 0)
                continue;
            var cards = activation.MaxCards == 0 ? [] : activation.SourceZones
                .SelectMany(zone => _cardZones.CardsAt(new CardLocation(zone, owner.Seat)))
                .Select(card => card.Id).Distinct().Order().ToArray();
            var targets = activation.MaxTargets == 0 ? [] : _players
                .Where(target => target.IsAlive &&
                    (target.Seat != owner.Seat || !activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GiveSelected)) &&
                    (activation.TargetKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded ||
                     target.Seat != owner.Seat) &&
                    (activation.TargetKind switch
                    {
                        SkillProgramTargetKind.OtherLiving => target.Seat != owner.Seat,
                        SkillProgramTargetKind.OtherLivingWithHand =>
                            target.Seat != owner.Seat && GetHand(target).Count > 0,
                        SkillProgramTargetKind.AnyLiving => true,
                        SkillProgramTargetKind.OtherWounded =>
                            target.Seat != owner.Seat && target.Hp < target.MaxHp,
                        SkillProgramTargetKind.AnyWounded => target.Hp < target.MaxHp,
                        SkillProgramTargetKind.AnyLivingHandBelowMaxHp => GetHand(target).Count < target.MaxHp,
                        SkillProgramTargetKind.EventTarget => false,
                        _ => false
                    }) &&
                    (!activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartPindian) ||
                     GetHand(target).Count > 0))
                .Select(target => target.Seat).Order().ToArray();
            if (cards.Length < activation.MinCards || targets.Length < activation.MinTargets) continue;
            yield return new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                $"发动【{_contentRegistry!.Skills[program.Id].Name}】",
                MinCardCount: activation.MinCards, MaxCardCount: activation.MaxCards,
                MinTargetCount: activation.MinTargets, MaxTargetCount: activation.MaxTargets)
            {
                ProgramSkillId = program.Id,
                ProgramActivationId = activation.Id,
                ProgramAiHint = CreateProgramAiHint(program, activation, context, owner.IsFaceDown),
                SelectableCardIds = Array.AsReadOnly(cards),
                SelectableTargetSeats = Array.AsReadOnly(targets)
            };
        }

        if (_rulesVersion < 85) yield break;
        foreach (var skillOwner in _players.Where(player => player.IsAlive && player.Seat != owner.Seat).OrderBy(player => player.Seat))
        foreach (var program in EnabledContributionPrograms(skillOwner))
        foreach (var contribution in program.Contributions)
        {
            var providerFaction = GetEffectiveFactionId(owner);
            var key = (owner.Seat, skillOwner.Seat, program.Id, contribution.Id);
            if (providerFaction is null ||
                !contribution.ProviderFactions.Contains(providerFaction, StringComparer.Ordinal) ||
                skillOwner.Role != contribution.OwnerRole ||
                _programContributionUses.GetValueOrDefault(key) >= contribution.UsesPerPlayPhase)
                continue;
            var cards = GetHand(owner)
                .Where(card => contribution.CardKinds.Contains(card.Kind) || contribution.CardSuits.Contains(card.Suit))
                .Select(card => card.Id).Order().ToArray();
            if (cards.Length == 0) continue;
            yield return new LegalAction(LegalActionKind.UseProgramSkill, null, skillOwner.Seat,
                $"响应【{_contentRegistry!.Skills[program.Id].Name}】，将一张牌交给 {skillOwner.Name}",
                MinCardCount: 1, MaxCardCount: 1, MinTargetCount: 1, MaxTargetCount: 1)
            {
                ProgramSkillId = program.Id,
                ProgramActivationId = contribution.Id,
                ProgramSkillOwnerSeat = skillOwner.Seat,
                ProgramAiHint = new SkillProgramAiHint(0, 0, 0, 0, 0, 0, true, false),
                SelectableCardIds = Array.AsReadOnly(cards),
                SelectableTargetSeats = Array.AsReadOnly(new[] { skillOwner.Seat })
            };
        }
    }

    private SkillProgramAiHint CreateProgramAiHint(
        SkillProgram program,
        SkillProgramActivation activation,
        PlayerSkillContext context,
        bool faceDown)
    {
        if (program.UsesCompositionKernel)
        {
            var owner = _players[context.Seat];
            var instanceId = GetRuntimeSkillInstanceId(owner, program.Id);
            return ProgramCompositionAi.Estimate(activation.Effects, context, faceDown,
                CreateProgramAiPublicContext(owner) with
                {
                    BooleanState = stateId => GetProgramBooleanState(owner.Seat, program.Id, instanceId, stateId)
                }).Hint;
        }
        var effects = activation.Effects.Where(effect => effect.Condition.Evaluate(context)).ToArray();
        int Sum(SkillProgramEffectOp op, SkillProgramEffectTarget target) =>
            effects.Where(effect => effect.Op == op && effect.Target == target).Sum(effect => effect.Amount);
        return new SkillProgramAiHint(
            Sum(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner),
            Sum(SkillProgramEffectOp.Recover, SkillProgramEffectTarget.Owner),
            Sum(SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.Owner),
            Sum(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.SelectedTarget),
            Sum(SkillProgramEffectOp.Recover, SkillProgramEffectTarget.SelectedTarget),
            Sum(SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.SelectedTarget),
            effects.Any(effect => effect.Op == SkillProgramEffectOp.GiveSelected),
            effects.Any(effect => effect.Op == SkillProgramEffectOp.DiscardSelected));
    }

    private static CommandError? ValidateProgramSelection(LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        if (cards.Count < action.MinCardCount || cards.Count > action.MaxCardCount ||
            cards.Distinct().Count() != cards.Count || cards.Any(id => !action.SelectableCardIds.Contains(id)))
            return new CommandError(CommandErrorCode.InvalidCard, "The program's card selection is invalid.");
        if (targets.Count < action.MinTargetCount || targets.Count > action.MaxTargetCount ||
            targets.Distinct().Count() != targets.Count || targets.Any(seat => !action.SelectableTargetSeats.Contains(seat)))
            return new CommandError(CommandErrorCode.InvalidTarget, "The program's target selection is invalid.");
        return null;
    }

    private CommandResult SubmitUseProgramSkill(UseProgramSkillCommand command)
    {
        var error = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var owner = _players[command.ActorSeat];
        var candidates = BuildProgramActions(owner).Where(candidate =>
            candidate.ProgramSkillId == command.SkillId &&
            candidate.ProgramActivationId == command.ActivationId).ToArray();
        var action = command.SkillOwnerSeat is { } skillOwnerSeat
            ? candidates.SingleOrDefault(candidate => candidate.ProgramSkillOwnerSeat == skillOwnerSeat)
            : candidates.Length == 1
                ? candidates[0]
                : candidates.SingleOrDefault(candidate => candidate.ProgramSkillOwnerSeat is { } candidateOwner &&
                    command.TargetSeats.Count == 1 && command.TargetSeats[0] == candidateOwner);
        if (action is null)
            return Reject(CommandErrorCode.IllegalAction, "The skill activation is not currently available.");
        error = ValidateProgramSelection(action, command.CardIds, command.TargetSeats);
        if (error is not null) return Reject(error.Code, error.Message);
        return Accept(() =>
        {
            ClearPendingDecision();
            ExecuteProgramSkill(owner, action, command.CardIds, command.TargetSeats);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ExecuteProgramSkill(CharacterState owner, LegalAction action,
        IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var current = BuildProgramActions(owner).SingleOrDefault(candidate =>
            candidate.ProgramSkillId == action.ProgramSkillId &&
            candidate.ProgramActivationId == action.ProgramActivationId &&
            candidate.ProgramSkillOwnerSeat == action.ProgramSkillOwnerSeat)
            ?? throw new InvalidOperationException("The skill activation is no longer available.");
        if (ValidateProgramSelection(current, cards, targets) is { } error)
            throw new InvalidOperationException(error.Message);
        if (current.ProgramSkillOwnerSeat is { } skillOwnerSeat)
        {
            ExecuteProgramContribution(owner, _players[skillOwnerSeat], current, cards[0]);
            return;
        }
        var program = GetEnabledSkillProgram(owner, action.ProgramSkillId ??
            throw new InvalidOperationException("The program action has no skill identity."));
        var activation = program.Activations.Single(item => item.Id == action.ProgramActivationId);
        var key = (owner.Seat, program.Id, activation.Id);
        _programUses[key] = _programUses.GetValueOrDefault(key) + 1;
        var frame = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, program.Id, activation.Id,
            program.GameplayHash, 0, Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(targets.ToArray()))
        {
            SkillInstanceId = GetRuntimeSkillInstanceId(owner, program.Id)
        };
        _resolutionStack.Add(frame);
        QueueGameEvent(new ProgramSkillStartedEvent(frame.Id, owner.Seat, program.Id, activation.Id));
        AddLog("ActiveSkill", $"{owner.Name} 发动【{_contentRegistry!.Skills[program.Id].Name}】。", owner.Seat);
        ContinueProgramSkill(frame.Id);
    }

    private void ExecuteProgramContribution(CharacterState provider, CharacterState skillOwner,
        LegalAction action, int cardId)
    {
        var program = GetEnabledSkillProgram(skillOwner, action.ProgramSkillId ??
            throw new InvalidOperationException("The contribution action has no skill identity."));
        var contribution = program.Contributions.Single(item => item.Id == action.ProgramActivationId);
        var key = (provider.Seat, skillOwner.Seat, program.Id, contribution.Id);
        if (_programContributionUses.GetValueOrDefault(key) >= contribution.UsesPerPlayPhase)
            throw new InvalidOperationException("The contribution was already used in this play phase.");
        var card = GetHand(provider).Single(candidate => candidate.Id == cardId);
        if (!contribution.CardKinds.Contains(card.Kind) && !contribution.CardSuits.Contains(card.Suit))
            throw new InvalidOperationException("The contributed physical hand card no longer matches the binding.");

        _programContributionUses[key] = _programContributionUses.GetValueOrDefault(key) + 1;
        var actionId = ++_resolutionSequence;
        var reason = new CardMoveReason($"skill-program.{program.Id}.{contribution.Id}.contribute");
        MoveCard(card, CardLocation.Hand(provider.Seat), CardLocation.Processing, reason);
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(skillOwner.Seat), reason);
        QueueGameEvent(new ProgramSkillContributionResolvedEvent(actionId, provider.Seat, skillOwner.Seat,
            program.Id, contribution.Id, card.Id, card.Kind, card.Suit));
        AddLog("ActiveSkill",
            $"{provider.Name} 响应【{_contentRegistry!.Skills[program.Id].Name}】，将【{card.DisplayName}】交给 {skillOwner.Name}。",
            provider.Seat, skillOwner.Seat);
    }

    private void ResetProgramContributionUsesForPlayPhase(int providerSeat)
    {
        foreach (var key in _programContributionUses.Keys
                     .Where(key => key.ProviderSeat == providerSeat).ToArray())
            _programContributionUses.Remove(key);
    }

    private void ContinueProgramSkill(long frameId)
    {
        var host = new ProgramSkillHost(this);
        new SkillProgramExecutor().Run(frameId, host, host);
    }

    private void FinishProgramSkill(ProgramSkillFrame frame, bool completed)
    {
        CleanupProgramBoundCards(frame, completed);
        if (frame.TriggerId is not null)
        {
            CompleteProgramBinding(frame, completed);
            if (_winner != Winner.None && _status != EngineStatus.Completed) CompleteGame();
            return;
        }
        QueueGameEvent(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        if (_winner != Winner.None && _status != EngineStatus.Completed) CompleteGame();
    }

    private void BeginProgramSkillDying(long parentFrameId, CharacterState victim)
    {
        if (_pendingDying is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != parentFrameId)
            throw new InvalidOperationException("A program dying continuation requires its program frame.");
        var responders = Array.AsReadOnly(BuildDyingResponderSeats(victim.Seat).ToArray());
        var id = ++_resolutionSequence;
        _resolutionStack.Add(new DyingFrame(id, parentFrameId, victim.Seat, null, responders, 0));
        _pendingDying = new DyingResolution(id, null, null, victim.Seat, null, responders, parentFrameId,
            DyingContinuation.ProgramSkill);
        QueueGameEvent(new PlayerDyingEvent(id, victim.Seat, null));
        _status = EngineStatus.Running;
        if (!TryResolveBuqu(_pendingDying)) ExposeHumanDyingPrompt();
    }

    private SkillProgramStepOutcome BeginProgramSkillDamage(
        ProgramSkillFrame frame,
        int targetSeat,
        int amount)
    {
        if (_pendingAttack is not null || _pendingDying is not null ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            amount <= 0 || !IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("A program damage effect requires one active program and living target.");
        var attack = new AttackResolution(
            frame.Id,
            frame.OwnerSeat,
            targetSeat,
            card: null,
            damageAmount: amount,
            damageNatureOverride: DamageNature.Normal,
            programSkillFrameId: frame.Id);
        _pendingAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgramStepOutcome BeginProgramSkillPindian(
        ProgramSkillFrame frame,
        int targetSeat)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.PindianResultBindings.Any(item => item.Name == SimpleProgramPindianResultBind) ||
            frame.SelectedCardIds.Count != 1 ||
            frame.SelectedTargetSeats.Count != 1 || frame.SelectedTargetSeats[0] != targetSeat ||
            !IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat ||
            !_players[targetSeat].IsAlive || GetHand(_players[targetSeat]).Count == 0 ||
            !GetHand(_players[frame.OwnerSeat]).Any(card => card.Id == frame.SelectedCardIds[0]))
        {
            throw new InvalidOperationException(
                "A program Pindian requires one current owner hand card and one living other target with hand cards.");
        }
        var definition = _contentRegistry!.Skills[frame.SkillId];
        BeginSharedPindian(
            frame.Id,
            new(frame.SkillId, definition.Name, definition.Name, "选择拼点牌"),
            frame.OwnerSeat,
            targetSeat,
            frame.SelectedCardIds[0],
            legacySkill: null,
            programResultBind: SimpleProgramPindianResultBind,
            programResultVisibility: SkillProgramCardSetVisibility.Public);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void CompleteProgramSkillAfterDying(DyingResolution dying)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != dying.ParentFrameId)
            throw new InvalidOperationException("Program dying resolution lost its continuation.");
        ContinueProgramSkill(frame.Id);
    }

    private void AssertProgramSkillState()
    {
        var frames = _resolutionStack.OfType<ProgramSkillFrame>().ToArray();
        foreach (var frame in frames)
        {
            var program = _contentRegistry?.Skills.GetValueOrDefault(frame.SkillId)?.Program;
            if (_rulesVersion < 79 || !IsValidPlayerSeat(frame.OwnerSeat) ||
                string.IsNullOrWhiteSpace(frame.SkillInstanceId) ||
                program?.GameplayHash != frame.GameplayHash)
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");

            ProgramExecutionPlan plan;
            try
            {
                plan = ProgramInstructionResolver.Default.Resolve(frame, program);
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
            }
            if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count ||
                frame.SelectedCardIds.Distinct().Count() != frame.SelectedCardIds.Count ||
                frame.SelectedTargetSeats.Any(seat => !IsValidPlayerSeat(seat)) ||
                frame.CardSetBindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count() !=
                    frame.CardSetBindings.Count ||
                frame.CardSetBindings.Any(binding =>
                    binding.CardIds.Count != binding.SourceLocations.Count ||
                    binding.CardIds.Distinct().Count() != binding.CardIds.Count) ||
                frame.PindianResultBindings.Select(binding => binding.Name)
                    .Distinct(StringComparer.Ordinal).Count() != frame.PindianResultBindings.Count)
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");

            var executedSelection = plan.Instructions.Take(frame.InstructionIndex)
                .LastOrDefault(effect => effect.Op is
                    SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets);
            var awaitingCurrentSelection = executedSelection is not null &&
                ReferenceEquals(executedSelection, plan.Instructions[frame.InstructionIndex - 1]) &&
                ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
                _pendingDecision is { Kind: DecisionKind.ProgramTrigger } pending &&
                pending.Choices.Count > 0 && pending.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("frame-id") ==
                    frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    choice.Parameters.GetValueOrDefault("program-action") ==
                    (executedSelection.Op == SkillProgramEffectOp.SelectTarget
                        ? "select-target"
                        : "select-targets"));
            var dynamicTargetsValid = executedSelection is null || executedSelection.Op switch
            {
                SkillProgramEffectOp.SelectTarget =>
                    frame.SelectedTargetSeats.Count == 1 || awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0,
                SkillProgramEffectOp.SelectTargets =>
                    awaitingCurrentSelection && frame.SelectedTargetSeats.Count == 0 ||
                    frame.SelectedTargetSeats.Count >= executedSelection.MinimumTargets &&
                    frame.SelectedTargetSeats.Count <= executedSelection.MaximumTargets &&
                    frame.SelectedTargetSeats.Distinct().Count() == frame.SelectedTargetSeats.Count,
                _ => false
            };
            if (!dynamicTargetsValid)
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");

            if (frame.TriggerId is { } triggerId)
            {
                var trigger = program.Triggers.SingleOrDefault(item => item.Id == triggerId);
                if (trigger is null || frame.WindowContext is not { } context ||
                    context.OwnerSeat != frame.OwnerSeat || context.Window != trigger.Window ||
                    frame.ActivationId != triggerId || frame.SelectedCardIds.Count != 0 ||
                    executedSelection is null && frame.SelectedTargetSeats.Count != 0)
                    throw new InvalidOperationException("An active trigger program has an invalid cursor or context.");
                continue;
            }

            var activation = program.Activations.SingleOrDefault(item => item.Id == frame.ActivationId);
            if (activation is null ||
                frame.SelectedCardIds.Count != activation.MinCards ||
                executedSelection is null &&
                (frame.SelectedTargetSeats.Count < activation.MinTargets ||
                 frame.SelectedTargetSeats.Count > activation.MaxTargets))
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
        }

        if (_pendingDying is { ResumesProgramSkill: true } dying)
        {
            var parentIndex = _resolutionStack.FindLastIndex(item => item is ProgramSkillFrame program &&
                program.Id == dying.ParentFrameId);
            if (parentIndex < 0 || parentIndex + 1 >= _resolutionStack.Count ||
                _resolutionStack[parentIndex + 1] is not DyingFrame child || child.Id != dying.FrameId ||
                child.ParentFrameId != dying.ParentFrameId || dying.Attack is not null ||
                dying.DamageFrameId is not null)
                throw new InvalidOperationException("A program dying continuation is missing its parent program frame.");
        }
    }

    private static PromptChoice CreateProgramPlayChoice(LegalAction action)
    {
        var parameters = new Dictionary<string, string>
        {
            ["action"] = "use-program-skill",
            ["skill-id"] = action.ProgramSkillId!,
            ["activation-id"] = action.ProgramActivationId!,
            ["min-card-count"] = action.MinCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max-card-count"] = action.MaxCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["min-target-count"] = action.MinTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max-target-count"] = action.MaxTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (action.ProgramSkillOwnerSeat is { } ownerSeat)
            parameters["skill-owner-seat"] = ownerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new PromptChoice(
            new ChoiceId($"play.program.{action.ProgramSkillId!.Length}:{action.ProgramSkillId}.{action.ProgramActivationId}" +
                (action.ProgramSkillOwnerSeat is { } seat ? $".owner-{seat}" : string.Empty)),
            action.Description, [], [], parameters);
    }
}

public sealed record ProgramSkillStartedEvent(long FrameId, int OwnerSeat, string SkillId, string ActivationId) : IGameEvent;
public sealed record ProgramSkillResolvedEvent(long FrameId, int OwnerSeat, string SkillId, string ActivationId, bool Completed) : IGameEvent;
public sealed record ProgramSkillHpLostEvent(long FrameId, string SkillId, int TargetSeat, int Amount, int RemainingHp) : IGameEvent;
public sealed record ProgramSkillContributionResolvedEvent(long ActionId, int ProviderSeat, int SkillOwnerSeat,
    string SkillId, string ContributionId, int CardId, CardKind CardKind, Suit CardSuit) : IGameEvent;
