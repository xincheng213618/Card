using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<CardKind> MouLixianAddedNames(int owner) => Array.AsReadOnly(ProgramEventHistory<MouLixianNameAddedEvent>().Where(e => e.OwnerSeat == owner).Select(e => e.OutputKind).Distinct().ToArray());
    private IReadOnlyList<CardKind> MouLixianUsedNames(int owner) => Array.AsReadOnly(ProgramEventHistory<MouLixianNameUsedEvent>().Where(e => e.OwnerSeat == owner).Select(e => e.OutputKind).Distinct().ToArray());
    private bool MouLixianPreparationUnlocked(int owner) => MouLixianAdditionalNames.All(MouLixianAddedNames(owner).Contains);
    private bool MouLixianEndingUnlocked(int owner) => MouLixianUsedNames(owner).Count >= 3;
    private IReadOnlyList<CardKind> MouLixianNames(int owner) => Array.AsReadOnly(new[] { CardKind.DrawTwo }.Concat(MouLixianAddedNames(owner)).Distinct().ToArray());
}
