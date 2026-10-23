namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MobileShiDaozhuanSkill = "mobile:shi-daozhuan";

    // 道转 uses are recorded per accepted conversion. The same facts drive the
    // per-round-per-name ledger, the shared one-per-turn ceiling, the round
    // disable after an own-turn use and audits.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 67211)]
    private void RecordMobileShiDaozhuanUse(CardActionAcceptedEvent accepted)
    {
        var action = accepted.Action;
        if (action.ConversionChain.Count == 0) return;
        var source = action.ConversionChain.FirstOrDefault(item => item.SkillId == MobileShiDaozhuanSkill);
        if (source is null) return;
        var owner = _players[source.OwnerSeat];
        var program = _contentRegistry.GetSkill(source.SkillId).Program;
        if (!owner.IsAlive || program is null || program.ViewAs.All(rule => rule.Id != source.BindingId)) return;
        if (ProgramEventHistory<MobileShiDaozhuanUseRecordedEvent>().Any(item => item.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new MobileShiDaozhuanUseRecordedEvent(source.OwnerSeat,
            MobileShiYuJiComposition.Name(action.EffectiveKind), action.ActionId, _turnNumber, _roundNumber,
            action.ActorSeat == source.OwnerSeat && source.OwnerSeat == _currentSeat));
    }

    [ProgramCardLegality(ProgramCardLegalityStage.ConversionUsageBan, 67211)]
    private bool MobileShiDaozhuanConversionUnavailable(ProgramConfiguredViewAsQuery query)
    {
        if (query.Instance.SkillId != MobileShiDaozhuanSkill) return false;
        var owner = query.Owner;
        var uses = CompleteProgramEventHistory().OfType<MobileShiDaozhuanUseRecordedEvent>()
            .Where(item => item.OwnerSeat == owner.Seat).ToArray();
        // 每回合限一次（跨牌名）。
        if (uses.Any(item => item.TurnNumber == _turnNumber)) return true;
        // 每轮每基本牌名限一次（普通杀与属性杀同名）。
        var name = MobileShiYuJiComposition.Name(query.Rule.OutputKind);
        if (uses.Any(item => item.RoundNumber == _roundNumber && item.Name == name)) return true;
        // 本轮失效：本回合自己发动过道转后，直至下轮开始不可再用。
        return uses.Any(item => item.RoundNumber == _roundNumber && item.OwnerIsTurnPlayer);
    }
}
