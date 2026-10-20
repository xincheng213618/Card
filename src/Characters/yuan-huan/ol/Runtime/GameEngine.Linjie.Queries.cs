using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PromptChoice> LinjieCards(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.YuanHuan!.TargetSeat, f.YuanHuan.TargetSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard);
}
