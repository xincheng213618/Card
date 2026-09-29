namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginProgramDesignatedVirtualSlash(
        ProgramSkillFrame frame, int targetSeat, string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind) ||
            active.ChoiceBindings.Any(binding => binding.Name == DesignatedSlashVictimBind(resultBind)))
            throw new InvalidOperationException("A named program choice cannot be answered twice.");
        if (active.SelectedTargetSeats is not [var selectedSeat] || selectedSeat != targetSeat)
            throw new InvalidOperationException(
                "A designated virtual slash requires the current single selected target.");
        var owner = _players[frame.OwnerSeat];
        var user = _players[targetSeat];
        if (!owner.IsAlive || !user.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId))
        {
            CancelProgramBindingAndCleanup(active, "技能拥有者或用牌角色已失效，技能剩余结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var candidates = DesignatedSlashCandidateSeats(user);
        if (candidates.Count == 0)
        {
            CommitProgramChoiceResult(frame.Id, resultBind,
                UseDesignatedVirtualSlashProgramOperationDescriptor.DeclinedOption,
                user.Seat, "其攻击范围内没有可指定的角色，摸一张牌。");
            return SkillProgramStepOutcome.Continue;
        }
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var choices = candidates.Select(seat => new PromptChoice(
            new ChoiceId($"program-designated-slash.frame-{active.Id}.victim-{seat}"),
            $"指定 {_players[seat].Name}。",
            [],
            [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "designated-slash-victim",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["result-bind"] = resultBind,
                ["victim-seat"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToList();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            owner.Seat,
            $"请选择 {user.Name} 攻击范围内的一名角色。",
            [],
            candidates.ToArray(),
            frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = user.Seat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 指定攻击目标", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramDesignatedSlashVictimChoice(ProgramSkillFrame frame, PromptChoice selected)
    {
        var effect = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.UseDesignatedVirtualSlash ||
            effect.ResultBind is not { } resultBind ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != resultBind ||
            selected.Cards.Count != 0 || selected.Targets.Count != 1)
            throw new InvalidOperationException("The designated-slash victim choice does not match its suspended instruction.");
        var owner = _players[frame.OwnerSeat];
        var user = _players[frame.SelectedTargetSeats.Single()];
        if (!owner.IsAlive || !user.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id),
                "技能拥有者或用牌角色已失效，技能剩余结算已取消。");
            return;
        }
        var victimSeat = selected.Targets.Single();
        if (!DesignatedSlashCandidateSeats(user).Contains(victimSeat))
            throw new InvalidOperationException("The designated slash victim is not within the user's attack range.");
        ClearPendingDecision();
        CommitProgramChoiceResult(frame.Id, DesignatedSlashVictimBind(resultBind), $"seat-{victimSeat}",
            frame.OwnerSeat, $"指定 {_players[victimSeat].Name}。");
        PublishProgramDesignatedSlashAnswerPrompt(frame, resultBind, user, victimSeat);
    }

    private void ResolveProgramDesignatedSlashAnswerChoice(ProgramSkillFrame frame, PromptChoice selected)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var effect = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.UseDesignatedVirtualSlash ||
            effect.ResultBind is not { } resultBind ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != resultBind)
            throw new InvalidOperationException("The designated-slash answer does not match its suspended instruction.");
        var victimBinding = active.ChoiceBindings
            .SingleOrDefault(binding => binding.Name == DesignatedSlashVictimBind(resultBind)) ??
            throw new InvalidOperationException("The designated-slash answer lost its designated victim.");
        var victimSeat = int.Parse(victimBinding.OptionId["seat-".Length..],
            System.Globalization.CultureInfo.InvariantCulture);
        var owner = _players[frame.OwnerSeat];
        var user = _players[frame.SelectedTargetSeats.Single()];
        var action = selected.Parameters.GetValueOrDefault("program-action");
        if (!owner.IsAlive || !user.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            !_players[victimSeat].IsAlive)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id),
                "指定杀参与者已失效，技能剩余结算已取消。");
            return;
        }
        ClearPendingDecision();
        if (action == "designated-slash-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            CommitProgramChoiceResult(frame.Id, resultBind,
                UseDesignatedVirtualSlashProgramOperationDescriptor.DeclinedOption,
                user.Seat, "不使用【杀】，摸一张牌。");
            ContinueProgramSkill(frame.Id);
            return;
        }
        if (action != "designated-slash-use" ||
            selected.Cards.Count != 0 || selected.Targets.Count != 1 ||
            selected.Targets[0] != victimSeat)
            throw new InvalidOperationException("The designated-slash answer is malformed.");
        if (!DesignatedSlashCandidateSeats(user).Contains(victimSeat))
            throw new InvalidOperationException("The designated slash victim is no longer legal.");
        CommitProgramChoiceResult(frame.Id, resultBind,
            UseDesignatedVirtualSlashProgramOperationDescriptor.UsedSlashOption,
            user.Seat, $"视为使用【杀】攻击 {_players[victimSeat].Name}。");
        BeginProgramDesignatedSlashAttack(frame, user, victimSeat);
    }

    private void PublishProgramDesignatedSlashAnswerPrompt(
        ProgramSkillFrame frame, string resultBind, CharacterState user, int victimSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var usable = DesignatedSlashCandidateSeats(user).Contains(victimSeat);
        var choices = new List<PromptChoice>();
        if (usable)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"program-designated-slash.frame-{active.Id}.use"),
                $"视为使用【杀】攻击 {_players[victimSeat].Name}。",
                [],
                [victimSeat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "designated-slash-use",
                    ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = resultBind
                }));
        }
        choices.Add(new PromptChoice(
            new ChoiceId($"program-designated-slash.frame-{active.Id}.decline"),
            "不使用【杀】，摸一张牌。",
            [],
            [],
            new Dictionary<string, string>
            {
                ["program-action"] = "designated-slash-decline",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["result-bind"] = resultBind
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            user.Seat,
            usable
                ? $"是否视为使用【杀】攻击 {_players[victimSeat].Name}？"
                : "指定的目标已不可攻击，摸一张牌。",
            [],
            usable ? [victimSeat] : [],
            frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = user.Seat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 是否使用【杀】", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = user.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void BeginProgramDesignatedSlashAttack(ProgramSkillFrame frame, CharacterState user, int victimSeat)
    {
        if (_pendingAttack is not null || _pendingDuel is not null)
            throw new InvalidOperationException("A designated virtual slash requires a quiet attack boundary.");
        var resolutionId = ++_resolutionSequence;
        _resolutionStack.Add(new CardUseFrame(resolutionId, user.Seat, 0, CardKind.Slash,
            Array.AsReadOnly(new[] { victimSeat }),
            PhysicalCardIds: Array.AsReadOnly(Array.Empty<int>())));
        QueueGameEvent(new CardUseDeclaredEvent(resolutionId, 0, CardKind.Slash, user.Seat));
        QueueGameEvent(new TargetsConfirmedEvent(resolutionId, Array.AsReadOnly(new[] { victimSeat })));
        var attack = new AttackResolution(resolutionId, user.Seat, victimSeat, card: null,
            playedCardKind: CardKind.Slash, programSkillCardUseFrameId: frame.Id);
        _pendingAttack = attack;
        QueueGameEvent(new CardUsedEvent(0, CardKind.Slash, user.Seat, victimSeat));
        ContinueSlashAfterResponsePrograms(attack);
    }

    private List<int> DesignatedSlashCandidateSeats(CharacterState user) =>
        _players.Where(player => player.IsAlive && player.Seat != user.Seat &&
                GetCombatDistance(user.Seat, player.Seat) <= GetAttackRange(user.Seat) &&
                !IsDirectedCardTargetProhibited(user.Seat, player.Seat, CardKind.Slash) &&
                !IsSlashProhibited(player))
            .Select(player => player.Seat).Order().ToList();

    private static string DesignatedSlashVictimBind(string resultBind) => $"{resultBind}-victim";
}
