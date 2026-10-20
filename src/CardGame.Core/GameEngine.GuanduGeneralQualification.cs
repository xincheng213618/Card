namespace CardGame.Core;

public sealed record GuanduAllCardBanIssuedEvent(long ProgramFrameId, int OwnerSeat, int TargetSeat, int ActualTurn,
    CardUseEffectSource Source) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 44000)]
    private bool AllowsGuanduGeneralActivation(ProgramActivationLegalityContext gate)
    {
        if (!gate.SkillId.StartsWith("guandu:", StringComparison.Ordinal)) return true;
        if (!IsGuanduActivity) return false;
        if (gate.SkillId == "guandu:shicai") return GuanduShicaiReady(gate.OwnerSeat, GetRuntimeSkillInstanceId(gate.Owner, gate.SkillId)) &&
            _cardZones.Count(CardLocation.DrawPile) > 0 && GuanduDiscardable(gate.OwnerSeat, gate.OwnerSeat).Length > 0;
        if (gate.SkillId == "guandu:zezhu") return _players[gate.OwnerSeat].SkillGrants.Grants.Any(g =>
            g.SkillId == gate.SkillId && GuanduGeneralGrantQualified(_players[gate.OwnerSeat], g));
        if (gate.SkillId == "guandu:yuanlve") return GetHand(_players[gate.OwnerSeat]).Any(c => !EquipmentCatalog.IsEquipment(c.Kind));
        if (gate.SkillId == "guandu:moushi") return GetHand(_players[gate.OwnerSeat]).Count > 0;
        return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44000)]
    private bool AllowsGuanduGeneralTrigger(ProgramTriggerLegalityContext gate)
    {
        if (!gate.SkillId.StartsWith("guandu:", StringComparison.Ordinal)) return true;
        if (!IsGuanduActivity) return false;
        var c = gate.Context; var owner = gate.Owner;
        switch (gate.SkillId)
        {
            case "guandu:chenggong":
                return c.CardUse is { } use && LifecycleCardUse(use.ParentCardUseFrameId)?.Action is { } action && action.EffectiveDesignatedTargetSeats.Distinct().Count() >= 2;
            case "guandu:jieliang":
                if (c.Window == SkillProgramTriggerWindow.DrawPhaseStarting) return c.SourceSeat != owner.Seat && GuanduDiscardable(owner.Seat, owner.Seat).Length > 0;
                return c.SourceSeat is { } discarded && ProgramEventHistory<GuanduJieliangPaidEvent>().Any(e => e.OwnerSeat == owner.Seat &&
                    e.TargetSeat == discarded && e.ActualTurn == _turnNumber) && GuanduClaimableDiscards(discarded).Length > 0;
            case "guandu:cangchu":
                return c.Window == SkillProgramTriggerWindow.GameStarting ||
                    c.TargetSeat == owner.Seat && c.Amount > 0 && CurrentDamageAttempt is { } fire && GetDamageNature(fire) == DamageNature.Fire;
            case "guandu:polu":
                return !GetEquipment(owner).Any(card => card.Kind == CardKind.ThunderChariot) &&
                    (c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || c.TargetSeat == owner.Seat && c.Amount > 0);
            case "guandu:thunder-chariot":
                return c.SourceSeat == owner.Seat && c.TargetSeat is { } victim && victim != owner.Seat && _players[victim].IsAlive &&
                    GetEquipment(owner).Any(card => card.Kind == CardKind.ThunderChariot && GuanduEquipmentSkillGrantId(card) == gate.Candidate.SkillInstanceId) &&
                    GuanduChariotDiscardable(owner.Seat, victim).Length > 0;
            case "guandu:fuyuan": return _turnNumber > 0 && _currentSeat != owner.Seat && _players[_currentSeat].IsAlive;
            case "guandu:hengjiang":
                return c.Window == SkillProgramTriggerWindow.AfterDamageApplied ? c.Amount > 0 && _turnNumber > 0 :
                    ProgramEventHistory<GuanduHengjiangPaidEvent>().Any(e => e.OwnerSeat == owner.Seat && e.ActualTurn == _turnNumber) && !GuanduTurnDiscarded(_currentSeat);
            case "guandu:xiying": return GuanduDiscardable(owner.Seat, owner.Seat).Length > 0;
            case "guandu:choulue": return GuanduLastDamageCard(owner.Seat) is not null && _players.Any(p => p.IsAlive && p.Seat != owner.Seat && GuanduOwnedCards(p.Seat).Length > 0);
            case "guandu:sijian":
                return c.MovementBatch is { } batch && batch.SourceCounts.Any(s => s.Location == CardLocation.Hand(owner.Seat) &&
                    s.CountBefore > 0 && s.CountAfter == 0) && _players.Any(p => p.IsAlive && p.Seat != owner.Seat && GuanduOwnedCards(p.Seat, true).Length > 0);
            case "guandu:suishi":
                if (c.TargetSeat is not { } other || other == owner.Seat) return false;
                return c.Window == SkillProgramTriggerWindow.DyingEntering ? ActiveDying is { DamageFrameId: not null } dying &&
                    dying.VictimSeat == other && GetDyingAttack(dying) is { IsSourceLess: false } causal &&
                    GetEffectiveFactionId(_players[causal.SourceSeat]) is { } faction && faction == GetEffectiveFactionId(owner) :
                    GetEffectiveFactionId(_players[other]) is { } deadFaction && deadFaction == GetEffectiveFactionId(owner);
            case "guandu:moushi": return GuanduMoushiQualifies(gate.OwnerSeat, gate.Candidate.SkillInstanceId, c);
            default: return true;
        }
    }
    private void IssueGuanduAllCardBan(ProgramSkillFrame f, int target)
    {
        if (!IsGuanduActivity || f.GuanduGeneral?.Operation != SkillProgramEffectOp.ResolveGuanduXiying ||
            !IsValidPlayerSeat(target) || target == f.OwnerSeat || !_players[target].IsAlive ||
            ProgramEventHistory<GuanduAllCardBanIssuedEvent>().Any(e => e.ProgramFrameId == f.Id && e.TargetSeat == target))
            throw new InvalidOperationException("Xiying requires its exact once-paid other-character decision.");
        AdvanceEventRulesAndQueueFact(new GuanduAllCardBanIssuedEvent(f.Id, f.OwnerSeat, target, _turnNumber, CreateProgramTurnEffectSource(f)));
    }
    private bool IsGuanduCardActionForbidden(int actorSeat, CardKind kind, CardActionType type) => IsGuanduActivity &&
        ProgramEventHistory<GuanduAllCardBanIssuedEvent>().Any(e => e.ActualTurn == _turnNumber && e.TargetSeat == actorSeat);

    private bool TryGetGuanduIncomingHpReplacement(IDamageAttempt attack, out CardUseEffectSource source)
    {
        source = null!;
        if (!IsGuanduActivity || attack.IsSourceLess || attack.SourceSeat == attack.TargetSeat || attack.DamageAmount <= 0 ||
            !IsValidPlayerSeat(attack.SourceSeat) || !IsValidPlayerSeat(attack.TargetSeat) || !_players[attack.TargetSeat].IsAlive ||
            !GuanduSkillEnabled(_players[attack.TargetSeat], "guandu:gangzhi")) return false;
        var grant = GetSkillBindingShard(_players[attack.TargetSeat]).ActiveGrants.First(g => g.SkillId == "guandu:gangzhi");
        source = new("guandu:gangzhi", "incoming-damage", attack.TargetSeat, grant.SkillInstanceId);
        return true;
    }

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 20)]
    private bool GuanduCardUseForbidden(ProgramCardUseQuery query) =>
        IsGuanduCardActionForbidden(query.ActorSeat, query.Kind, query.ActionType);
}
