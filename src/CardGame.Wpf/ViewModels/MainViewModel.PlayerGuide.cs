using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GuideSection? _selectedGuideSection;
    private string _guideSearchText = string.Empty;
    private string _selectedGuideCategory = "全部";
    private GuideCardEntry? _selectedGuideCard;
    private string _currentGuideTitle = string.Empty;
    private string _currentGuideBody = string.Empty;
    private string _guideIdentity = string.Empty;
    private IReadOnlyList<GuideCardEntry> _guideCards = [];
    private int _guideRulesVersion;

    public IReadOnlyList<GuideSection> GuideSections { get; } =
    [new("current", "当前操作", "操作说明与出牌建议"),
        new("basics", "阵营与回合", "了解怎样获胜"),
        new("cards", "卡牌图鉴", "查找牌面规则"),
        new("keys", "操作与存档", "快捷键与继续游戏")];
    public IReadOnlyList<string> GuideCategories { get; } = ["全部", "基本牌", "锦囊牌", "装备牌"];
    public ObservableCollection<GuideCardEntry> FilteredGuideCards { get; } = [];
    public ObservableCollection<GuideStep> CurrentGuideSteps { get; } = [];
    public ObservableCollection<GuideHandEntry> GuideHand { get; } = [];
    public bool HasGuideHand => GuideHand.Count > 0;
    public bool HasGuideCard => SelectedGuideCard is not null;
    public bool HasNoGuideCards => FilteredGuideCards.Count == 0;
    public string GuideCardCountText => $"找到 {FilteredGuideCards.Count} 张牌";
    public bool IsGuideCurrent => SelectedGuideSection?.Key == "current";
    public bool IsGuideBasics => SelectedGuideSection?.Key == "basics";
    public bool IsGuideCards => SelectedGuideSection?.Key == "cards";
    public bool IsGuideKeys => SelectedGuideSection?.Key == "keys";
    public bool IsTeamGuide => IsNewGameSetupOpen ? IsTeamModeSelection : IsTeamSnapshot;
    public bool IsNationalGuide => IsNewGameSetupOpen ? IsNationalModeSelection : IsNationalSnapshot;
    public bool IsIdentityGuide => !IsTeamGuide && !IsNationalGuide;
    public string CurrentGuideTitle { get => _currentGuideTitle; private set => SetProperty(ref _currentGuideTitle, value); }
    public string CurrentGuideBody { get => _currentGuideBody; private set => SetProperty(ref _currentGuideBody, value); }
    public string GuideIdentity { get => _guideIdentity; private set => SetProperty(ref _guideIdentity, value); }

    public GuideSection? SelectedGuideSection
    {
        get => _selectedGuideSection;
        set
        {
            if (!SetProperty(ref _selectedGuideSection, value)) return;
            foreach (var name in new[] { nameof(IsGuideCurrent), nameof(IsGuideBasics), nameof(IsGuideCards), nameof(IsGuideKeys) }) RaisePropertyChanged(name);
        }
    }
    public string GuideSearchText { get => _guideSearchText; set { if (SetProperty(ref _guideSearchText, value ?? string.Empty)) FilterGuideCards(); } }
    public string SelectedGuideCategory { get => _selectedGuideCategory; set { if (SetProperty(ref _selectedGuideCategory, value)) FilterGuideCards(); } }
    public GuideCardEntry? SelectedGuideCard
    {
        get => _selectedGuideCard;
        set { if (SetProperty(ref _selectedGuideCard, value)) RaisePropertyChanged(nameof(HasGuideCard)); }
    }
    public ICommand OpenContextGuideCommand { get; private set; } = null!;
    public ICommand OpenCardGuideCommand { get; private set; } = null!;
    public ICommand ClearGuideSearchCommand { get; private set; } = null!;

    private void InitializePlayerGuide()
    {
        RequestPlayAdviceCommand = new RelayCommand(RequestPlayAdvice, () => CanRequestPlayAdvice);
        _guideCards = CardCatalog.ImplementedCards.Select(definition => new GuideCardEntry(definition, GetCardDescription(definition.Kind))).ToArray();
        _guideRulesVersion = GameCheckpoint.CurrentRulesVersion;
        SelectedGuideSection = GuideSections[0];
        FilterGuideCards();
        OpenContextGuideCommand = new RelayCommand(() => { SelectedGuideSection = GuideSections[0]; IsHelpOpen = true; });
        OpenCardGuideCommand = new RelayCommand<GuideHandEntry>(entry =>
        {
            GuideSearchText = string.Empty;
            SelectedGuideCategory = "全部";
            SelectedGuideCard = _guideCards.Single(card => card.Name == entry.Name);
            SelectedGuideSection = GuideSections[2];
        });
        ClearGuideSearchCommand = new RelayCommand(() => { GuideSearchText = string.Empty; SelectedGuideCategory = "全部"; });
    }

    private void FilterGuideCards()
    {
        var term = GuideSearchText.Trim();
        var matches = _guideCards.Where(card => (SelectedGuideCategory == "全部" || card.Category == SelectedGuideCategory) &&
            (term.Length == 0 || card.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || card.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
        FilteredGuideCards.Clear();
        foreach (var card in matches) FilteredGuideCards.Add(card);
        if (SelectedGuideCard is null || !FilteredGuideCards.Contains(SelectedGuideCard)) SelectedGuideCard = FilteredGuideCards.FirstOrDefault();
        RaisePropertyChanged(nameof(GuideCardCountText));
        RaisePropertyChanged(nameof(HasNoGuideCards));
    }

    private void RefreshPlayerGuide()
    {
        RefreshPlayAdvice();
        if (_snapshot is null) return;
        if (_guideRulesVersion != _game.RulesVersion)
        {
            var selectedKind = SelectedGuideCard?.Kind;
            _guideRulesVersion = _game.RulesVersion;
            _guideCards = CardCatalog.ImplementedCards.Select(definition => new GuideCardEntry(definition, GetCardDescription(definition.Kind))).ToArray();
            FilterGuideCards();
            SelectedGuideCard = FilteredGuideCards.FirstOrDefault(card => card.Kind == selectedKind) ?? FilteredGuideCards.FirstOrDefault();
        }
        var human = _snapshot.Players.SingleOrDefault(player => player.IsHuman);
        var prompt = _snapshot.PendingDecision;
        RaisePropertyChanged(nameof(IsTeamGuide));
        RaisePropertyChanged(nameof(IsNationalGuide));
        RaisePropertyChanged(nameof(NationalHealthRuleText));
        RaisePropertyChanged(nameof(NationalSetupText));
        RaisePropertyChanged(nameof(NationalScopeText));
        RaisePropertyChanged(nameof(IsIdentityGuide));
        GuideIdentity = IsNewGameSetupOpen ? $"开局选择：{SelectedTableMode.Name} · {(IsNationalModeSelection ? "系统分配势力" : IsTeamModeSelection ? SelectedStartingTeam.Name : SelectedStartingRole.Name)}" : IdentityObjective;
        string[] steps;
        if (IsNationalGuide && (IsNewGameSetupOpen || IsGeneralSelectionPending))
        {
            CurrentGuideTitle = IsNewGameSetupOpen ? "同势力双将，暗置入场" : "依次选择两名同势力武将";
            CurrentGuideBody = IsNewGameSetupOpen ? NationalSetupText : GeneralSelectionSubtitle;
            steps = IsAmbitiousNationalMode
                ? ["系统分配魏 3 人、蜀 2 人和野心家 1 人；你的候选和暗将只对自己可见。", "依次选择主将、副将；出牌时点击各自的明置按钮，启用对应技能并公开势力。", "消灭其他势力即可获胜；这是六人独立势力试验，完整野心家规则、阵法和明置奖励仍未开放。"]
                : ["系统分配魏或蜀势力，每个势力两人。你的候选和暗将只对自己可见。", "依次选择主将、副将；出牌时点击各自的明置按钮，启用对应技能并公开势力。", "消灭另一势力即可获胜；本模式为四人简化国战，没有野心家、阵法或明置奖励。"];
        }
        else if (IsNewGameSetupOpen && IsTeamModeSelection)
        {
            CurrentGuideTitle = "选择队伍，与同伴并肩作战";
            CurrentGuideBody = TeamModeSetupText;
            steps = ["选择青队或赤队，再点击「开始对局」选择武将。", "牌桌会标明队友与对手；可选目标表示规则允许，并不代表应该攻击。", "先手第一回合少摸一张；本模式没有身份局的击杀奖惩。"];
        }
        else if (IsNewGameSetupOpen)
        {
            CurrentGuideTitle = "先选择战场与身份";
            CurrentGuideBody = "五人桌更容易熟悉节奏；主公先行动，反贼的胜利目标更直接。选定后点击「开始对局」，再选择武将。";
            steps = ["在开局面板选择五人或八人身份场，以及想扮演的身份。", "保留「自己选择回合末弃牌」，练习决定留下哪些牌。", "已有存档时，可直接继续上次对局。关闭指南会回到原来的开局设置。"];
        }
        else if (HasGameOver)
        {
            CurrentGuideTitle = "本局已经结束";
            CurrentGuideBody = GameOverText;
            steps = ["打开战报回顾公开行动与关键伤害。", "点击「再战一局」可重新选择模式与阵营。"];
        }
        else if (human is { IsAlive: false })
        {
            CurrentGuideTitle = "你已阵亡，仍可观战";
            CurrentGuideBody = IsNationalSnapshot ? "双将与势力已公开，同势力角色仍可争取胜利。保持自动推进，观看剩余角色行动直到势力胜负确定。" :
                IsTeamSnapshot ? "队友仍可争取胜利。保持自动推进，观看剩余角色行动直到队伍胜负确定。" : "身份已公开。保持自动推进可观看剩余角色行动，直到阵营胜负确定。";
            steps = ["点击「快速观战」开启快速自动推进；取消「自动推进」可随时暂停。右下角可选择从容、标准或快速节奏。", "关闭指南后，牌局会按你原来的自动推进设置继续；本局结束后按你的阵营显示结果。"];
        }
        else if (IsGeneralSelectionPending)
        {
            CurrentGuideTitle = "选择一位武将出征";
            CurrentGuideBody = GeneralSelectionSubtitle;
            steps = ["查看三张候选的体力和技能说明；候选只对你可见。", "点击一位武将完成选择，其他玩家选完后一起亮相。", IsTeamSnapshot ? "魏、蜀、吴等武将势力不决定队伍；请看青队、赤队和队友标识。" : "武将技能与身份目标是两件事；始终按你的身份决定胜利目标。"];
        }
        else if (IsDiscardSelectionPending)
        {
            CurrentGuideTitle = SelectedDiscardCount == RequiredDiscardCount ? "数量已够，确认弃牌" : "选出本回合要弃置的牌";
            CurrentGuideBody = $"手牌上限为当前体力 {human?.Hp}，本次要弃置 {RequiredDiscardCount} 张，已选 {SelectedDiscardCount} 张。";
            steps = ["点击多张手牌选为弃牌，再次点击可取消；未确认前不会失去牌。", "「推荐弃牌」只替你选中候选，你可以修改。", "选够指定数量后点击确认。弃牌完成后进入下一名角色的回合。"];
        }
        else if (IsActiveSkillSelectionPending && HumanActiveSkillAction is { } skillAction)
        {
            CurrentGuideTitle = CanConfirmActiveSkill ? $"确认发动【{human?.SkillName}】" : $"选择【{human?.SkillName}】的牌和目标";
            CurrentGuideBody = $"{human?.SkillDescription}\n{GetActiveSkillSelectionHint()}";
            var selectedNames = Seats.Where(seat => _selectedActiveSkillTargetSeats.Contains(seat.Seat))
                .Select(seat => seat.IsHuman ? $"你（{seat.GeneralName}）" : $"{seat.Seat + 1} 号位 {seat.GeneralName}").ToArray();
            steps = [$"本次需要选择{BuildActiveSkillRequirement(skillAction)}。再次点击已选牌或目标可以取消。",
                selectedNames.Length == 0 ? "尚未选择目标；不要求目标的技能只需选牌。" : $"已选目标：{string.Join("、", selectedNames)}。",
                $"点击「发动{human?.SkillName}」或按 Enter 才会支付代价并提交技能；Esc 取消整次选择。关闭指南会保留已选牌和目标。"];
        }
        else if (CanEndTurn)
        {
            if (IsMultiTargetCardSelected)
            {
                CurrentGuideTitle = CanConfirmSelected ? "确认铁索连环的目标" : "选择铁索连环的目标";
                CurrentGuideBody = MultiTargetSelectionHint;
                steps = MultiTargetGuideSteps();
            }
            else if (HasPublicTargetChoices || HasTargetCombinationChoices)
            {
                CurrentGuideTitle = HasPublicTargetChoices ? "选择具体的目标牌" : "选择目标组合";
                CurrentGuideBody = ActionHint;
                steps = ["中央列出的每个候选都代表一个完整的合法选择。", "点击中央候选会直接执行这次出牌；如需重选，先按 Esc 或点取消。"];
            }
            else if (HasSelection)
            {
                CurrentGuideTitle = CanConfirmSelected ? "确认这次出牌" : "选择亮起的目标";
                CurrentGuideBody = ActionHint;
                steps = ["亮起的武将才是当前合法目标；再次点击可以取消目标。", "点击金色出牌按钮或按 Enter 确认。存在转化时，按钮会注明当作杀使用。", "确认前可以取消或换另一张手牌。"];
            }
            else
            {
                CurrentGuideTitle = CanUseActiveSkill ? "轮到你行动，可出牌或发动技能" : Hand.Any(card => card.IsPlayable) ? "轮到你出牌" : "当前没有可以主动使用的牌";
                CurrentGuideBody = CanUseActiveSkill ? $"{human?.SkillName}：{human?.SkillDescription}\n点击「{ActiveSkillEntryText}」发动；需要选牌或目标时，选齐后再确认。无须选择的技能点击后立即结算。" : Hand.Any(card => card.IsPlayable) ? "选中一张亮起的手牌，再按提示选择目标或直接确认。你可以连续使用不同的牌，直到决定结束出牌。" : "可以点击「结束出牌」。灰色牌仍可能在响应时发挥作用，不代表它没有用。";
                steps = ["杀通常每回合限一次，目标须在攻击范围内；部分技能和装备会改变限制。", "灰色手牌悬停可查看当前不可用的原因，也可在下面查阅图鉴。", "想保留资源时，可直接结束出牌；超出体力的手牌会进入弃牌选择。"];
            }
        }
        else if (prompt is not null)
        {
            (CurrentGuideTitle, steps) = prompt.Kind switch
            {
                DecisionKind.RespondDodge or DecisionKind.RespondSlash => ("选择手牌并确认响应", new[] { "读清这次需要杀还是闪；中央会列出合法的手牌、技能或装备选项。", "点击中央候选会立即提交响应。选择不响应可能受到伤害。" }),
                DecisionKind.RescueDying => ("决定是否救援濒死角色", new[] { "桃和酒都可用于救援当前濒死角色；酒也会在濒死窗口中恢复 1 点体力。", "选择使用哪张牌或不救援；按当前模式的阵营关系决定希望保护谁。" }),
                DecisionKind.SelectHarvestCard => ("从公开牌中取走一张", new[] { "点击中央的一张公开牌，它会加入你的手牌。", "这是选牌，不需要再选择武将或点击出牌。" }),
                DecisionKind.SelectTargetCard => ("选择一张暗牌位", new[] { "目标手牌的牌面不会展示；每个按钮只代表一个不透明的牌位。", "选择后，拆桥会弃置该牌，顺手会将该牌交给你。" }),
                DecisionKind.Nullification => ("决定是否使用无懈可击", new[] { "看清候选写的是使锦囊失效，还是恢复已被无懈的效果。", "点击使用会消耗所选的无懈；也可跳过并保留手牌。" }),
                DecisionKind.FireAttackReveal => ("展示一张手牌", new[] { "在中央选择要展示的牌；此时只是展示，并非主动弃牌。", "随后由火攻使用者决定是否弃置同花色牌造成伤害。" }),
                DecisionKind.FireAttackDiscard => ("决定是否为火攻弃牌", new[] { "中央列出了可弃置的同花色手牌；点击候选将立即支付代价。", "也可以跳过，保留手牌并结束这次火攻。" }),
                _ => ("处理当前技能选择", new[] { "先读中央说明，再选择发动、支付代价或跳过。", "中央的每个按钮都是完整选择，点击后立即执行。" })
            };
            CurrentGuideBody = prompt.Prompt;
            if (IsHandResponsePending)
                steps = [HandResponseHint, "点选手牌不会立即消耗；再次点击或按 Esc 可取消，关闭指南会保留选择。", .. steps];
        }
        else
        {
            CurrentGuideTitle = IsAutoAdvance ? "其他角色正在行动" : "自动推进已暂停";
            CurrentGuideBody = "游戏会在需要你出牌、响应或弃牌时停下来。打开指南期间不会自动推进。";
            steps = [IsAutoAdvance ? "关闭指南后，其他角色会继续行动。" : "关闭指南后，可开启右下角「自动推进」继续牌局。", "可随时在右下角调整声音与动画，或通过战报查看刚才发生的事。"];
        }
        CurrentGuideSteps.Clear();
        for (var index = 0; index < steps.Length; index++) CurrentGuideSteps.Add(new GuideStep((index + 1).ToString("00"), steps[index]));
        GuideHand.Clear();
        if (!IsNewGameSetupOpen && !IsGeneralSelectionPending)
            foreach (var card in Hand) GuideHand.Add(new GuideHandEntry(card.Name, card.AvailabilityText));
        RaisePropertyChanged(nameof(HasGuideHand));
    }

    private string GetCardDescription(CardKind kind)
    {
        var rulesVersion = _game is null ? GameCheckpoint.CurrentRulesVersion : _game.RulesVersion;
        if (kind == CardKind.IronChain && rulesVersion >= 6)
            return "选择一到两名存活角色（可包含自己），横置或重置；连环角色受到火焰或雷电伤害时会传导同量伤害。也可重铸：不选目标，将此牌置入弃牌堆并摸一张牌；重铸不属于使用锦囊，不进入无懈响应。";
        return rulesVersion >= 4
            ? kind switch
            {
                CardKind.Dismantlement => "选择一名其他角色；从其手牌的不透明牌位中选择一张弃置，或弃置其一张公开装备/判定区牌。",
                CardKind.Snatch => "选择一名距离为 1 的其他角色；从其手牌的不透明牌位中选择一张获得，或获得其一张公开装备/判定区牌。",
                _ => CardCatalog.Get(kind).Description
            }
            : CardCatalog.Get(kind).Description;
    }
}

public sealed record GuideSection(string Key, string Title, string Subtitle);
public sealed record GuideStep(string Number, string Text);
public sealed record GuideHandEntry(string Name, string Message);
public sealed class GuideCardEntry(CardDefinition definition, string? effectiveDescription = null)
{
    public CardKind Kind => definition.Kind;
    public string Name => definition.DisplayName;
    public string Description => effectiveDescription ?? definition.Description;
    public string Category => definition.CategoryName;
    public string Timing => Kind switch
    {
        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash => "出牌阶段进攻 · 决斗或南蛮入侵中响应杀",
        CardKind.Dodge => "受到杀或万箭齐发时响应",
        CardKind.Nullification => "锦囊响应窗口",
        CardKind.Peach => "出牌阶段回复自己 · 濒死时救援",
        CardKind.Alcohol => "出牌阶段饮酒 · 濒死时救援",
        _ => "自己的出牌阶段"
    };
    public string VerticalName => string.Join("\n", Name.ToCharArray());
    public double NameSize => Name.Length > 3 ? 24 : Name.Length > 1 ? 32 : 48;
}
