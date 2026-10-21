namespace CardGame.Core;

public sealed record WushuangJiushiDamageFaceEvent(long DamageFrameId, int OwnerSeat, bool WasFaceDown) : IGameEvent;
public sealed record WushuangDiscardPhaseBoundaryEvent(int TurnNumber, int ActorSeat, long MovementSequence) : IGameEvent;

public sealed partial class GameEngine
{
    private const string BJiushi = "wushuang:skill-3269", BGuzheng = "wushuang:skill-3139", BFuqi = "wushuang:skill-3100",
        BDuwu = "wushuang:skill-3062", BGuanchao = "wushuang:skill-3076", BLonghun = "wushuang:skill-3023";

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 61201)]
    private void ObserveWushuangBAudit(IGameEvent payload)
    {
        if (payload is DamageRequestedEvent request && GetSkillBindingShard(_players[request.TargetSeat]).HasSkill(BJiushi))
            AdvanceEventRulesAndQueueFact(new WushuangJiushiDamageFaceEvent(request.ResolutionId, request.TargetSeat, _players[request.TargetSeat].IsFaceDown));
        if (payload is PhaseChangedEvent { Phase: TurnPhase.Discard } phase && _contentRegistry.Skills.ContainsKey(BGuzheng))
            AdvanceEventRulesAndQueueFact(new WushuangDiscardPhaseBoundaryEvent(_turnNumber, phase.ActorSeat, _movementSequence));
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 61201)]
    private bool CanTriggerWushuangBAudit(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (c.SkillId == BJiushi && trigger.Id == "flip-back-after-damage")
            return _players[c.OwnerSeat].IsFaceDown && ProgramEventHistory<WushuangJiushiDamageFaceEvent>().Any(e =>
                e.OwnerSeat == c.OwnerSeat && e.DamageFrameId == context.DamageFrameId && e.WasFaceDown);
        if (c.SkillId == BLonghun) return context.CardUse is { } use && ProgramEventHistory<WushuangCardUseNameRecordedEvent>().Any(e => e.CardActionId == use.CardActionId);
        if (c.SkillId == BGuzheng) return context.SourceSeat is { } seat && WushuangGuzhengCards(seat).Count > 0;
        return true;
    }

    private bool WushuangFuqiCannotRespond(int source, int responder) => source != responder && IsValidPlayerSeat(source) &&
        IsValidPlayerSeat(responder) && _players[source].IsAlive && _players[responder].IsAlive &&
        GetSkillBindingShard(_players[source]).HasSkill(BFuqi) && GetCombatDistance(source, responder) == 1;

    private bool WushuangFuqiNullificationBanned(int responder)
    {
        if (ActiveNullificationWindow is not { } window) return false;
        var source = window.ChainDepth == 0 ? window.SourceSeat : ProgramEventHistory<NullificationRespondedEvent>()
            .LastOrDefault(e => e.ResolutionId == window.ParentFrameId && e.ChainDepth == window.ChainDepth)?.ResponderSeat ?? -1;
        return WushuangFuqiCannotRespond(source, responder);
    }
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 61201)]
    private bool WushuangFuqiNativeResponseForbidden(ProgramNativeResponseQuery query) =>
        !query.DyingUse && query.Kind == CardKind.Nullification && WushuangFuqiNullificationBanned(query.Provider.Seat);

    private IReadOnlyList<int> WushuangGuzhengCards(int phaseOwner)
    {
        var boundary = ProgramEventHistory<WushuangDiscardPhaseBoundaryEvent>().LastOrDefault(e => e.TurnNumber == _turnNumber && e.ActorSeat == phaseOwner);
        if (boundary is null) return [];
        return _cardMovements.Where(m => m.Sequence > boundary.MovementSequence && m.TurnNumber == _turnNumber &&
            m.To == CardLocation.DiscardPile && GetProgramDiscardSource(m) is not null && _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile)
            .Select(m => m.CardId).Distinct().Order().ToArray();
    }

    private void AddWushuangGuzhengChoices(ProgramSkillFrame f, ICollection<PromptChoice> choices)
    {
        var r = f.WushuangB!;
        if (r.Stage == "guzheng-return")
            foreach (var id in WushuangGuzhengCards(r.TargetSeat))
                choices.Add(BChoice(f, r.Stage, id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"将【{GetAdvancedCard(id).DisplayName}】交还给{_players[r.TargetSeat].Name}", [id]));
        if (r.Stage == "guzheng-claim")
        {
            choices.Add(BChoice(f, r.Stage, "claim", "获得剩余弃置牌"));
            choices.Add(BChoice(f, r.Stage, "pass", "不获得剩余弃置牌"));
        }
    }

    private bool AnswerWushuangGuzheng(ProgramSkillFrame f, PromptChoice choice)
    {
        var r = f.WushuangB!;
        if (r.Stage is not ("guzheng-return" or "guzheng-claim")) return false;
        if (r.Stage == "guzheng-return")
        {
            var card = GetAdvancedCard(choice.Cards.Single());
            ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "guzheng-returned", Cards = WushuangGuzhengCards(r.TargetSeat).Where(id => id != card.Id).ToArray() } });
            MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(r.TargetSeat), new("skill-program.wushuang-guzheng.return"));
            AdvanceRuntimeProgram(f.Id); return true;
        }
        ReplaceRuntimeTop(f = f with { WushuangB = r with { Stage = "complete" } });
        if (choice.Parameters["value"] == "claim")
            foreach (var id in r.Cards.Where(id => _cardZones.GetLocation(id) == CardLocation.DiscardPile))
                MoveCard(GetAdvancedCard(id), CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new("skill-program.wushuang-guzheng.claim"));
        AdvanceRuntimeProgram(f.Id); return true;
    }

    private (bool Ascending, int PhaseEventIndex)? WushuangGuanchaoPattern(int owner)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        var turn = Array.FindLastIndex(facts, e => e is TurnStartedEvent);
        var choice = facts.Skip(turn + 1).OfType<ProgramGuanchaoPatternChosenEvent>().LastOrDefault(e => e.OwnerSeat == owner && e.SkillId == BGuanchao);
        var phase = Array.FindLastIndex(facts, e => e is PhaseChangedEvent);
        return choice is null || owner != _currentSeat ? null : (choice.Ascending, phase);
    }
}
