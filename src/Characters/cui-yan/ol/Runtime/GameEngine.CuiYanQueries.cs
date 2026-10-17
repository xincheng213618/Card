namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsCuiYanNativeResponseForbidden(CharacterState owner,CardKind required,IReadOnlyList<Card> cards)
    {
        if(!HasCuiYanCapability||required!=CardKind.Nullification&&(required!=CardKind.Dodge||!IsProgramResponseCardUse(owner,required)))return false;
        return IsCuiYanNextUseForbidden(owner.Seat,cards.Select(c=>c.Id).ToArray(),cards.Count==1?EffectiveSuit(owner,cards[0]):null);
    }
}
