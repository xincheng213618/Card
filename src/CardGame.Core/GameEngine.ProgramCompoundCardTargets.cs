namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome SelectProgramAddedBorrowedSwordVictim(ProgramSkillFrame frame, CardUseFrame use, int weaponOwner)
    {
        var choices = _players.Where(victim => IsLegalBorrowedSwordSlashTarget(_players[weaponOwner], victim))
            .Select(victim => new PromptChoice(new ChoiceId($"program-compound-target.frame-{frame.Id}.victim-{victim.Seat}"),
                $"令 {_players[weaponOwner].Name} 对 {victim.Name} 使用杀", [], [victim.Seat],
                new Dictionary<string, string> { ["program-action"] = "add-compound-card-target",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-use-id"] = use.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["weapon-owner"] = weaponOwner.ToString(System.Globalization.CultureInfo.InvariantCulture) })).ToArray();
        if (choices.Length == 0) throw new InvalidOperationException("A borrowed-sword invitee lost all legal Slash victims.");
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            "为借刀杀人的额外目标选择杀的目标。", [], choices.SelectMany(choice => choice.Targets).ToArray(), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), TargetSeat = weaponOwner, IsPrivate = false,
            SkillPrompt = new(frame.SkillId, _contentRegistry!.GetSkill(frame.SkillId).Name,
                "选择借刀的杀目标", _contentRegistry.GetSkill(frame.SkillId).Description),
            Choices = Array.AsReadOnly(choices)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramCompoundCardTarget(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A compound target lost its frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var use = GetProgramRoleCardUse(frame);
        var owner = frame.SelectedTargetSeats.Single();
        if (effect.Op != SkillProgramEffectOp.AddCurrentCardUseTarget || use.CardKind != CardKind.BorrowedSword ||
            selected.Parameters.GetValueOrDefault("program-action") != "add-compound-card-target" ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("card-use-id") != use.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("weapon-owner") != owner.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Cards.Count != 0 || selected.Targets.Count != 1 ||
            !GetProgramCardUseRoleTargets(frame).Contains(owner) ||
            !IsLegalBorrowedSwordSlashTarget(_players[owner], _players[selected.Targets[0]]))
            throw new InvalidOperationException("The compound target answer lost its frozen instruction or legal participants.");
        ClearPendingDecision();
        CompleteProgramCardUseTargetAddition(frame, use, [.. use.TargetSeats, owner, selected.Targets[0]], owner);
        _adjustedTargetCardUses.Add(use.Id);
        ContinueProgramSkill(frame.Id);
    }

    private void AssertProgramCompoundCardTarget(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (_pendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "add-compound-card-target") != true) return;
        var use = GetProgramRoleCardUse(frame);
        var owner = frame.SelectedTargetSeats.Single();
        if (paused.Op != SkillProgramEffectOp.AddCurrentCardUseTarget || use.CardKind != CardKind.BorrowedSword ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: false } decision ||
            decision.PlayerSeat != frame.OwnerSeat || decision.TargetSeat != owner ||
            decision.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 1 ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                !IsLegalBorrowedSwordSlashTarget(_players[owner], _players[choice.Targets[0]])))
            throw new InvalidOperationException("A compound target prompt lost its exact owner, instruction or victim domain.");
    }
}
