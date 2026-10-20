namespace CardGame.Core;

public sealed partial class GameEngine
{
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
}
