using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GeneralSelectionChecks
{
    public static void RelatedVariants(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new EditionPackage());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = EditionPackage.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The edition fixture must start through a real command.");
        for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "The edition fixture must reach its actual selection prompt.");
        VerifyFocusedVersionEnter(game.CreateCheckpoint(), registry);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var engine = Program.Engine(vm);
        var revision = engine.Revision;
        var state = SnapshotJson.Serialize(engine.CreateSnapshot(0));
        var window = new MainWindow(vm)
        {
            ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var root = (FrameworkElement)window.Content;
        try
        {
            // Give the actual WPF source message-only ownership, so retiring a
            // focused control still falls back through this source's HWND. It
            // cannot become a visible desktop surface or a foreground window.
            var handle = new WindowInteropHelper(window).EnsureHandle();
            const int extendedStyle = -20;
            const int noActivate = 0x08000000;
            var originalStyle = GetWindowLong(handle, extendedStyle);
            SetWindowLong(handle, extendedStyle, originalStyle | noActivate);
            var previousParent = SetParent(handle, new nint(-3));
            var parentError = Marshal.GetLastPInvokeError();
            Program.Assert((previousParent != 0 || parentError == 0) && IsMessageOnlyWindow(handle),
                $"The fixture's own HWND must become message-only before loading: parent={previousParent}, error={parentError}.");
            Program.Assert((GetWindowLong(handle, extendedStyle) & noActivate) != 0,
                "The hidden edition fixture must keep native activation disabled independently of earlier test windows.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Console.WriteLine($"General Window ready: style={GetWindowLong(handle, extendedStyle):X}, native focus={GetFocus()}, active={GetActiveWindow()}, foreground={GetForegroundWindow()}, native visible={IsWindowVisible(handle)}.");
            Program.Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) && GetForegroundWindow() != handle,
                $"The edition fixture must load a hidden Window: loaded={window.IsLoaded}, " +
                $"WPF visible={window.IsVisible}, native visible={IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}.");
            AssertHiddenFocusHost();
            var ling = vm.GeneralChoices.Single(choice => EditionPackage.Ling.Contains(choice.GeneralId));
            vm.PreviewGeneralChoiceCommand.Execute(ling);
            Program.Assert(vm.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(EditionPackage.Ling),
                "Ling Tong must expose both ordinary and boundary editions.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "boundary:ling-tong"));
            Program.Assert(vm.GeneralChoices.Count == 3 && vm.GeneralChoices.Any(choice => choice.GeneralId == "boundary:ling-tong") &&
                vm.SelectedGeneralChoice!.SkillDescription.Contains("旋风"), "Switching editions must replace the same candidate slot and its skills.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Zhao.Contains(choice.GeneralId)));
            Program.Assert(vm.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(EditionPackage.Zhao),
                "Zhao Yun must expose the requested ordinary, boundary, SP, God and Gundam editions.");
            Program.Render(root, 1120, 820, Path.Combine(output, "general-variant-before.png"));
            VerifyGeneralPreviewScroll();
            VerifyEnlargedCandidateLayout();
            foreach (var id in EditionPackage.Zhao)
            {
                var choices = (ItemsControl)window.FindName("GeneralVariantChoices");
                var button = Program.Find<Button>(choices).Single(button => button.DataContext is GeneralChoiceViewModel choice && choice.GeneralId == id);
                button.Focus();
                Console.WriteLine($"General version focus: native visible={IsWindowVisible(handle)}, owns foreground={GetForegroundWindow() == handle}, exact WPF focus={ReferenceEquals(Keyboard.FocusedElement, button)}.");
                AssertHiddenFocusHost();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, button),
                    "The edition fixture must focus the actual version button before switching.");
                button.Command!.Execute(button.CommandParameter);
                Program.Render(root, 1120, 820, Path.Combine(output, $"general-variant-{id.Replace(':', '_')}.png"));
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Console.WriteLine($"General version settled: native focus={GetFocus()}, active={GetActiveWindow()}, foreground={GetForegroundWindow()}, native visible={IsWindowVisible(handle)}, exact WPF focus={ReferenceEquals(Keyboard.FocusedElement, button)}.");
                AssertHiddenFocusHost();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, button) && button.IsVisible && button.IsEnabled && button.IsDescendantOf(choices),
                    $"Switching to {id} retired the focused version button: focused={Keyboard.FocusedElement?.GetType().Name ?? "null"}, " +
                    $"original visible={button.IsVisible}, still attached={button.IsDescendantOf(choices)}.");
                Program.Assert(vm.SelectedGeneralChoice!.GeneralId == id && vm.GeneralChoices.Count == 3 &&
                    vm.GeneralChoices.Count(choice => choice.IsPreviewSelected) == 1 && engine.Revision == revision &&
                    vm.SelectedGeneralChoice.HealthText.Contains((registry.Generals[id].BaseHp + 1).ToString()),
                    "An edition click must update portrait, health and skills without submitting or adding candidate slots.");
                AssertVersionSelectionVisual($"edition {id}");
                button.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Program.Assert(Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } next &&
                               !ReferenceEquals(next, button) && next.IsDescendantOf((DependencyObject)window.FindName("GeneralSelectionOverlay")),
                    "Keyboard navigation after switching an edition must reach another visible selection control.");
                AssertVersionSelectionVisual($"unfocused edition {id}", requireUnfocused: true);
                ((UIElement)Keyboard.FocusedElement!).MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, button) &&
                               SnapshotJson.Serialize(engine.CreateSnapshot(0)) == state,
                    "Reverse navigation must return to the still-live version button without changing the match.");
                AssertHiddenFocusHost();
            }
            var zhaoChoice = vm.SelectedGeneralChoice!;
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Ling.Contains(choice.GeneralId)));
            Program.Render(root, 1120, 820, Path.Combine(output, "general-variant-family-change.png"));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Console.WriteLine($"General family focus: native={GetFocus()}, window={handle}, active={GetActiveWindow()}, foreground={GetForegroundWindow()}, WPF={(Keyboard.FocusedElement as FrameworkElement)?.Name ?? Keyboard.FocusedElement?.GetType().Name ?? "null"}, panelWithin={((FrameworkElement)window.FindName("GeneralSelectionPanel")).IsKeyboardFocusWithin}.");
            AssertHiddenFocusHost();
            Program.Assert(vm.SelectedGeneralVariants.Count == 2 && Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } familyFocus &&
                           familyFocus.IsDescendantOf((DependencyObject)window.FindName("GeneralSelectionOverlay")),
                "Changing the candidate family must restore a visible selection action after retiring version controls.");
            AssertVersionSelectionVisual("changed family");
            var lingButton = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices")).First();
            lingButton.Focus();
            vm.PreviewGeneralChoiceCommand.Execute(zhaoChoice);
            Program.Render(root, 1120, 820, Path.Combine(output, "general-variant-family-return.png"));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(vm.SelectedGeneralVariants.Count == 6 && ReferenceEquals(vm.SelectedGeneralChoice, zhaoChoice) &&
                           Keyboard.FocusedElement is UIElement { IsVisible: true, IsEnabled: true } returnedFamilyFocus &&
                           returnedFamilyFocus.IsDescendantOf((DependencyObject)window.FindName("GeneralSelectionOverlay")),
                "Returning to Zhao Yun must restore the current family and a visible selection action without adding a candidate.");
            var expectedEditionIds = vm.GeneralChoices.Select(choice => choice.GeneralId).ToArray();
            var selectionPrompt = engine.CreateSnapshot(0).PendingDecision!.PromptId;
            var selectionBoundary = Boundary(engine);
            Program.Assert(vm.SelectedGeneralChoice!.GeneralId == "classic:gao-da-yi-hao" &&
                           expectedEditionIds.Contains("boundary:ling-tong") && vm.CanConfirmGeneralChoice,
                "The refresh regression must prepare both the unselected Ling Tong edition and the selected Gundam edition through current choices.");
            foreach (var developerView in new[] { true, false })
            {
                var retiredChoices = vm.GeneralChoices.Concat(vm.SelectedGeneralVariants).Distinct().ToArray();
                vm.IsDeveloperView = developerView;
                root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Program.Assert(ReferenceEquals(Program.Engine(vm), engine),
                    "Changing the public developer setting must refresh the same native selection engine.");
                VerifyRefreshedEditions($"developer={developerView}", retiredChoices);
            }
            FocusVersion();
            var oldVariants = vm.SelectedGeneralVariants;
            var staleChoice = vm.SelectedGeneralChoice!;
            var choicesBeforeLoad = vm.GeneralChoices.Concat(vm.SelectedGeneralVariants).Distinct().ToArray();
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Program.Render(root, 1120, 820, Path.Combine(output, "general-variant-reloaded.png"));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(!ReferenceEquals(Program.Engine(vm), engine),
                "The edition reload regression must replace the native engine through the real saved checkpoint.");
            VerifyRefreshedEditions("manual reload", choicesBeforeLoad);
            Program.Assert(vm.GeneralChoices.Count == 3 && vm.SelectedGeneralVariants.Count == 6,
                "Reloading a pending prompt lost its edition choices or changed its candidate count.");
            Program.Assert(vm.SelectedGeneralVariants.All(current => oldVariants.All(old => !ReferenceEquals(current, old))) &&
                           Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices")).All(button =>
                               button.DataContext is GeneralChoiceViewModel choice && vm.SelectedGeneralVariants.Any(current => ReferenceEquals(current, choice))),
                "Reloaded version controls must bind to current prompt objects rather than cached retired choices.");
            var currentChoice = vm.SelectedGeneralChoice;
            vm.PreviewGeneralChoiceCommand.Execute(staleChoice);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, currentChoice) && Program.Engine(vm).Revision == revision &&
                           SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == state,
                "A retired version choice must not change the reloaded preview or match.");
            var selectedCandidate = Program.Find<Button>((ItemsControl)window.FindName("GeneralCandidateCards"))
                .Single(button => ReferenceEquals(button.DataContext, vm.SelectedGeneralChoice));
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, selectedCandidate),
                $"Reloading the focused edition did not focus the current selected candidate: " +
                $"{(Keyboard.FocusedElement as FrameworkElement)?.Name ?? "null"}.");

            FocusVersion();
            vm.LoadManualGameCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
            confirm.Focus();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, confirm),
                "The deferred selection fallback must preserve the confirmation button the user already focused.");

            Program.Render(root, 1120, 820, Path.Combine(output, "general-variant-current.png"));
            FocusVersion();
            vm.LoadManualGameCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            vm.OpenSettingsCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, window.FindName("SettingsSoundToggle")),
                "Opening settings above a pending selection fallback must focus its real default action.");
            var motion = (CheckBox)window.FindName("SettingsMotionToggle");
            motion.Focus();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, motion),
                "An old selection fallback must not replace the settings control the user focused.");
            vm.CloseSettingsCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Assert(Program.Engine(vm).Revision == revision && SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == state,
                "Focus recovery and modal navigation must leave the real pending selection untouched.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Ling.Contains(choice.GeneralId)));
            vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "boundary:ling-tong"));
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Zhao.Contains(choice.GeneralId)));
            root.UpdateLayout();
            var gundamButton = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices"))
                .Single(button => button.DataContext is GeneralChoiceViewModel choice && choice.GeneralId == "classic:gao-da-yi-hao");
            typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(gundamButton, null);
            var beforeTutorial = Program.Engine(vm);
            var prepared = Boundary(beforeTutorial);
            var originalPreview = vm.SelectedGeneralChoice!;
            var editionIds = vm.GeneralChoices.Select(choice => choice.GeneralId).ToArray();
            var originalPrompt = beforeTutorial.CreateSnapshot(0).PendingDecision!.PromptId;
            Program.Assert(originalPreview.GeneralId == "classic:gao-da-yi-hao" &&
                editionIds.Contains("boundary:ling-tong") && vm.CanConfirmGeneralChoice &&
                beforeTutorial.Revision == revision,
                "The tutorial fixture must start with a real Gundam preview and a separately changed Ling Tong candidate edition.");
            vm.StartTutorialCommand.Execute(null);
            Program.Assert(vm.IsTutorialActive && !ReferenceEquals(Program.Engine(vm), beforeTutorial),
                "The actual tutorial command must suspend the private edition-selection prompt independently.");
            vm.ExitTutorialCommand.Execute(null);
            root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Console.WriteLine($"Tutorial resume general: selected={vm.SelectedGeneralChoice?.GeneralId}, editions={string.Join(',', vm.GeneralChoices.Select(choice => choice.GeneralId))}, confirm={vm.CanConfirmGeneralChoice}.");
            Program.Assert(!vm.IsTutorialActive && ReferenceEquals(Program.Engine(vm), beforeTutorial) &&
                Boundary(beforeTutorial) == prepared && beforeTutorial.CreateSnapshot(0).PendingDecision!.PromptId == originalPrompt &&
                vm.GeneralChoices.Select(choice => choice.GeneralId).SequenceEqual(editionIds) &&
                vm.SelectedGeneralChoice is { GeneralId: "classic:gao-da-yi-hao", IsPreviewSelected: true } resumed &&
                !ReferenceEquals(resumed, originalPreview) && vm.GeneralChoices.Any(choice => ReferenceEquals(choice, resumed)) &&
                vm.GeneralChoices.Count(choice => choice.IsPreviewSelected) == 1 &&
                vm.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(EditionPackage.Zhao) &&
                vm.SelectedGeneralVariants.Any(choice => ReferenceEquals(choice, resumed)) && vm.CanConfirmGeneralChoice,
                "Tutorial return must restore both changed candidate editions and the exact selected family using fresh current prompt objects, without changing any safe view or journal.");
            var resumedPreview = vm.SelectedGeneralChoice;
            vm.PreviewGeneralChoiceCommand.Execute(originalPreview);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, resumedPreview) &&
                vm.GeneralChoices.Select(choice => choice.GeneralId).SequenceEqual(editionIds) && Boundary(beforeTutorial) == prepared,
                "The retired pre-tutorial preview parameter must not alter the resumed family, editions or native game.");
            var resumedConfirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
            Program.Assert(resumedConfirm.IsEnabled && resumedConfirm.Command!.CanExecute(resumedConfirm.CommandParameter),
                "The resumed edition must retain its real enabled confirmation button.");
            var checkpoint = beforeTutorial.CreateCheckpoint();
            var reference = GameReplay.Restore(checkpoint, registry);
            var referenceResult = reference.Submit(new SelectGeneralCommand(0, "classic:gao-da-yi-hao", revision, originalPrompt));
            Program.Assert(referenceResult.Accepted, referenceResult.Error?.Message ?? "The independently resumed Gundam edition must be natively accepted.");
            typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(resumedConfirm, null);
            Program.Assert(Program.Engine(vm).CreateSnapshot(0).Players[0].GeneralId == "classic:gao-da-yi-hao" &&
                Program.Engine(vm).Revision == revision + 1 && Program.Engine(vm).AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                Program.Engine(vm).AcceptedCommands.Last() is SelectGeneralCommand submitted && submitted.GeneralId == "classic:gao-da-yi-hao" &&
                submitted.ActorSeat == 0 && submitted.PromptId == originalPrompt && submitted.ExpectedRevision == revision &&
                Boundary(Program.Engine(vm)) == Boundary(reference), "Confirm must submit the exact selected Gundam edition once and match independent native execution after tutorial return.");
            var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint())), registry);
            Program.Assert(Boundary(Program.Engine(vm)) == Boundary(cold),
                "The selected returned edition must cold restore every safe player view and native journal exactly.");
            VerifyChangedCandidateContract(registry, game.CreateCheckpoint(), EditionPackage.FewerCandidates, 2);
            VerifyChangedCandidateContract(registry, game.CreateCheckpoint(), EditionPackage.FewerVariants, 3);

            void VerifyEnlargedCandidateLayout()
            {
                var panel = (Border)window.FindName("GeneralSelectionPanel");
                var content = (Grid)panel.Child;
                var originalTransform = content.LayoutTransform;
                var originalId = vm.SelectedGeneralChoice!.GeneralId;
                var boundary = Boundary(engine);
                try
                {
                    vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "ol:shen-zhao-yun"));
                    content.LayoutTransform = new ScaleTransform(1.5, 1.5);
                    Program.Render(root, 1120, 740, Path.Combine(output, "general-selection-compact-enlarged.png"));
                    // Finish the preview's Loaded-priority scroll reset before testing new reading and focus actions.
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var cards = Program.Find<Button>((ItemsControl)window.FindName("GeneralCandidateCards")).ToArray();
                    var versions = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices")).ToArray();
                    var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
                    var skills = (ScrollViewer)window.FindName("GeneralSelectionSkillsScroller");
                    var description = (TextBlock)window.FindName("GeneralSelectionSkillDescription");
                    var presenter = Program.Find<ScrollContentPresenter>(skills).Single(item => ReferenceEquals(item.TemplatedParent, skills));
                    var viewport = new Rect(0, 0, 1120, 740);
                    Rect Bounds(FrameworkElement control) => control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
                    Rect DetailBounds(FrameworkElement control) => control.TransformToAncestor(presenter).TransformBounds(new Rect(control.RenderSize));
                    var preview = vm.SelectedGeneralChoice;
                    var confirmationPosition = confirm.TranslatePoint(new Point(), root);
                    Console.WriteLine($"Enlarged selection: card={Bounds(cards[0])}, skills={Bounds(skills)}, confirm={Bounds(confirm)}, " +
                        $"versions={string.Join(";", versions.Select(button => Bounds(button).ToString()))}.");
                    Program.Assert(cards.Length == vm.GeneralChoices.Count && cards.All(card => viewport.Contains(Bounds(card)) && Bounds(card).Width >= 100) &&
                        versions.Length == vm.SelectedGeneralVariants.Count && versions.All(button => Bounds(button).Height >= 22) &&
                        viewport.Contains(Bounds(confirm)) && viewport.Contains(Bounds(skills)) && Bounds(skills).Height >= 46 &&
                        description.Text == preview!.SkillDescription && Boundary(engine) == boundary,
                        "Enlarged selection at the minimum client size must retain readable cards, a useful detail viewport and the fixed complete confirmation action.");
                    skills.ScrollToEnd();
                    Program.Render(root, 1120, 740, Path.Combine(output, "general-selection-compact-skills-bottom.png"));
                    var text = DetailBounds(description);
                    Program.Assert(skills.ScrollableHeight > 0 && Math.Abs(skills.VerticalOffset - skills.ScrollableHeight) < 1 &&
                        text.Bottom > 0 && text.Bottom <= presenter.ActualHeight + 1 && text.Left >= -1 && text.Right <= presenter.ActualWidth + 1 &&
                        (confirm.TranslatePoint(new Point(), root) - confirmationPosition).Length < 1 && Boundary(engine) == boundary,
                        "Scrolling enlarged details to the end must expose the complete skill text's final line and both edges while confirmation stays fixed.");
                    foreach (var version in versions)
                    {
                        var sameFocusBefore = ReferenceEquals(Keyboard.FocusedElement, version);
                        var focusedBefore = Keyboard.FocusedElement;
                        var offsetBefore = skills.VerticalOffset;
                        var boundsBefore = DetailBounds(version);
                        var focusReturned = version.Focus();
                        root.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        var button = DetailBounds(version);
                        var choice = (GeneralChoiceViewModel)version.CommandParameter;
                        Console.WriteLine($"Enlarged version focus: id={choice.GeneralId}, name={choice.Name}, before same={sameFocusBefore}, " +
                            $"before focus={focusedBefore?.GetType().Name}/{(focusedBefore as FrameworkElement)?.Name}, Focus returned={focusReturned}, " +
                            $"actual focus={Keyboard.FocusedElement?.GetType().Name}/{(Keyboard.FocusedElement as FrameworkElement)?.Name}, " +
                            $"exact={ReferenceEquals(Keyboard.FocusedElement, version)}, offset={offsetBefore:F3}->{skills.VerticalOffset:F3}, " +
                            $"scrollable={skills.ScrollableHeight:F3}, before bounds={boundsBefore}, bounds={button}, viewport={presenter.RenderSize}.");
                        Program.Assert(ReferenceEquals(Keyboard.FocusedElement, version) && button.Top >= -1 &&
                            button.Bottom <= presenter.ActualHeight + 1 && button.Left >= -1 && button.Right <= presenter.ActualWidth + 1 &&
                            ReferenceEquals(vm.SelectedGeneralChoice, preview) && Boundary(engine) == boundary,
                            "Every enlarged edition must scroll fully into its actual detail viewport when focused, without changing the current preview or journal.");
                    }
                    cards[0].Focus();
                    for (var step = 0; step < 40 && !ReferenceEquals(Keyboard.FocusedElement, confirm); step++)
                    {
                        Program.Assert(Keyboard.FocusedElement is UIElement current &&
                            current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                            "Enlarged selection Tab navigation must continue through actual controls.");
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
                    }
                    Program.Assert(ReferenceEquals(Keyboard.FocusedElement, confirm) && Boundary(engine) == boundary,
                        "The enlarged candidate and edition controls must retain a real Tab route to confirmation without submitting the preview.");
                }
                finally
                {
                    content.LayoutTransform = originalTransform;
                    vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == originalId));
                    Program.Render(root, 1120, 820, null);
                }
            }

            void AssertVersionSelectionVisual(string label, bool requireUnfocused = false)
            {
                root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                var buttons = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices")).ToArray();
                foreach (var button in buttons)
                {
                    var choice = (GeneralChoiceViewModel)button.CommandParameter;
                    var text = Program.Find<TextBlock>(button).Where(block => block.IsVisible &&
                        block.ActualWidth > 0 && block.ActualHeight > 0).Select(block => block.Text).ToArray();
                    var accessibleName = AutomationProperties.GetName(button);
                    Program.Assert(text.Contains(choice.Name) && text.Contains(choice.EditionLabel) &&
                        accessibleName.Contains(choice.Name, StringComparison.Ordinal) && accessibleName.Contains(choice.EditionLabel, StringComparison.Ordinal),
                        $"{label} must identify every actual version with its visible name and compact edition and expose both to assistive technology.");
                }
                var selected = buttons.Single(button => ReferenceEquals(button.CommandParameter, vm.SelectedGeneralChoice));
                var background = selected.Background.ToString();
                var chrome = (Border)selected.Template.FindName("Chrome", selected);
                var focusRing = (Border)selected.Template.FindName("FocusRing", selected);
                Program.Assert(selected.CommandParameter is GeneralChoiceViewModel { IsPreviewSelected: true } &&
                    buttons.Count(button => button.Background.ToString() == background) == 1 &&
                    chrome.Background.ToString() == background,
                    $"{label} must visibly highlight exactly the current preview's actual version button without selecting another candidate.");
                if (requireUnfocused)
                    Program.Assert(!selected.IsKeyboardFocused && focusRing.Visibility != Visibility.Visible &&
                        chrome.Background.ToString() == background,
                        $"{label} must retain its selected background after the independent keyboard focus ring has moved away.");
            }

            void VerifyGeneralPreviewScroll()
            {
                var scroller = (ScrollViewer)window.FindName("GeneralSelectionSkillsScroller");
                var originalHeight = scroller.ReadLocalValue(FrameworkElement.HeightProperty);
                var originalId = vm.SelectedGeneralChoice!.GeneralId;
                var originalSearch = vm.CustomGeneralSearch;
                var boundary = Boundary(engine);
                try
                {
                    // Keep real skill text and bindings; a short viewport makes
                    // the actual scroll state deterministic across font metrics.
                    scroller.Height = 23;
                    vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Ling.Contains(choice.GeneralId)));
                    vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "classic:ling-tong"));
                    PumpPreview();
                    var version = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices"))
                        .Single(button => button.DataContext is GeneralChoiceViewModel { GeneralId: "boundary:ling-tong" });
                    version.Focus();
                    ScrollDetails();
                    typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(version, null);
                    PumpPreview();
                    Program.Assert(vm.SelectedGeneralChoice?.GeneralId == "boundary:ling-tong" && scroller.VerticalOffset == 0 &&
                        ReferenceEquals(Keyboard.FocusedElement, version),
                        "Switching the real edition button must show its details from the top and retain that live button's focus.");

                    var offset = ScrollDetails();
                    vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralChoice!);
                    PumpPreview();
                    Program.Assert(scroller.VerticalOffset == offset && ReferenceEquals(Keyboard.FocusedElement, version),
                        "Previewing the same edition must retain the reader's scroll position and focus.");
                    var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
                    confirm.Focus();
                    vm.CustomGeneralSearch = "凌统 草稿";
                    var retiredPreview = vm.SelectedGeneralChoice;
                    vm.IsDeveloperView = true;
                    vm.IsDeveloperView = false;
                    PumpPreview();
                    Program.Assert(vm.SelectedGeneralChoice?.GeneralId == "boundary:ling-tong" &&
                        !ReferenceEquals(vm.SelectedGeneralChoice, retiredPreview) && scroller.VerticalOffset == offset &&
                        vm.CustomGeneralSearch == "凌统 草稿" && ReferenceEquals(Keyboard.FocusedElement, confirm),
                        "Rebinding the same preview through complete snapshots must retain scroll, search draft and the live focus target.");

                    var candidate = Program.Find<Button>((ItemsControl)window.FindName("GeneralCandidateCards"))
                        .Single(button => button.DataContext is GeneralChoiceViewModel choice && EditionPackage.Zhao.Contains(choice.GeneralId));
                    candidate.Focus();
                    typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(candidate, null);
                    PumpPreview();
                    Program.Assert(EditionPackage.Zhao.Contains(vm.SelectedGeneralChoice!.GeneralId) && scroller.VerticalOffset == 0 &&
                        ReferenceEquals(Keyboard.FocusedElement, candidate) && Boundary(engine) == boundary,
                        "Switching the real candidate must show its first skill without moving focus or changing any player view or native journal.");
                    vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == originalId));
                }
                finally
                {
                    vm.CustomGeneralSearch = originalSearch;
                    if (ReferenceEquals(originalHeight, DependencyProperty.UnsetValue)) scroller.ClearValue(FrameworkElement.HeightProperty);
                    else scroller.SetValue(FrameworkElement.HeightProperty, originalHeight);
                    PumpPreview();
                }

                double ScrollDetails()
                {
                    scroller.ScrollToEnd();
                    PumpPreview();
                    Program.Assert(scroller.ScrollableHeight > 0 && scroller.VerticalOffset > 0,
                        "The skill-reading fixture must actually scroll the real description before changing previews.");
                    return scroller.VerticalOffset;
                }

                void PumpPreview()
                {
                    root.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    root.UpdateLayout();
                }
            }

            void FocusVersion()
            {
                var button = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices"))
                    .Single(button => ReferenceEquals(button.DataContext, vm.SelectedGeneralChoice));
                button.Focus();
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, button),
                    "The reload fixture must begin on the current live version button.");
                AssertHiddenFocusHost();
            }

            void AssertHiddenFocusHost() => Program.Assert(IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                (GetWindowLong(handle, extendedStyle) & noActivate) != 0,
                "Actual WPF focus and navigation must keep the message-only MainWindow HWND focused, hidden and nonforeground.");

            void VerifyRefreshedEditions(string label, IReadOnlyList<GeneralChoiceViewModel> retiredChoices)
            {
                var currentGame = Program.Engine(vm);
                var currentPreview = vm.SelectedGeneralChoice;
                Console.WriteLine($"General refresh {label}: selected={currentPreview?.GeneralId}, editions={string.Join(',', vm.GeneralChoices.Select(choice => choice.GeneralId))}.");
                Program.Assert(currentGame.CreateSnapshot(0).PendingDecision!.PromptId == selectionPrompt &&
                               Boundary(currentGame) == selectionBoundary &&
                               vm.GeneralChoices.Select(choice => choice.GeneralId).SequenceEqual(expectedEditionIds) &&
                               currentPreview is { GeneralId: "classic:gao-da-yi-hao", IsPreviewSelected: true } &&
                               vm.GeneralChoices.Any(choice => ReferenceEquals(choice, currentPreview)) &&
                               vm.GeneralChoices.Count(choice => choice.IsPreviewSelected) == 1 &&
                               vm.SelectedGeneralVariants.Select(choice => choice.GeneralId).ToHashSet().SetEquals(EditionPackage.Zhao) &&
                               vm.SelectedGeneralVariants.Any(choice => ReferenceEquals(choice, currentPreview)) &&
                               vm.CanConfirmGeneralChoice,
                    $"{label} must preserve every candidate family's edition and the selected Gundam preview without changing the original prompt, any player-safe view or journal.");
                var versionButtons = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices")).ToArray();
                Program.Assert(vm.GeneralChoices.Concat(vm.SelectedGeneralVariants).All(current =>
                                   retiredChoices.All(old => !ReferenceEquals(current, old))) &&
                               versionButtons.Length == EditionPackage.Zhao.Length && versionButtons.All(button =>
                                   button.DataContext is GeneralChoiceViewModel choice &&
                                   ReferenceEquals(button.CommandParameter, choice) &&
                                   vm.SelectedGeneralVariants.Any(current => ReferenceEquals(current, choice))),
                    $"{label} must rebuild fresh current prompt objects and bind the real version buttons to those objects.");
                foreach (var retired in retiredChoices) vm.PreviewGeneralChoiceCommand.Execute(retired);
                Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, currentPreview) &&
                               vm.GeneralChoices.Select(choice => choice.GeneralId).SequenceEqual(expectedEditionIds) &&
                               Boundary(currentGame) == selectionBoundary,
                    $"{label} must reject every retired candidate/version preview parameter without changing either family edition or submitting a command.");
                AssertVersionSelectionVisual(label);
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void VerifyFocusedVersionEnter(GameCheckpoint checkpoint, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        using var vm = new MainViewModel(autoAdvance: false, seed: checkpoint.Options.Seed, showSetup: true,
            saveStore: store, useExpandedContent: false, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var engine = Program.Engine(vm);
        var prompt = vm.ChoiceSnapshot.PendingDecision!;
        var boundary = Boundary(engine);
        Program.Assert(!vm.HasSaveError && prompt is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0, IsPrivate: true } &&
            engine.CreateSnapshot(0).Players.Count == 5 && Boundary(GameReplay.Restore(checkpoint, registry)) == boundary,
            "The focused version fixture must restore the original native five-player private general-selection boundary.");
        vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Zhao.Contains(choice.GeneralId)));
        var ordinary = vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "classic:zhao-yun");
        var gundam = vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "classic:gao-da-yi-hao");
        Program.Assert(prompt.Choices.Any(choice => choice.ContentIds.Contains(ordinary.GeneralId)) &&
            prompt.Choices.Any(choice => choice.ContentIds.Contains(gundam.GeneralId)),
            "Both the ordinary Zhao Yun and Gundam editions must be native published choices.");
        vm.PreviewGeneralChoiceCommand.Execute(ordinary);
        var window = new MainWindow(vm)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var root = (FrameworkElement)window.Content;
        var previewCount = 0;
        var bubbleCount = 0;
        KeyEventHandler observePreview = (_, args) => { if (args.Key == Key.Enter) previewCount++; };
        KeyEventHandler observeBubble = (_, args) => { if (args.Key == Key.Enter) bubbleCount++; };
        Button? versionButton = null;
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            const int extendedStyle = -20;
            const int noActivate = 0x08000000;
            SetWindowLong(handle, extendedStyle, GetWindowLong(handle, extendedStyle) | noActivate);
            var previousParent = SetParent(handle, new nint(-3));
            var parentError = Marshal.GetLastPInvokeError();
            Program.Assert((previousParent != 0 || parentError == 0) && IsMessageOnlyWindow(handle),
                $"The focused version fixture's own HWND must become message-only: parent={previousParent}, error={parentError}.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var versions = (ItemsControl)window.FindName("GeneralVariantChoices");
            versionButton = Program.Find<Button>(versions).Single(button => ReferenceEquals(button.CommandParameter, gundam));
            versionButton.Focus();
            Program.Assert(window.IsLoaded && window.IsVisible && IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                (GetWindowLong(handle, extendedStyle) & noActivate) != 0 &&
                ReferenceEquals(PresentationSource.FromVisual(versionButton), PresentationSource.FromVisual(window)) &&
                versionButton.IsVisible && versionButton.IsEnabled &&
                ReferenceEquals(versionButton.Command, vm.PreviewGeneralChoiceCommand) &&
                versionButton.Command!.CanExecute(versionButton.CommandParameter) &&
                ReferenceEquals(Keyboard.FocusedElement, versionButton) && Keyboard.Modifiers == ModifierKeys.None &&
                ReferenceEquals(vm.SelectedGeneralChoice, ordinary) && !gundam.IsPreviewSelected &&
                vm.CanConfirmGeneralChoice && vm.SelectedGeneralVariants.Any(choice => ReferenceEquals(choice, gundam)) &&
                Boundary(engine) == boundary,
                "The actual unselected Gundam version button must own exact keyboard focus while ordinary Zhao Yun remains the reversible preview, without taking foreground.");
            window.AddHandler(Keyboard.PreviewKeyDownEvent, observePreview, handledEventsToo: true);
            versionButton.AddHandler(Keyboard.KeyDownEvent, observeBubble, handledEventsToo: true);
            // Exercise the real window tunnel and, only when unhandled, the
            // actual focused Button's keyboard activation of its preview command.
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(versionButton)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            versionButton.RaiseEvent(key);
            var previewHandled = key.Handled;
            if (!key.Handled)
            {
                key.RoutedEvent = Keyboard.KeyDownEvent;
                versionButton.RaiseEvent(key);
            }
            Console.WriteLine($"Focused general version Enter: seed=31, prompt={prompt.PromptId}, previous={ordinary.GeneralId}, " +
                $"focused={gundam.GeneralId}, selected={vm.SelectedGeneralChoice?.GeneralId ?? "none"}, " +
                $"submitted={(engine.AcceptedCommands.LastOrDefault() as SelectGeneralCommand)?.GeneralId ?? "none"}, " +
                $"preview={previewCount}, preview handled={previewHandled}, bubble={bubbleCount}, handled={key.Handled}, " +
                $"added commands={engine.AcceptedCommands.Count - checkpoint.Commands.Count}, same native boundary={Boundary(engine) == boundary}.");
            Program.Assert(previewCount == 1,
                "The actual Window PreviewKeyDown must observe the focused version button's Enter exactly once.");
            Program.Assert(vm.IsGeneralSelectionPending && ReferenceEquals(vm.SelectedGeneralChoice, gundam) &&
                gundam.IsPreviewSelected && !ordinary.IsPreviewSelected && vm.GeneralChoices.Any(choice => ReferenceEquals(choice, gundam)) &&
                engine.Revision == checkpoint.Revision && engine.AcceptedCommands.Count == checkpoint.Commands.Count &&
                Boundary(engine) == boundary,
                "First Enter on the focused unselected Gundam version must only preview that edition, never submit the old ordinary Zhao Yun preview.");
            Program.Assert(!previewHandled && bubbleCount == 1 && key.Handled && ReferenceEquals(Keyboard.FocusedElement, versionButton),
                "The version preview must be owned by the actual focused Button's KeyDown, with no global confirmation or focus loss.");

            var versionConfirm = Press(versionButton, Key.Enter);
            Program.Assert(versionConfirm.PreviewHandled && versionConfirm.BubbleCount == 0 && versionConfirm.Handled,
                "A second fresh Enter on the current version must use the current selection confirmation shortcut.");
            AssertConfirmed("current version", gundam.GeneralId);

            Reload();
            var previous = vm.SelectedGeneralChoice!;
            var different = vm.GeneralChoices.First(choice => !ReferenceEquals(choice, previous));
            var candidateButton = Candidate(different);
            var candidatePreview = Press(candidateButton, Key.Enter);
            Program.Assert(!candidatePreview.PreviewHandled && candidatePreview.BubbleCount == 1 && candidatePreview.Handled &&
                ReferenceEquals(vm.SelectedGeneralChoice, different) && different.IsPreviewSelected && !previous.IsPreviewSelected &&
                Boundary(Program.Engine(vm)) == boundary && ReferenceEquals(Keyboard.FocusedElement, candidateButton),
                "First Enter on another actual candidate must only preview its current native choice and preserve exact focus and every safe view and journal.");
            Console.WriteLine($"Focused general candidate Enter: previous={previous.GeneralId}, focused={different.GeneralId}, preview only=True, added commands=0, same native boundary=True.");
            var candidateConfirm = Press(candidateButton, Key.Enter);
            Program.Assert(candidateConfirm.PreviewHandled && candidateConfirm.BubbleCount == 0 && candidateConfirm.Handled,
                "A second fresh Enter on the current candidate must confirm through the existing selection shortcut.");
            AssertConfirmed("current candidate", different.GeneralId);

            Reload();
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Zhao.Contains(choice.GeneralId)));
            var currentGundam = vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "classic:gao-da-yi-hao");
            vm.PreviewGeneralChoiceCommand.Execute(currentGundam);
            Pump();
            var confirmButton = (Button)window.FindName("ConfirmGeneralChoiceButton");
            var explicitConfirm = Press(confirmButton, Key.Enter);
            Program.Assert(!explicitConfirm.PreviewHandled && explicitConfirm.BubbleCount == 1 && explicitConfirm.Handled,
                "Enter on the actual dedicated confirmation button must execute its own native Button command once.");
            AssertConfirmed("dedicated confirm", currentGundam.GeneralId);

            foreach (var firstNumber in new[] { Key.D1, Key.NumPad1 })
            {
                Reload();
                var initial = vm.SelectedGeneralChoice!;
                var initialButton = Candidate(initial);
                var numberIndex = Enumerable.Range(0, vm.GeneralChoices.Count)
                    .First(index => !ReferenceEquals(vm.GeneralChoices[index], initial));
                var numbered = vm.GeneralChoices[numberIndex];
                var keyNumber = firstNumber + numberIndex;
                foreach (var modifier in new[] { ModifierKeys.Control, ModifierKeys.Shift })
                {
                    var modified = Press(initialButton, keyNumber, modifier);
                    Program.Assert(!modified.PreviewHandled && !modified.Handled &&
                        ReferenceEquals(vm.SelectedGeneralChoice, initial) && Boundary(Program.Engine(vm)) == boundary,
                        "Modified number keys must stay with the focused control without previewing or confirming a general.");
                }
                Program.Assert(!TableShortcutChecks.Input(window, keyNumber, ModifierKeys.Alt) &&
                    ReferenceEquals(vm.SelectedGeneralChoice, initial) && Boundary(Program.Engine(vm)) == boundary,
                    "Alt-number must not enter the selection shortcut branch.");
                var numberPreview = Press(initialButton, keyNumber);
                Program.Assert(numberPreview.PreviewHandled && numberPreview.BubbleCount == 0 && numberPreview.Handled &&
                    ReferenceEquals(vm.SelectedGeneralChoice, numbered) &&
                    ReferenceEquals(Keyboard.FocusedElement, Candidate(numbered)) &&
                    !ReferenceEquals(Keyboard.FocusedElement, initialButton) && Boundary(Program.Engine(vm)) == boundary,
                    "An actual number key must preview the current candidate without native commands and move exact keyboard focus away from the old candidate.");
                var heldNumber = Press(Candidate(numbered), keyNumber, repeat: true);
                Program.Assert(heldNumber.PreviewHandled && heldNumber.BubbleCount == 0 && heldNumber.Handled &&
                    ReferenceEquals(vm.SelectedGeneralChoice, numbered) && ReferenceEquals(Keyboard.FocusedElement, Candidate(numbered)) &&
                    Boundary(Program.Engine(vm)) == boundary,
                    "Holding a number key must be consumed before another native activation, without changing or confirming its preview.");
                Console.WriteLine($"Focused general number preview: key={keyNumber}, index={numberIndex + 1}, previous={initial.GeneralId}, selected={numbered.GeneralId}, exact candidate focus=True, added commands=0, same native boundary=True.");
                var numberConfirm = Press(Candidate(numbered), Key.Enter);
                Program.Assert(numberConfirm.PreviewHandled && numberConfirm.BubbleCount == 0 && numberConfirm.Handled,
                    "Enter immediately after a number key must confirm the newly focused preview, without returning to the old candidate.");
                AssertConfirmed(firstNumber + " then Enter", numbered.GeneralId);
            }

            Reload();
            vm.ToggleGeneralRosterCommand.Execute(null);
            Pump();
            var search = (TextBox)window.FindName("CustomGeneralSearchBox");
            var searchPreview = vm.SelectedGeneralChoice;
            var searchDraft = vm.CustomGeneralSearch;
            var rosterBoundary = Boundary(Program.Engine(vm));
            var searchNumber = Press(search, Key.NumPad1);
            var searchRepeat = Press(search, Key.NumPad1, repeat: true);
            Program.Assert(!searchNumber.PreviewHandled && !searchRepeat.PreviewHandled &&
                searchNumber.BubbleCount == 1 && searchRepeat.BubbleCount == 1 &&
                ReferenceEquals(Keyboard.FocusedElement, search) && ReferenceEquals(vm.SelectedGeneralChoice, searchPreview) &&
                vm.CustomGeneralSearch == searchDraft && Boundary(Program.Engine(vm)) == rosterBoundary,
                "Fresh and held keypad digits in the real roster search must remain text input without changing its preview or native match.");
            vm.IsSettingsOpen = true;
            Pump();
            var settingsToggle = (CheckBox)window.FindName("SettingsSoundToggle");
            var coveredNumber = Press(settingsToggle, Key.NumPad1);
            var coveredRepeat = Press(settingsToggle, Key.NumPad1, repeat: true);
            Program.Assert(!coveredNumber.PreviewHandled && !coveredRepeat.PreviewHandled &&
                ReferenceEquals(Keyboard.FocusedElement, settingsToggle) && ReferenceEquals(vm.SelectedGeneralChoice, searchPreview) &&
                vm.CustomGeneralSearch == searchDraft && Boundary(Program.Engine(vm)) == rosterBoundary,
                "Fresh and held keypad digits in covering settings must not reach or refocus the hidden roster.");
            vm.IsSettingsOpen = false;
            vm.CustomGeneralSearch = "__no_keypad_general_match__";
            var emptyNumber = Press(search, Key.NumPad1);
            var emptyRepeat = Press(search, Key.NumPad1, repeat: true);
            Program.Assert(!emptyNumber.PreviewHandled && !emptyRepeat.PreviewHandled &&
                ReferenceEquals(Keyboard.FocusedElement, search) && vm.CustomGeneralChoices.Count == 0 &&
                vm.SelectedGeneralChoice is null && !vm.CanConfirmGeneralChoice && Boundary(Program.Engine(vm)) == rosterBoundary,
                "Keypad text input with no results must not revive or confirm a hidden general.");

            Reload();
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => EditionPackage.Zhao.Contains(choice.GeneralId)));
            var controlPreview = vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == "classic:gao-da-yi-hao");
            vm.PreviewGeneralChoiceCommand.Execute(controlPreview);
            Pump();
            var controlConfirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
            Program.Assert(vm.IsGeneralSelectionPending && vm.CanConfirmGeneralChoice && !vm.CanEndTurn &&
                ReferenceEquals(vm.SelectedGeneralChoice, controlPreview) && controlPreview.IsPreviewSelected &&
                vm.GeneralChoices.Any(choice => ReferenceEquals(choice, controlPreview)) &&
                vm.ChoiceSnapshot.PendingDecision?.PromptId == prompt.PromptId && Boundary(Program.Engine(vm)) == boundary,
                "The unavailable end-turn shortcut must start with the same native private prompt and a current confirmable Gundam preview.");
            var controlEnter = Press(controlConfirm, Key.Enter, ModifierKeys.Control);
            Pump();
            var controlGame = Program.Engine(vm);
            var controlPreserved = vm.IsGeneralSelectionPending && vm.CanConfirmGeneralChoice &&
                ReferenceEquals(vm.SelectedGeneralChoice, controlPreview) && controlPreview.IsPreviewSelected &&
                vm.GeneralChoices.Any(choice => ReferenceEquals(choice, controlPreview)) &&
                vm.ChoiceSnapshot.PendingDecision?.PromptId == prompt.PromptId &&
                Boundary(controlGame) == boundary && ReferenceEquals(Keyboard.FocusedElement, controlConfirm) &&
                controlConfirm.IsVisible && controlConfirm.IsEnabled;
            Console.WriteLine($"Focused general confirm Ctrl+Enter: seed=31, prompt={prompt.PromptId}, selected={controlPreview.GeneralId}, " +
                $"submitted={(controlGame.AcceptedCommands.Skip(checkpoint.Commands.Count).LastOrDefault() as SelectGeneralCommand)?.GeneralId ?? "none"}, " +
                $"preview handled={controlEnter.PreviewHandled}, bubble={controlEnter.BubbleCount}, handled={controlEnter.Handled}, " +
                $"added commands={controlGame.AcceptedCommands.Count - checkpoint.Commands.Count}, " +
                $"same native boundary={Boundary(controlGame) == boundary}, exact confirm focus={ReferenceEquals(Keyboard.FocusedElement, controlConfirm)}.");
            Program.Assert(controlPreserved && controlEnter.PreviewHandled && controlEnter.BubbleCount == 0 && controlEnter.Handled,
                "Ctrl+Enter on the actual general confirmation button must preserve the current native prompt, preview, exact focus and every safe view and journal without selecting a general.");

            Button Candidate(GeneralChoiceViewModel choice) => Program.Find<Button>((ItemsControl)window.FindName("GeneralCandidateCards"))
                .Single(button => ReferenceEquals(button.CommandParameter, choice));

            void Pump()
            {
                root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }

            void Reload()
            {
                vm.LoadManualGameCommand.Execute(null);
                Pump();
                Program.Assert(!vm.HasSaveError && vm.IsGeneralSelectionPending && Boundary(Program.Engine(vm)) == boundary,
                    "Each actual keyboard route must reload the same untouched native selection checkpoint.");
            }

            (bool PreviewHandled, int BubbleCount, bool Handled) Press(UIElement button, Key pressed, ModifierKeys modifiers = ModifierKeys.None, bool repeat = false)
            {
                Pump();
                button.Focus();
                Program.Assert(window.IsLoaded && window.IsVisible && IsMessageOnlyWindow(handle) && GetFocus() == handle &&
                    !IsWindowVisible(handle) && GetForegroundWindow() != handle &&
                    (GetWindowLong(handle, extendedStyle) & noActivate) != 0 &&
                    button.IsVisible && button.IsEnabled && button.IsDescendantOf(window) &&
                    ReferenceEquals(PresentationSource.FromVisual(button), PresentationSource.FromVisual(window)) &&
                    ReferenceEquals(Keyboard.FocusedElement, button) && Keyboard.Modifiers == ModifierKeys.None &&
                    (button is not Button action || action.Command is not null && action.Command.CanExecute(action.CommandParameter)),
                    "Every keyboard route must start on its own loaded, enabled actual Button with exact focus in the hidden nonforeground message-only HWND.");
                var tunnels = 0;
                var bubbles = 0;
                KeyEventHandler observeTunnel = (_, args) => { if (args.Key == pressed) tunnels++; };
                KeyEventHandler observeKeyDown = (_, args) => { if (args.Key == pressed) bubbles++; };
                window.AddHandler(Keyboard.PreviewKeyDownEvent, observeTunnel, handledEventsToo: true);
                button.AddHandler(Keyboard.KeyDownEvent, observeKeyDown, handledEventsToo: true);
                try
                {
                    return TableShortcutChecks.WithModifiers(modifiers, Route);

                    (bool PreviewHandled, int BubbleCount, bool Handled) Route()
                    {
                        Program.Assert(Keyboard.Modifiers == modifiers && ReferenceEquals(Keyboard.FocusedElement, button),
                            "The actual focused Button and primary keyboard modifiers must remain current during the key route.");
                        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, 0, pressed)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                        button.RaiseEvent(input);
                        var wasPreviewHandled = input.Handled;
                        if (!input.Handled)
                        {
                            input.RoutedEvent = Keyboard.KeyDownEvent;
                            button.RaiseEvent(input);
                        }
                        Program.Assert(tunnels == 1 && IsMessageOnlyWindow(handle) && !IsWindowVisible(handle) &&
                            GetForegroundWindow() != handle,
                            "The actual Window must observe each fresh key once while the owned HWND stays hidden and nonforeground.");
                        return (wasPreviewHandled, bubbles, input.Handled);
                    }
                }
                finally
                {
                    window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observeTunnel);
                    button.RemoveHandler(Keyboard.KeyDownEvent, observeKeyDown);
                }
            }

            void AssertConfirmed(string label, string generalId)
            {
                var actual = Program.Engine(vm);
                var native = GameReplay.Restore(checkpoint, registry);
                var result = native.Submit(new SelectGeneralCommand(0, generalId, checkpoint.Revision, prompt.PromptId));
                Program.Assert(result.Accepted, result.Error?.Message ?? "The current published general must be independently accepted.");
                Program.Assert(actual.Revision == checkpoint.Revision + 1 &&
                    actual.AcceptedCommands.Count == checkpoint.Commands.Count + 1 &&
                    actual.AcceptedCommands.Last() is SelectGeneralCommand submitted &&
                    submitted.ActorSeat == 0 && submitted.GeneralId == generalId &&
                    submitted.PromptId == prompt.PromptId && submitted.ExpectedRevision == checkpoint.Revision &&
                    actual.CreateSnapshot(0).Players[0].GeneralId == generalId && !vm.IsGeneralSelectionPending &&
                    Boundary(actual) == Boundary(native),
                    "Keyboard confirmation must submit the exact current native general once and match all five safe views, typed events, commands and movements.");
                var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(actual.CreateCheckpoint())), registry);
                Program.Assert(Boundary(cold) == Boundary(actual),
                    "Each actual keyboard selection must cold restore every safe view and full native journal exactly.");
                Console.WriteLine($"Focused general {label}: selected={generalId}, added commands=1, exact native selection=True, cold boundary=True.");
            }
        }
        finally
        {
            window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observePreview);
            versionButton?.RemoveHandler(Keyboard.KeyDownEvent, observeBubble);
            window.Content = null;
            window.Close();
        }
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });

    private static void VerifyChangedCandidateContract(ContentRegistry registry, GameCheckpoint original, string mode, int candidateCount)
    {
        var replacement = GameEngine.CreateStandard(original.Options with { ModeId = mode }, registry);
        Program.Assert(replacement.Submit(new StartGameCommand()).Accepted, "The changed-contract fixture must start natively.");
        for (var step = 0; step < 16 && replacement.PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Program.Assert(replacement.Submit(new AdvanceOneStepCommand(replacement.Revision)).Accepted,
                "The changed-contract fixture must reach a real general prompt.");
        var replacementPrompt = replacement.CreateSnapshot(0).PendingDecision!;
        Program.Assert(replacementPrompt.Kind == DecisionKind.SelectGeneral, "The changed fixture must publish its candidate contract.");
        var expectedIds = replacementPrompt.Choices.Where(choice => choice.ContentIds.Count == 1 &&
                choice.Parameters.GetValueOrDefault("candidate-general-id", choice.ContentIds[0]) == choice.ContentIds[0])
            .Select(choice => choice.ContentIds[0]).ToArray();
        var selectedId = expectedIds[0] == "classic:gao-da-yi-hao" ? "boundary:ling-tong" : "classic:gao-da-yi-hao";
        Program.Assert(expectedIds.Length == candidateCount && replacementPrompt.Choices.Any(choice => choice.ContentIds.Contains(selectedId)),
            "The changed contract must retain the old selected edition as a legal nondefault preview.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, original));
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var previousGame = Program.Engine(vm);
        var previousBoundary = Boundary(previousGame);
        foreach (var edition in new[] { "boundary:ling-tong", "classic:gao-da-yi-hao", selectedId })
        {
            var family = EditionPackage.Ling.Contains(edition) ? EditionPackage.Ling : EditionPackage.Zhao;
            vm.PreviewGeneralChoiceCommand.Execute(vm.GeneralChoices.Single(choice => family.Contains(choice.GeneralId)));
            vm.PreviewGeneralChoiceCommand.Execute(vm.SelectedGeneralVariants.Single(choice => choice.GeneralId == edition));
        }
        var retired = vm.GeneralChoices.Concat(vm.SelectedGeneralVariants).Distinct().ToArray();
        Program.Assert(previousGame.CreateSnapshot(0).PendingDecision!.PromptId == replacementPrompt.PromptId &&
                       previousGame.CreateSnapshot(0).PendingDecision!.Choices.Count != replacementPrompt.Choices.Count &&
                       vm.GeneralChoices.Count == 3 &&
                       vm.SelectedGeneralChoice!.GeneralId == selectedId && Boundary(previousGame) == previousBoundary,
            "Separate native checkpoints must reuse the prompt number while the original draft remains uncommitted.");
        var expectedBoundary = Boundary(replacement);
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, replacement.CreateCheckpoint()));
        vm.LoadManualGameCommand.Execute(null);
        var currentGame = Program.Engine(vm);
        var currentPreview = vm.SelectedGeneralChoice;
        Console.WriteLine($"General changed contract {mode}: prompt={replacementPrompt.PromptId}, selected={currentPreview?.GeneralId}, editions={string.Join(',', vm.GeneralChoices.Select(choice => choice.GeneralId))}.");
        Program.Assert(!ReferenceEquals(currentGame, previousGame) && currentGame.CreateSnapshot(0).PendingDecision!.PromptId == replacementPrompt.PromptId &&
                       Boundary(currentGame) == expectedBoundary && Boundary(previousGame) == previousBoundary &&
                       vm.GeneralChoices.Select(choice => choice.GeneralId).SequenceEqual(expectedIds) &&
                       ReferenceEquals(currentPreview, vm.GeneralChoices[0]) && currentPreview!.GeneralId != selectedId &&
                       vm.GeneralChoices.Count(choice => choice.IsPreviewSelected) == 1 && vm.CanConfirmGeneralChoice &&
                       vm.GeneralChoices.Concat(vm.SelectedGeneralVariants).All(current => retired.All(old => !ReferenceEquals(current, old))),
            "A reused prompt number with a different candidate/variant contract must start at fresh defaults without changing either checkpoint's views or journal.");
        foreach (var old in retired) vm.PreviewGeneralChoiceCommand.Execute(old);
        Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, currentPreview) &&
                       vm.GeneralChoices.Select(choice => choice.GeneralId).SequenceEqual(expectedIds) && Boundary(currentGame) == expectedBoundary,
            "Every retired candidate/version parameter must remain invalid after replacing the candidate contract.");
        var currentVersion = vm.SelectedGeneralVariants.Last();
        vm.PreviewGeneralChoiceCommand.Execute(currentVersion);
        Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, currentVersion) && Boundary(currentGame) == expectedBoundary,
            "The changed contract's current version must remain usable as an uncommitted preview.");
        var reference = GameReplay.Restore(currentGame.CreateCheckpoint(), registry);
        Program.Assert(reference.Submit(new SelectGeneralCommand(0, currentVersion.GeneralId,
                currentGame.Revision, replacementPrompt.PromptId)).Accepted,
            "The changed contract's current version must be independently accepted by native rules.");
        vm.ConfirmGeneralChoiceCommand.Execute(null);
        Program.Assert(Boundary(Program.Engine(vm)) == Boundary(reference),
            "Confirming the changed contract must match independent execution in all safe views and native journals.");
    }

    private static bool IsMessageOnlyWindow(nint handle)
    {
        nint previous = 0;
        for (var count = 0; count < 4096; count++)
        {
            previous = FindWindowEx(new nint(-3), previous, null, null);
            if (previous == handle) return true;
            if (previous == 0) return false;
        }
        return false;
    }

    private static void VerifyContextIgnoresDiagnosticReveal(MainViewModel vm)
    {
        var title = vm.GeneralSelectionContextTitle;
        var detail = vm.GeneralSelectionContextDetail;
        var game = Program.Engine(vm);
        var revision = game.Revision;
        var acceptedCommands = game.AcceptedCommands.Count;
        foreach (var reveal in new[] { true, false })
        {
            vm.IsDeveloperView = reveal;
            Program.Assert(vm.GeneralSelectionContextTitle == title && vm.GeneralSelectionContextDetail == detail &&
                ReferenceEquals(Program.Engine(vm), game) && game.Revision == revision && game.AcceptedCommands.Count == acceptedCommands,
                "The selection context must remain viewer-safe and read-only when the diagnostic view reveals other players' private state.");
        }
    }

    public static void InputBoundaries(string output)
    {
        VerifyThirteenCandidateLayout(output);
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore(), useExpandedContent: true)
        {
            IsMotionEnabled = false
        };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 740, Path.Combine(output, "general-input-selection.png"));
            var candidates = (ItemsControl)window.FindName("GeneralCandidateCards");
            Button Card(GeneralChoiceViewModel choice) => Program.Find<Button>(candidates)
                .Single(button => ReferenceEquals(button.DataContext, choice));
            var selected = vm.GeneralChoices[1];
            var engine = Program.Engine(vm);
            var revision = engine.Revision;
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Card(selected), null);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, selected) && engine.Revision == revision,
                "The first candidate click must remain a reversible preview.");

            foreach (var (name, setOpen) in new (string, Action<bool>)[]
            {
                ("guide", open => vm.IsHelpOpen = open),
                ("log", open => vm.IsLogOpen = open),
                ("lobby", open => vm.IsNewGameSetupOpen = open),
                ("settings", open => vm.IsSettingsOpen = open),
                ("history", open => vm.IsHistoryOpen = open),
                ("gallery", open => vm.IsGeneralGalleryOpen = open)
            })
            {
                setOpen(true);
                Shortcut(window, Key.D1);
                Shortcut(window, Key.NumPad1);
                Shortcut(window, Key.NumPad1, repeat: true);
                Shortcut(window, Key.Enter);
                Program.Assert(engine.Revision == revision && ReferenceEquals(vm.SelectedGeneralChoice, selected),
                    $"The {name} overlay allowed keyboard input to change or confirm a hidden candidate.");
                var handled = DoubleClick(Card(selected));
                Program.Assert(!handled && engine.Revision == revision && ReferenceEquals(vm.SelectedGeneralChoice, selected),
                    $"The {name} overlay allowed a hidden candidate to be changed or confirmed.");
                setOpen(false);
            }

            Program.Assert(!DoubleClick(Card(selected), MouseButton.Right) &&
                           !DoubleClick(candidates) &&
                           !DoubleClick(Card(vm.GeneralChoices[0])) && engine.Revision == revision,
                "Right clicks, empty space or unselected candidates committed a choice.");
            var selectedButton = Card(selected);
            Program.Assert(DoubleClick(Program.Find<System.Windows.Shapes.Rectangle>(selectedButton).First()) &&
                           engine.Revision == revision + 1 &&
                           !vm.IsGeneralSelectionPending,
                "Double-clicking the previewed candidate must confirm exactly one choice.");
            Program.Assert(!DoubleClick(selectedButton) && engine.Revision == revision + 1,
                "A stale candidate submitted a second command after selection ended.");

            vm.NewGameCommand.Execute(null);
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "national:ambitious-6");
            vm.StartNewGameCommand.Execute(null);
            engine = Program.Engine(vm);
            revision = engine.Revision;
            Program.Render(root, 1120, 740, Path.Combine(output, "general-input-identity.png"));
            Program.Assert(vm.IsIdentityRevealOpen && vm.IsGeneralSelectionPending &&
                           !DoubleClick(Card(vm.SelectedGeneralChoice!)) && engine.Revision == revision,
                "A candidate click bypassed the identity reveal.");
            Program.Assert(Shortcut(window, Key.Enter) && !vm.IsIdentityRevealOpen && engine.Revision == revision,
                "Enter must dismiss the identity reveal without also choosing a general.");
            var nationalContextTitle = $"你的势力 · {vm.HumanPlayer!.RoleLabel}";
            Program.Assert(vm.GeneralSelectionContextTitle == nationalContextTitle &&
                vm.GeneralSelectionContextDetail == "选择主将 · 双将同势力，暗置入场",
                "National primary selection must describe only the viewer's own faction and hidden dual-general setup.");
            VerifyContextIgnoresDiagnosticReveal(vm);
            root.UpdateLayout();
            var primary = vm.SelectedGeneralChoice!;
            var primaryButton = Card(primary);
            var primaryPrompt = engine.PendingDecision!.PromptId;
            var primaryHandled = DoubleClick(primaryButton);
            Program.Assert(primaryHandled && engine.Revision == revision + 1,
                "The primary general double-click submitted more than one command.");
            Program.Assert(!DoubleClick(primaryButton) && engine.Revision == revision + 1,
                "A primary general's stale input advanced the selection transition.");
            for (var step = 0; step < 8 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending && engine.PendingDecision!.PromptId != primaryPrompt,
                "The national fixture did not reach its separate secondary general prompt.");
            var ownPrimary = engine.CreateSnapshot(0).Players.Single(player => player.IsHuman);
            Program.Assert(vm.GeneralSelectionContextTitle == nationalContextTitle &&
                vm.GeneralSelectionContextDetail == $"主将 · {ownPrimary.GeneralName} · 选择同势力副将" &&
                !ownPrimary.IsGeneralPublic,
                "National secondary selection may show the viewer's private primary but must not present it as a public Lord.");
            var secondaryRevision = engine.Revision;
            var secondaryPreview = vm.SelectedGeneralChoice;
            Program.Assert(Shortcut(window, Key.Enter, repeat: true) &&
                           Shortcut(window, Key.D2, repeat: true) &&
                           Shortcut(window, Key.NumPad2, repeat: true) && engine.Revision == secondaryRevision &&
                           ReferenceEquals(vm.SelectedGeneralChoice, secondaryPreview),
                "A held primary selection key changed or confirmed the separate secondary prompt.");
            Program.Assert(!DoubleClick(primaryButton) && engine.Revision == secondaryRevision,
                "A primary general's stale input selected the next slot.");
            Program.Render(root, 1120, 740, Path.Combine(output, "general-input-secondary.png"));
            Program.Assert(vm.GeneralChoices.All(choice => choice.GeneralId != primary.GeneralId),
                "The secondary prompt retained the already chosen primary general.");
            Program.Assert(Shortcut(window, Key.D1) && engine.Revision == secondaryRevision &&
                           ReferenceEquals(vm.SelectedGeneralChoice, vm.GeneralChoices[0]),
                "Number keys did not preview the current prompt without submitting it.");
            Program.Assert(DoubleClick(Card(vm.SelectedGeneralChoice!)) &&
                           engine.Revision == secondaryRevision + 1 && !vm.IsGeneralSelectionPending,
                "The secondary general did not confirm once through its own card.");
            Program.Assert(vm.GeneralSelectionContextTitle.Length == 0 && vm.GeneralSelectionContextDetail.Length == 0,
                "Completing both national slots must retire the private primary-selection context.");

            // Reuse the same small national setup to exercise both full-roster
            // slots without changing the existing consecutive-click checks.
            vm.StartNewGameCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            vm.ToggleGeneralRosterCommand.Execute(null);
            var rosterPrimary = vm.CustomGeneralChoices[0];
            vm.CustomGeneralSearch = rosterPrimary.Name;
            Program.Assert(vm.CustomGeneralChoices.Count == 1 && ReferenceEquals(vm.CustomGeneralChoices[0], rosterPrimary),
                "The national roster fixture must search one actual primary general by its unique name.");
            var rosterPrimaryPrompt = vm.ChoiceSnapshot.PendingDecision!.PromptId;
            vm.IsDeveloperView = true;
            vm.IsDeveloperView = false;
            Program.Assert(vm.CustomGeneralSearch == rosterPrimary.Name && vm.CustomGeneralChoices.Count == 1,
                "Refreshing the same national selection slot must retain its full-roster search.");
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            engine = Program.Engine(vm);
            Program.Assert(!vm.HasSaveError && vm.IsCustomGeneralSelectionOpen &&
                vm.ChoiceSnapshot.PendingDecision?.PromptId == rosterPrimaryPrompt &&
                vm.CustomGeneralSearch == rosterPrimary.Name && vm.CustomGeneralChoices.Count == 1,
                "Restoring the same national selection prompt must retain its search and current filtered roster.");
            var restoredPrimary = vm.CustomGeneralChoices.Single();
            var roster = (ListBox)window.FindName("CustomGeneralRoster");
            Button RosterCard(GeneralChoiceViewModel choice)
            {
                root.Measure(new Size(1120, 740));
                root.Arrange(new Rect(0, 0, 1120, 740));
                root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                roster.ScrollIntoView(choice);
                roster.UpdateLayout();
                return Program.Find<Button>(roster).Single(button =>
                    ReferenceEquals(button.Command, vm.PreviewGeneralChoiceCommand) && ReferenceEquals(button.CommandParameter, choice));
            }
            var rosterPrimaryButton = RosterCard(restoredPrimary);
            var primaryAccepted = engine.AcceptedCommands.Count;
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(rosterPrimaryButton, null);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, restoredPrimary) && engine.AcceptedCommands.Count == primaryAccepted,
                "The first click on the actual full-roster primary row must only preview its current general.");
            Program.Assert(DoubleClick(rosterPrimaryButton) && engine.AcceptedCommands.Count == primaryAccepted + 1 &&
                engine.CreateSnapshot(0).Players.Single(player => player.IsHuman).GeneralId == restoredPrimary.GeneralId,
                "Double-clicking the actual full-roster primary row must confirm that general exactly once.");
            Program.Assert(!DoubleClick(rosterPrimaryButton) && engine.AcceptedCommands.Count == primaryAccepted + 1,
                "A retired primary roster row must not submit again during the transition to the secondary slot.");
            Program.Assert(vm.CustomGeneralSearch.Length == 0,
                "Successfully selecting a national primary must clear its search before the different secondary slot opens.");
            for (var step = 0; step < 8 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending && vm.ChoiceSnapshot.PendingDecision?.PromptId != rosterPrimaryPrompt,
                "The searched full-roster primary must advance to its own fresh secondary prompt.");
            vm.ToggleGeneralRosterCommand.Execute(null);
            var own = engine.CreateSnapshot(0).Players.Single(player => player.IsHuman);
            Program.Assert(vm.CustomGeneralChoices.Count > 0 && vm.CustomGeneralSearch.Length == 0 &&
                own.GeneralId == restoredPrimary.GeneralId && vm.CustomGeneralChoices.All(choice =>
                    choice.GeneralId != restoredPrimary.GeneralId && engine.ContentRegistry.Generals[choice.GeneralId].FactionId == own.FactionId) &&
                Enumerable.Range(1, vm.Seats.Count - 1).All(seat => engine.CreateSnapshot(seat).PendingDecision is null &&
                    engine.CreateSnapshot(seat).Players[0].GeneralId.Length == 0),
                "The secondary full roster must expose remaining same-faction generals without the primary's stale search or private general leaks.");
            var secondaryAccepted = engine.AcceptedCommands.Count;
            vm.SelectGeneralChoiceCommand.Execute(restoredPrimary);
            Program.Assert(engine.AcceptedCommands.Count == secondaryAccepted && vm.IsGeneralSelectionPending,
                "A retired full-roster primary control must not select or consume the secondary prompt.");
            var rosterSecondaryPreview = vm.SelectedGeneralChoice;
            Program.Assert(!DoubleClick(rosterPrimaryButton) && engine.AcceptedCommands.Count == secondaryAccepted &&
                ReferenceEquals(vm.SelectedGeneralChoice, rosterSecondaryPreview),
                "A stale double-click from the primary roster row must not change or consume the secondary full-roster prompt.");
            var rosterSecondary = vm.CustomGeneralChoices[0];
            var rosterSecondaryButton = RosterCard(rosterSecondary);
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(rosterSecondaryButton, null);
            Program.Assert(ReferenceEquals(vm.SelectedGeneralChoice, rosterSecondary) && engine.AcceptedCommands.Count == secondaryAccepted,
                "The first click on the actual secondary roster row must remain a reversible preview.");
            Program.Assert(DoubleClick(Program.Find<TextBlock>(rosterSecondaryButton).First()),
                "The secondary roster row must handle a double-click originating on its actual text child.");
            own = engine.CreateSnapshot(0).Players.Single(player => player.IsHuman);
            Program.Assert(engine.AcceptedCommands.Count == secondaryAccepted + 1 && own.GeneralId == restoredPrimary.GeneralId &&
                own.SecondaryGeneralId == rosterSecondary.GeneralId && Enumerable.Range(1, vm.Seats.Count - 1).All(seat =>
                    engine.CreateSnapshot(seat).Players[0] is { GeneralId.Length: 0, SecondaryGeneralId: null }),
                "The secondary full-roster control must commit that exact second general once while both national slots remain private.");
            Program.Assert(!DoubleClick(rosterSecondaryButton) && engine.AcceptedCommands.Count == secondaryAccepted + 1,
                "A retired secondary roster row must not submit a second choice after both slots are selected.");
            VerifyHeldNationalConfirm(vm);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifyThirteenCandidateLayout(string output)
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var unreadable = new List<string>();
        foreach (var role in new[] { Role.Lord, Role.Renegade })
        {
            using var vm = new MainViewModel(false, 31, true, new MemorySaveStore(),
                useExpandedContent: true, contentRegistry: registry, wushuangSaveStore: new MemorySaveStore()) { IsMotionEnabled = false };
            vm.SelectedTableMode = vm.TableModes.Single(mode => mode.ModeId == "identity:classic-5");
            vm.SelectedStartingRole = vm.StartingRoles.Single(option => option.Role == role);
            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            var game = Program.Engine(vm);
            var boundary = Boundary(game);
            Program.Assert(vm.IsGeneralSelectionPending && !vm.IsIdentityRevealOpen && !vm.IsCustomGeneralSelectionOpen &&
                game.CreateSnapshot(0).Players.Single(player => player.IsHuman).Role == role && vm.GeneralChoices.Count == 13,
                "The normal-size candidate fixture must use the real Standard identity role's thirteen published candidate families.");
            using var host = new NativeResponseStatusChecks.NativeHost(vm);
            var window = host.Window;
            var root = host.Root;
            window.Height = window.MinHeight;
            root.Width = 1120; root.Height = 740;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Program.Render(root, 1120, 740, Path.Combine(output, $"general-thirteen-candidates-{role}.png"));
            var panel = (Border)window.FindName("GeneralSelectionPanel");
            var candidates = (ItemsControl)window.FindName("GeneralCandidateCards");
            var cards = Program.Find<Button>(candidates).ToArray();
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            var bounds = cards.Select(Bounds).ToArray();
            var textScales = cards.Select(card =>
            {
                var text = Program.Find<TextBlock>(card).Single(item => item.Name == "ChoiceStatusText");
                var transform = text.TransformToAncestor(root);
                return (transform.Transform(new Point(1, 0)) - transform.Transform(new Point())).Length;
            }).ToArray();
            var minimumWidth = bounds.Min(rect => rect.Width);
            var minimumTextScale = textScales.Min();
            Console.WriteLine($"Normal thirteen-candidate layout: role={role}, root={root.RenderSize}, " +
                $"window={window.ActualWidth:F3}x{window.ActualHeight:F3}, min-height={window.MinHeight:F0}, " +
                $"cards={cards.Length}, columns={vm.GeneralChoiceColumns}, first={bounds[0]}, last={bounds[^1]}, " +
                $"minimum width={minimumWidth:F3}/{cards[0].Width:F0}, minimum text scale={minimumTextScale:F4}, " +
                $"10-DIP skill text={10 * minimumTextScale:F3}, same journal={Boundary(game) == boundary}.");
            Program.Assert(root.RenderSize == new Size(1120, 740) && window.ActualHeight >= window.MinHeight &&
                Math.Abs(window.ActualHeight - window.MinHeight) <= 1 &&
                ((Grid)panel.Child).LayoutTransform.Value.IsIdentity && cards.Length == 13 &&
                cards.All(card => ReferenceEquals(card.Command, vm.PreviewGeneralChoiceCommand) &&
                    vm.GeneralChoices.Contains((GeneralChoiceViewModel)card.CommandParameter)) && Boundary(game) == boundary,
                "Measuring the loaded ordinary-size selection must retain exact client bounds, current real candidates and the native private boundary without applying a text zoom.");
            // The actual template declares a 142-DIP portrait and 10-DIP skill
            // text. More legal candidates must add scrolling, not shrink both.
            if (minimumWidth < cards[0].Width - 1 || minimumTextScale < .99)
                unreadable.Add($"{role}: card {minimumWidth:F2}/{cards[0].Width:F0} DIP, text scale {minimumTextScale:F3}");
            else if (role == Role.Lord)
            {
                var scroll = (ScrollViewer)window.FindName("GeneralCandidateScroller");
                var presenter = Program.Find<ScrollContentPresenter>(scroll).Single(item => ReferenceEquals(item.TemplatedParent, scroll));
                var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
                var confirmBounds = Bounds(confirm);
                var last = cards[^1];
                var lastChoice = (GeneralChoiceViewModel)last.CommandParameter;
                Rect InViewport(FrameworkElement element) => element.TransformToAncestor(presenter).TransformBounds(new Rect(element.RenderSize));
                bool FullyVisible(FrameworkElement element)
                {
                    var area = InViewport(element);
                    return area.Left >= -1 && area.Top >= -1 && area.Right <= presenter.ActualWidth + 1 && area.Bottom <= presenter.ActualHeight + 1;
                }
                void Pump()
                {
                    root.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    root.UpdateLayout();
                }
                Program.Assert(scroll.ScrollableHeight > 0 && presenter.ActualHeight > 0 && !scroll.Focusable,
                    "Thirteen unscaled portraits must scroll vertically without adding a candidate-pane Tab stop.");
                scroll.ScrollToEnd();
                Pump();
                Program.Render(root, 1120, 740, Path.Combine(output, "general-thirteen-candidates-last.png"));
                Program.Assert(scroll.VerticalOffset > 0 && FullyVisible(last) && Bounds(last).Width >= last.Width - 1 &&
                    Boundary(game) == boundary && Bounds(confirm) == confirmBounds,
                    "Scrolling the real thirteen-choice pane must expose its entire last unscaled portrait while confirmation stays fixed and all player views remain unchanged.");
                scroll.ScrollToHome();
                SetFocus(new WindowInteropHelper(window).Handle);
                cards[0].Focus();
                Pump();
                var tabCount = 0;
                while (!ReferenceEquals(Keyboard.FocusedElement, last) && tabCount++ < cards.Length + 4)
                {
                    Program.Assert(!TableShortcutChecks.Input(window, Key.Tab) && Keyboard.FocusedElement is UIElement current &&
                        current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                        "The normal candidate pane must retain native forward Tab traversal through its real portraits.");
                    Pump();
                }
                Program.Assert(ReferenceEquals(Keyboard.FocusedElement, last) && FullyVisible(last) && scroll.VerticalOffset > 0 &&
                    !ReferenceEquals(vm.SelectedGeneralChoice, lastChoice) && Boundary(game) == boundary,
                    "Tab must bring the thirteenth real candidate fully into view without previewing or confirming it.");
                var accepted = game.AcceptedCommands.Count;
                var first = Enter(repeat: false);
                Pump();
                Program.Assert(!first.PreviewHandled && first.Bubbles == 1 && first.Handled &&
                    ReferenceEquals(vm.SelectedGeneralChoice, lastChoice) && ReferenceEquals(Keyboard.FocusedElement, last) &&
                    FullyVisible(last) && Boundary(game) == boundary,
                    "Fresh Enter on the last unselected candidate must only preview its exact published choice while keeping its complete portrait visible.");
                var held = Enter(repeat: true);
                Pump();
                Program.Assert(held.PreviewHandled && held.Bubbles == 0 && held.Handled &&
                    ReferenceEquals(vm.SelectedGeneralChoice, lastChoice) && Boundary(game) == boundary,
                    "Holding the preview key must not turn the thirteenth candidate's reversible preview into a confirmation.");
                var fresh = Enter(repeat: false);
                Pump();
                Program.Assert(fresh.PreviewHandled && fresh.Bubbles == 0 && fresh.Handled &&
                    game.AcceptedCommands.Count == accepted + 1 && game.AcceptedCommands.Last() is SelectGeneralCommand selection &&
                    selection.GeneralId == lastChoice.GeneralId && game.CreateSnapshot(0).Players.Single(player => player.IsHuman).GeneralId == lastChoice.GeneralId,
                    "After releasing Enter, one fresh press must confirm the exact last real candidate once through the existing native command boundary.");
                Console.WriteLine($"Thirteen-candidate native navigation: {tabCount} Tab moves, last={lastChoice.GeneralId}, " +
                    $"preview-only=True, held preview consumed=True, confirmation commands={game.AcceptedCommands.Count - accepted}.");

                (bool PreviewHandled, int Bubbles, bool Handled) Enter(bool repeat) => TableShortcutChecks.WithModifiers(ModifierKeys.None, () =>
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    Program.Assert(ReferenceEquals(Keyboard.FocusedElement, last) && GetFocus() == handle && IsMessageOnlyWindow(handle) &&
                        !IsWindowVisible(handle) && GetForegroundWindow() != handle && last.IsVisible && last.IsEnabled,
                        "Each candidate Enter must originate on the focused real Button in its hidden nonforeground native host.");
                    var bubbles = 0;
                    KeyEventHandler observe = (_, args) => { if (args.Key == Key.Enter) bubbles++; };
                    last.AddHandler(Keyboard.KeyDownEvent, observe, handledEventsToo: true);
                    try
                    {
                        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(last)!, 0, Key.Enter)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                        last.RaiseEvent(input);
                        var previewHandled = input.Handled;
                        if (!input.Handled) { input.RoutedEvent = Keyboard.KeyDownEvent; last.RaiseEvent(input); }
                        return (previewHandled, bubbles, input.Handled);
                    }
                    finally { last.RemoveHandler(Keyboard.KeyDownEvent, observe); }
                });
            }
        }
        Program.Assert(unreadable.Count == 0,
            "Thirteen real identity candidates in the minimum-height window with a 1120x740 client must retain their declared card and text sizes: " + string.Join("; ", unreadable));
    }

    private static void VerifyHeldNationalConfirm(MainViewModel vm)
    {
        // Reuse the current small national mode and its already loaded registry.
        // The old render-only Window is not loaded; the shared native host owns
        // the real keyboard route for both consecutive selection prompts.
        vm.StartNewGameCommand.Execute(null);
        vm.ContinueFromIdentityRevealCommand.Execute(null);
        using var host = new NativeResponseStatusChecks.NativeHost(vm);
        var window = host.Window;
        var root = host.Root;
        SetFocus(new WindowInteropHelper(window).Handle);
        Pump();
        var game = Program.Engine(vm);
        var primary = vm.SelectedGeneralChoice ?? throw new InvalidOperationException("The national fixture must publish a primary preview.");
        var primaryPrompt = game.PendingDecision?.PromptId ?? throw new InvalidOperationException("The national fixture must publish a primary prompt.");
        var accepted = game.AcceptedCommands.Count;
        var confirm = (Button)window.FindName("ConfirmGeneralChoiceButton");
        confirm.Focus();
        Program.Assert(vm.IsGeneralSelectionPending && game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
            game.CreateSnapshot(0).Players.Count == 6 &&
            ReferenceEquals(Keyboard.FocusedElement, confirm) && confirm.IsVisible && confirm.IsEnabled,
            "The national repeat fixture must focus the actual primary confirmation button on its native private prompt.");
        var first = Route(repeat: false);
        Pump();
        var primaryState = game.CreateSnapshot(0).Players.Single(player => player.IsHuman);
        Console.WriteLine($"National fresh primary Shift+Enter: chosen={primary.GeneralId}, actual={primaryState.GeneralId}, " +
            $"preview handled={first.PreviewHandled}, bubbles={first.Bubbles}, handled={first.Handled}, " +
            $"accepted={accepted}->{game.AcceptedCommands.Count}, prompt={primaryPrompt}->{game.PendingDecision?.PromptId}, " +
            $"status={game.State.Status}, general panel={vm.IsGeneralSelectionPending}, focused={FocusName()}.");
        Program.Assert(!first.PreviewHandled && first.Bubbles == 1 && first.Handled &&
            ReferenceEquals(Program.Engine(vm), game) && game.AcceptedCommands.Count == accepted + 1 &&
            game.AcceptedCommands.Last() is SelectGeneralCommand primaryCommand && primaryCommand.GeneralId == primary.GeneralId &&
            primaryState.GeneralId == primary.GeneralId && primaryState.SecondaryGeneralId is null,
            "A fresh Shift+Enter must confirm only the primary through the native Button exactly once.");
        // Human commands deliberately stop before automatic setup progression.
        // Match the existing national fixture's bounded steps to publish the next
        // prompt; its deferred focus policy need not retain the old Button.
        var transitionSteps = 0;
        for (; transitionSteps < 8 && !vm.IsGeneralSelectionPending; transitionSteps++)
        {
            Program.Assert(vm.CanStepAi, "The national primary transition must remain available to the existing setup step command.");
            vm.StepAiCommand.Execute(null);
        }
        Pump();
        var secondary = vm.SelectedGeneralChoice ?? throw new InvalidOperationException("The national transition must publish a secondary preview.");
        Program.Assert(vm.IsGeneralSelectionPending && game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } secondaryPrompt &&
            secondaryPrompt.PromptId != primaryPrompt && confirm.IsVisible && confirm.IsEnabled,
            "The existing bounded setup steps must publish a fresh human secondary prompt and its actual confirmation button.");
        var focusAfterTransition = FocusName();
        confirm.Focus();
        var secondaryPromptId = game.PendingDecision!.PromptId;
        var secondaryAccepted = game.AcceptedCommands.Count;
        var boundary = Boundary(game);
        var held = Route(repeat: true);
        Pump();
        Console.WriteLine($"National held Shift+Enter: primary={primary.GeneralId}, secondary preview={secondary.GeneralId}, " +
            $"transition steps={transitionSteps}, transition focus={focusAfterTransition}, repeat focus={FocusName()}, " +
            $"preview handled={held.PreviewHandled}, bubbles={held.Bubbles}, handled={held.Handled}, " +
            $"prompt={secondaryPromptId}->{game.PendingDecision?.PromptId}, accepted={secondaryAccepted}->{game.AcceptedCommands.Count}.");
        Program.Assert(held.PreviewHandled && held.Bubbles == 0 && held.Handled && Boundary(game) == boundary &&
            ReferenceEquals(Program.Engine(vm), game) && vm.IsGeneralSelectionPending &&
            game.PendingDecision?.PromptId == secondaryPromptId && ReferenceEquals(vm.SelectedGeneralChoice, secondary) &&
            ReferenceEquals(Keyboard.FocusedElement, confirm),
            "A repeated Shift+Enter on the actual freshly focused secondary confirmation must not select its default general, change its preview or mutate any safe view or native journal.");
        var second = Route(repeat: false);
        Program.Assert(!second.PreviewHandled && second.Bubbles == 1 && second.Handled &&
            game.AcceptedCommands.Count == secondaryAccepted + 1 &&
            game.AcceptedCommands.Last() is SelectGeneralCommand secondaryCommand && secondaryCommand.GeneralId == secondary.GeneralId &&
            game.CreateSnapshot(0).Players.Single(player => player.IsHuman) is { } selected &&
            selected.GeneralId == primary.GeneralId && selected.SecondaryGeneralId == secondary.GeneralId && !vm.IsGeneralSelectionPending,
            "After releasing the held key, a fresh Shift+Enter must still confirm the actual secondary through its native Button exactly once.");

        string FocusName() => $"{Keyboard.FocusedElement?.GetType().Name}/{(Keyboard.FocusedElement as FrameworkElement)?.Name}";

        void Pump()
        {
            root.Measure(new Size(1120, 820)); root.Arrange(new Rect(0, 0, 1120, 820)); root.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        (bool PreviewHandled, int Bubbles, bool Handled) Route(bool repeat)
        {
            Program.Assert(ReferenceEquals(Keyboard.FocusedElement, confirm) && confirm.IsVisible && confirm.IsEnabled &&
                GetFocus() == new WindowInteropHelper(window).Handle &&
                ReferenceEquals(PresentationSource.FromVisual(confirm), PresentationSource.FromVisual(window)),
                "Each national key event must use the genuinely focused confirmation Button and its native host; repeat handling cannot change that focus.");
            return TableShortcutChecks.WithModifiers(ModifierKeys.Shift, () =>
            {
                var previews = 0;
                var bubbles = 0;
                KeyEventHandler observePreview = (_, args) => { if (args.Key == Key.Enter) previews++; };
                KeyEventHandler observeBubble = (_, args) => { if (args.Key == Key.Enter) bubbles++; };
                window.AddHandler(Keyboard.PreviewKeyDownEvent, observePreview, handledEventsToo: true);
                confirm.AddHandler(Keyboard.KeyDownEvent, observeBubble, handledEventsToo: true);
                try
                {
                    var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(confirm)!, 0, Key.Enter)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    OpeningCustomizationUiChecks.SetKeyRepeat(input, repeat);
                    confirm.RaiseEvent(input);
                    var previewHandled = input.Handled;
                    if (!input.Handled)
                    {
                        input.RoutedEvent = Keyboard.KeyDownEvent;
                        confirm.RaiseEvent(input);
                    }
                    Program.Assert(previews == 1, "The actual Window must observe exactly one national selection key tunnel.");
                    return (previewHandled, bubbles, input.Handled);
                }
                finally
                {
                    window.RemoveHandler(Keyboard.PreviewKeyDownEvent, observePreview);
                    confirm.RemoveHandler(Keyboard.KeyDownEvent, observeBubble);
                }
            });
        }
    }

    private static bool Shortcut(MainWindow window, Key key, bool repeat = false) => (bool)typeof(MainWindow)
        .GetMethod("HandleKeyInput", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [key, ModifierKeys.None, repeat])!;

    private static bool DoubleClick(UIElement source, MouseButton button = MouseButton.Left)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        };
        // WPF normally sets this while reading native input. Drive the actual preview
        // route so Control must generate the double-click and consume its second press.
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, 2);
        source.RaiseEvent(args);
        return args.Handled;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);

    private sealed class EditionPackage : IGameContentPackage
    {
        internal const string Mode = "identity:classic-variants-ui-5";
        internal const string FewerCandidates = "identity:classic-variants-ui-fewer-candidates";
        internal const string FewerVariants = "identity:classic-variants-ui-fewer-variants";
        internal static readonly string[] Ling = ["classic:ling-tong", "boundary:ling-tong"];
        internal static readonly string[] Zhao = ["classic:zhao-yun", "boundary:zhao-yun", "sp:zhao-yun", "classic:shen-zhao-yun", "ol:shen-zhao-yun", "classic:gao-da-yi-hao"];
        public PackageManifest Manifest { get; } = new("general-edition-ui-check", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            AddMode(Mode, Zhao, includeCao: true);
            AddMode(FewerCandidates, Zhao, includeCao: false);
            AddMode(FewerVariants, Zhao.Where(id => id != "sp:zhao-yun").ToArray(), includeCao: true);

            void AddMode(string id, IReadOnlyList<string> zhao, bool includeCao) => builder.AddMode(new(id, "版本切换检查", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "classic:standard-deck", GeneralCandidateCount: includeCao ? 3 : 2,
                GeneralPoolIds: Ling.Concat(zhao).Concat(includeCao ? new[] { "classic:cao-cao" } : []).ToArray())
                { GeneralVariantGroups = new Dictionary<string, IReadOnlyList<string>> { ["character:ling-tong"] = Ling, ["character:zhao-yun"] = zhao } });
        }
    }
}
