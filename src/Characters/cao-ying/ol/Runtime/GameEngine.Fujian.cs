using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 30)]
    private IEnumerable<CardSnapshot> GetCaoYingPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewer && f.CaoYing?.Stage == CaoYingStage.Inspection).SelectMany(f => f.CaoYing!.Cards);
}
