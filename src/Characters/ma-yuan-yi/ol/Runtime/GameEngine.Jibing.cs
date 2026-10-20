using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<Card> JibingPileCards(CharacterState owner)=>Array.AsReadOnly(PublicPileSources(owner.Seat).Where(s=>s.SkillId=="ol:jibing"&&HasRuntimeSkillInstance(owner,s.SkillId,s.SkillInstanceId)).SelectMany(PublicPileCards).ToArray());
}
