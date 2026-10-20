namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MubingMilitaryCount(int seat) => ProgramEventHistory<MubingCardsAcquiredEvent>().Where(e => e.Source.OwnerSeat == seat).Sum(e => e.MilitaryCardCount);
    private static bool IsMubingMilitaryCard(Card card) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_liao_ol_sp_Mubing_Queries.IsMubingMilitaryCard(card);

    private IReadOnlyList<int> MubingAvailableReveal(ProgramSkillFrame f) =>
        GetProgramCardSet(f, MubingReveal).CardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
    private IReadOnlyList<int> MubingAvailableGifts(ProgramSkillFrame f) =>
        f.SpZhangLiao!.Obtained.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.OwnerSeat)).ToArray();
}
