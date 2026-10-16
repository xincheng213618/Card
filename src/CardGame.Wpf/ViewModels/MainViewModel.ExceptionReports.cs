using CardGame.Core;
using CardGame.Wpf.Diagnostics;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IGameExceptionReportStore _exceptionReportStore = null!;
    private Exception? _reportedException;
    private GameEngine? _reportedFaultedGame;
    private string _exceptionReportStatus = string.Empty;

    public string? LastExceptionReportDirectory { get; private set; }

    internal void RecordExceptionReport(Exception exception, string operation, GameCommand? failedCommand = null)
    {
        if (ReferenceEquals(exception, _reportedException) ||
            _game?.IsFaulted == true && ReferenceEquals(_game, _reportedFaultedGame)) return;
        _reportedException = exception;
        if (_game?.IsFaulted == true) _reportedFaultedGame = _game;
        try
        {
            var result = _exceptionReportStore.Write(exception, operation, _game, failedCommand);
            LastExceptionReportDirectory = result.Directory;
            _exceptionReportStatus = result.Saved
                ? $"异常{(result.Error is null ? "日志和复现材料" : "堆栈（部分复现材料未能采集）")}已保存：{result.Directory}"
                : $"异常日志写入失败：{result.Error}";
        }
        catch (Exception error)
        {
            // A diagnostic failure must never replace the original rules failure.
            LastExceptionReportDirectory = null;
            _exceptionReportStatus = $"异常日志写入失败：{error.Message}";
        }
    }
}
