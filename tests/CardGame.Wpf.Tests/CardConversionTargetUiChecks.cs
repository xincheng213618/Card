using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class CardConversionTargetUiChecks
{
    public static void WushengSkillFirstSelectsRedCards(string output)
    {
        foreach (var skillId in new[] { "standard:wusheng", "classic:wusheng", "boundary:wusheng" })
        {
            var(model,_)=Create(false,skillId);
            using(model)
            {
                var window=new MainWindow(model);
                var root=(FrameworkElement)window.Content;
                try
                {
                    Layout(root);
                    var game=Program.Engine(model);
                    var revision=game.Revision;
                    var count=game.AcceptedCommands.Count;
                    var chip=model.HumanSkillCards.Single(s=>s.ContentId==skillId);
                    var black=model.Hand.First(c=>c.SuitGlyph=="♠");
                    var diamond=model.Hand.First(c=>c.SuitGlyph=="♦");
                    Program.Assert(chip.IsAvailable&&black.IsPlayable&&!model.HasSelection,
                        "The conversion skill chip is enabled before selecting a card; black Iron Chain has a native use.");
                    Click(BoundButton(root,model.ActivateHumanSkillCommand,chip));
                    Layout(root);
                    Program.Assert(model.HasSelection&&model.CanCancelAction&&!model.CanConfirmSelected&&
                        !black.IsPlayable&&!black.IsWushengConversion&&
                        model.Hand.Where(c=>c.SuitGlyph is "♥" or "♦").All(c=>c.IsPlayable&&c.IsWushengConversion&&
                            ReferenceEquals(c.Artwork,CardArt.Get(CardKind.Slash)))&&
                        model.HumanSkillCards.Single(s=>s.ContentId==skillId).StateText.Contains("已选择")&&
                        model.ActionHint.Contains("武圣")&&game.Revision==revision&&game.AcceptedCommands.Count==count,
                        "Skill-first Wusheng limits selection to real legal red conversions without committing a command.");
                    model.SelectCardCommand.Execute(black);
                    Program.Assert(model.Hand.All(c=>!c.IsSelected),"Wusheng mode rejects a native black card.");
                    Click(BoundButton(root,model.SelectCardCommand,diamond));
                    Layout(root);
                    Program.Assert(!model.IsMultiTargetCardSelected&&!model.ShowRecastAction&&!model.HasCenterChoices&&
                        model.EquipmentPlayChoices.Count==0&&diamond.KindLabel.Contains("武圣当杀")&&
                        model.ActionHint.Contains("当【杀】"),
                        "The red card is directly a Slash draft without a conversion popup or native Iron Chain/Recast mode.");
                    var expected=game.GetHumanLegalActions().Where(a=>a.CardId==diamond.Id&&a.ConversionSource?.SkillId==skillId).ToArray();
                    Program.Assert(model.Seats.Where(s=>s.IsLegalTarget).Select(s=>s.Seat).ToHashSet()
                        .SetEquals(expected.SelectMany(a=>a.TargetSeats)),"Wusheng highlights only its exact actual Slash targets.");
                    if(skillId=="boundary:wusheng")
                    {
                        Program.Assert(model.Seats.Single(s=>s.Seat==2).IsLegalTarget,"Boundary Diamond Slash highlights the distant seat.");
                        Program.Render(root,1120,740,System.IO.Path.Combine(output,"wusheng-skill-first-1120.png"));
                        Program.Render(root,1920,1080,System.IO.Path.Combine(output,"wusheng-skill-first-1920.png"));
                    }
                    model.ClearSelectionCommand.Execute(null);
                    Layout(root);
                    Program.Assert(!model.HasSelection&&black.IsPlayable&&model.Hand.All(c=>!c.IsWushengConversion&&
                        !c.KindLabel.Contains("武圣当杀")&&ReferenceEquals(c.Artwork,CardArt.Get(c.Kind))),
                        "Cancel restores native card availability and labels.");
                    model.ActivateHumanSkillCommand.Execute(chip);
                    Program.Assert(!model.HasSelection,"A retired skill chip cannot start a new draft.");
                    Click(BoundButton(root,model.ActivateHumanSkillCommand,model.HumanSkillCards.Single(s=>s.ContentId==skillId)));
                    Layout(root);
                    Click(BoundButton(root,model.ActivateHumanSkillCommand,model.HumanSkillCards.Single(s=>s.ContentId==skillId)));
                    Program.Assert(!model.HasSelection&&black.IsPlayable,"Reclicking the live Wusheng chip cancels its mode.");
                    Layout(root);
                    Click(BoundButton(root,model.ActivateHumanSkillCommand,model.HumanSkillCards.Single(s=>s.ContentId==skillId)));
                    Layout(root);
                    Click(BoundButton(root,model.SelectCardCommand,diamond));
                    Layout(root);
                    var action=expected.First(a=>a.TargetSeats.Count==1);
                    Click(BoundButton(root,model.SelectTargetCommand,model.Seats.Single(s=>s.Seat==action.TargetSeat)));
                    Program.Assert(model.CanConfirmSelected,"Skill-first red Slash is confirmable after selecting its real target.");
                    model.ConfirmSelectedCommand.Execute(null);
                    Program.Assert(game.AcceptedCommands.Count==count+1&&game.AcceptedCommands.Last() is PlayCardCommand paid&&
                        paid.CardId==diamond.Id&&paid.ConversionSource==action.ConversionSource&&
                        paid.TargetSeats.SequenceEqual(action.TargetSeats)&&!model.HasSelection&&
                        model.Hand.All(c=>!c.IsSelected&&!c.IsWushengConversion&&
                            !c.KindLabel.Contains("武圣当杀")&&ReferenceEquals(c.Artwork,CardArt.Get(c.Kind)))&&
                        model.Seats.All(s=>!s.IsSelectedTarget),
                        "Confirmation submits the exact material, conversion source and targets once.");
                }
                finally { window.Close(); }
            }
        }
    }

    public static void RedIronChainUsesSelectedSlashMode()
    {
        var (model, registry) = Create(guhuo: false);
        using (model)
        {
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                const BindingFlags aimAccess = BindingFlags.Instance | BindingFlags.NonPublic;
                var captureAim = typeof(MainViewModel).GetMethod("CapturePlayCardAimDraft", aimAccess)!;
                var restoreAim = typeof(MainViewModel).GetMethod("RestorePlayCardAimDraft", aimAccess)!;
                var aimContext = typeof(MainViewModel).GetMethod("IsCardAimContext", aimAccess)!;
                var targetlessAim = typeof(MainViewModel).GetMethod("CanAimCardWithoutTarget", aimAccess)!;
                object? retiredAim = null;
                GameEngine? retiredGame = null;
                CardViewModel? retiredCard = null;
                long retiredRevision = -1;
                foreach (var route in new[] { "button", "Enter" })
                {
                    model.LoadManualGameCommand.Execute(null);
                    var game = Program.Engine(model);
                    var card = model.Hand.First(item => item.Kind == CardKind.IronChain);
                    var native = game.GetHumanLegalActions().Where(action => action.CardId == card.Id).ToArray();
                    var slash = native.First(action => action.Kind == LegalActionKind.Slash &&
                        action.ConversionSource?.SkillId == "standard:wusheng" && action.TargetSeats.Count == 1);
                    Program.Assert(!model.HasSaveError && game.CreateSnapshot(0).Players[0].Hand.Single(item => item.Id == card.Id)
                            is { Kind: CardKind.IronChain, Suit: Suit.Heart } &&
                        native.Any(action => action.Kind == LegalActionKind.IronChain && action.ConversionSource is null) &&
                        native.Any(action => action.Kind == LegalActionKind.Recast && action.ConversionSource is null),
                        "The red-card fixture must publish native Iron Chain/Recast and a distinct actual Wusheng Slash source.");
                    var before = game.CreateCheckpoint();
                    var views = Views(game);
                    var commands = game.AcceptedCommands.Count;
                    var moves = game.CardMovements.Count;
                    var events = game.Events.Count;
                    var revision = game.Revision;
                    var prompt = game.CreateSnapshot(0).PendingDecision!.PromptId;
                    var cues = model.BattleCues.ToArray();
                    if (retiredAim is not null)
                    {
                        Program.Assert(!ReferenceEquals(game, retiredGame) && !ReferenceEquals(card, retiredCard) &&
                            card.Id == retiredCard!.Id && revision == retiredRevision,
                            "The real manual reload must replace game/card identity at the same checkpoint revision before testing a retired aim draft.");
                        Program.Assert(restoreAim.Invoke(model, [retiredAim]) is false && !model.HasSelection &&
                            model.Hand.All(item => !item.IsSelected) && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                            game.Revision == revision && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves &&
                            game.Events.Count == events && Views(game).SequenceEqual(views) && model.BattleCues.SequenceEqual(cues),
                            "A retired aim token restored conversion/targets after actual same-revision manual load or changed committed views/feedback.");
                    }
                    var copiedCard = new CardViewModel
                    {
                        Id = card.Id, Kind = card.Kind, Name = card.Name, KindLabel = card.KindLabel,
                        SuitGlyph = card.SuitGlyph, Rank = card.Rank, Description = card.Description,
                        IsPlayable = true, IsSelected = true
                    };
                    Program.Assert(captureAim.Invoke(model, [card]) is null && captureAim.Invoke(model, [copiedCard]) is null &&
                        aimContext.Invoke(model, [copiedCard]) is false,
                        "Aim capture must reject an unselected current card and a copied same-ID model outside the current hand.");
                    Layout(root);
                    Click(BoundButton(root, model.SelectCardCommand, card));
                    Layout(root);
                    Program.Assert(model.IsMultiTargetCardSelected && model.ShowRecastAction,
                        "The physical Iron Chain must begin in its genuine reversible native chain mode.");
                    var retired = SourceChoice(model, card.Id, slash.ConversionSource!);
                    var sourceButton = BoundButton(root, model.SelectEquipmentPlayChoiceCommand, retired);
                    Click(sourceButton);
                    Layout(root);
                    var targets = native.Where(action => action.Kind == LegalActionKind.Slash &&
                            action.ConversionSource == slash.ConversionSource)
                        .SelectMany(action => action.TargetSeats).ToHashSet();
                    Console.WriteLine($"Red Iron Chain {route}: source={slash.ConversionSource}, " +
                        $"multi={model.IsMultiTargetCardSelected}, recast={model.ShowRecastAction}, " +
                        $"legal={string.Join(',', model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat))}, " +
                        $"native={string.Join(',', targets.Order())}.");
                    Program.Assert(!model.IsMultiTargetCardSelected && !model.ShowRecastAction && !model.CanRecastSelected &&
                        model.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(targets) &&
                        !model.Seats.Single(seat => seat.Seat == 0).IsLegalTarget,
                        "Choosing the current Wusheng source must leave chain mode and highlight only its native Slash targets.");
                    model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 0));
                    Program.Assert(model.Seats.All(seat => !seat.IsSelectedTarget) && card.IsSelected,
                        "The switched Slash draft must reject self-selection without losing its physical card.");
                    var target = model.Seats.Single(seat => seat.Seat == slash.TargetSeats[0]);
                    Click(BoundButton(root, model.SelectTargetCommand, target));
                    var aimWatch = Stopwatch.StartNew();
                    var aim = captureAim.Invoke(model, [card]);
                    Program.Assert(aim is not null && model.CanConfirmSelected,
                        "The genuine selected Wusheng Slash and its target must produce an opaque aim draft.");
                    // Use the exact physical-card command executed by a hand
                    // press. Drag-threshold recovery must preserve its already
                    // selected conversion rather than selecting native chain.
                    model.SelectCardCommand.Execute(card);
                    Program.Assert(!card.IsSelected && !model.HasSelection && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                        aimContext.Invoke(model, [card]) is true && captureAim.Invoke(model, [card]) is null,
                        "The existing press must toggle the draft off while leaving its current physical card eligible for aim recovery.");
                    Program.Assert(restoreAim.Invoke(model, [aim!]) is true, "The current converted card draft did not restore after its ordinary press.");
                    Layout(root);
                    Program.Assert(model.CanConfirmSelected && !model.IsMultiTargetCardSelected && !model.ShowRecastAction &&
                        model.Hand.Where(item => item.IsSelected).SequenceEqual([card]) &&
                        model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(slash.TargetSeats) &&
                        targetlessAim.Invoke(model, [card]) is false && game.Revision == revision &&
                        game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves && game.Events.Count == events &&
                        Views(game).SequenceEqual(views) && model.BattleCues.SequenceEqual(cues),
                        "Recovering aim lost the actual physical card/Slash target, enabled a targetless commit or changed rules/player-safe views/feedback.");
                    aimWatch.Stop();
                    Console.WriteLine($"Aim draft recovery {route}: physical={card.Id}, target={target.Seat}, " +
                        $"accepted delta=0, safe views={views.Length}, cues unchanged; elapsed={aimWatch.Elapsed.TotalMilliseconds:F2}ms.");
                    retiredAim = aim; retiredGame = game; retiredCard = card; retiredRevision = revision;
                    Reject(model, sourceButton.Command!, retired,
                        "A retired conversion button must not toggle or erase the prepared Slash draft.");
                    Reject(model, model.SelectEquipmentPlayChoiceCommand, SourceChoice(model, card.Id, slash.ConversionSource!) with { },
                        "An equal copied source choice must not toggle or erase the prepared Slash draft.");
                    Program.Assert(!model.ActionHint.Contains("铁索连环 · 已选", StringComparison.Ordinal),
                        "The Slash hint may name its physical Iron Chain, but must not retain chain-target instructions.");
                    model.OpenContextGuideCommand.Execute(null);
                    Program.Assert(!model.CurrentGuideTitle.Contains("铁索连环的目标", StringComparison.Ordinal) &&
                        !model.CurrentGuideSteps.Any(step => step.Text.Contains("切换连环", StringComparison.Ordinal) ||
                            step.Text.Contains("点击「重铸换牌」", StringComparison.Ordinal)),
                        "The current guide must describe the selected Slash mode rather than Iron Chain targeting/recast.");
                    model.ToggleHelpCommand.Execute(null);
                    Layout(root);
                    var confirm = (Button)window.FindName("PlayCardButton");
                    Program.Assert(model.CanConfirmSelected && confirm.IsEnabled && confirm.Visibility == Visibility.Visible &&
                        confirm.Command!.CanExecute(confirm.CommandParameter) &&
                        model.Hand.Where(item => item.IsSelected).Select(item => item.Id).SequenceEqual([card.Id]) &&
                        model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(slash.TargetSeats) &&
                        game.Revision == revision && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves &&
                        game.Events.Count == events && Views(game).SequenceEqual(views),
                        "The exact current Slash draft must enable the actual confirm button while every rule/view remains unchanged.");
                    var reference = GameReplay.Restore(before, registry);
                    Accept(reference, Command(reference, slash));
                    if (route == "button") Click(confirm);
                    else Program.Assert(TableShortcutChecks.Input(window, Key.Enter),
                        "Enter must confirm the current single-target Wusheng draft.");
                    VerifyCommit(model, game, registry, reference, slash, commands, moves, revision, prompt,
                        $"Wusheng {route}");
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    public static void GuhuoKeepsPublishedWholeTargetCombinations()
    {
        var (model, registry) = Create(guhuo: true);
        using (model)
        {
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                var game = Program.Engine(model);
                var card = model.Hand.First(item => item.Kind == CardKind.BarbarianAssault);
                var native = game.GetHumanLegalActions().Where(action => action.CardId == card.Id).ToArray();
                var printed = native.Single(action => action.Kind == LegalActionKind.BarbarianAssault && action.ConversionSource is null);
                var chain = native.First(action => action.Kind == LegalActionKind.IronChain && action.TargetSeats.Count == 2 &&
                    action.ConversionSource?.SkillId == "classic:guhuo");
                var aoe = native.Single(action => action.Kind == LegalActionKind.BarbarianAssault &&
                    action.ConversionSource?.SkillId == "classic:guhuo");
                Program.Assert(printed.TargetSeats.Count == 0 && aoe.TargetSeats.SequenceEqual([1, 2, 3]) &&
                    chain.ConversionSource != aoe.ConversionSource &&
                    !native.Any(action => action.Kind == LegalActionKind.Recast && action.ConversionSource == chain.ConversionSource),
                    "The native Guhuo fixture must publish a targetless printed AOE, a no-Recast declared chain and an exact three-target declared AOE.");
                Layout(root);
                Click(BoundButton(root, model.SelectCardCommand, card));
                Layout(root);
                var retiredChainSource = SourceChoice(model, card.Id, chain.ConversionSource!);
                var oldSourceButton = BoundButton(root, model.SelectEquipmentPlayChoiceCommand, retiredChainSource);
                Click(oldSourceButton);
                Layout(root);
                var emptyChainHint = model.ActionHint;
                foreach (var seat in chain.TargetSeats)
                    Click(BoundButton(root, model.SelectTargetCommand, model.Seats.Single(item => item.Seat == seat)));
                var chainMode = model.IsMultiTargetCardSelected;
                var chainShowRecast = model.ShowRecastAction;
                var chainCanRecast = model.CanRecastSelected;
                var chainHint = model.ActionHint;
                var chainTargets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).Order().ToArray();
                model.OpenContextGuideCommand.Execute(null);
                var chainGuideSteps = model.CurrentGuideSteps.Select(step => step.Text).ToArray();
                model.ToggleHelpCommand.Execute(null);
                Layout(root);
                Click(BoundButton(root, model.SelectEquipmentPlayChoiceCommand, SourceChoice(model, card.Id, aoe.ConversionSource!)));
                Layout(root);
                Console.WriteLine($"Guhuo AOE: source={aoe.ConversionSource}, multi={model.IsMultiTargetCardSelected}, " +
                    $"combos={model.TargetCombinationChoices.Count}, native targets={string.Join(',', aoe.TargetSeats)}, " +
                    $"chain mode={chainMode}, chain recast={chainShowRecast}, chain hint={chainHint}.");
                AssertAoeChoices(model, card.Id, aoe);
                Program.Assert(chainMode && !chainShowRecast && !chainCanRecast && chainTargets.SequenceEqual(chain.TargetSeats.Order()) &&
                    !emptyChainHint.Contains("重铸", StringComparison.Ordinal) &&
                    !chainHint.Contains("重铸", StringComparison.Ordinal) &&
                    chainGuideSteps.All(step => !step.Contains("重铸", StringComparison.Ordinal)),
                    "A native declared Iron Chain must retain its exact portrait draft without offering nonexistent Recast in controls/hint/guide.");
                var retiredCombo = AoeChoice(model, card.Id, aoe);
                var oldComboButton = BoundButton(root, model.SelectTargetCombinationChoiceCommand, retiredCombo);
                model.LoadManualGameCommand.Execute(null);
                game = Program.Engine(model);
                card = model.Hand.Single(item => item.Id == card.Id);
                aoe = game.GetHumanLegalActions().Single(action => action.CardId == card.Id &&
                    action.Kind == LegalActionKind.BarbarianAssault && action.ConversionSource?.SkillId == "classic:guhuo");
                Layout(root);
                Click(BoundButton(root, model.SelectCardCommand, card));
                Layout(root);
                Click(BoundButton(root, model.SelectEquipmentPlayChoiceCommand, SourceChoice(model, card.Id, aoe.ConversionSource!)));
                Layout(root);
                AssertAoeChoices(model, card.Id, aoe);
                Program.Assert(!model.HasSaveError && !model.TargetCombinationChoices.Any(choice => ReferenceEquals(choice, retiredCombo)),
                    "Real reload must retire the previously rendered whole-target combination.");
                Reject(model, oldSourceButton.Command!, retiredChainSource,
                    "A pre-reload chain source must not switch the current AOE draft.");
                Reject(model, oldComboButton.Command!, retiredCombo,
                    "A pre-reload whole-target button must not submit the current AOE or lose its draft.");
                Reject(model, model.SelectTargetCombinationChoiceCommand, AoeChoice(model, card.Id, aoe) with { },
                    "An equal copied whole-target choice must not submit or lose the current AOE draft.");
                Reject(model, model.SelectEquipmentPlayChoiceCommand, SourceChoice(model, card.Id, aoe.ConversionSource!) with { },
                    "An equal copied AOE source must not switch or lose the current draft.");

                var withdrawnCombo = AoeChoice(model, card.Id, aoe);
                Click(BoundButton(root, model.SelectEquipmentPlayChoiceCommand, SourceChoice(model, card.Id, aoe.ConversionSource!)));
                Layout(root);
                Program.Assert(!model.IsMultiTargetCardSelected && !model.ShowRecastAction && model.CanConfirmSelected &&
                    model.TargetCombinationChoices.Count == 0 && model.Seats.All(seat => !seat.IsSelectedTarget) &&
                    !model.ActionHint.Contains("铁索连环 · 已选", StringComparison.Ordinal),
                    "Returning to the original printed AOE must enable its targetless ordinary use without a foreign-source chain/combo draft.");
                model.OpenContextGuideCommand.Execute(null);
                Program.Assert(!model.CurrentGuideTitle.Contains("铁索连环的目标", StringComparison.Ordinal),
                    "The printed AOE guide must not retain a previously selected declared chain mode.");
                model.ToggleHelpCommand.Execute(null);
                Layout(root);
                Reject(model, model.SelectTargetCombinationChoiceCommand, withdrawnCombo,
                    "A withdrawn declared AOE combination must not bypass the currently selected printed source.");
                Click(BoundButton(root, model.SelectEquipmentPlayChoiceCommand, SourceChoice(model, card.Id, aoe.ConversionSource!)));
                Layout(root);
                AssertAoeChoices(model, card.Id, aoe);
                var combo = AoeChoice(model, card.Id, aoe);
                var button = BoundButton(root, model.SelectTargetCombinationChoiceCommand, combo);
                var before = game.CreateCheckpoint();
                var reference = GameReplay.Restore(before, registry);
                Accept(reference, Command(reference, aoe));
                var commands = game.AcceptedCommands.Count;
                var moves = game.CardMovements.Count;
                var revision = game.Revision;
                var prompt = game.CreateSnapshot(0).PendingDecision!.PromptId;
                Click(button);
                VerifyCommit(model, game, registry, reference, aoe, commands, moves, revision, prompt, "Guhuo AOE combination");
                Program.Assert(game.CardMovements.Skip(moves).Count(move => move.CardId == card.Id &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing &&
                    move.Reason.Value == "conversion.declaration.pay") == 1,
                    "The actual declared AOE must pay its unique printed entity once before any optional challenge outcome.");
                Reject(model, button.Command!, combo, "The accepted AOE combination must not pay again or answer a later prompt.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static void AssertAoeChoices(MainViewModel model, int cardId, LegalAction action)
    {
        Program.Assert(!model.IsMultiTargetCardSelected && !model.ShowRecastAction && !model.CanRecastSelected &&
            model.HasTargetCombinationChoices && model.TargetCombinationChoices.Count > 0 &&
            model.TargetCombinationChoices.All(choice => choice.Cards.SequenceEqual([cardId]) &&
                MatchesSource(choice, action.ConversionSource!) && choice.Targets.SequenceEqual(action.TargetSeats)) &&
            model.TargetCombinationChoices.Any(choice => choice.Targets.SequenceEqual([1, 2, 3])),
            "The selected declared AOE must publish its exact complete native target combination, without another source's Iron Chain choices.");
    }

    private static PromptChoice AoeChoice(MainViewModel model, int cardId, LegalAction action) =>
        model.TargetCombinationChoices.Single(choice => choice.Cards.SequenceEqual([cardId]) &&
            MatchesSource(choice, action.ConversionSource!) && choice.Targets.SequenceEqual(action.TargetSeats));

    private static PromptChoice SourceChoice(MainViewModel model, int cardId, CardConversionSource source) =>
        model.EquipmentPlayChoices.Single(choice => choice.Cards.SequenceEqual([cardId]) && MatchesSource(choice, source));

    private static bool MatchesSource(PromptChoice choice, CardConversionSource source) =>
        choice.Parameters.GetValueOrDefault("conversion-skill-id") == source.SkillId &&
        choice.Parameters.GetValueOrDefault("conversion-binding-id") == source.BindingId &&
        choice.Parameters.GetValueOrDefault("conversion-owner-seat") == source.OwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
        choice.Parameters.GetValueOrDefault("conversion-instance-id") == source.SkillInstanceId;

    private static void Reject(MainViewModel model, ICommand command, PromptChoice parameter, string message)
    {
        Program.Assert(!model.EquipmentPlayChoices.Any(choice => ReferenceEquals(choice, parameter)) &&
            !model.TargetCombinationChoices.Any(choice => ReferenceEquals(choice, parameter)),
            "The rejected button parameter must actually be retired, withdrawn or copied outside both published choice lists.");
        var game = Program.Engine(model);
        var revision = game.Revision;
        var commands = game.AcceptedCommands.Count;
        var moves = game.CardMovements.Count;
        var events = game.Events.Count;
        var views = Views(game);
        var draft = Draft(model);
        command.Execute(parameter);
        Program.Assert(ReferenceEquals(game, Program.Engine(model)) && game.Revision == revision &&
            game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves && game.Events.Count == events &&
            Views(game).SequenceEqual(views) && Draft(model) == draft, message);
    }

    private static string Draft(MainViewModel model) => JsonSerializer.Serialize(new
    {
        Cards = model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray(),
        Targets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray(),
        Source = typeof(MainViewModel).GetField("_selectedConversionSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model),
        model.SelectedCardText, model.PromptText, model.ActionHint, model.HasSelection,
        model.IsMultiTargetCardSelected, model.CanConfirmSelected
    });

    private static void VerifyCommit(MainViewModel model, GameEngine game, ContentRegistry registry, GameEngine reference,
        LegalAction action, int commands, int moves, long revision, PromptId prompt, string label)
    {
        Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
            game.AcceptedCommands.Last() is PlayCardCommand submitted && submitted.ActorSeat == 0 &&
            submitted.CardId == action.CardId && submitted.TargetSeats.SequenceEqual(action.TargetSeats) &&
            submitted.PlayedCardKind == action.PlayedCardKind && submitted.TargetCardId == action.TargetCardId &&
            submitted.ConversionSource == action.ConversionSource && submitted.ExpectedRevision == revision && submitted.PromptId == prompt &&
            game.CardMovements.Skip(moves).Count(move => move.CardId == action.CardId &&
                move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 && !model.HasSelection &&
            Views(game).SequenceEqual(Views(reference)) && game.CardMovements.SequenceEqual(reference.CardMovements) &&
            CommandJson.Serialize(game.AcceptedCommands) == CommandJson.Serialize(reference.AcceptedCommands),
            $"{label} must submit one exact native PlayCard/source/target contract and pay its unique entity once, matching independent native execution.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(Views(game).SequenceEqual(Views(restored)) && game.CardMovements.SequenceEqual(restored.CardMovements) &&
            CommandJson.Serialize(game.AcceptedCommands) == CommandJson.Serialize(restored.AcceptedCommands),
            $"{label} must cold-restore all four player-safe views, exact commands and physical movements.");
    }

    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();

    private static PlayCardCommand Command(GameEngine game, LegalAction action) =>
        new(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId,
            action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources };

    private static Button BoundButton(FrameworkElement root, ICommand command, object parameter) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter),
            "The conversion regression must operate its real current enabled bound Button.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740));
        root.Arrange(new Rect(0, 0, 1120, 740));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.UpdateLayout();
    }

    private static (MainViewModel, ContentRegistry) Create(bool guhuo,string? wusheng=null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(guhuo,wusheng));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } general &&
            general.ValidContentIds.Contains(Fixture.Owner), "The fixed four-seat fixture must publish its actual native owner choice.");
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed conversion fixture must reach a real human Play prompt through native accepted commands.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        try
        {
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && Views(Program.Engine(model)).SequenceEqual(Views(game)),
                "The actual manual-load command must restore every player-safe fixture view.");
            return (model, registry);
        }
        catch { model.Dispose(); throw; }
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "The native conversion fixture rejected a command.");
    }

    private sealed class Fixture(bool guhuo,string? wusheng) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-conversion-target-ui";
        internal const string Owner = "fixture:conversion-target-owner";
        public PackageManifest Manifest { get; } = new("fixture-conversion-target-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "原生转换入口", "supporter", wusheng??(guhuo ? "classic:guhuo" : "standard:wusheng"), "qun", 6));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:conversion-target-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "当前合法目标", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:conversion-target-deck", "固定真实转换实体", wusheng is null?2:8, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(
                    guhuo ? "standard:barbarian_assault" : "standard:iron_chain",
                    wusheng is null?Suit.Heart:index%3==0?Suit.Spade:index%3==1?Suit.Heart:Suit.Diamond, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "转换目标输入", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:conversion-target-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
