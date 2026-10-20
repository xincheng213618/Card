using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_pang_tong_friend_FriendYangming
{
    internal static CardLocation FriendYangmingLocation(ProgramSkillFrame frame) => new(CardZoneKind.PublicPersistentPile,
        frame.OwnerSeat, "friend.yangming." + frame.Id.ToString(CultureInfo.InvariantCulture));
}
