namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RevealRecordedSuitSlash(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized,
                CardUse: { } context, TargetSeat: { } target } ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == context.ParentCardUseFrameId) is not { Action: { } action } use ||
            action.ActionId != context.CardActionId || action.Type != CardActionType.Use || !IsSlashCard(action.EffectiveKind) ||
            action.ActorSeat != frame.OwnerSeat || action.EffectiveDesignatedTargetSeats.Count != 1 ||
            action.EffectiveDesignatedTargetSeats[0] != target)
            throw new InvalidOperationException("Recorded-suit Slash lost its exact sole target and owning action.");
        var mask = ProgramRecordedSuitSlash.Suits.Where(suit => GetProgramBooleanState(frame,
            ProgramRecordedSuitSlash.StateId(effect.StateId!, suit))).Aggregate(0, (bits, suit) => bits | ProgramRecordedSuitSlash.Bit(suit));
        var count = Math.Max(0, System.Numerics.BitOperations.PopCount((uint)mask) - 1);
        RevealProgramTopCards(frame.Id, frame.OwnerSeat, count, null, effect.ResultBind!, SkillProgramCardSetVisibility.Public);
        var bonus = GetProgramCardSet(GetActiveProgramFrame(frame.Id), effect.ResultBind!).CardIds.Count(id =>
            (ProgramRecordedSuitSlash.Bit(_cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == id).Suit) & mask) != 0);
        var receipt = new ProgramRecordedSuitSlashReceipt(action.ActionId, use.Id, frame.Id, action.ActorSeat, target,
            frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, frame.TriggerId!, frame.InstructionIndex - 1,
            effect.StateId!, mask, bonus);
        use = _resolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id);
        ReplaceRuntimeFrame(use.Id, use with { RecordedSuitSlashReceipts = Array.AsReadOnly((use.RecordedSuitSlashReceipts ?? []).Append(receipt).ToArray()) });
        AdvanceEventRulesAndQueueFact(new ProgramRecordedSuitSlashIssuedEvent(receipt));
        AddLog("SkillTriggered", $"{_players[frame.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(frame.SkillId).Name}】，亮出 {count} 张牌，此【杀】伤害+{bonus}，目标不能使用已记录花色的牌响应。", frame.OwnerSeat);
    }

    private IEnumerable<ProgramRecordedSuitSlashReceipt> RecordedSuitSlashReceipts(long frameId, int target) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == frameId) is { Action: { } action, RecordedSuitSlashReceipts: { } receipts }
            ? receipts.Where(r => r.CardUseFrameId == frameId && r.ActionId == action.ActionId && r.ActorSeat == action.ActorSeat &&
                r.TargetSeat == target && action.EffectiveDesignatedTargetSeats.Contains(target)) : [];

    private int RecordedSuitSlashDamage(IDamageAttempt attack) =>
        !attack.IsChainPropagation && attack is CardAttackHandle && IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash)
            ? RecordedSuitSlashReceipts(attack.ResolutionId, attack.TargetSeat).Sum(r => r.DamageBonus) : 0;

    private bool IsRecordedSuitResponseAllowed(CharacterState responder, IReadOnlyList<Card> cards, CardKind kind)
    {
        if (kind != CardKind.Dodge || ActiveCardAttack is not { } attack ||
            !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) ||
            responder.Seat != attack.TargetSeat && ActiveFactionDefense?.CurrentCandidateSeat != responder.Seat) return true;
        var bit = ProgramRecordedSuitSlash.Bit(PhysicalGroupSuit(responder, cards));
        return !RecordedSuitSlashReceipts(attack.ResolutionId, attack.TargetSeat).Any(r => (r.SuitMask & bit) != 0);
    }

    private void ClearRecordedSuitSlash(CardUseFrame use)
    {
        foreach (var receipt in use.RecordedSuitSlashReceipts ?? [])
            foreach (var suit in ProgramRecordedSuitSlash.Suits)
                SetProgramBooleanState(receipt.ActorSeat, receipt.SkillId, receipt.SkillInstanceId,
                    ProgramRecordedSuitSlash.StateId(receipt.StatePrefix, suit), false);
    }

    private void AssertRecordedSuitSlashReceipts()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>())
        foreach (var receipt in use.RecordedSuitSlashReceipts ?? [])
        {
            var trigger = _contentRegistry.Skills.GetValueOrDefault(receipt.SkillId)?.Program?.Triggers.SingleOrDefault(t => t.Id == receipt.TriggerId);
            if (use.Action is not { } action || action.Type != CardActionType.Use || !IsSlashCard(action.EffectiveKind) ||
                receipt.CardUseFrameId != use.Id || receipt.ActionId != action.ActionId || !IsValidPlayerSeat(receipt.ActorSeat) ||
                !IsValidPlayerSeat(receipt.TargetSeat) || receipt.SuitMask is < 0 or > 15 ||
                receipt.DamageBonus < 0 || receipt.DamageBonus > Math.Max(0, System.Numerics.BitOperations.PopCount((uint)receipt.SuitMask) - 1) ||
                receipt.ProducerFrameId <= 0 || string.IsNullOrEmpty(receipt.SkillInstanceId) ||
                _contentRegistry.GetSkill(receipt.SkillId).Program!.GameplayHash != receipt.GameplayHash || trigger is null ||
                receipt.EffectIndex < 0 || receipt.EffectIndex >= trigger.Effects.Count ||
                trigger.Effects[receipt.EffectIndex].Op != SkillProgramEffectOp.RevealRecordedSuitSlash ||
                trigger.Effects[receipt.EffectIndex].StateId != receipt.StatePrefix)
                throw new InvalidOperationException("Recorded-suit Slash receipt lost its owning action or producer.");
        }
    }

    private sealed partial class ProgramSkillHost : IRecordedSuitSlashProgramHost
    {
        public void RevealRecordedSuitSlash(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.RevealRecordedSuitSlash(frame, effect);
    }
}
