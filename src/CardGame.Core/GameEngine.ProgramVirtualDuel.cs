namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int? FrozenProgramDuelInstructionIndex(long frameId)
    {
        if (_resolutionStack.SingleOrDefault(frame => frame.Id == frameId) is not ProgramSkillFrame
            { CardAttack: { ProgramUseInstructionIndex: { } instructionIndex } } owner) return null;
        if (owner.CardAttack.ProgramSkillFrameId != owner.Id || owner.CardAttack.EffectiveCardKind != CardKind.Duel ||
            owner.CardAttack.Active && owner.InstructionIndex != instructionIndex || instructionIndex < 1 ||
            _contentRegistry.Skills.GetValueOrDefault(owner.SkillId)?.Program is not { } program ||
            program.GameplayHash != owner.GameplayHash)
            throw new InvalidOperationException("A program Duel lost its frozen use instruction.");
        var plan = ProgramInstructionResolver.Default.Resolve(owner, program);
        // Completed attacks may remain as receipts while the program advances.
        // A live attack must still own the paused instruction checked above.
        if (instructionIndex > plan.Instructions.Count ||
            plan.GetPausedInstruction(instructionIndex).Effect.Op != SkillProgramEffectOp.StartVirtualDuel)
            throw new InvalidOperationException("A program Duel use must remain on its issued StartVirtualDuel instruction.");
        return instructionIndex;
    }

    private int? DamageProgramUseInstructionIndex(IDamageAttempt attack) =>
        attack is CardAttackHandle ? FrozenProgramDuelInstructionIndex(attack.ResolutionId) : null;

    private bool IsProgramVirtualDuelAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (processing.Count != 0 || attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            attack.ProgramSkillFrameId != attack.ResolutionId || attack.EffectiveCardKind != CardKind.Duel ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == attack.ResolutionId) is not ProgramSkillFrame owner ||
            owner.CardAttack is not { Active: true } state || owner.Continuations.Duel is not { Active: true } ||
            _contentRegistry.Skills.GetValueOrDefault(owner.SkillId)?.Program is not { } program ||
            program.GameplayHash != owner.GameplayHash || owner.InstructionIndex < 1) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(owner, program);
        if (state.ProgramUseInstructionIndex != owner.InstructionIndex || owner.InstructionIndex > plan.Instructions.Count ||
            plan.GetPausedInstruction(owner.InstructionIndex).Effect.Op != SkillProgramEffectOp.StartVirtualDuel) return false;
        int originalSource;
        int originalTarget;
        if (owner.TriggerId is null)
        {
            if (owner.SelectedTargetSeats is not [var source, var target]) return false;
            originalSource = source; originalTarget = target;
        }
        else
        {
            if (owner.WindowContext?.Window != SkillProgramTriggerWindow.PlayPhaseStarting ||
                owner.SelectedTargetSeats is not [var counterpart]) return false;
            originalSource = owner.OwnerSeat; originalTarget = counterpart;
        }
        if (!IsValidPlayerSeat(originalSource) || !IsValidPlayerSeat(originalTarget) || originalSource == originalTarget ||
            attack.CardUserSeat != originalSource || attack.IsSourceLess ||
            attack.SourceSeat != originalSource && attack.SourceSeat != originalTarget) return false;
        var firstDamageTarget = attack.SourceSeat == originalSource ? originalTarget : originalSource;
        if (!attack.IsChainPropagation) return attack.TargetSeat == firstDamageTarget;

        // The declaration pair never changes. Elemental propagation instead
        // selects an exact issued recipient from this attack's own frozen list.
        var targets = state.ChainedTargetSeats;
        var cursor = state.ChainedTargetIndex;
        var nature = GetDamageNature(attack);
        if (nature is not (DamageNature.Fire or DamageNature.Thunder) || targets.Count == 0 ||
            targets.Distinct().Count() != targets.Count || targets.Any(seat => !IsValidPlayerSeat(seat) || seat == firstDamageTarget) ||
            cursor < 1 || cursor > targets.Count || targets[cursor - 1] != attack.TargetSeat) return false;
        var steps = CompleteProgramEventHistory().OfType<ChainedDamagePropagatedEvent>()
            .Where(step => step.ResolutionId == attack.ResolutionId &&
                step.ProgramUseInstructionIndex == state.ProgramUseInstructionIndex).ToArray();
        if (steps.Length == 0) return false;
        var previousSeat = firstDamageTarget;
        var previousIndex = -1;
        foreach (var step in steps)
        {
            var targetIndex = Array.IndexOf(targets.ToArray(), step.TargetSeat);
            if (step.SourceSeat != attack.SourceSeat || step.FromSeat != previousSeat || step.Nature != nature || step.Amount <= 0 ||
                targetIndex <= previousIndex || targetIndex >= cursor ||
                state.RecipientDamageScope is { } scope && step.Amount != scope.PropagationBaseAmount) return false;
            previousSeat = step.TargetSeat; previousIndex = targetIndex;
        }
        return previousIndex == cursor - 1 && previousSeat == attack.TargetSeat;
    }

    /// <summary>Resolves a direct duel effect between ordered participants; their eligibility belongs to the activation.</summary>
    private SkillProgramStepOutcome BeginProgramVirtualDuel(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        int sourceSeat;
        int targetSeat;
        if (active.TriggerId is null)
        {
            if (active.SelectedTargetSeats.Count != 2 ||
                active.SelectedTargetSeats[0] == active.SelectedTargetSeats[1])
                throw new InvalidOperationException("A virtual duel needs an activation with two different ordered participants.");
            sourceSeat = active.SelectedTargetSeats[0];
            targetSeat = active.SelectedTargetSeats[1];
        }
        else
        {
            // A play-phase trigger duel takes one selected counterpart; the skill
            // owner is the duel source.
            if (active.WindowContext?.Window is not SkillProgramTriggerWindow.PlayPhaseStarting ||
                active.SelectedTargetSeats is not [var counterpart] || counterpart == active.OwnerSeat)
                throw new InvalidOperationException("A triggered virtual duel needs one play-phase selected counterpart.");
            sourceSeat = active.OwnerSeat;
            targetSeat = counterpart;
        }
        if (active.SelectedTargetSeats.Any(seat => seat < 0 || seat >= _players.Count))
            throw new InvalidOperationException("A virtual duel participant seat is invalid.");
        // Paying a preceding cost can trigger other effects; a deceased participant cannot enter a new duel.
        if (active.SelectedTargetSeats.Any(seat => !_players[seat].IsAlive))
            return SkillProgramStepOutcome.Continue;
        if (ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("A virtual duel cannot overwrite a pending card resolution.");

        var attack = new CardAttackHandle(this, active.Id, sourceSeat, targetSeat,
            card: null, playedCardKind: CardKind.Duel, programSkillFrameId: active.Id);
        ActiveCardAttack = attack;
        ActiveDuel = new DuelHandle(this, attack);
        UpdateCardAttackState(active.Id, state => state! with { ProgramUseInstructionIndex = active.InstructionIndex });
        CaptureWenlanDirectDuel(active, sourceSeat, targetSeat);
        IssueMouZhaJiangLegacyDuel(active, sourceSeat);
        if (TracksWholeActualTurnUseOrdinal)
        {
            if (CurrentUseKindDeduplicationHistory().OfType<CurrentTurnCardUseKindsRecordedEvent>().Any(fact =>
                    fact.OriginFrameId == active.Id && fact.ProgramInstructionIndex == active.InstructionIndex))
                throw new InvalidOperationException("A virtual Duel instruction cannot declare its actual use twice.");
            RecordCurrentTurnUseKinds(active.Id, sourceSeat, null, CardKind.Duel, false, active.InstructionIndex);
        }
        BeginDuelResponse(ActiveDuel);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
