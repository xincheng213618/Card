using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly Lazy<ContentRegistry> WushuangRegistry = new(StandardWushuangContentRegistry.Create);
    private IGameSaveStore? _wushuangSaveStore;
    private bool _isWushuangShopOpen;
    private bool _isWushuangShopPreview;
    private bool _hasWushuangSave;
    private bool _hasWushuangManualSave;
    private string? _wushuangPendingOfferId;
    private long _wushuangVisibleWindowId;
    private string _wushuangShopMessage = "";

    public bool IsWushuangTable => _game?.IsWushuangMode == true;
    public bool IsWushuangModeSelection => SelectedTableMode?.ModeId == WushuangMode.ModeId;
    public bool IsWushuangGuide => IsNewGameSetupOpen ? IsWushuangModeSelection : IsWushuangTable;
    public string WushuangRulesText => "单机 1v1，对手由 AI 操控。开局免费从三个战法中选一个；战法不占额外技能槽。单局限时15分钟，暂停自动推进、浏览指南和商店仍然计时。\n\n虎符是本模式货币：每个自然回合开始双方各获得1枚，新一轮开始另获得1枚。没有经验和商店升级。每个自然回合结束后进入购买阶段，商品包含技能、实体卡牌和战法。回合中仅可预览下次商品；购买、刷新、锁定只在购买阶段开放。优先消耗免费刷新次数，否则花1虎符刷新；锁定后保留整页，已购买的商品不会补货。购买阶段20秒，结束后继续牌局。\n\n初始有两个额外技能槽，战法可增加槽位；满槽后选择替换。持有一忘皆空时也可替换武将初始技能。击败AI获胜；时间到按本地体力比例、体力值、手牌数依次计分。独立存档保存在本机。";
    public bool IsWushuangShopOpen { get => _isWushuangShopOpen; private set => SetProperty(ref _isWushuangShopOpen, value); }
    public bool HasWushuangSave { get => _hasWushuangSave; private set => SetProperty(ref _hasWushuangSave, value); }
    public bool HasWushuangManualSave { get => _hasWushuangManualSave; private set => SetProperty(ref _hasWushuangManualSave, value); }
    public string WushuangShopMessage { get => _wushuangShopMessage; private set => SetProperty(ref _wushuangShopMessage, value); }
    public string WushuangClockText => FormatWushuangTime(WushuangVisibleRemainingMilliseconds);
    public string WushuangClockCaption => $"总局时限 · 剩余 {WushuangClockText}";
    public string WushuangProgressText => _game?.CreateWushuangSnapshot(0) is { } state
        ? $"虎符 {state.Tokens}" : "虎符商店 · 15 分钟 1v1";
    public bool IsWushuangOpening => _game?.CreateWushuangSnapshot(0)?.Phase == WushuangShopPhase.OpeningTactic;
    public int WushuangOfferColumns => _game?.CreateWushuangSnapshot(0) is { } state ? Math.Max(1, state.Offers.Count + state.PurchasedOffers.Count) : 4;
    public bool IsWushuangShopInteractive => _game?.CreateWushuangSnapshot(0)?.CanShop == true;
    public int WushuangShopWindowSeconds => IsWushuangOpening ? 15 : 20;
    public string WushuangCloseText => IsWushuangShopInteractive ? "结束" : "返回";
    public System.Windows.Media.ImageSource WushuangShopTitleImage => WushuangArt.Get(IsWushuangOpening ? "rg1v1_ttl1.png" : "rg1v1_ttl0.png");
    public System.Windows.Media.ImageSource WushuangLockImage => WushuangArt.Get(_game?.CreateWushuangSnapshot(0)?.IsLocked == true ? "roguelike1v1_shop_lock.png" : "roguelike1v1_shop_unlock.png");
    public string WushuangLockText => _game?.CreateWushuangSnapshot(0)?.IsLocked == true ? "已锁定，下次保留本页" : "锁定本页";
    public string WushuangPromptText => !IsWushuangShopInteractive ? "暂时无法购买商品" : IsWushuangOpening ? "请选择一个初始奖励" : "请选择购买商品";
    public string WushuangTacticsText => _game?.CreateWushuangSnapshot(0) is { } state ? string.Join("　", state.TacticIds.Select(id => WushuangTactics.ById[id].Name)) : string.Empty;
    public string WushuangSkillSlotsText => _game?.CreateWushuangSnapshot(0) is { } state
        ? $"额外技能 {state.ExtraSkillIds.Count}/{state.ExtraSkillCapacity}" : "额外技能 0/2";
    public string WushuangUpgradeText => _game?.CreateWushuangSnapshot(0) is { } state
        ? state.LevelUpCost > 0 ? $"提升等级 · {state.LevelUpCost} 虎符" : "等级已满" : "提升等级";
    public string WushuangRerollText => _game?.CreateWushuangSnapshot(0) is { } state
        ? state.FreeRerolls > 0 ? $"免费刷新 · 剩余 {state.FreeRerolls} 次" : $"刷新商店 · {state.RerollCost} 虎符" : "刷新商店";
    public string WushuangRerollPriceText => _game?.CreateWushuangSnapshot(0)?.RerollCost == 0 ? "免费" : "1";
    public bool WushuangRerollShowsToken => _game?.CreateWushuangSnapshot(0)?.RerollCost != 0;
    public bool IsWushuangReplacementPending => _wushuangPendingOfferId is not null;
    public string WushuangReplacementTitle => _wushuangPendingOfferId is { } id && WushuangOffers.FirstOrDefault(offer => offer.Id == id) is { } offer
        ? $"习得「{offer.Name}」：选择技能槽或要替换的技能" : string.Empty;
    public ObservableCollection<WushuangShopOfferViewModel> WushuangOffers { get; } = [];
    public ObservableCollection<WushuangSkillSlotViewModel> WushuangSkillSlots { get; } = [];
    public ICommand OpenWushuangModeCommand { get; private set; } = null!;
    public ICommand ContinueWushuangCommand { get; private set; } = null!;
    public ICommand LoadWushuangManualGameCommand { get; private set; } = null!;
    public ICommand OpenWushuangShopCommand { get; private set; } = null!;
    public ICommand CloseWushuangShopCommand { get; private set; } = null!;
    public ICommand BuyWushuangOfferCommand { get; private set; } = null!;
    public ICommand ReplaceWushuangSkillCommand { get; private set; } = null!;
    public ICommand CancelWushuangReplacementCommand { get; private set; } = null!;
    public ICommand RerollWushuangShopCommand { get; private set; } = null!;
    public ICommand UpgradeWushuangShopCommand { get; private set; } = null!;
    public ICommand ToggleWushuangShopLockCommand { get; private set; } = null!;

    private void InitializeWushuang(IGameSaveStore? store)
    {
        _wushuangSaveStore = store ?? new FileGameSaveStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardGame", "Saves", "Wushuang"));
        OpenWushuangModeCommand = new RelayCommand(() =>
        {
            if (IsTutorialActive) return;
            OpenGameSetup();
            SelectedModeCategory = ModeCategories[0];
            IsLobbyConfigurationOpen = true;
            SelectedTableMode = TableModes.Single(mode => mode.ModeId == WushuangMode.ModeId);
            IsModeConfigurationOpen = true;
        }, () => !IsTutorialActive);
        ContinueWushuangCommand = new RelayCommand(() => LoadGame(GameSaveSlot.Automatic, wushuang: true), () => HasWushuangSave && !IsTutorialActive);
        LoadWushuangManualGameCommand = new RelayCommand(() => LoadGame(GameSaveSlot.Manual, wushuang: true), () => HasWushuangManualSave && !IsTutorialActive);
        OpenWushuangShopCommand = new RelayCommand(() =>
        {
            FlushWushuangClock();
            if (!IsWushuangTable || !_game.IsWushuangClockRunning) return;
            _wushuangPendingOfferId = null;
            _isWushuangShopPreview = !IsWushuangShopInteractive;
            IsWushuangShopOpen = true;
            RefreshWushuangPresentation();
        }, () => IsWushuangTable && _game.IsWushuangClockRunning && !IsTutorialActive);
        CloseWushuangShopCommand = new RelayCommand(FinishWushuangShopping, () => !IsWushuangOpening);
        CancelWushuangReplacementCommand = new RelayCommand(() =>
        { _wushuangPendingOfferId = null; RefreshWushuangPresentation(); });
        BuyWushuangOfferCommand = new RelayCommand<WushuangShopOfferViewModel>(BuyWushuangOffer, IsCurrentWushuangOffer);
        ReplaceWushuangSkillCommand = new RelayCommand<WushuangSkillSlotViewModel>(ReplaceWushuangSkill,
            slot => slot is not null && WushuangSkillSlots.Contains(slot) && IsWushuangReplacementPending);
        RerollWushuangShopCommand = new RelayCommand(() => ApplyWushuangShopCommand(new RerollWushuangShopCommand(0, _game.Revision)),
            () => _game?.CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping } state && state.Tokens >= state.RerollCost);
        ToggleWushuangShopLockCommand = new RelayCommand(() => ApplyWushuangShopCommand(new ToggleWushuangShopLockCommand(0, _game.Revision)),
            () => _game?.CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping });
        UpgradeWushuangShopCommand = new RelayCommand(() => ApplyWushuangShopCommand(new UpgradeWushuangShopCommand(0, _game.Revision)),
            () => _game?.CreateWushuangSnapshot(0) is { CanShop: true } state && state.LevelUpCost > 0 && state.Tokens >= state.LevelUpCost);
        RefreshWushuangSaveSlot();
        InitializeWushuangClock();
    }

    private void CloseWushuangShop()
    {
        _isWushuangShopPreview = false;
        IsWushuangShopOpen = false;
        _wushuangPendingOfferId = null;
        RaisePropertyChanged(nameof(IsWushuangReplacementPending));
    }

    private void FinishWushuangShopping()
    {
        if (_game?.CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping })
            ApplyWushuangShopCommand(new FinishWushuangShopCommand(0, _game.Revision));
        else if (!IsWushuangOpening) CloseWushuangShop();
    }

    private bool IsCurrentWushuangOffer(WushuangShopOfferViewModel? offer) => offer is not null &&
        !_game.IsFaulted && WushuangOffers.Contains(offer) && offer.IsEnabled && offer.Revision == _game.Revision;

    private void BuyWushuangOffer(WushuangShopOfferViewModel offer)
    {
        if (!IsCurrentWushuangOffer(offer)) return;
        if (offer.SkillId is not null && _game.CreateWushuangSnapshot(0) is { } state &&
            (state.ExtraSkillIds.Count >= state.ExtraSkillCapacity || state.ReplaceableOriginalSkillIds.Count > 0))
        {
            _wushuangPendingOfferId = offer.Id;
            RefreshWushuangPresentation();
            return;
        }
        ApplyWushuangShopCommand(new BuyWushuangOfferCommand(0, offer.Id, null, offer.Revision));
    }

    private void ReplaceWushuangSkill(WushuangSkillSlotViewModel slot)
    {
        if (!WushuangSkillSlots.Contains(slot) || _wushuangPendingOfferId is not { } id ||
            WushuangOffers.FirstOrDefault(offer => offer.Id == id && offer.IsEnabled) is not { } current) return;
        ApplyWushuangShopCommand(new BuyWushuangOfferCommand(0, current.Id, slot.SkillId, current.Revision));
    }

    private void ApplyWushuangShopCommand(GameCommand command) => ExecuteSafely(() =>
    {
        if (IsTutorialActive || !IsWushuangTable || _game.IsFaulted) return;
        var result = SubmitCommand(command);
        if (result.Accepted) _wushuangPendingOfferId = null;
        WushuangShopMessage = result.Accepted ? "" : result.Error?.Message ?? "当前无法购买。";
        if (result.Accepted && _game.CreateWushuangSnapshot(0)?.CanShop != true) CloseWushuangShop();
        RefreshCommandResult(result.State);
        RefreshWushuangPresentation();
    });

    private void RefreshWushuangPresentation()
    {
        var state = _game?.CreateWushuangSnapshot(0);
        WushuangOffers.Clear();
        WushuangSkillSlots.Clear();
        if (state is not null)
        {
            foreach (var offer in state.Offers.Concat(state.PurchasedOffers).OrderBy(offer => int.Parse(offer.Id.Split(':')[3])))
                WushuangOffers.Add(new(offer.Id, offer.Name, offer.Description, offer.Cost, offer.SkillId,
                    state.CanShop && state.Tokens >= offer.Cost && !state.PurchasedOffers.Any(p => p.Id == offer.Id), state.Revision)
                { Quality = offer.Quality, Kind = offer.Kind, TacticId = offer.TacticId, CardKind = offer.CardKind, CardOfferId = offer.CardOfferId,
                    IsPurchased = state.PurchasedOffers.Any(p => p.Id == offer.Id), IsOpening = state.Phase == WushuangShopPhase.OpeningTactic,
                    IsAffordable = state.Tokens >= offer.Cost, IsPurchasingPhase = state.CanShop,
                    RequiresReplacement = offer.SkillId is not null && state.ExtraSkillIds.Count >= state.ExtraSkillCapacity && !state.PurchasedOffers.Any(p => p.Id == offer.Id) });
            for (var slot = 0; slot < state.ExtraSkillCapacity; slot++)
            {
                var id = state.ExtraSkillIds.ElementAtOrDefault(slot);
                var skill = id is not null ? _game!.ContentRegistry?.GetSkill(id) : null;
                WushuangSkillSlots.Add(new(id, id is null ? $"额外技能槽 {slot + 1} · 空" : skill?.Name ?? id,
                    skill?.Description ?? $"在虎符商店习得技能。当前可用额外技能槽：{state.ExtraSkillCapacity}。"));
            }
        }
        if (state is not null && IsWushuangReplacementPending)
            foreach (var id in state.ReplaceableOriginalSkillIds)
            {
                var skill = _game!.ContentRegistry!.GetSkill(id);
                WushuangSkillSlots.Add(new(id, $"武将技能 · {skill.Name}", skill.Description));
            }
        if (state is { CanShop: true })
        {
            _isWushuangShopPreview = false;
            if (_wushuangVisibleWindowId != state.WindowId)
            {
                _wushuangVisibleWindowId = state.WindowId;
                _wushuangShopOpenedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            IsWushuangShopOpen = true;
        }
        else if (!_isWushuangShopPreview) CloseWushuangShop();
        if (state is null || state.Phase == WushuangShopPhase.Closed)
        { _wushuangVisibleWindowId = 0; if (!_isWushuangShopPreview) CloseWushuangShop(); }
        if (_wushuangPendingOfferId is { } selected && !WushuangOffers.Any(offer => offer.Id == selected && offer.IsEnabled))
            _wushuangPendingOfferId = null;
        if (!IsWushuangTable || _game?.IsWushuangClockRunning != true) CloseWushuangShop();
        foreach (var name in new[] { nameof(IsWushuangTable), nameof(IsWushuangModeSelection), nameof(WushuangProgressText),
            nameof(WushuangSkillSlotsText), nameof(WushuangUpgradeText), nameof(WushuangRerollText), nameof(WushuangRerollPriceText), nameof(WushuangRerollShowsToken),
            nameof(IsWushuangReplacementPending), nameof(WushuangReplacementTitle), nameof(WushuangClockText), nameof(WushuangClockCaption), nameof(WushuangShopTimeoutText) })
            RaisePropertyChanged(name);
        foreach (var name in new[] { nameof(IsWushuangOpening), nameof(WushuangOfferColumns), nameof(WushuangShopTitleImage), nameof(WushuangLockImage), nameof(WushuangLockText), nameof(WushuangPromptText), nameof(WushuangTacticsText), nameof(WushuangShopSecondsRemaining) }) RaisePropertyChanged(name);
        foreach (var name in new[] { nameof(IsWushuangShopInteractive), nameof(WushuangShopWindowSeconds), nameof(WushuangCloseText) }) RaisePropertyChanged(name);
        foreach (var command in new[] { OpenWushuangShopCommand, BuyWushuangOfferCommand, ReplaceWushuangSkillCommand,
            RerollWushuangShopCommand, UpgradeWushuangShopCommand, ToggleWushuangShopLockCommand, CloseWushuangShopCommand })
            (command as RelayCommand)?.NotifyCanExecuteChanged();
        (ReplaceWushuangSkillCommand as RelayCommand<WushuangSkillSlotViewModel>)?.NotifyCanExecuteChanged();
        (BuyWushuangOfferCommand as RelayCommand<WushuangShopOfferViewModel>)?.NotifyCanExecuteChanged();
    }

    private void RefreshWushuangSaveSlot()
    {
        try
        {
            HasWushuangSave = _wushuangSaveStore?.GetSavedAt(GameSaveSlot.Automatic) is not null;
            HasWushuangManualSave = _wushuangSaveStore?.GetSavedAt(GameSaveSlot.Manual) is not null;
        }
        catch (Exception error) when (IsSaveException(error)) { HasWushuangSave = HasWushuangManualSave = false; }
        (ContinueWushuangCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (LoadWushuangManualGameCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}

public sealed record WushuangShopOfferViewModel(string Id, string Name, string Description, int Cost, string? SkillId, bool IsEnabled, long Revision)
{
    public WushuangOfferKind Kind { get; init; }
    public int Quality { get; init; } = 1;
    public int? TacticId { get; init; }
    public CardKind? CardKind { get; init; }
    public int? CardOfferId { get; init; }
    public bool IsEquipment => CardKind is { } kind && EquipmentCatalog.IsEquipment(kind);
    public bool IsPurchased { get; init; }
    public bool IsOpening { get; init; }
    public bool IsAffordable { get; init; }
    public bool IsPurchasingPhase { get; init; }
    public bool RequiresReplacement { get; init; }
    public bool IsReadOnly => !IsPurchasingPhase && !IsOpening;
    public bool ShowsPriceIcon => !IsOpening && Cost > 0;
    public bool IsCard => CardKind is not null;
    public bool IsTactic => Kind == WushuangOfferKind.Tactic;
    public bool IsSkill => SkillId is not null;
    public string PriceText => IsOpening ? "选择" : Cost == 0 ? "免费" : Cost.ToString();
    public string PriceForeground => IsAffordable || IsOpening ? "#F7E8A8" : "#F04436";
    public System.Windows.Media.ImageSource PurchaseButtonImage => WushuangArt.Get(Cost == 0 && !IsOpening ? "rg1v1_btn_green0.png" : "rg1v1_btn_red0.png");
    public string TypeText => IsTactic ? "战法" : IsSkill ? "技能" : IsEquipment ? "装备" : "卡牌";
    public string DetailText => IsEquipment ? "立即装备\n离开装备区后销毁" : IsCard ? "获得卡牌" : Description;
    public string DisplayName => Name.Replace("传习 · ", "", StringComparison.Ordinal);
    public System.Windows.Media.ImageSource FrameImage => WushuangArt.Get($"rogue_award_quality_bg_{Math.Clamp(Quality, 1, 4)}.png");
    public System.Windows.Media.ImageSource NameImage => WushuangArt.Get($"rogue_award_name_bg_{Math.Clamp(Quality, 1, 4)}.png");
    public System.Windows.Media.ImageSource IconFrameImage => WushuangArt.Get($"rogue_zhanfa_bg_{Math.Clamp(Quality, 1, 4)}.png");
    public System.Windows.Media.ImageSource TypeImage => WushuangArt.Get($"rogue_award_type_icon_{(IsTactic ? 2 : IsSkill ? 3 : 4)}.png");
    public System.Windows.Media.ImageSource? Artwork => IsCard ? CardArt.Get(CardKind) : TacticId is { } id ? ShanheArt.Get(id) : null;
}
public sealed record WushuangSkillSlotViewModel(string? SkillId, string Name, string Description);
