namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool GuanOpeningUseExists(int owner, bool red) => CompleteProgramEventHistory().OfType<GuanOpeningUseEvent>()
        .Any(e => e.OwnerSeat == owner && e.TurnNumber == _turnNumber && (red ? e.IsRed : e.IsBasic));
    private bool GuanFirstBasicAvailable(CharacterState owner, CardKind kind) => HasRuntimeSkill(owner, "mou-ol:duoshou") &&
        CardUseCategoryCatalog.Get(kind) == CardUseCategories.Basic && !GuanOpeningUseExists(owner.Seat, false);
    private bool GuanFirstRedAvailable(CharacterState owner, Suit? suit) => suit is Suit.Heart or Suit.Diamond && HasRuntimeSkill(owner, "mou-ol:duoshou") && !GuanOpeningUseExists(owner.Seat, true);
    private bool GuanFirstRedDistance(CharacterState owner, Card card, long? useId = null) =>
        useId is { } id ? LifecycleCardUse(id)?.GuanFirstRedDistance == true : GuanFirstRedAvailable(owner, EffectiveSuit(owner, card));

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 220)]
    private bool DistancePhysicalSlash220GuanFirstRedDistance(ProgramSlashDistanceQuery q) =>
        GuanFirstRedDistance(q.Actor, q.PhysicalCard!, q.ExistingUseFrameId);

    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 40)]
    private bool DistanceVirtualSlash40GuanFirstRedAvailable(ProgramSlashDistanceQuery q) =>
        GuanFirstRedAvailable(q.Actor, q.PhysicalSuit);

}
