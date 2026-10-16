using System.Reflection;
using System.Collections.Specialized;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class FilteredPresentationChecks
{
    public static void BatchFiltersPreserveEntries()
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore());
        var addLog = typeof(MainViewModel).GetMethod("AddBattleLogEntry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sequenceStart = model.FilteredBattleLog.Select(entry => entry.Sequence).DefaultIfEmpty().Max();
        for (var index = 1; index <= 400; index++)
            addLog.Invoke(model, [new GameLogEntry(sequenceStart + index, 1, index % 2 == 0 ? "Damage" : "CardUsed", $"Public entry {index}", 0, 1)]);
        var original = model.FilteredBattleLog.ToArray();
        var resets = 0;
        var changes = 0;
        model.FilteredBattleLog.CollectionChanged += (_, e) => { changes++; if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        model.SelectBattleLogCategoryCommand.Execute("damage");
        Program.Assert(changes == 1 && resets == 1 && model.FilteredBattleLog.Count == 200 &&
            model.FilteredBattleLog.SequenceEqual(original.Where(entry => entry.Type == "Damage")),
            "Filtering 400 public logs must publish one result while retaining entry identity and order.");
        changes = 0;
        model.SelectBattleLogCategoryCommand.Execute("all");
        Program.Assert(changes == 1 && model.FilteredBattleLog.SequenceEqual(original),
            "Removing a log filter must restore the retained public history with one update.");

        var guideChanges = 0;
        model.FilteredGuideCards.CollectionChanged += (_, _) => guideChanges++;
        model.GuideSearchText = "杀";
        var selected = model.FilteredGuideCards.Last();
        model.SelectedGuideCard = selected;
        var matching = model.FilteredGuideCards.ToArray();
        guideChanges = 0;
        model.GuideSearchText = " 杀 ";
        Program.Assert(guideChanges == 0 && model.FilteredGuideCards.SequenceEqual(matching) &&
            ReferenceEquals(model.SelectedGuideCard, selected),
            "An equivalent search must preserve guide controls and its selected card without a reset.");
        model.GuideSearchText = "__missing_card_guide__";
        Program.Assert(guideChanges == 1 && model.FilteredGuideCards.Count == 0 && model.HasNoGuideCards && model.SelectedGuideCard is null,
            "Empty guide results must publish once and clear the unavailable selection.");
    }
}
