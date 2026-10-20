namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int[] KuangshouDamagedSeats(int cutoff)
    {
        var events = CompleteProgramEventHistory().Take(cutoff).ToArray(); var start = Array.FindLastIndex(events, e => e is TurnStartedEvent t && t.TurnNumber == _turnNumber);
        return events.Skip(start + 1).OfType<DamageAppliedEvent>().Where(e => e.Amount > 0).Select(e => e.TargetSeat).Distinct().Order().ToArray();
    }
    private void FreezeKuangshouDiscard(ProgramSkillFrame f)
    {
        var cutoff=CompleteProgramEventHistory().Count(); var seats=KuangshouDamagedSeats(cutoff); var candidates=HanFuDiscardCards(f); var required=Math.Min(seats.Length,candidates.Count);
        ReplaceRuntimeTop(f=f with { HanFu=f.HanFu! with { Stage=HanFuStage.DiscardChoice,HistoryCutoff=cutoff,DamagedSeats=seats,RequiredDiscard=required,DiscardCandidates=candidates,SelectedDiscard=[] } });
        AdvanceEventRulesAndQueueFact(new KuangshouQuotaFrozenEvent(f.Id,cutoff,seats.Length,required));
        if(required==0) { PayHanFuCards(f,[],CardLocation.DiscardPile,KuangshouDiscardReason,HanFuStage.DiscardChildren); ContinueHanFu(f.Id); }
        else PublishHanFuChoice(f);
    }
}
