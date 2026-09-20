namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The journal rebuilds these counters through accepted commands. They are
    // keyed by content identities, never by translated presentation text.
    private readonly Dictionary<(int Seat, string Skill, string Activation), int> _programUses = new();
    private readonly Dictionary<(int ProviderSeat, int SkillOwnerSeat, string Skill, string Contribution), int>
        _programContributionUses = new();

    private IReadOnlyList<SkillProgram> EnabledSkillPrograms(PlayerRuntime player)
    {
        if (_rulesVersion < 79 || _contentRegistry is null) return [];
        var ids = new List<string>();
        void AddGeneral(GeneralDefinition general)
        {
            if (_contentRegistry.Generals.TryGetValue(general.Id, out var definition))
                ids.AddRange(definition.SkillIds);
        }
        if (!IsNationalWarMode || player.GeneralSelected && player.GeneralRevealed)
            AddGeneral(player.General);
        if (IsNationalWarMode && player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed &&
            player.SecondaryGeneral is { } secondary)
            AddGeneral(secondary);
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => _contentRegistry.Skills[id].Program)
            .OfType<SkillProgram>().ToArray();
    }

    private IEnumerable<LegalAction> BuildProgramActions(PlayerRuntime owner)
    {
        if (!owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Play) yield break;
        var context = CreateSkillContext(owner);
        foreach (var program in EnabledSkillPrograms(owner))
        foreach (var activation in program.Activations)
        {
            if (!activation.Condition.Evaluate(context) ||
                activation.UsesPerTurn is { } limit &&
                _programUses.GetValueOrDefault((owner.Seat, program.Id, activation.Id)) >= limit)
                continue;
            var cards = activation.MaxCards == 0 ? [] : GetHand(owner).Select(card => card.Id).Order().ToArray();
            var targets = activation.MaxTargets == 0 ? [] : _players
                .Where(target => target.IsAlive &&
                    (target.Seat != owner.Seat || !activation.Effects.Any(effect => effect.Op == SkillProgramEffectOp.GiveSelected)) &&
                    (activation.TargetKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded ||
                     target.Seat != owner.Seat) &&
                    (activation.TargetKind is SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLiving ||
                     target.Hp < target.MaxHp))
                .Select(target => target.Seat).Order().ToArray();
            if (cards.Length < activation.MinCards || targets.Length < activation.MinTargets) continue;
            yield return new LegalAction(LegalActionKind.UseProgramSkill, null, null,
                $"发动【{_contentRegistry!.Skills[program.Id].Name}】",
                MinCardCount: activation.MinCards, MaxCardCount: activation.MaxCards,
                MinTargetCount: activation.MinTargets, MaxTargetCount: activation.MaxTargets)
            {
                ProgramSkillId = program.Id,
                ProgramActivationId = activation.Id,
                ProgramAiHint = CreateProgramAiHint(activation, context),
                SelectableCardIds = Array.AsReadOnly(cards),
                SelectableTargetSeats = Array.AsReadOnly(targets)
            };
        }

        if (_rulesVersion < 85) yield break;
        foreach (var skillOwner in _players.Where(player => player.IsAlive && player.Seat != owner.Seat).OrderBy(player => player.Seat))
        foreach (var program in EnabledSkillPrograms(skillOwner))
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

    private static SkillProgramAiHint CreateProgramAiHint(SkillProgramActivation activation, PlayerSkillContext context)
    {
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

    private void ExecuteProgramSkill(PlayerRuntime owner, LegalAction action,
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
        var program = EnabledSkillPrograms(owner).Single(item => item.Id == action.ProgramSkillId);
        var activation = program.Activations.Single(item => item.Id == action.ProgramActivationId);
        var key = (owner.Seat, program.Id, activation.Id);
        _programUses[key] = _programUses.GetValueOrDefault(key) + 1;
        var frame = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, program.Id, activation.Id,
            program.GameplayHash, 0, Array.AsReadOnly(cards.ToArray()), Array.AsReadOnly(targets.ToArray()));
        _resolutionStack.Add(frame);
        QueueGameEvent(new ProgramSkillStartedEvent(frame.Id, owner.Seat, program.Id, activation.Id));
        AddLog("ActiveSkill", $"{owner.Name} 发动【{_contentRegistry!.Skills[program.Id].Name}】。", owner.Seat);
        ContinueProgramSkill(frame.Id);
    }

    private void ExecuteProgramContribution(PlayerRuntime provider, PlayerRuntime skillOwner,
        LegalAction action, int cardId)
    {
        var program = EnabledSkillPrograms(skillOwner).Single(item => item.Id == action.ProgramSkillId);
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
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame frame && frame.Id == frameId)
        {
            var owner = _players[frame.OwnerSeat];
            var program = _contentRegistry!.Skills[frame.SkillId].Program
                ?? throw new InvalidOperationException("A running program is missing its compiled definition.");
            if (program.GameplayHash != frame.GameplayHash)
                throw new InvalidOperationException("A running program's gameplay hash has changed.");
            var activation = program.Activations.Single(item => item.Id == frame.ActivationId);
            if (!owner.IsAlive || _winner != Winner.None || frame.InstructionIndex >= activation.Effects.Count)
            {
                FinishProgramSkill(frame, completed: owner.IsAlive && frame.InstructionIndex >= activation.Effects.Count);
                return;
            }
            var effect = activation.Effects[frame.InstructionIndex];
            // Commit the cursor before any child resolution can suspend it.
            _resolutionStack[^1] = frame with { InstructionIndex = frame.InstructionIndex + 1 };
            if (!effect.Condition.Evaluate(CreateSkillContext(owner))) continue;
            var target = effect.Target == SkillProgramEffectTarget.Owner
                ? owner : _players[frame.SelectedTargetSeats.Single()];
            if (effect.Op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected &&
                (!target.IsAlive || frame.SelectedCardIds.Any(id => GetHand(owner).All(card => card.Id != id))))
            {
                // A rescue may consume a previously selected physical card.
                // Do not pay partially, or grant later benefits for an unpaid step.
                AddLog("ActiveSkill", "所选牌或接收者在结算中已失效，技能剩余步骤取消。", owner.Seat);
                FinishProgramSkill(frame, completed: false);
                return;
            }
            if (!target.IsAlive) continue;
            var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
            switch (effect.Op)
            {
                case SkillProgramEffectOp.Draw:
                    DrawCards(target, effect.Amount, log: true, reason: reason);
                    break;
                case SkillProgramEffectOp.Recover:
                    var amount = Math.Min(effect.Amount, target.MaxHp - target.Hp);
                    if (amount <= 0) break;
                    var recovery = BeginRecovery(frame.Id, owner.Seat, target.Seat, amount);
                    target.Hp += amount;
                    QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, target.Seat, amount, target.Hp));
                    PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
                    break;
                case SkillProgramEffectOp.LoseHp:
                    var lost = Math.Min(target.Hp, effect.Amount);
                    target.Hp = Math.Max(0, target.Hp - effect.Amount);
                    QueueGameEvent(new ProgramSkillHpLostEvent(frame.Id, frame.SkillId, target.Seat, lost, target.Hp));
                    if (target.Hp == 0)
                    {
                        BeginProgramSkillDying(frame.Id, target);
                        return;
                    }
                    break;
                case SkillProgramEffectOp.GiveSelected:
                case SkillProgramEffectOp.DiscardSelected:
                    var selected = frame.SelectedCardIds.Select(id => GetHand(owner).Single(card => card.Id == id)).ToArray();
                    MoveCards(selected, CardLocation.Hand(owner.Seat),
                        effect.Op == SkillProgramEffectOp.GiveSelected ? CardLocation.Hand(target.Seat) : CardLocation.DiscardPile,
                        reason);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported compiled skill effect {effect.Op}.");
            }
        }
    }

    private void FinishProgramSkill(ProgramSkillFrame frame, bool completed)
    {
        QueueGameEvent(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        if (_winner != Winner.None && _status != EngineStatus.Completed) CompleteGame();
    }

    private void BeginProgramSkillDying(long parentFrameId, PlayerRuntime victim)
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

    private void CompleteProgramSkillAfterDying(DyingResolution dying)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != dying.ParentFrameId)
            throw new InvalidOperationException("Program dying resolution lost its continuation.");
        ContinueProgramSkill(frame.Id);
    }

    private void AssertProgramSkillState()
    {
        var frames = _resolutionStack.OfType<ProgramSkillFrame>().ToArray();
        if (frames.Length > 1) throw new InvalidOperationException("Active skill programs cannot overlap.");
        if (frames.SingleOrDefault() is { } frame)
        {
            var program = _contentRegistry?.Skills.GetValueOrDefault(frame.SkillId)?.Program;
            var activation = program?.Activations.SingleOrDefault(item => item.Id == frame.ActivationId);
            if (_rulesVersion < 79 || !IsValidPlayerSeat(frame.OwnerSeat) ||
                program?.GameplayHash != frame.GameplayHash || activation is null ||
                frame.InstructionIndex < 1 || frame.InstructionIndex > activation.Effects.Count ||
                frame.SelectedCardIds.Count != activation.MinCards ||
                frame.SelectedCardIds.Distinct().Count() != frame.SelectedCardIds.Count ||
                frame.SelectedTargetSeats.Count < activation.MinTargets ||
                frame.SelectedTargetSeats.Count > activation.MaxTargets ||
                frame.SelectedTargetSeats.Any(seat => !IsValidPlayerSeat(seat)))
                throw new InvalidOperationException("An active skill program has an invalid cursor or selection.");
            if (_pendingDying is not { ResumesProgramSkill: true } dying ||
                dying.ParentFrameId != frame.Id || dying.Attack is not null || dying.DamageFrameId is not null ||
                _resolutionStack.LastOrDefault() is not DyingFrame child || child.Id != dying.FrameId ||
                child.ParentFrameId != frame.Id)
                throw new InvalidOperationException("A suspended skill program must retain its dying child.");
        }
        else if (_pendingDying?.ResumesProgramSkill == true)
            throw new InvalidOperationException("A program dying continuation is missing its program frame.");
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
