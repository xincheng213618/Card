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

    public IReadOnlyList<GuideSection> GuideSections { get; } =
    [new("current", "当前操作", "操作说明与出牌建议"),
        new("basics", "阵营与回合", "了解怎样获胜"),
        new("cards", "卡牌图鉴", "查找牌面规则"),
        new("keys", "操作与存档", "快捷键与继续游戏")];
    public IReadOnlyList<string> GuideCategories { get; } = ["全部", "基本牌", "锦囊牌", "装备牌"];
    public ObservableRangeCollection<GuideCardEntry> FilteredGuideCards { get; } = [];
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
    public bool IsTeamGuide => (IsNewGameSetupOpen ? IsTeamModeSelection : IsTeamSnapshot) && !IsDragonBoatGuide && !IsGuanduGuide && !IsWushuangGuide;
    public bool IsNationalGuide => IsNewGameSetupOpen ? IsNationalModeSelection : IsNationalSnapshot;
    public bool IsIdentityGuide => !IsTeamGuide && !IsNationalGuide && !IsWenheGuide && !IsDragonBoatGuide && !IsGuanduGuide && !IsWushuangGuide;
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
        _guideCards = CardCatalog.ImplementedCards
            .Select(definition => new GuideCardEntry(
                definition,
                GetCardDescription(definition.Kind),
                GetCardTiming(definition.Kind)))
            .ToArray();
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
        var selected = SelectedGuideCard;
        var term = GuideSearchText.Trim();
        var matches = _guideCards.Where(card => (SelectedGuideCategory == "全部" || card.Category == SelectedGuideCategory) &&
            (term.Length == 0 || card.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || card.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
        FilteredGuideCards.ReplaceAll(matches);
        SelectedGuideCard = selected is not null && FilteredGuideCards.Contains(selected)
            ? selected : FilteredGuideCards.FirstOrDefault();
        RaisePropertyChanged(nameof(GuideCardCountText));
        RaisePropertyChanged(nameof(HasNoGuideCards));
    }

    private void RefreshPlayerGuide()
    {
        RefreshPlayAdvice();
        if (_snapshot is null) return;
        var human = _snapshot.Players.SingleOrDefault(player => player.IsHuman);
        var prompt = _snapshot.PendingDecision;
        RaisePropertyChanged(nameof(IsTeamGuide));
        RaisePropertyChanged(nameof(IsNationalGuide));
        RaisePropertyChanged(nameof(NationalHealthRuleText));
        RaisePropertyChanged(nameof(NationalSetupText));
        RaisePropertyChanged(nameof(NationalScopeText));
        RaisePropertyChanged(nameof(IsIdentityGuide));
        RaisePropertyChanged(nameof(IsWenheGuide));
        RaisePropertyChanged(nameof(IsDragonBoatGuide));
        RaisePropertyChanged(nameof(IsGuanduGuide));
        RaisePropertyChanged(nameof(IsWushuangGuide));
        GuideIdentity = IsNewGameSetupOpen
            ? $"开局选择：{SelectedTableMode.Name} · {(IsWushuangModeSelection ? "单机 1v1 AI" : IsWenheActivityModeSelection ? "各自求生" : IsNationalModeSelection ? "系统分配势力" : IsTeamModeSelection ? SelectedStartingTeam.Name : SelectedStartingRole.Name)}" +
              (IsClassicIdentityModeSelection && SelectedDeck is not null ? $" · {SelectedDeck.Name}" : string.Empty)
            : IdentityObjective;
        string[] steps;
        if (IsWushuangGuide && (IsNewGameSetupOpen || IsGeneralSelectionPending))
        {
            CurrentGuideTitle = "单骑无双 · 限时单机决战";
            CurrentGuideBody = WushuangRulesText;
            steps = ["选择一名武将，独自迎战 AI。", "在牌桌左上查看总倒计时，点击「虎符商店 / 升级」进行整备。", "原武将技能不占额外技能槽；第三个额外技能必须替换已有的一个。"];
        }
        else if (IsGuanduGuide && (IsNewGameSetupOpen || IsGeneralSelectionPending))
        {
            CurrentGuideTitle = "官渡之战 · 两军主帅对决";
            CurrentGuideBody = IsNewGameSetupOpen ? TeamModeSetupText : GeneralSelectionSubtitle;
            steps = GuanduGuideSteps;
        }
        else if (IsDragonBoatGuide && (IsNewGameSetupOpen || IsGeneralSelectionPending))
        {
            CurrentGuideTitle = "龙舟争渡 · 八人四船竞分";
            CurrentGuideBody = IsNewGameSetupOpen ? TeamModeSetupText : GeneralSelectionSubtitle;
            steps = DragonBoatActivityGuideSteps;
        }
        else if (IsWenheGuide && (IsNewGameSetupOpen || IsGeneralSelectionPending))
        {
            CurrentGuideTitle = "文和乱武 · 八人各自求生";
            CurrentGuideBody = IsNewGameSetupOpen ? TeamModeSetupText : GeneralSelectionSubtitle;
            steps = WenheActivityGuideSteps;
        }
        else if (IsNationalGuide && (IsNewGameSetupOpen || IsGeneralSelectionPending))
        {
            CurrentGuideTitle = IsNewGameSetupOpen ? "同势力双将，暗置入场" : "依次选择两名同势力武将";
            CurrentGuideBody = IsNewGameSetupOpen ? NationalSetupText : GeneralSelectionSubtitle;
            steps = IsAmbitiousNationalMode
                ? ["系统分配魏 3 人、蜀 2 人和野心家 1 人；你的候选和暗将只对自己可见。", "依次选择主将、副将；出牌时点击各自的明置按钮，启用对应技能并公开势力。", "消灭其他势力即可获胜；这是六人独立势力试验，完整野心家规则、阵法和明置奖励仍未开放。"]
                : ["系统分配魏或蜀势力，每个势力两人。你的候选和暗将只对自己可见。", "依次选择主将、副将；出牌时点击各自的明置按钮，启用对应技能并公开势力。", "消灭另一势力即可获胜；本模式为四人简化国战，没有野心家、阵法或明置奖励。"];
        }
        else if (IsNewGameSetupOpen && IsHellActivityModeSelection)
        {
            CurrentGuideTitle = "十殿阎罗 · 五阶联合军";
            CurrentGuideBody = TeamModeSetupText;
            steps = HellActivityGuideSteps;
        }
        else if (IsNewGameSetupOpen && IsNianActivityModeSelection)
        {
            CurrentGuideTitle = "挑战年兽 · 五阶联合军";
            CurrentGuideBody = TeamModeSetupText;
            steps = NianActivityGuideSteps;
        }
        else if (IsNewGameSetupOpen && IsShenwuActivityModeSelection)
        {
            CurrentGuideTitle = "神武再世 · 五阶联合军";
            CurrentGuideBody = TeamModeSetupText;
            steps = ShenwuActivityGuideSteps;
        }
        else if (IsNewGameSetupOpen && IsBossActivityModeSelection)
        {
            CurrentGuideTitle = BossActivityGuideTitle;
            CurrentGuideBody = TeamModeSetupText;
            steps = BossActivityGuideSteps;
        }
        else if (IsNewGameSetupOpen && IsActivityModeSelection)
        {
            CurrentGuideTitle = "剑阁决战 · 五阶出征";
            CurrentGuideBody = TeamModeSetupText;
            steps = ["选择普通、进阶或困难，再选择魏军或蜀军；搭档由人机操控。", "普通为英灵一阶、器械一阶；进阶为英灵二至四阶、器械二阶；困难为英灵三至五阶、器械三阶。", "万夫莫开只加成普通武将席位；击败两名敌方英灵即可获胜。"];
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
            CurrentGuideBody = IsClassicIdentityModeSelection
                ? $"五人桌更容易熟悉节奏；当前选择{SelectedDeck?.Name}。标准牌堆节奏更集中，军争牌堆加入属性伤害、铁索、酒和扩展装备。"
                : "五人桌更容易熟悉节奏；主公先行动，反贼的胜利目标更直接。选定后点击「开始对局」，再选择武将。";
            steps = IsClassicIdentityModeSelection
                ? ["选择五人或八人经典身份场、牌堆扩展和想扮演的身份。", "标准 108 张不含军争牌；军争 160 张是标准、EX 与军争篇合并牌堆。", "选定后点击「开始对局」，牌堆选择会随存档与回放保存。"]
                : ["在开局面板选择五人或八人身份场，以及想扮演的身份。", "保留「自己选择回合末弃牌」，练习决定留下哪些牌。", "已有存档时，可直接继续上次对局。关闭指南会回到原来的开局设置。"];
        }
        else if (HasGameOver)
        {
            CurrentGuideTitle = "本局已经结束";
            CurrentGuideBody = GameOverText;
            steps = ["打开战报回顾公开行动与关键伤害。", "点击「再战一局」可重新选择模式与阵营。"];
        }
        else if (human is { IsAlive: false } && prompt?.PlayerSeat != human.Seat)
        {
            CurrentGuideTitle = "你已阵亡，仍可观战";
            CurrentGuideBody = IsWenheActivitySnapshot ? WenheSpectatorHint : IsNianActivitySnapshot ? $"存活队友可以在出牌阶段发动「复活队友」；全队共享三次机会。保持自动推进让人机队友继续行动，暂停自动推进仍会消耗挑战时间。当前{NianActivityStageText}。" : IsNationalSnapshot ? "双将与势力已公开，同势力角色仍可争取胜利。保持自动推进，观看剩余角色行动直到势力胜负确定。" :
                IsTeamSnapshot ? "队友仍可争取胜利。保持自动推进，观看剩余角色行动直到队伍胜负确定。" : "身份已公开。保持自动推进可观看剩余角色行动，直到阵营胜负确定。";
            steps = ["点击「快速观战」开启快速自动推进；取消「自动推进」可随时暂停。右下角可选择从容、标准或快速节奏。", IsWenheActivitySnapshot
                ? "你的名次已经确定。继续观战至最后存活者产生后，战报会按个人名次排列。"
                : "关闭指南后，牌局会按你原来的自动推进设置继续；本局结束后按你的阵营显示结果。"];
        }
        else if (CanRerollOpeningHand)
        {
            CurrentGuideTitle = "确认起手牌再开局";
            CurrentGuideBody = OpeningDealSummary;
            steps = ["直接在底部手牌区查看起手牌，牌面仅对你可见。", "点击「随机」将自己的牌放回牌堆，洗牌后抽取同样数量。", "点击「换牌」，点选一张手牌，搜索牌名后点击「换入手牌」，例如选择丈八蛇矛。自选会优先从牌堆换入；若该牌已发给其他角色，则交换双方的这张起手牌，所有角色手牌数量不变。", "点击「确定」开始对局，此后不能再随机或换牌。"];
        }
        else if (IsGeneralSelectionPending)
        {
            CurrentGuideTitle = "选择一位武将出征";
            CurrentGuideBody = GeneralSelectionSubtitle;
            steps = IsHellActivitySnapshot ? HellActivityGuideSteps : IsNianActivitySnapshot ? NianActivityGuideSteps : IsShenwuActivitySnapshot ? ShenwuActivityGuideSteps : ["查看候选的体力和技能说明，也可点击「自选武将」搜索当前模式的完整可用武将库。", "预览一位武将后确认出征；身份场还可在确认前改选身份。", IsBossActivitySnapshot ? "联合军的两名队友由人机操控；所选武将的魏蜀吴群势力不改变联合军归属。" : IsActivitySnapshot ? "选择本阵营的普通武将，与人机搭档和两具器械合作击败两名敌方英灵。" : IsTeamSnapshot ? "魏、蜀、吴等武将势力不决定队伍；请看青队、赤队和队友标识。" : "武将技能与身份目标是两件事；始终按你的身份决定胜利目标。"];
        }
        else if (IsDiscardSelectionPending)
        {
            CurrentGuideTitle = SelectedDiscardCount > 0 ? "确认弃置已选手牌" : "选出本回合要弃置的牌";
            CurrentGuideBody = DiscardSelectionHint;
            steps = ["可选择一张或多张手牌，再次点击可取消；点击「弃置」后才会失去所选手牌。", "「推荐弃牌」只替你选中候选，你可以修改。", "支持分次确认弃牌；累计弃够后自动结束弃牌阶段。"];
        }
        else if (IsActiveSkillSelectionPending && HumanActiveSkillAction is { } skillAction)
        {
            var activeSkillName = ActiveSkillName(skillAction);
            CurrentGuideTitle = CanConfirmActiveSkill ? $"确认发动【{activeSkillName}】" : $"选择【{activeSkillName}】的牌和目标";
            CurrentGuideBody = $"{VisibleSkillDescriptions(human?.Skills)}\n{GetActiveSkillSelectionHint()}";
            var selectedNames = _selectedActiveSkillTargetSeats.Select(selected => Seats.Single(seat => seat.Seat == selected))
                .Select(seat => seat.IsHuman ? $"你（{seat.GeneralName}）" : $"{seat.DisplaySeatNumber} 号位 {seat.GeneralName}").ToArray();
            var targetOrderHint = IsTieredRoundZeroAction(skillAction) ? "目标按原公开动作顺序提交" : "目标按点击顺序记录";
            steps = [$"本次需要选择{BuildActiveSkillRequirement(skillAction)}，{targetOrderHint}。再次点击已选牌或目标可以取消。",
                selectedNames.Length == 0 ? "尚未选择目标；不要求目标的技能只需选牌。" : $"已选目标：{string.Join("、", selectedNames)}。",
                "点击「确定」或按 Enter 才会支付代价并提交技能；Esc 取消整次选择。关闭指南会保留已选牌和目标。"];
        }
        else if (CanEndTurn)
        {
            if (SelectedHellReturnSoulAction() is not null)
            {
                CurrentGuideTitle = "确认使用回魂";
                CurrentGuideBody = ActionHint;
                steps = ["牌桌已标明全部存活且仍有忘魂技能待恢复的合法角色，无须逐个选择目标。",
                    "点击「确定」或按 Enter，依次结算各对象的无懈响应与技能恢复。",
                    "确认前可取消或换牌；使用后这张回魂牌会销毁。"];
            }
            else if (IsMultiTargetCardSelected)
            {
                CurrentGuideTitle = CanConfirmSelected ? $"确认{MultiTargetCardName}的目标" : $"选择{MultiTargetCardName}的目标";
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
                steps = ["亮起的武将才是当前合法目标；再次点击可以取消目标。", "点击「确定」或按 Enter 确认。存在转化时，操作提示会注明实际使用的有效牌型。", "确认前可以取消或换另一张实体牌。"];
            }
            else
            {
                CurrentGuideTitle = CanUseActiveSkill ? "轮到你行动，可出牌或发动技能" : Hand.Any(card => card.IsPlayable) ? "轮到你出牌" : "当前没有可以主动使用的牌";
                CurrentGuideBody = CanUseActiveSkill ? $"{VisibleSkillNames(WenheDisplaySkills(human))}：{VisibleSkillDescriptions(WenheDisplaySkills(human))}\n点击对应的【{HumanActiveSkillName}】技能或装备按钮发动；需要选牌或目标时，选齐后再确认。无须选择的技能点击后立即结算。" : Hand.Any(card => card.IsPlayable) ? "选中一张亮起的手牌，再按提示选择目标或直接确认。你可以连续使用不同的牌，直到决定结束出牌。" : "可以点击「结束」。灰色牌仍可能在响应时发挥作用，不代表它没有用。";
                steps = ["杀通常每回合限一次，目标须在攻击范围内；部分技能和装备会改变限制。", "灰色手牌悬停可查看当前不可用的原因，也可在下面查阅图鉴。", "想保留资源时，可直接结束出牌；超出体力的手牌会进入弃牌选择。"];
            }
        }
        else if (prompt is not null)
        {
            if (prompt.SkillPrompt is { } skillPrompt)
            {
                CurrentGuideTitle = skillPrompt.Title;
                steps = [skillPrompt.Instructions];
            }
            else
            {
                (CurrentGuideTitle, steps) = prompt.Kind switch
                {
                    DecisionKind.RespondDodge when prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "faction-defense-request") => ("决定是否发动护驾", new[] { "发动后，会按当前行动顺序依次询问其他存活的魏势力角色。", "若无人代出闪，你仍可使用自己的闪或八卦阵，也可以放弃响应。" }),
                DecisionKind.RespondDodge when prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "faction-defense-dodge" or "faction-defense-bagua") => ("响应曹操的护驾", new[] { "你可以打出自己的闪，或发动自己的八卦阵；成功后视为曹操打出闪。", "也可以拒绝，系统会继续询问下一名魏势力角色。" }),
                DecisionKind.RespondSlash when prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "faction-slash-request") => ("决定是否发动激将", new[] { "发动后，会按当前行动顺序依次询问其他存活的蜀势力角色。", "若无人代出杀，你仍可使用自己的杀或放弃响应。" }),
                DecisionKind.RespondSlash when prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash") => ("响应刘备的激将", new[] { "你可以打出自己的一张杀；成功后视为刘备打出杀。", "也可以拒绝，系统会继续询问下一名蜀势力角色。" }),
                DecisionKind.RespondDodge or DecisionKind.RespondSlash => ("选择手牌并确认响应", new[] { "读清这次需要杀还是闪，选择亮起的手牌后点击下方确认。其他技能、装备或多牌响应仍在中央选择。", HandResponseDeclineChoice is not null ? $"点击下方「{ActionDockCancelText}」放弃响应，可能受到伤害；Esc 只取消当前选牌。" : "Esc 只取消当前选牌；确认前手牌不会消耗。" }),
DecisionKind.RescueDying => ("决定是否救援濒死角色", (new[] { "桃可用于救援当前濒死角色；只有濒死者本人可额外使用酒自救。庞统还可发动一次限定技涅槃。", "选择使用哪张牌、发动涅槃或不救援；按当前模式的阵营关系决定希望保护谁。" }
)),                                DecisionKind.SelectHarvestCard => ("从公开牌中取走一张", new[] { "点击中央的一张公开牌，它会加入你的手牌。", "这是选牌，不需要再选择武将或点击出牌。" }),
                DecisionKind.SelectTargetCard => ("选择一张暗牌位", new[] { "目标手牌的牌面不会展示；每个按钮只代表一个不透明的牌位。", "选择后，拆桥会弃置该牌，顺手会将该牌交给你。" }),
                DecisionKind.Nullification => ("决定是否使用无懈可击", new[] { "看清本次是使锦囊失效，还是恢复已被无懈的效果。", HandResponseDeclineChoice is not null ? $"选择亮起的无懈并点击「确认无懈」；点击「{ActionDockCancelText}」跳过并保留手牌。" : "选择亮起的无懈并点击「确认无懈」；Esc 只取消当前选牌。" }),
                DecisionKind.FireAttackReveal => ("展示一张手牌", new[] { "点选底部手牌，再点击「展示此牌」；此时只是展示，并非主动弃牌，也不能跳过展示。", "随后由火攻使用者决定是否弃置同花色牌造成伤害。" }),
                DecisionKind.FireAttackDiscard => ("决定是否为火攻弃牌", new[] { "选择亮起的同花色手牌，再点击「弃牌火攻」支付代价。", HandResponseDeclineChoice is not null ? $"点击「{ActionDockCancelText}」保留手牌并结束这次火攻。" : "Esc 只取消当前选牌；确认前手牌不会消耗。" }),




                DecisionKind.SkipDiscardPolicy => ("决定是否发动克己", new[] { "本回合出牌阶段没有使用或打出过杀，因此可以跳过弃牌阶段。", "发动后保留全部手牌并结束回合；跳过则按当前体力上限弃牌。" }),





                DecisionKind.Yingbo => ("决定英博交牌", new[] { "本轮首次使用的同名伤害牌不能被响应。", "这张实体牌结算后仍在处理区时，你可以将它交给一名其他角色；选择不交则正常进入弃牌堆。" }),
                DecisionKind.StoneAxe => ("决定是否发动贯石斧", new[] { "目标已用足够的闪抵消此杀；点选两张牌后点击下方「是」，未选满两张时不能确认。", "代价可来自你的手牌或装备区，也可包含贯石斧本身；点击「否」放弃发动，Esc 只取消选牌。" }),
                DecisionKind.CixiongDoubleSwords => ("处理雌雄双股剑", new[] { "使用者以杀指定异性目标后，可选择发动雌雄双股剑。", "发动后目标从自己的手牌中精确弃一张，或令使用者摸一张牌；随后继续原杀响应。" }),

                DecisionKind.QinglongCrescentBlade => ("决定是否发动青龙偃月刀", new[] { "目标已经用足够的闪抵消此杀；中央列出你当前可以使用的每一张精确杀，刘备也可选择激将。", "发动后只对同一目标开启一张新杀，不受本回合普通出杀次数和距离限制；激将提供者支付实体牌，新杀被闪抵消时仍可再次发动。" }),
                DecisionKind.IceSword => ("处理寒冰剑", new[] { "先在下方操作区选择发动或不发动；不发动则保留原伤害。", "发动后在中央牌区依次弃置目标至多两张手牌/装备，防止全部伤害；目标手牌仅显示牌背，若第二张仍可弃置就必须继续选择。" }),
                DecisionKind.QilinBow => ("决定是否发动麒麟弓", new[] { "你的杀即将对目标造成伤害；中央只列出其装备区中当前公开的坐骑。", "选择一张坐骑会先将其弃置，再继续原伤害；也可以跳过。" }),




                DecisionKind.ZhuqueFan => ("决定是否发动朱雀羽扇", new[] { "蜀势力角色已经为激将提供普通杀；现在由实际使用者决定牌名。", "改为火杀会造成火焰伤害，并可能沿公开的连环状态传导；保持普通杀则没有属性传导。" }),
                    _ => ("处理当前技能选择", new[] { "先读中央说明，再选择发动、支付代价或跳过。", "中央的每个按钮都是完整选择，点击后立即执行。" })
                };
            }
            CurrentGuideBody = prompt.Prompt;
            if (IsOwnedCardSelectionPending)
                steps = [OwnedCardSelectionHint, "可以同时选中多张牌；再次点击可取消该牌，确认前不会交出或消耗所选牌。", .. steps];
            else if (HasCardNameSelection)
                steps = ["在手牌上方的选择区点选牌名，点击后执行当前选项。", "牌名较多时可滚动列表；定汉会标出已记录的牌名及增加、移除操作。", "牌名下方保留当前操作说明；选取后按下一步提示继续。"];
            else if (HasExpandedSelection)
                steps = ["在手牌上方的选择区选择当前效果；选项较多时可滚动列表。", "每个按钮保留当前选项的完整说明，点击后按该选项继续结算。"];
            else if (HasDockSkillChoices)
                steps = ["在下方操作区选择发动方式或不发动。", .. steps];
            else if (IsCardPaymentSelectionPending)
                steps = [HandResponseHint, "直接点选合法手牌或装备；未选够时确认按钮置灰。再次点击或按 Esc 可取消选牌，确认前不消耗牌。"];
            else if (IsHandResponsePending)
                steps = [HandResponseHint, "点选手牌不会立即消耗；再次点击或按 Esc 可取消选牌，关闭指南会保留选择。", .. steps];
        }
        else
        {
            CurrentGuideTitle = IsAutoAdvance ? "其他角色正在行动" : "自动推进已暂停";
            CurrentGuideBody = "游戏会在需要你出牌、响应或弃牌时停下来。打开指南期间不会自动推进。";
            steps = [IsAutoAdvance ? "关闭指南后，其他角色会继续行动。" : "关闭指南后，可开启右下角「自动推进」继续牌局。", "可随时在右下角调整声音与动画，或通过战报查看刚才发生的事。"];
        }
        SyncGuideItems(CurrentGuideSteps, steps.Select((text, index) => new GuideStep((index + 1).ToString("00"), text)));
        SyncGuideItems(GuideHand, IsNewGameSetupOpen || IsGeneralSelectionPending
            ? [] : Hand.Select(card => new GuideHandEntry(card.Name, card.AvailabilityText)));
        RaisePropertyChanged(nameof(HasGuideHand));
    }

    private static void SyncGuideItems<T>(ObservableCollection<T> items, IEnumerable<T> desired)
    {
        // The guide is bound even while closed. Retain unchanged rows so a hand
        // selection does not recreate its hidden controls or reset its scroll.
        var index = 0;
        foreach (var item in desired)
        {
            if (index >= items.Count) items.Add(item);
            else if (!EqualityComparer<T>.Default.Equals(items[index], item)) items[index] = item;
            index++;
        }
        while (items.Count > index) items.RemoveAt(items.Count - 1);
    }

    private string GetCardDescription(CardKind kind) => kind switch
    {
        CardKind.IronChain => "选择一到两名存活角色（可包含自己），横置或重置；连环角色受到火焰或雷电伤害时会传导同量伤害。也可重铸：不选目标，将此牌置入弃牌堆并摸一张牌；重铸不属于使用锦囊，不进入无懈响应。",
        CardKind.SupplyShortage => "选择一名距离为 1 的其他角色；其下个回合判定，若结果不为梅花则跳过摸牌阶段。",
        CardKind.Alcohol => "出牌阶段每回合限使用一次，令本回合下一张杀造成的伤害 +1；濒死时仅可对自己使用并回复 1 点体力。",
        CardKind.Dismantlement => "选择一名其他角色；从其手牌的不透明牌位中选择一张弃置，或弃置其一张公开装备/判定区牌。",
        CardKind.Snatch => "选择一名距离为 1 的其他角色；从其手牌的不透明牌位中选择一张获得，或获得其一张公开装备/判定区牌。",
        _ => CardCatalog.Get(kind).Description
    };

    private string GetCardTiming(CardKind kind)
    {
        return kind switch
        {
            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash => "出牌阶段进攻 · 决斗或南蛮入侵中响应杀",
            CardKind.Dodge => "受到杀或万箭齐发时响应",
            CardKind.Nullification => "锦囊响应窗口",
            CardKind.Peach => "出牌阶段回复自己 · 濒死时救援",
            CardKind.Alcohol => "出牌阶段每回合限一次 · 自己濒死时仅可自救",                                    _ => "自己的出牌阶段"
        };
    }
}

public sealed record GuideSection(string Key, string Title, string Subtitle);
public sealed record GuideStep(string Number, string Text);
public sealed record GuideHandEntry(string Name, string Message);
public sealed class GuideCardEntry(
    CardDefinition definition,
    string? effectiveDescription = null,
    string? effectiveTiming = null)
{
    public CardKind Kind => definition.Kind;
    public System.Windows.Media.ImageSource? Artwork => CardArt.Get(Kind);
    public bool HasArtwork => Artwork is not null;
    public string Name => definition.DisplayName;
    public string Description => effectiveDescription ?? definition.Description;
    public string Category => definition.CategoryName;
    public string Timing => effectiveTiming ?? "自己的出牌阶段";
    public string VerticalName => string.Join("\n", Name.ToCharArray());
    public double NameSize => Name.Length > 3 ? 24 : Name.Length > 1 ? 32 : 48;
}
