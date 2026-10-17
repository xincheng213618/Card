using System.Windows.Input;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private ShanheCampaignSession? _dismissedShanheSettlement;
    private string? _shanheSettlementError;
    public bool IsShanheSettlementOpen => _shanheSession is { } session &&
        session.Snapshot.Phase is ShanheCampaignPhase.Won or ShanheCampaignPhase.Lost && !ReferenceEquals(session, _dismissedShanheSettlement);
    public bool HasShanheBookReward => _shanheSession is { } session && ShanheCodexSettlement.ConfirmedBookAmount(session, _shanheRunDifficulty) > 0;
    public bool IsShanheSettlementPending => HasShanheBookReward && _shanheSession is { } session && !ShanheCodexSettlement.HasClaimed(_shanheCodexProfile, session);
    public string ShanheSettlementTitle => _shanheSession?.Snapshot.Phase == ShanheCampaignPhase.Won ? "胜 利" : "征 程 结 束";
    public string ShanheSettlementSubtitle => _shanheSession?.Snapshot.EndingText ?? "";
    public string ShanheSettlementBookText => _shanheSession is { } session ? $"× {ShanheCodexSettlement.ConfirmedBookAmount(session, _shanheRunDifficulty)}" : "";
    public string ShanheSettlementReceipt => _shanheSettlementError ?? (_shanheSession is { } session && ShanheCodexSettlement.HasClaimed(_shanheCodexProfile, session)
        ? "图册已存入山河册 · 可用于研习初始战法、技能与装备"
        : HasShanheBookReward ? "图册等待保存，请点击重试结算。" : "本次征程暂无图册结算，可返回山河册重新整备。");
    private RelayCommand? _closeShanheSettlementCommand;
    public ICommand CloseShanheSettlementCommand => _closeShanheSettlementCommand ??= new RelayCommand(() =>
    {
        _dismissedShanheSettlement = _shanheSession;
        RefreshShanheSettlement();
    }, () => IsShanheSettlementOpen);
    private RelayCommand? _retryShanheSettlementCommand;
    public ICommand RetryShanheSettlementCommand => _retryShanheSettlementCommand ??= new RelayCommand(() => SaveShanheProgress(),
        () => IsShanheSettlementPending);

    // Save the expedition journal first. A cold restore can safely retry an unpaid receipt.
    private void SettleShanheCodexReward()
    {
        if (_shanheSession is not { } session || _shanheCodexReadFailed) return;
        _shanheSettlementError = null;
        if (ShanheCodexSettlement.TryPrepare(_shanheCodexProfile, session, _shanheRunDifficulty, out var next) &&
            !SaveCodexProfile(next, "通关图册已入册，当前共有 " + next.Books + " 册。"))
            _shanheSettlementError = "图册尚未保存，本次未入账；可重试结算。";
        RefreshShanheSettlement();
    }
    private void RefreshShanheSettlement()
    {
        foreach (var property in new[] { nameof(IsShanheSettlementOpen), nameof(IsShanheSettlementPending), nameof(HasShanheBookReward), nameof(ShanheSettlementTitle),
            nameof(ShanheSettlementSubtitle), nameof(ShanheSettlementBookText), nameof(ShanheSettlementReceipt) }) RaisePropertyChanged(property);
        _closeShanheSettlementCommand?.NotifyCanExecuteChanged();
        _retryShanheSettlementCommand?.NotifyCanExecuteChanged();
    }
}
