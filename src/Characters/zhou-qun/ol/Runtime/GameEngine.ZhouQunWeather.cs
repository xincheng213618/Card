namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static (string Id, string Name) ZhouQunWeather(Suit suit) => suit switch
    { Suit.Heart => ("ol:lieshu", "烈暑"), Suit.Diamond => ("ol:ningwu", "凝雾"), Suit.Spade => ("ol:zhouyu", "骤雨"), Suit.Club => ("ol:yanshuang", "严霜"), _ => ("", "无天气") };
    private void ExpireTianhouWeather(ProgramSkillFrame f)
    {
        foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
        var expired = ProgramEventHistory<TianhouWeatherExpiredEvent>().Select(e => e.GrantId).ToHashSet(StringComparer.Ordinal);
        foreach (var old in ProgramEventHistory<TianhouWeatherGrantedEvent>().Where(e => e.Source.OwnerSeat == f.OwnerSeat && !expired.Contains(e.GrantId)).ToArray())
        {
            var grant = _players[old.RecipientSeat].SkillGrants.Grants.SingleOrDefault(g => g.GrantId == old.GrantId);
            if (grant is not null && (grant.SkillId != old.SkillId || grant.SkillInstanceId != old.SkillInstanceId || grant.SourceId != old.GrantSourceId)) throw new InvalidOperationException("Tianhou may expire only its independently issued weather source.");
            if (grant is not null) _players[old.RecipientSeat].SkillGrants.RemoveGrant(old.GrantId);
            AdvanceEventRulesAndQueueFact(new TianhouWeatherExpiredEvent(f.Id, old.FrameId, old.RecipientSeat, old.GrantId));
        }
    }
    private void GrantTianhouWeather(ProgramSkillFrame f, int recipient)
    {
        var r = f.ZhouQun!;
        if (r.Stage != ZhouQunStage.ChooseWeatherRecipient || !_players[f.OwnerSeat].IsAlive || !_players[recipient].IsAlive || !r.Viewed.Contains(r.ChosenCard) || _cardZones.GetLocation(r.ChosenCard) != CardLocation.DrawPile || ProgramEventHistory<TianhouCardShownEvent>().Count(e => e.FrameId == f.Id && e.CardId == r.ChosenCard) != 1) throw new InvalidOperationException("Tianhou lost its publicly chosen exact top card or real living recipient.");
        foreach (var p in _players) _observedSkillGrantRevisions.TryAdd(p.Seat, p.SkillGrants.Revision);
        ReplaceRuntimeTop(f = f with { ZhouQun = r with { Stage = ZhouQunStage.GrantChildren, Peer = recipient } });
        var weather = ZhouQunWeather(GetAdvancedCard(r.ChosenCard).Suit);
        if (weather.Id.Length == 0) return;
        _ = _contentRegistry.GetSkill(weather.Id);
        var source = $"tianhou:{f.OwnerSeat}:{f.Id}:{r.Source.SkillInstanceId}:{recipient}"; var id = source + ":" + weather.Id;
        var instance = GetSkillBindingShard(_players[recipient]).ActiveGrants.Where(g => g.SkillId == weather.Id).OrderBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()?.SkillInstanceId ?? $"tianhou-weather:{recipient}:{weather.Id}";
        _players[recipient].SkillGrants.Grant(new(id, weather.Id, instance, source));
        AdvanceEventRulesAndQueueFact(new TianhouWeatherGrantedEvent(f.Id, r.Source, recipient, weather.Id, id, instance, source));
    }
    private bool CanBeginNingwuJudgment(ProgramSkillFrame f, int target, string reason, string bind, SkillProgramCardSetVisibility visibility) => f.ZhouQun is { Op: SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment, Stage: ZhouQunStage.Judgment } r && target == r.Peer && reason == "ningwu" && bind == "ningwu-result" && visibility == SkillProgramCardSetVisibility.Public;
    private bool IsNingwuJudgment(JudgmentFrame j, ProgramSkillFrame f) => f.ZhouQun is { Op: SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment, Stage: ZhouQunStage.Judgment } r && j.Id == r.JudgmentId && j.ParentFrameId == f.Id && j.TargetSeat == r.Peer && j.SourceSeat == f.OwnerSeat && j.Continuation == JudgmentContinuationKind.ProgramSkill && j.Reason == "ningwu" && j.ProgramResultBind == "ningwu-result" && j.ProgramResultVisibility == SkillProgramCardSetVisibility.Public;
    private bool TryReturnNingwuJudgment(JudgmentFrame j)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == j.ParentFrameId) is not { ZhouQun: { } r } f) return false;
        if (!IsNingwuJudgment(j, f)) throw new InvalidOperationException("Ningwu received a different native judgment return.");
        var result = ProgramEventHistory<JudgmentResolvedEvent>().Single(e => e.ResolutionId == j.Id && e.ParentResolutionId == f.Id && e.TargetSeat == r.Peer && e.Reason == "ningwu");
        ReplaceRuntimeTop(f = f with { ZhouQun = r with { Stage = ZhouQunStage.JudgmentChildren, JudgmentCardId = result.CardId, JudgmentRank = result.Rank } });
        AdvanceEventRulesAndQueueFact(new NingwuJudgmentReturnedEvent(f.Id, j.Id, result.CardId, result.Rank)); BeginZhouQunMovement(ref f, ZhouQunStage.JudgmentChildren);
        if (result.CardId is { } id && _cardZones.GetLocation(id) == CardLocation.Processing)
        {
            var entry = _cardMovements.Last(m => m.CardId == id);
            if (entry.From != CardLocation.Judgment(r.Peer) || entry.To != CardLocation.Processing || entry.Reason.Value != "skill-program.judgment.result") throw new InvalidOperationException("Ningwu cannot clean a foreign claimed judgment entity.");
            MoveCard(GetAdvancedCard(id), CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.JudgmentFinish);
        }
        CompleteZhouQunMovement(f.Id); ContinueZhouQun(f.Id); return true;
    }
    private void ApplyNingwuJudgment(ProgramSkillFrame f)
    {
        var r = f.ZhouQun!;
        if (r.SlashRank is not { } slashRank || r.JudgmentRank is not { } rank || rank <= slashRank || LifecycleCardUse(r.UseId) is not { Action: { } action } use || action.ActionId != r.ActionId) return;
        if (ProgramEventHistory<NingwuUseNullifiedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Ningwu may finish its own native judgment once.");
        AdvanceEventRulesAndQueueFact(new NingwuUseNullifiedEvent(f.Id, use.Id, action.ActionId, r.JudgmentId, r.Peer, r.Targets.Single(), slashRank, rank));
        foreach (var target in action.EffectiveDesignatedTargetSeats)
        {
            MarkCardEffectIneffective(use.Id, target);
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id, action.ActorSeat, target, action.EffectiveKind, CardEffectSkipReason.SkillNullified));
        }
    }
    private bool IsNingwuUseIneffective(CardUseFrame use)
    {
        if (!HasZhouQunCapability) return false;
        foreach (var e in ProgramEventHistory<NingwuUseNullifiedEvent>().Where(e => e.UseId == use.Id))
        {
            if (use.Action?.ActionId != e.ActionId || e.JudgmentRank <= e.SlashRank || !IsSlashCard(use.CardKind) ||
                ProgramEventHistory<NingwuJudgmentIssuedEvent>().Count(i => i.FrameId == e.FrameId && i.JudgmentId == e.JudgmentId && i.UseId == use.Id && i.ActionId == e.ActionId && i.ActorSeat == e.ActorSeat && i.TargetSeat == e.TargetSeat && i.SlashRank == e.SlashRank) != 1 ||
                ProgramEventHistory<NingwuJudgmentReturnedEvent>().Count(i => i.FrameId == e.FrameId && i.JudgmentId == e.JudgmentId && i.Rank == e.JudgmentRank) != 1 ||
                ProgramEventHistory<ZhouQunStartedEvent>().Count(i => i.FrameId == e.FrameId && i.Op == SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment && i.Source.SkillId == "ol:ningwu") != 1 ||
                ProgramEventHistory<JudgmentResolvedEvent>().Count(i => i.ResolutionId == e.JudgmentId && i.ParentResolutionId == e.FrameId && i.TargetSeat == e.ActorSeat && i.Rank == e.JudgmentRank && i.Reason == "ningwu") != 1) throw new InvalidOperationException("Ningwu whole-use nullification lost its exact issued native judgment proof.");
            return true;
        }
        return false;
    }
    private bool TryReturnZhouQunPaidPreparation(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (!HasZhouQunCapability || frame.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || frame.Continuation != ProgramLifecycleContinuation.NormalTurnStart || frame.OwnerSeat != _currentSeat || frame.OwnerSeat != _turnProgression.OwnerSeat || frame.CandidateIndex != frame.Candidates.Count || _resolutionStack.Count != 0 || _winner == Winner.None && _players[frame.OwnerSeat].IsAlive) return false;
        if (!ProgramEventHistory<ZhouQunStartedEvent>().Any(e => e.WindowId == frame.Id && e.Source.OwnerSeat == frame.OwnerSeat && e.ActualTurn == _turnNumber && e.TurnOwner == frame.OwnerSeat && e.Op == SkillProgramEffectOp.ResolveChenshuoRepeatedReveal && frame.Candidates.Any(c => c.OwnerSeat == e.Source.OwnerSeat && c.SkillId == e.Source.SkillId && c.BindingId == e.Source.BindingId && c.SkillInstanceId == e.Source.SkillInstanceId && c.GameplayHash == e.GameplayHash) && ProgramEventHistory<ZhouQunCompletedEvent>().Count(done => done.FrameId == e.FrameId && done.Applied) == 1)) return false;
        if (ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == frame.OwnerSeat)) throw new InvalidOperationException("Chenshuo cannot finalize the same dead actual turn twice.");
        ClearActualDiscardRecoveryPhase(); if (_winner == Winner.None) FinalizeEndTurn(_players[frame.OwnerSeat]); return true;
    }
}
