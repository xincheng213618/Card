namespace CardGame.Core;

public sealed partial class GameEngine
{

    private sealed record JiezhenConversionState(int OwnerSeat, IReadOnlyList<string> ReplacedSkillIds,
        string BazhenGrantId);

    // 解阵 replace: the selected counterpart's replaceable skills (every enabled
    // content skill that is not 锁定技/限定技/主公技) are disabled and 八阵 is granted.
    private SkillProgramStepOutcome JiezhenProgramReplaceSkills(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var targetSeat] || targetSeat == active.OwnerSeat)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "解阵没有可选的其他角色，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var target = _players[targetSeat];
        if (!target.IsAlive || _jiezhenConversions.ContainsKey(targetSeat))
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id),
                _jiezhenConversions.ContainsKey(targetSeat) ? "该角色的技能已被“解阵”替换，剩余结算取消。" : "解阵的目标角色已失效，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var replaceable = EnabledContentSkillIds(target)
            .Where(skillId => (_contentRegistry!.GetSkill(skillId).Tags &
                (SkillTag.Locked | SkillTag.Limited | SkillTag.Lord)) == 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var skillId in replaceable)
            foreach (var grant in target.SkillGrants.Grants
                         .Where(grant => grant.IsEnabled && grant.SkillId == skillId).ToArray())
                target.SkillGrants.SetEnabled(grant.GrantId, false);
        var sourceSkillId = $"jiezhen:{targetSeat}";
        var bazhenGrantId = $"acquired:{sourceSkillId}:classic:bazhen";
        AcquireRuntimeSkills(target, sourceSkillId, ["classic:bazhen"]);
        _jiezhenConversions[targetSeat] = new(active.OwnerSeat, replaceable, bazhenGrantId);
        AddLog("SkillTriggered",
            replaceable.Length == 0
                ? $"{target.Name} 的技能已被替换为“八阵”。"
                : $"{target.Name} 的【{string.Join("】【", replaceable.Select(GetSkillDisplayName))}】被替换为“八阵”。",
            active.OwnerSeat, targetSeat);
        AdvanceEventRulesAndQueueFact(new ProgramJiezhenConvertedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, targetSeat, replaceable));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    // 解阵 restore: the owner's next turn start or the converted character's
    // 八卦阵 judgment restores the replaced skills, removes the granted 八阵 and
    // then takes one card from that character's zones.
    private SkillProgramStepOutcome JiezhenProgramRestoreSkills(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var window = frame.WindowContext?.Window;
        int? targetSeat = window switch
        {
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => _jiezhenConversions
                .Where(pair => pair.Value.OwnerSeat == active.OwnerSeat)
                .Select(pair => (int?)pair.Key).FirstOrDefault(),
            SkillProgramTriggerWindow.JudgmentFinalized when frame.WindowContext?.Judgment is { } judgment &&
                _jiezhenConversions.TryGetValue(judgment.SubjectSeat, out var converted) &&
                converted.OwnerSeat == active.OwnerSeat => judgment.SubjectSeat,
            _ => null
        };
        if (targetSeat is not { } seat) return SkillProgramStepOutcome.Continue;
        var state = _jiezhenConversions[seat];
        var target = _players[seat];
        foreach (var skillId in state.ReplacedSkillIds)
            foreach (var grant in target.SkillGrants.Grants
                         .Where(grant => !grant.IsEnabled && grant.SkillId == skillId &&
                             grant.GrantId != state.BazhenGrantId).ToArray())
                target.SkillGrants.SetEnabled(grant.GrantId, true);
        if (target.SkillGrants.Grants.Any(grant => grant.GrantId == state.BazhenGrantId))
            target.SkillGrants.RemoveGrant(state.BazhenGrantId);
        _jiezhenConversions.Remove(seat);
        var takenCardId = JiezhenTakeOneCardFromZones(active, seat);
        AddLog("SkillTriggered",
            $"{target.Name} 失去“八阵”并获得原技能。", active.OwnerSeat, seat);
        AdvanceEventRulesAndQueueFact(new ProgramJiezhenRestoredEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, seat, state.ReplacedSkillIds, takenCardId));
        if (takenCardId >= 0 && !TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return takenCardId >= 0 ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    // The gainer draws one uniform-random card across the character's hand,
    // equipment and judgment zones (the established multi-zone 口径).
    private int JiezhenTakeOneCardFromZones(ProgramSkillFrame frame, int targetSeat)
    {
        var target = _players[targetSeat];
        if (!target.IsAlive || !_players[frame.OwnerSeat].IsAlive) return -1;
        var candidates = GetHand(target).Select(card => (Card: card, Location: CardLocation.Hand(target.Seat)))
            .Concat(GetEquipment(target).Select(card => (Card: card, Location: CardLocation.Equipment(target.Seat))))
            .Concat(GetJudgment(target).Select(card => (Card: card, Location: CardLocation.Judgment(target.Seat))))
            .OrderBy(entry => entry.Card.Id).ToArray();
        if (candidates.Length == 0) return -1;
        var pick = candidates[_random.Next(candidates.Length)];
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.jiezhen-take");
        MoveCard(pick.Card, pick.Location, CardLocation.Processing, reason,
            movement => RecordBianFuRenOwnedTransferProducer(movement,frame.OwnerSeat,frame.Id));
        if (!MoveProcessingCardUnlessDestroyed(pick.Card, CardLocation.Hand(frame.OwnerSeat), reason)) return -1;
        AddLog("CardGained",
            $"{_players[frame.OwnerSeat].Name} 获得 {target.Name} 区域里的【{pick.Card.DisplayName}】。",
            frame.OwnerSeat, targetSeat);
        return pick.Card.Id;
    }
}
