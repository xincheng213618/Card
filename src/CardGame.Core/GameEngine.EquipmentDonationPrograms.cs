using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string EquipmentDonationMoveReason = "program.all-equipment-donation.give";
    private static string EquipmentDonationUsage(string id) => $"equipment-donation:{id}";
    private long EquipmentDonationSequence => _cardMovements.Count == 0 ? 0 : _cardMovements[^1].Sequence;
    private SkillProgramStepOutcome AwaitEquipmentDonationMovements(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(frameId)) return SkillProgramStepOutcome.AwaitChild;
        ReplaceRuntimeTop(GetActiveProgramFrame(frameId) with { PendingMovementContinuation = null });
        return SkillProgramStepOutcome.Continue;
    }
    private bool HasPayableAllEquipmentDonation(int ownerSeat, string skillId, string usageId) =>
        IsValidPlayerSeat(ownerSeat) && _players[ownerSeat].IsAlive && GetEquipment(_players[ownerSeat]) is { Count: > 0 } equipment &&
        equipment.All(c => !c.IsGeneralWeapon) &&
        _skillRuntimeState.GetUsage(ownerSeat, skillId, EquipmentDonationUsage(usageId), SkillUsageScope.Game) == 0;
    private bool EquipmentDonationSourceValid(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) &&
        EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p => p.Id == f.SkillId && p.GameplayHash == f.GameplayHash);
    private Dictionary<string,string> EquipmentDonationParameters(ProgramSkillFrame f, string action, string option) => new()
    { ["program-action"] = action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option };
    private void PublishEquipmentDonationPrompt(ProgramSkillFrame f, int chooser, string text, IReadOnlyList<PromptChoice> choices)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), SourceSeat: f.OwnerSeat)
        { PromptId = CreatePromptId(), TargetSeat = chooser, Choices = Array.AsReadOnly(choices.ToArray()),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private SkillProgramStepOutcome RunAllEquipmentDonation(ProgramSkillFrame frame, string usageId)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.EquipmentDonation is null)
        {
            if (frame.TriggerId is not null || frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 ||
                frame.SelectedTargetSeats is not [var recipient] || recipient == frame.OwnerSeat ||
                !IsValidPlayerSeat(recipient) || !_players[recipient].IsAlive || !EquipmentDonationSourceValid(frame) ||
                !HasPayableAllEquipmentDonation(frame.OwnerSeat, frame.SkillId, usageId))
                throw new InvalidOperationException("All-equipment donation requires one current unpaid source and recipient.");
            var cards = GetEquipment(_players[frame.OwnerSeat]).OrderBy(c => c.Id).ToArray();
            var before = EquipmentDonationSequence;
            // One actual source batch, and one actual recipient batch. All entities are paid before any observer resumes.
            MoveCards(cards, CardLocation.Equipment(frame.OwnerSeat), CardLocation.Processing, new(EquipmentDonationMoveReason));
            MoveCards(cards, CardLocation.Processing, CardLocation.Hand(recipient), new(EquipmentDonationMoveReason));
            var after = EquipmentDonationSequence;
            var delivered = cards.Count(c => _cardMovements.Any(m => m.Sequence > before && m.Sequence <= after &&
                m.CardId == c.Id && m.From == CardLocation.Processing && m.To == CardLocation.Hand(recipient) && m.Reason.Value == EquipmentDonationMoveReason));
            var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
            if (!_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat, frame.SkillId, EquipmentDonationUsage(usageId), SkillUsageScope.Game, 1))
                throw new InvalidOperationException("The paid donation lost its exact unconsumed game-wide opportunity.");
            frame = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(frame with { ReexecuteParticipantInstruction = true,
                EquipmentDonation = new(0, source, frame.GameplayHash, usageId, _turnNumber, _currentSeat, recipient,
                    cards.Select(c => c.Id).ToArray(), delivered, before, after, EquipmentDonationStage.PaidMovement, []) });
            foreach (var (card, index) in cards.Select((card, index) => (card, index)))
            {
                var movement = _cardMovements.Single(m => m.Sequence > before && m.Sequence <= after && m.CardId == card.Id &&
                    m.From == CardLocation.Equipment(frame.OwnerSeat) && m.To == CardLocation.Processing && m.Reason.Value == EquipmentDonationMoveReason);
                AdvanceEventRulesAndQueueFact(new EquipmentDonationEntityPaidEvent(frame.Id, source, recipient, card.Id, index,
                    movement.Sequence, _cardMovements.Any(m => m.Sequence > before && m.Sequence <= after && m.CardId == card.Id &&
                        m.From == CardLocation.Processing && m.To == CardLocation.Hand(recipient) && m.Reason.Value == EquipmentDonationMoveReason)));
            }
            AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(frame.OwnerSeat, frame.SkillId, EquipmentDonationUsage(usageId), SkillUsageScope.Game, 1));
            AdvanceEventRulesAndQueueFact(new EquipmentDonationPaidEvent(frame.Id, source, frame.GameplayHash, usageId,
                _turnNumber, _currentSeat, recipient, cards.Length, delivered, before, after));
            return AwaitEquipmentDonationMovements(frame.Id);
        }
        if (!IsValidAllEquipmentDonation(frame)) throw new InvalidOperationException("Donation lost its frozen actual payment or limited usage.");
        var receipt = frame.EquipmentDonation;
        if (receipt.Stage is EquipmentDonationStage.Complete or EquipmentDonationStage.RecoveryIssued)
        { ReplaceRuntimeTop(frame with { EquipmentDonation = receipt with { Stage = EquipmentDonationStage.Complete } }); return SkillProgramStepOutcome.Continue; }
        if (_winner != Winner.None || !EquipmentDonationSourceValid(frame) || !_players[receipt.RecipientSeat].IsAlive)
        { ReplaceRuntimeTop(frame with { EquipmentDonation = receipt with { Stage = EquipmentDonationStage.Complete } }); return SkillProgramStepOutcome.Continue; }
        if (receipt.Stage == EquipmentDonationStage.Damaging)
        {
            var cursor = receipt.DamageCursor;
            while (cursor < receipt.DamageTargets.Count && !_players[receipt.DamageTargets[cursor]].IsAlive) cursor++;
            if (cursor >= receipt.DamageTargets.Count)
            { ReplaceRuntimeTop(frame with { EquipmentDonation = receipt with { Stage = EquipmentDonationStage.Complete } }); return SkillProgramStepOutcome.Continue; }
            var target = receipt.DamageTargets[cursor];
            receipt = receipt with { DamageCursor = cursor + 1 };
            ReplaceRuntimeTop(frame = frame with { EquipmentDonation = receipt, ReexecuteParticipantInstruction = true });
            AdvanceEventRulesAndQueueFact(new EquipmentDonationBenefitIssuedEvent(frame.Id, receipt.Source, receipt.RecipientSeat,
                "damage", receipt.ActualDeliveredCount, target, cursor));
            return BeginProgramSkillDamage(frame, target, 1, new(ProgramParticipantRef.SelectedTarget));
        }
        receipt = receipt with { Stage = receipt.Stage == EquipmentDonationStage.PaidMovement ? EquipmentDonationStage.RecipientChoice : receipt.Stage };
        ReplaceRuntimeTop(frame = frame with { EquipmentDonation = receipt, ReexecuteParticipantInstruction = true });
        PublishEquipmentDonationPrompt(frame, receipt.RecipientSeat, "选择回复持有者体力，或选择攻击范围内的角色造成伤害。", EquipmentDonationChoices(frame));
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> EquipmentDonationChoices(ProgramSkillFrame f)
    {
        var receipt = f.EquipmentDonation!; var choices = new List<PromptChoice>();
        void Add(string option, string label, IReadOnlyList<int> targets) => choices.Add(new(new($"equipment-donation.{f.Id}.{option}"),
            label, [], targets, EquipmentDonationParameters(f, "equipment-donation", option)));
        if (receipt.Stage == EquipmentDonationStage.RecipientChoice)
        { Add("recover", $"令持有者回复 {receipt.ActualDeliveredCount} 点体力。", [f.OwnerSeat]); Add("damage", "选择攻击范围内至多 X 名角色。", []); return choices; }
        if (receipt.Stage != EquipmentDonationStage.SelectingTargets) throw new InvalidOperationException("Donation target prompt has no selecting receipt.");
        if (receipt.DamageTargets.Count < receipt.ActualDeliveredCount)
            foreach (var target in _players.Where(p => p.IsAlive && p.Seat != receipt.RecipientSeat &&
                !receipt.DamageTargets.Contains(p.Seat) && IsWithinAttackRange(receipt.RecipientSeat, p.Seat)).OrderBy(p => p.Seat))
                Add($"target:{target.Seat}", $"选择 {target.Name}。", [target.Seat]);
        Add("confirm", "确认已选目标；可不选择任何角色。", receipt.DamageTargets);
        return choices;
    }
    private void ResolveEquipmentDonationChoice(ProgramSkillFrame f, PromptChoice choice)
    {
        if (!IsValidAllEquipmentDonation(f) || f.EquipmentDonation is not { } receipt ||
            _pendingDecision?.PlayerSeat != receipt.RecipientSeat)
            throw new InvalidOperationException("The recipient choice lost its exact paid donation.");
        choice = EquipmentDonationChoices(f).Single(c => c.Id == choice.Id);
        ClearPendingDecision();
        if (!EquipmentDonationSourceValid(f) || !_players[receipt.RecipientSeat].IsAlive || _winner != Winner.None)
        { ReplaceRuntimeTop(f with { EquipmentDonation = receipt with { Stage = EquipmentDonationStage.Complete }, ReexecuteParticipantInstruction = true }); AdvanceRuntimeProgram(f.Id); return; }
        switch (choice.Parameters["option"])
        {
            case "recover":
                ReplaceRuntimeTop(f with { EquipmentDonation = receipt with { Stage = EquipmentDonationStage.RecoveryIssued }, ReexecuteParticipantInstruction = true });
                AdvanceEventRulesAndQueueFact(new EquipmentDonationBenefitIssuedEvent(f.Id, receipt.Source, receipt.RecipientSeat, "recover", receipt.ActualDeliveredCount, f.OwnerSeat, 0));
                new ProgramSkillHost(this).Recover(f.Id, receipt.RecipientSeat, f.OwnerSeat, receipt.ActualDeliveredCount, null, null);
                AdvanceRuntimeProgram(f.Id); return;
            case "damage": receipt = receipt with { Stage = EquipmentDonationStage.SelectingTargets }; break;
            case "confirm":
                if (receipt.DamageTargets.Any(seat => !_players[seat].IsAlive || !IsWithinAttackRange(receipt.RecipientSeat, seat)))
                    throw new InvalidOperationException("A donation target left the recipient's actual range before confirmation.");
                ReplaceRuntimeTop(f with { EquipmentDonation = receipt with { Stage = EquipmentDonationStage.Damaging }, ReexecuteParticipantInstruction = true });
                AdvanceRuntimeProgram(f.Id); return;
            default:
                if (choice.Targets is not [var seat] || receipt.DamageTargets.Contains(seat) || receipt.DamageTargets.Count >= receipt.ActualDeliveredCount)
                    throw new InvalidOperationException("Donation requires distinct targets within the frozen actual X.");
                receipt = receipt with { DamageTargets = receipt.DamageTargets.Append(seat).ToArray() }; break;
        }
        ReplaceRuntimeTop(f = f with { EquipmentDonation = receipt, ReexecuteParticipantInstruction = true });
        PublishEquipmentDonationPrompt(f, receipt.RecipientSeat, $"已选择 {receipt.DamageTargets.Count} 名角色。", EquipmentDonationChoices(f));
    }
    private PromptChoice SelectAiEquipmentDonation(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.EquipmentDonation!; var view = CreateSnapshot(r.RecipientSeat);
        if (r.Stage == EquipmentDonationStage.RecipientChoice)
        {
            var recover = _aiBrains[r.RecipientSeat].ScoreProgramTarget(view, f.OwnerSeat, new(0,0,0,0,r.ActualDeliveredCount,0,false,false));
            return decision.Choices.Single(c => c.Parameters["option"] == (recover > 0 && _players[f.OwnerSeat].Hp < _players[f.OwnerSeat].MaxHp ? "recover" : "damage"));
        }
        return decision.Choices.Where(c => c.Parameters["option"].StartsWith("target:", StringComparison.Ordinal))
            .Select(c => (Choice:c, Value:_aiBrains[r.RecipientSeat].ScoreProgramTarget(view, c.Targets.Single(), new(0,0,0,0,0,1,false,false))))
            .Where(c => c.Value > 0).OrderByDescending(c => c.Value).ThenBy(c => c.Choice.Targets.Single())
            .Select(c => c.Choice).FirstOrDefault() ?? decision.Choices.Single(c => c.Parameters["option"] == "confirm");
    }
    private sealed partial class ProgramSkillHost : IEquipmentDonationProgramHost
    {
        public SkillProgramStepOutcome DonateAllEquipmentAndOfferRecipientBenefits(ProgramSkillFrame f, string usageId) => engine.RunAllEquipmentDonation(f, usageId);
        public SkillProgramStepOutcome ChooseEquipmentOrDrawAfterOtherActualTurn(ProgramSkillFrame f) => engine.RunActualEndedTurnEquipment(f);
    }
}
