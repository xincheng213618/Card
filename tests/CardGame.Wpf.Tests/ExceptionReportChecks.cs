using System.IO;
using System.Reflection;
using CardGame.Core;
using CardGame.Wpf.Diagnostics;
using CardGame.Wpf.ViewModels;

internal static class ExceptionReportChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void FaultReportsSurviveNewMatches(string output)
    {
        var root = Path.Combine(output, "exception-fixtures", Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "exceptions");
        var saves = new MemorySaveStore();
        using var vm = new MainViewModel(false, 337, false, saves,
            exceptionReportStore: new FileGameExceptionReportStore(logs));
        Program.StartLordFixture(vm);
        vm.SaveGameCommand.Execute(null);
        Program.Assert(vm.FlushPendingSave(), vm.SaveStatus);
        var saved = saves.Read(CardGame.Wpf.Persistence.GameSaveSlot.Manual);
        var writes = saves.WriteCount;
        var game = Program.Engine(vm);
        var before = SnapshotJson.Serialize(game.State);

        FailNextSelection(vm);
        var directory = vm.LastExceptionReportDirectory!;
        Program.Assert(game.IsFaulted && !vm.CanStepAi && !vm.IsAutoAdvance &&
            vm.ActionHint.Contains(directory) && Directory.Exists(directory), "An actual failed Submit must stop and name its diagnostic directory.");
        var stack = File.ReadAllText(Path.Combine(directory, "exception.txt"));
        Program.Assert(stack.Contains("InvalidOperationException") && stack.Contains("AssertCoreInvariants") &&
            stack.Contains("SubmitCommand"), "The report must retain the original rules stack, not just the recovery message.");
        var checkpoint = GameCheckpointJson.Deserialize(File.ReadAllText(Path.Combine(directory, "checkpoint.json")));
        var failed = CommandJson.Deserialize(File.ReadAllText(Path.Combine(directory, "failed-command.json")));
        Program.Assert(failed is [SelectGeneralCommand command] && command.ExpectedRevision == checkpoint.Revision &&
            checkpoint.Commands.Count == saved.Checkpoint.Commands.Count && checkpoint.ContentHash == saved.Checkpoint.ContentHash,
            "The exact rejected operation and trusted committed prefix must be independently readable.");
        var registry = (ContentRegistry)typeof(MainViewModel).GetField("_contentRegistry", PrivateInstance)!.GetValue(vm)!;
        var restored = GameReplay.Restore(checkpoint, registry);
        Program.Assert(!restored.IsFaulted && SnapshotJson.Serialize(restored.State) == before &&
            restored.Submit(failed.Single()).Accepted, "The report must cold-restore the preceding boundary and preserve an executable failing command.");
        Program.Assert(File.Exists(Path.Combine(directory, "resolution-stack.json")) &&
            File.Exists(Path.Combine(directory, "card-zones.json")) &&
            File.Exists(Path.Combine(directory, "player-snapshot.json")) &&
            !File.Exists(Path.Combine(directory, "capture-errors.txt")), "A normal fault must capture all diagnostic components.");

        vm.SaveGameCommand.Execute(null);
        Program.Assert(saves.WriteCount == writes && ReferenceEquals(saved, saves.Read(CardGame.Wpf.Persistence.GameSaveSlot.Manual)) &&
            Directory.GetDirectories(logs).Length == 1, "A failed save or outer UI catch must not overwrite saves or duplicate the incident.");
        var originalFiles = Directory.GetFiles(directory).ToDictionary(file => Path.GetFileName(file)!, File.ReadAllText);
        Program.StartLordFixture(vm);
        vm.SaveGameCommand.Execute(null);
        Program.Assert(!Program.Engine(vm).IsFaulted && originalFiles.All(file =>
            File.ReadAllText(Path.Combine(directory, file.Key!)) == file.Value), "Starting and saving another match must leave all previous evidence unchanged.");
        FailNextSelection(vm);
        Program.Assert(vm.LastExceptionReportDirectory != directory && Directory.GetDirectories(logs).Length == 2 &&
            originalFiles.All(file => File.ReadAllText(Path.Combine(directory, file.Key!)) == file.Value),
            "A second match failure needs its own immutable report directory.");
    }

    public static void DiagnosticWriteFailurePreservesFaultHandling(string output)
    {
        var root = Path.Combine(output, "exception-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var blocker = Path.Combine(root, "unwritable-logs");
        File.WriteAllText(blocker, "This file prevents creation of a log directory.");
        using var vm = new MainViewModel(false, 337, false, new MemorySaveStore(),
            exceptionReportStore: new FileGameExceptionReportStore(blocker));
        Program.StartLordFixture(vm);
        FailNextSelection(vm);
        Program.Assert(Program.Engine(vm).IsFaulted && !vm.CanStepAi && !vm.IsAutoAdvance &&
            vm.PromptText.Contains("已停止推进") && vm.ActionHint.Contains("异常日志写入失败") &&
            vm.LastExceptionReportDirectory is null && File.ReadAllText(blocker).StartsWith("This file"),
            "An unavailable log directory must explain its write failure while preserving the original stop/recovery behavior.");
    }

    public static void MissingRecoveryPrefixStillPreservesStack(string output)
    {
        var root = Path.Combine(output, "exception-fixtures", Guid.NewGuid().ToString("N"));
        using var vm = new MainViewModel(false, 337, false, new MemorySaveStore(), exceptionReportStore: new IgnoredReports());
        Program.StartLordFixture(vm);
        FailNextSelection(vm);
        var game = Program.Engine(vm);
        var session = typeof(GameEngine).GetField("_commandSession", PrivateInstance)!.GetValue(game)!;
        session.GetType().GetField("LastTrustedCheckpoint")!.SetValue(session, null);
        var exception = new InvalidOperationException("original fixture failure", new IOException("inner fixture failure"));
        var result = new FileGameExceptionReportStore(root).Write(exception, "Missing trusted prefix", game);
        Program.Assert(result.Saved && result.Error?.Contains("checkpoint.json") == true &&
            File.ReadAllText(Path.Combine(result.Directory!, "exception.txt")).Contains("inner fixture failure") &&
            File.ReadAllText(Path.Combine(result.Directory!, "capture-errors.txt")).Contains("No trusted command prefix") &&
            File.Exists(Path.Combine(result.Directory!, "card-zones.json")),
            "One unavailable component must not discard the original exception or other reproducibility evidence.");
        var background = new FileGameExceptionReportStore(root).Write(exception, "Unhandled background error");
        Program.Assert(background.Saved && background.Error is null && background.Directory != result.Directory &&
            File.Exists(Path.Combine(background.Directory!, "manifest.json")), "An unhandled error without a game must still preserve its exception and runtime identity.");
    }

    private static void FailNextSelection(MainViewModel vm)
    {
        var game = Program.Engine(vm);
        var count = typeof(GameEngine).GetField("_initialCardCount", PrivateInstance)!;
        count.SetValue(game, (int)count.GetValue(game)! + 1);
        Program.Assert(vm.IsGeneralSelectionPending && vm.GeneralChoices.Count > 0, "Fixture needs a real human general selection.");
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
    }

    internal sealed class IgnoredReports : IGameExceptionReportStore
    {
        public ExceptionReportResult Write(Exception exception, string operation, GameEngine? game = null,
            GameCommand? failedCommand = null) => new(null, "Fixture does not persist diagnostics.");
    }
}
