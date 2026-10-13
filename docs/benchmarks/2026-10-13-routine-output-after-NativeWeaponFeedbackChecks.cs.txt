using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Audio;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class NativeWeaponFeedbackChecks
{
    public static void NativeWeapons(string output)
    {
        BattleAudioBeatChecks.BeginNativeEvidence();
        var errors = new BindingListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            StoneAxe(output);
            Qinglong(output);
            FastEightSeatChain(output);
            Assert(errors.Errors.Count == 0, "Native weapon binding errors: " + string.Join(Environment.NewLine, errors.Errors));
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            BattleAudioBeatChecks.WriteNativeEvidence(output);
        }
    }

    private static void StoneAxe(string output)
    {
        var witness = StoneAxeScenario.FindHumanTrigger(seed: 1);
        using var vm = RestoreBeforeSlash(witness, CardKind.StoneAxe);
        using var host = new NativeHost(vm, "native-stone-axe-seed1");
        ReachNativeWeapon(host, witness, DecisionKind.StoneAxe, output, "stone-axe");
        var game = Engine(vm);
        var original = Boundary(game);
        vm.SaveGameCommand.Execute(null);
        Assert(!vm.HasSaveError && Same(game, original), "Saving the native weapon prompt changed its actual pending state.");
        var targetHp = Hp(game, witness.TargetSeat);
        var sourceHp = Hp(game, 0);
        var cursor = game.Events.Count; var commands = game.AcceptedCommands.Count;
        var moveCursor = game.CardMovements.Count;
        var use = vm.SkillChoices.First(choice => choice.Parameters.GetValueOrDefault("action") == "stone-axe-use" &&
            choice.Cards.Contains(witness.StoneAxeCardId));
        var cost = use.Cards.ToArray();
        var promptSounds = host.AudioOutput.Count(GameSound.Prompt);
        Assert(cost.Length == 2 && cost.Distinct().Count() == 2, "The actual axe choice lost its exact two-card cost.");
        SubmitChoice(vm, use);
        Assert(host.AudioOutput.Count(GameSound.StoneAxe) == 1 && host.AudioOutput.Count(GameSound.Hit) == 0 &&
            host.AudioOutput.Count(GameSound.Prompt) == promptSounds + 1,
            "The real paid axe and new Zhiman prompt must sound immediately while its uncommitted hit remains silent.");
        var resolved = game.Events.Skip(cursor).Select(item => item.Payload).OfType<StoneAxeResolvedEvent>().Single();
        var cue = AssertWeaponCue(vm, resolved, CardKind.StoneAxe, true, resolved.ResolutionId, witness.TargetSeat);
        Assert(resolved.SourceSeat == 0 && resolved.TargetSeat == witness.TargetSeat && resolved.DiscardedCardIds.SequenceEqual(cost) &&
            game.AcceptedCommands.Count == commands + 1 &&
            game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: "classic:zhiman" } &&
            Enumerable.Range(1, 4).All(seat => game.CreateSnapshot(seat).PendingDecision is null) &&
            Hp(game, witness.TargetSeat) == targetHp && !game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent),
            "Paying the native axe must retain its real private optional pre-damage child, without inventing a guaranteed hit.");
        AssertCost(game, cost, moveCursor, CardMoveReasons.StoneAxeDiscard);
        Assert(!game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == witness.StoneAxeCardId),
            "The physical weapon included in the exact native cost remained equipped.");

        // Do not advance the injected presentation clock between paying and
        // answering the genuine pre-damage child. Rules commit immediately;
        // only the later drawing waits for the public axe's arrival.
        var pass = vm.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        SubmitChoice(vm, pass);
        var damage = game.Events.Skip(cursor).Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .Single(item => item.SourceSeat == 0 && item.TargetSeat == witness.TargetSeat);
        Assert(damage.Amount == 1 && damage.RemainingHp == targetHp - 1 && Hp(game, witness.TargetSeat) == damage.RemainingHp &&
            Hp(game, 0) == sourceHp && game.AcceptedCommands.Count == commands + 2 &&
            game.Events.Skip(cursor).Count(item => item.Payload is StoneAxeResolvedEvent) == 1 &&
            vm.Seats.Single(seat => seat.Seat == witness.TargetSeat).Hp == damage.RemainingHp,
            "The real Zhiman pass must commit one actual axe damage immediately, independently of visual travel.");
        AssertCost(game, cost, moveCursor, CardMoveReasons.StoneAxeDiscard);
        AssertRetiredPrompt(vm, resolved.ResolutionId);
        Assert(host.AudioOutput.Count(GameSound.Hit) == 0,
            "The real axe damage sound ignored the visual .24s arrival even though its rules HP was already committed.");
        WeaponFrames(host, output, "stone-axe-paid", cue, witness.TargetSeat, expectDamage: true, record: RecordMotion);
        ClearAndReload(host, original, output, "stone-axe", use);
        game = Engine(vm); cursor = game.Events.Count; commands = game.AcceptedCommands.Count;
        var skip = vm.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "stone-axe-skip");
        var weaponSounds = host.AudioOutput.Count(GameSound.StoneAxe);
        SubmitChoice(vm, skip);
        var declined = game.Events.Skip(cursor).Select(item => item.Payload).OfType<StoneAxeResolvedEvent>().Single();
        AssertWeaponCue(vm, declined, CardKind.StoneAxe, false, declined.ResolutionId, witness.TargetSeat);
        Assert(!declined.Used && declined.DiscardedCardIds.Count == 0 && game.AcceptedCommands.Count == commands + 1 &&
            Hp(game, witness.TargetSeat) == targetHp && Hp(game, 0) == sourceHp &&
            !game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent),
            "The native axe decline must preserve the actual Dodge, physical cards and target HP.");
        AssertDeclineFinishesOriginalSlash(game, witness, original.Movements, cursor, declined.ResolutionId);
        AssertRetiredPrompt(vm, declined.ResolutionId);
        DeclinedFrames(host, output, "stone-axe-declined", witness.TargetSeat);
        Assert(host.AudioOutput.Count(GameSound.StoneAxe) == weaponSounds,
            "Declining the actual axe played a phantom weapon activation sound.");
        ResetWithoutReplay(host, output, "stone-axe");
        Console.WriteLine($"Native Stone Axe: fixed seed1, equipped physical{witness.StoneAxeCardId}, Slash{witness.SlashAction.CardId}, " +
            $"actual target{witness.TargetSeat} Dodge; exact cost[{string.Join(',', cost)}], Zhiman skip, HP{targetHp}->{damage.RemainingHp} " +
            $"committed before .24s visual arrival; same checkpoint decline HP{targetHp}; all5 safe views stable during sampling.");
    }

    private static void Qinglong(string output)
    {
        // This is the same verified small setup/seed, with only the physical
        // weapon definition replaced. Never call the shared Qinglong seed search.
        var witness = StoneAxeScenario.FindHumanTrigger(seed: 1, weapon: CardKind.QinglongCrescentBlade,
            expectedDecision: DecisionKind.QinglongCrescentBlade);
        using var vm = RestoreBeforeSlash(witness, CardKind.QinglongCrescentBlade);
        using var host = new NativeHost(vm, "native-qinglong-seed1");
        ReachNativeWeapon(host, witness, DecisionKind.QinglongCrescentBlade, output, "qinglong");
        var game = Engine(vm); var original = Boundary(game); var targetHp = Hp(game, witness.TargetSeat);
        vm.SaveGameCommand.Execute(null);
        Assert(!vm.HasSaveError && Same(game, original), "Saving the real Qinglong prompt changed its pending follow-up materials.");
        var available = vm.SkillChoices.Where(choice => choice.Parameters.GetValueOrDefault("action") == "qinglong-slash").ToArray();
        if (available.Length > 0)
        {
            var use = available[0]; var cardId = use.Cards.Single();
            var printed = game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == cardId);
            var cursor = game.Events.Count; var commands = game.AcceptedCommands.Count; var moveCursor = game.CardMovements.Count;
            Assert(use.Targets.SequenceEqual(new[] { witness.TargetSeat }) && printed.Kind == CardKind.Slash,
                "The fixed native Qinglong follow-up must use a real available physical Slash against the original target.");
            SubmitChoice(vm, use);
            Assert(host.AudioOutput.Count(GameSound.Qinglong) == 1 && host.AudioOutput.Count(GameSound.Card) == 2 &&
                host.AudioOutput.Count(GameSound.Hit) == 0,
                "The actual Qinglong and independent new physical Slash must both sound once without inventing a weapon hit or replaying the VM batch.");
            var resolved = game.Events.Skip(cursor).Select(item => item.Payload).OfType<QinglongCrescentBladeResolvedEvent>().Single();
            var cue = AssertWeaponCue(vm, resolved, CardKind.QinglongCrescentBlade, true, resolved.ResolutionId, witness.TargetSeat);
            var slash = game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardUsedEvent>()
                .Single(item => item.SourceSeat == 0 && item.TargetSeat == witness.TargetSeat);
            var declared = game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Single();
            Assert(resolved.SourceSeat == 0 && resolved.SlashCardIds.SequenceEqual(new[] { cardId }) &&
                resolved.EffectiveSlashKind == CardKind.Slash && slash.CardId == cardId && declared.CardId == cardId &&
                declared.CardKind == CardKind.Slash && declared.ResolutionId != resolved.ResolutionId &&
                game.AcceptedCommands.Count == commands + 1 && !vm.Hand.Any(card => card.Id == cardId) &&
                game.CardMovements.Skip(moveCursor).Count(move => move.CardId == cardId && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1 &&
                Hp(game, witness.TargetSeat) == targetHp && !game.Events.Skip(cursor).Any(item => item.Payload is DamageAppliedEvent),
                "Native Qinglong activation must pay exactly one real new Slash once and open its own resolution, without claiming a weapon hit.");
            var cardCue = vm.BattleCues.Single(item => item.Sequence == game.Events.Single(item => ReferenceEquals(item.Payload, declared)).Sequence);
            Assert(cardCue is { Kind: BattleCueKind.Card, CardKind: CardKind.Slash, SourceSeat: 0 } &&
                cardCue.PublicCardId == cardId && cardCue.PublicCard?.Id == cardId && cardCue.TargetSeats.SequenceEqual(new[] { witness.TargetSeat }),
                "The Qinglong follow-up lost the independent real Slash face/flight or used the weapon as a synthetic card.");
            AssertRetiredPrompt(vm, resolved.ResolutionId);
            WeaponFrames(host, output, "qinglong-paid", cue, witness.TargetSeat, expectDamage: false, record: false);
            ClearAndReload(host, original, output, "qinglong", use);
            Console.WriteLine($"Native Qinglong: fixed seed1 weapon-only deck variant, physical weapon{witness.StoneAxeCardId}, " +
                $"original Slash{witness.SlashAction.CardId}->actual target{witness.TargetSeat} Dodge; paid physical Slash{cardId}, " +
                $"public resolutions {resolved.ResolutionId}->{declared.ResolutionId}, one command/cost, no invented hit.");
        }
        else
        {
            Assert(vm.SkillChoices.All(choice => choice.Parameters.GetValueOrDefault("action") == "qinglong-skip") &&
                !game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash),
                "The fixed seed's empty Qinglong materials must expose only the actual decline boundary.");
            Console.WriteLine("Native Qinglong: fixed seed1 weapon-only variant has no remaining physical Slash; genuine decline boundary only, no seed search or invented pursuit.");
        }
        game = Engine(vm); var declineCursor = game.Events.Count; var declineCommands = game.AcceptedCommands.Count;
        var weaponSounds = host.AudioOutput.Count(GameSound.Qinglong);
        SubmitChoice(vm, vm.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "qinglong-skip"));
        var declined = game.Events.Skip(declineCursor).Select(item => item.Payload).OfType<QinglongCrescentBladeResolvedEvent>().Single();
        AssertWeaponCue(vm, declined, CardKind.QinglongCrescentBlade, false, declined.ResolutionId, witness.TargetSeat);
        Assert(!declined.Used && declined.SlashCardIds.Count == 0 && game.AcceptedCommands.Count == declineCommands + 1 &&
            Hp(game, witness.TargetSeat) == targetHp &&
            !game.Events.Skip(declineCursor).Any(item => item.Payload is CardUseDeclaredEvent or DamageAppliedEvent),
            "The actual Qinglong decline invented a follow-up card, weapon cost or target damage.");
        AssertDeclineFinishesOriginalSlash(game, witness, original.Movements, declineCursor, declined.ResolutionId);
        AssertRetiredPrompt(vm, declined.ResolutionId);
        DeclinedFrames(host, output, "qinglong-declined", witness.TargetSeat);
        Assert(host.AudioOutput.Count(GameSound.Qinglong) == weaponSounds,
            "Declining the actual Qinglong played a phantom pursuit sound.");
        ResetWithoutReplay(host, output, "qinglong");
    }

    private static MainViewModel RestoreBeforeSlash(StoneAxeBoundary witness, CardKind weapon)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, witness.BeforeSlash));
        var vm = new MainViewModel(autoAdvance: false, seed: 1, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: witness.Game.ContentRegistry);
        vm.LoadManualGameCommand.Execute(null);
        vm.SelectedPlaybackSpeed = PlaybackSpeed.Normal; vm.IsMotionEnabled = true;
        var game = Engine(vm);
        Assert(!vm.HasSaveError && game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
            game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == witness.StoneAxeCardId && card.Kind == weapon) &&
            game.Events.Any(item => item.Payload is CardUseDeclaredEvent declared && declared.CardId == witness.StoneAxeCardId),
            "The fixed native weapon fixture must load its genuinely equipped physical weapon and current legal Slash boundary.");
        return vm;
    }

    private static void FastEightSeatChain(string output)
    {
        // Reuse the already-verified eight-seat pool, deck and four real chain
        // payments. Only this final physical Thunder Slash is commanded by the
        // VM; the input checkpoint is an actual committed setup, never edited.
        var (registry, prepared, slash) = LongChainFeedbackChecks.CreateFixture();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
        using var vm = new MainViewModel(autoAdvance: false, seed: prepared.Seed, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry, historyStore: new MemoryMatchHistoryStore(),
            preferencesStore: new MemoryPlayerPreferencesStore());
        vm.LoadManualGameCommand.Execute(null);
        var game = Engine(vm);
        Assert(!vm.HasSaveError && Enumerable.Range(0, 8).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
            SnapshotJson.Serialize(prepared.CreateSnapshot(seat))),
            "The actual fast chain checkpoint failed to preserve all eight viewer-safe prepared states.");
        vm.SelectedPlaybackSpeed = PlaybackSpeed.Fast; vm.IsMotionEnabled = true;
        using var host = new NativeHost(vm, "native-fast-chain-seed2");
        host.Capture(output, "audio-native-fast-chain-before", 1120);
        var scale = vm.BattleAnimationTimeScale;
        Assert(Math.Abs(host.Layer.PlaybackTimeScale - scale) < .000001 && scale < 1 &&
            game.CreateSnapshot(0).Players.Count == 8 && game.CreateSnapshot(0).Players.All(player => player.IsAlive && player.IsChained),
            "The loaded native chain did not use the actual fast binding and all eight living chained players.");
        var commands = game.AcceptedCommands.Count; var cursor = game.Events.Count;
        var draft = Boundary(game);
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == slash.Id));
        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 1));
        Assert(vm.CanConfirmSelected && host.AudioOutput.Events.Count == 0 && game.AcceptedCommands.Count == commands &&
            Same(game, draft),
            "Preparing the real fast Thunder Slash committed a command, exposed another safe view or requested uncommitted audio.");
        vm.ConfirmSelectedCommand.Execute(null);
        var committed = game.Events.Skip(cursor).ToArray();
        var damage = committed.Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray();
        var relays = committed.Select(item => item.Payload).OfType<ChainedDamagePropagatedEvent>().ToArray();
        var declaration = committed.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Single();
        Console.WriteLine($"Native fast chain committed facts: commands {commands}->{game.AcceptedCommands.Count}; " +
            $"declared {declaration.CardId}:{declaration.CardKind} source={declaration.SourceSeat}, printed={declaration.PublicCard?.Kind}; " +
            $"damage [{string.Join("; ", damage.Select(item => $"{item.SourceSeat}->{item.TargetSeat} amount={item.Amount} nature={item.Nature}"))}]; " +
            $"relays [{string.Join("; ", relays.Select(item => $"{item.SourceSeat}:{item.FromSeat}->{item.TargetSeat} amount={item.Amount} nature={item.Nature}"))}]; " +
            $"cues Damage={vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.Damage)}, " +
            $"ThunderDamage={vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.Damage && cue.Nature == DamageNature.Thunder)}, " +
            $"Chain={vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation)}; " +
            $"audio Card={host.AudioOutput.Count(GameSound.Card)}, Thunder={host.AudioOutput.Count(GameSound.Thunder)}.");
        Assert(declaration is { SourceSeat: 0, CardKind: CardKind.ThunderSlash } && declaration.CardId == slash.Id && slash.Id == 37,
            "The native physical Thunder37 must declare its actual effective Thunder Slash from seat0.");
        Assert(game.AcceptedCommands.Count == commands + 1 && damage.Length == 8 && relays.Length == 7 &&
            damage.All(item => item.SourceSeat == 0 && item.Amount == 1 && item.Nature == DamageNature.Thunder) &&
            damage.Select(item => item.TargetSeat).SequenceEqual(new[] { 1, 0, 2, 3, 4, 5, 6, 7 }),
            "One real native Thunder Slash must commit exactly eight public hits/seven relays in the actual seat enumeration order.");
        Assert(
            vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.Damage && cue.Nature == DamageNature.Thunder) == 8 &&
            vm.BattleCues.Count(cue => cue.Kind == BattleCueKind.ChainPropagation) == 7,
            "The real native fast chain lost one of eight public Thunder hits or seven relay cues.");
        Assert(host.AudioOutput.Count(GameSound.Card) == 1 && host.AudioOutput.Count(GameSound.Thunder) == 0,
            "The real native fast chain must sound its launch once before any paced result.");
        var settled = Boundary(game);
        for (var frame = 1; frame <= 250; frame++)
        {
            host.Clock.Advance(.005);
            BattleAudioBeatChecks.Tick(host.Layer);
            Assert(game.Revision == settled.Revision && game.AcceptedCommands.Count == settled.Commands && game.Events.Count == settled.Events &&
                game.CardMovements.Count == settled.Movements,
                "Sampling the real fast chain's audio requests changed its committed rule command or physical cards.");
        }
        host.Layer.InvalidateVisual(); host.Capture(output, "audio-native-fast-chain-retired", 1120);
        var thunders = host.AudioOutput.Events.Where(item => item.Sound == GameSound.Thunder.ToString()).ToArray();
        var firstMilliseconds = .42 * scale * 1000;
        var intervalMilliseconds = .36 * scale * 1000;
        Assert(thunders.Length == damage.Length && thunders.Select((item, index) =>
                item.Milliseconds >= firstMilliseconds + index * intervalMilliseconds - .001 &&
                item.Milliseconds <= firstMilliseconds + index * intervalMilliseconds + 5.001).All(matches => matches) &&
            thunders.All(item => item.MaximumStartDelayMilliseconds is { } budget && Math.Abs(budget - 120 * scale) < .001) &&
            host.AudioOutput.Count(GameSound.Card) == 1 && host.Layer.ActiveEffectCount == 0 && !host.Layer.IsFrameTimerRunning &&
            Same(game, settled),
            "Fast playback swallowed a same-sound recipient, replayed its launch, missed its visual cadence/start budget or changed one of eight safe views. " +
            "Actual Thunder request milliseconds: " + string.Join(", ", thunders.Select(item => item.Milliseconds.ToString("F3"))));
        File.WriteAllText(Path.Combine(output, "native-fast-chain-events.json"), System.Text.Json.JsonSerializer.Serialize(
            host.AudioOutput.Events, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Native fast chain audio: seed2, Thunder37, commands{commands}->{game.AcceptedCommands.Count}, " +
            $"8 public hits/7 relays, scale{scale:F6}, 5ms controlled ticks, Thunder requests[{string.Join(',', thunders.Select(item => item.Milliseconds.ToString("F3")))}]ms; " +
            $"first≈{firstMilliseconds:F3}ms, interval≈{intervalMilliseconds:F3}ms, all8 safe views unchanged; recording output only, no speaker capture.");
    }

    private static void ReachNativeWeapon(NativeHost host, StoneAxeBoundary witness, DecisionKind kind, string output, string label)
    {
        var vm = host.Model; var game = Engine(vm); var cursor = game.Events.Count; var commands = game.AcceptedCommands.Count;
        host.Capture(output, $"weapon-{label}-equipped", 1120);
        var silentMotionCard = kind == DecisionKind.QinglongCrescentBlade;
        if (silentMotionCard)
        {
            vm.IsMotionEnabled = false;
            host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.Root.UpdateLayout();
        }
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == witness.SlashAction.CardId));
        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == witness.TargetSeat));
        Assert(vm.CanConfirmSelected, "The actual equipped-weapon fixture's physical Slash could not be prepared natively.");
        vm.ConfirmSelectedCommand.Execute(null);
        Assert(host.AudioOutput.Count(GameSound.Card) == 1,
            "The real native Slash lost its immediate card sound or duplicated the visual/VM routes.");
        if (silentMotionCard)
        {
            Assert(host.Layer.ActiveEffectCount == 0, "The real motion-disabled native Slash created a historical animation queue.");
            vm.IsMotionEnabled = true;
            host.Root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.Root.UpdateLayout();
            Assert(host.AudioOutput.Count(GameSound.Card) == 1 && host.Layer.ActiveEffectCount == 0,
                "Restoring motion replayed the native Slash already sounded through its immediate fallback.");
        }
        Assert(game.AcceptedCommands.Count == commands + 1 && game.Events.Skip(cursor).Any(item =>
            item.Payload is CardUseDeclaredEvent { SourceSeat: 0, CardKind: CardKind.Slash } declared && declared.CardId == witness.SlashAction.CardId),
            "The actual hand and portrait commands must commit the fixture's physical Slash once.");
        for (var step = 0; step < 16 && game.CreateSnapshot(0).PendingDecision?.Kind != kind; step++)
        {
            Assert(game.CreateSnapshot(0).PendingDecision is null,
                "The fixed native weapon path reached a different human prompt; do not search another seed.");
            var before = game.AcceptedCommands.Count; vm.StepAiCommand.Execute(null);
            Assert(game.AcceptedCommands.Count == before + 1, "A native AI step did not advance the genuine target Dodge boundary.");
        }
        var own = game.CreateSnapshot(0).PendingDecision;
        var dodge = game.Events.Skip(cursor).Select(item => item.Payload).OfType<CardRespondedEvent>()
            .Single(item => item.ResponderSeat == witness.TargetSeat);
        Assert(own is { PlayerSeat: 0, IsPrivate: true } && own.Kind == kind && vm.IsSkillSelectionPending &&
            dodge.PublicCard?.Kind == CardKind.Dodge && Enumerable.Range(1, 4).All(seat => game.CreateSnapshot(seat).PendingDecision is null) &&
            Enumerable.Range(1, 4).All(seat => game.CreateSnapshot(seat).Players[0].Hand.Count == 0),
            "The fixed real target Dodge must expose the weapon choices only to its current source, preserving all five filtered views.");
        Assert(Enumerable.Range(0, 5).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
            SnapshotJson.Serialize(witness.Game.CreateSnapshot(seat))),
            "Native hand/target/AI commands departed from the verified fixed shared weapon witness.");
        var promptId = own?.PromptId ?? throw new InvalidOperationException("The verified native weapon prompt disappeared before its wait.");
        Assert(host.AudioOutput.Count(GameSound.Prompt) == 1 && host.AudioOutput.Events.Single(item => item.Sound == GameSound.Prompt.ToString()).Milliseconds == 0,
            "The genuine new human weapon prompt waited for attack/Dodge animation instead of retaining its independent immediate attention sound.");
        host.Clock.Advance(2); host.Layer.InvalidateVisual();
        foreach (var width in new[] { 1120, 1920 }) host.Capture(output, $"weapon-{label}-await-choice", width, saveImage: width != 1120);
        host.Capture(output, $"weapon-{label}-await-choice", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && game.CreateSnapshot(0).PendingDecision?.PromptId == promptId,
            "The genuine long weapon choice wait retained an old transient Slash/Dodge or answered its prompt.");
        Console.WriteLine($"Native {label} boundary: seed1 physical weapon{witness.StoneAxeCardId}, Slash{witness.SlashAction.CardId}, " +
            $"actual Dodge{dodge.CardId} from target{witness.TargetSeat}, native commands+{game.AcceptedCommands.Count - commands}, private prompt{promptId}.");
    }

    private static BattleCue AssertWeaponCue(MainViewModel vm, IGameEvent fact, CardKind weapon, bool used, long resolution, int target)
    {
        var envelope = Engine(vm).Events.Single(item => ReferenceEquals(item.Payload, fact));
        var cue = vm.BattleCues.Single(item => item.Sequence == envelope.Sequence);
        Assert(cue.Kind == BattleCueKind.Weapon && !cue.IsInquiry && cue.Revision == envelope.Revision &&
            cue.WeaponKind == weapon && cue.WeaponResolutionId == resolution && cue.IsWeaponActivated == used &&
            cue.SourceSeat == 0 && cue.TargetSeats.SequenceEqual(new[] { target }) &&
            cue.CardKind is null && cue.PublicCardId is null && cue.PublicCard is null && !cue.HasResponseProgress &&
            !new[] { "伤害已造成", "命中成功", "闪避失败" }.Any(claim => (cue.Label + cue.Detail).Contains(claim, StringComparison.Ordinal)),
            "The native weapon cue must derive exactly from its public used/declined fact, with no synthetic physical face or guaranteed damage claim.");
        return cue;
    }

    private static void SubmitChoice(MainViewModel vm, PromptChoice choice)
    {
        var game = Engine(vm); var prompt = game.CreateSnapshot(0).PendingDecision!; var before = game.AcceptedCommands.Count;
        Assert(vm.IsSkillSelectionPending && vm.SkillChoices.Any(current => ReferenceEquals(current, choice)),
            "The weapon/child answer must be the exact current published native choice object.");
        vm.SelectSkillChoiceCommand.Execute(choice);
        Assert(game.AcceptedCommands.Count == before + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.PromptId == prompt.PromptId && answer.Choice == choice.Id,
            "A native weapon/child choice must submit one exact current answer command.");
        vm.SelectSkillChoiceCommand.Execute(choice);
        Assert(game.AcceptedCommands.Count == before + 1, "A retired weapon button paid or submitted its choice twice.");
    }

    private static void AssertCost(GameEngine game, int[] cards, int cursor, CardMoveReason reason)
    {
        foreach (var id in cards)
        {
            var paid = game.CardMovements.Skip(cursor).Where(move => move.CardId == id && move.Reason == reason).ToArray();
            Assert(paid.Length == 2 && paid[0].To == CardLocation.Processing && paid[1].From == CardLocation.Processing &&
                paid[1].To == CardLocation.DiscardPile,
                $"The actual weapon cost physical{id} must move to Processing and Discard once each, without paying on resume.");
        }
    }

    private static void AssertDeclineFinishesOriginalSlash(GameEngine game, StoneAxeBoundary witness,
        int movementCursor, int eventCursor, long resolution)
    {
        // Both real decline paths call CompleteAttack. The canceled physical
        // Slash is still in Processing at the weapon prompt, so completing it
        // must perform its one normal UseFinished movement to Discard.
        var tail = game.CardMovements.Skip(movementCursor).ToArray();
        var finished = game.Events.Skip(eventCursor).Select(item => item.Payload).OfType<CardUseFinishedEvent>().ToArray();
        Assert(tail.Length == 1 && tail[0].CardId == witness.SlashAction.CardId && tail[0].CardKind == CardKind.Slash &&
            tail[0].From == CardLocation.Processing && tail[0].To == CardLocation.DiscardPile && tail[0].Reason == CardMoveReasons.UseFinished &&
            finished.Length == 1 && finished[0].ResolutionId == resolution && finished[0].CardId == witness.SlashAction.CardId &&
            !game.Events.Skip(eventCursor).Any(item => item.Payload is CardUseDeclaredEvent or CardUsedEvent) &&
            game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == witness.StoneAxeCardId),
            "Declining the real weapon must only finish the original physical Slash once; no extra weapon cost, new play or equipment removal is allowed. " +
            "Actual tail: " + string.Join("; ", tail.Select(move => $"physical{move.CardId} {move.From}->{move.To} {move.Reason}")));
        Console.WriteLine($"Native weapon decline completion: publicresolution{resolution}, original Slash{witness.SlashAction.CardId}, " +
            "one Processing->Discard card.use-finished movement; no extra cost/new play.");
    }

    private static void AssertRetiredPrompt(MainViewModel vm, long resolution)
    {
        var current = Engine(vm).CreateSnapshot(0).PendingDecision;
        Assert(current?.Kind is not (DecisionKind.StoneAxe or DecisionKind.QinglongCrescentBlade) &&
            vm.BattleCues.Any(cue => cue.Kind == BattleCueKind.Weapon && cue.WeaponResolutionId == resolution),
            "The actual resolved weapon retained its old native choice instead of proceeding to the current rules boundary.");
    }

    private static void WeaponFrames(NativeHost host, string output, string label, BattleCue cue, int target, bool expectDamage, bool record)
    {
        var game = Engine(host.Model); var boundary = Boundary(game);
        var stages = new Dictionary<int, string> { [40] = "startup-40ms", [180] = "travel-180ms", [260] = "arrival-260ms", [600] = "tail-600ms", [800] = "retired-800ms" };
        var samples = new SortedSet<int>(stages.Keys);
        if (expectDamage) { samples.Add(239); samples.Add(240); }
        if (record) foreach (var frame in Enumerable.Range(1, 40)) samples.Add(frame * 40);
        var movie = record ? new GifBitmapEncoder() : null; var elapsed = 0;
        foreach (var sample in samples)
        {
            host.Clock.Advance((sample - elapsed) / 1000d); elapsed = sample;
            BattleAudioBeatChecks.Tick(host.Layer); host.Layer.InvalidateVisual();
            Assert(host.AudioOutput.Count(GameSound.Hit) == (expectDamage && sample >= 240 ? 1 : 0),
                $"The native {label} hit sound at {sample}ms did not follow its real visual arrival once.");
            if (stages.TryGetValue(sample, out var stage))
            {
                host.Capture(output, $"weapon-{label}-{stage}", 1120, saveImage: sample != 180);
                Assert(Same(game, boundary), "Native weapon sampling changed an accepted command, physical cost, event or any of five safe views.");
                var source = SeatBounds(host, 0); var targetBounds = SeatBounds(host, target);
                var gold = VisibleInk(host, 255, 203, 100).ToArray();
                if (sample <= 260)
                    Assert(gold.Any(ink => Intersects(ink, Artwork(source))), "The actual paid weapon lost its compact source activation.");
                if (sample >= 600)
                    Assert(!gold.Any(ink => Intersects(ink, Artwork(source))), "The old paid weapon retained its source body after the brief startup.");
                if (cue.WeaponKind == CardKind.StoneAxe)
                    Assert(gold.All(ink => !Intersects(ink, targetBounds)), "The weapon itself painted a hit over the recipient's actual portrait.");
                Assert(!HasImage(host.Layer, CardArt.Get(cue.WeaponKind)), "The weapon activation flew an invented physical weapon card.");
                var hit = ActualHit(host, targetBounds);
                Assert(sample == 260 ? hit == expectDamage : !hit,
                    $"Native {label} age{sample}ms: actual portrait hit={hit}, expected={(sample == 260 && expectDamage)}; rules HP must remain immediate while hit waits for .24s arrival.");
                AssertNoCaptionOcclusion(host);
                if (sample == 180)
                {
                    host.Capture(output, $"weapon-{label}-{stage}", 1920);
                    AssertNoCaptionOcclusion(host);
                    host.Capture(output, $"weapon-{label}-{stage}", 1120);
                }
                Console.WriteLine($"Native weapon sample: {label}, publicsequence{cue.Sequence}, resolution{cue.WeaponResolutionId}, " +
                    $"age{sample}ms, actualHP{Hp(game, target)}, hit={hit}, goldprimitives{gold.Length}, all5 views unchanged.");
            }
            if (movie is not null && sample % 40 == 0)
            {
                var bitmap = host.MovieFrame(); var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", (ushort)4); metadata.SetQuery("/grctlext/Disposal", (byte)2);
                if (movie.Frames.Count == 0)
                {
                    metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
                }
                movie.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
            }
        }
        if (movie is not null)
        {
            var path = Path.Combine(output, "weapon-native-stone-axe-paid.gif");
            using (var stream = File.Create(path)) movie.Save(stream);
            BattleEffectsChecks.SetGifTiming(path, 4);
            using var input = File.OpenRead(path);
            var decoded = new GifBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(decoded.Frames.Count == 40 && decoded.Frames.All(frame => frame.PixelWidth == 728 && frame.PixelHeight == 481 &&
                Convert.ToInt32(((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay")) == 4),
                "The actual paid axe movie lost its 40 continuous native samples, compact dimensions or 40ms encoded timing.");
            Assert(!Pixels(decoded.Frames[0]).AsSpan().SequenceEqual(Pixels(decoded.Frames[9])) &&
                !Pixels(decoded.Frames[9]).AsSpan().SequenceEqual(Pixels(decoded.Frames[39])),
                "The native axe movie repeated old static captures instead of showing startup, committed hit and retirement.");
            Console.WriteLine("Native axe movie: 40x40ms, 728x481, one exact paid cost + real Zhiman skip, all5 views unchanged.");
        }
    }

    private static void ClearAndReload(NativeHost host, MatchBoundary original, string output, string label, PromptChoice oldChoice)
    {
        var vm = host.Model; var game = Engine(vm); var paid = Boundary(game);
        var sounds = host.AudioOutput.Events.Count;
        vm.IsMotionEnabled = false; host.Capture(output, $"weapon-{label}-motion-off", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && !VisibleInk(host, 255, 203, 100).Any() && Same(game, paid),
            "Disabling native weapon motion must clear drawing without changing its committed cost/result.");
        vm.IsMotionEnabled = true; host.Clock.Advance(.04); host.Layer.InvalidateVisual();
        host.Capture(output, $"weapon-{label}-motion-on-no-replay", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && !VisibleInk(host, 255, 203, 100).Any() && Same(game, paid),
            "Re-enabling weapon motion replayed a historical activation or changed rules state.");
        vm.LoadManualGameCommand.Execute(null); game = Engine(vm);
        Assert(!vm.HasSaveError && Same(game, original) && host.Layer.ActiveEffectCount == 0,
            "Loading the actual unpaid weapon checkpoint carried a historical activation, Slash cost or hit into its real choice.");
        vm.SelectSkillChoiceCommand.Execute(oldChoice);
        Assert(Same(game, original), "An old pre-load weapon button answered the restored prompt because its ID recurred.");
        host.Clock.Advance(.18); host.Layer.InvalidateVisual();
        host.Capture(output, $"weapon-{label}-loaded-no-replay", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && !VisibleInk(host, 255, 203, 100).Any(),
            "The restored weapon prompt replayed past source activation or a historical card flight.");
        BattleAudioBeatChecks.Tick(host.Layer);
        Assert(host.AudioOutput.Events.Count == sounds,
            "Motion off/on or loading the exact native weapon prompt replayed historical combat/attention sounds.");
    }

    private static void DeclinedFrames(NativeHost host, string output, string label, int target)
    {
        var game = Engine(host.Model); var boundary = Boundary(game);
        host.Clock.Advance(.18); host.Layer.InvalidateVisual();
        foreach (var width in new[] { 1120, 1920 })
        {
            host.Capture(output, $"weapon-{label}-180ms", width);
            Assert(!VisibleInk(host, 255, 203, 100).Any(ink => Intersects(ink, Artwork(SeatBounds(host, 0))) ||
                Intersects(ink, SeatBounds(host, target))) && !ActualHit(host, SeatBounds(host, target)) && Same(game, boundary),
                "A genuine declined weapon drew activation/target-hit ink or altered the preserved native result.");
            AssertNoCaptionOcclusion(host);
        }
    }

    private static void ResetWithoutReplay(NativeHost host, string output, string label)
    {
        var previous = Engine(host.Model);
        host.Model.StartNewGameCommand.Execute(null);
        host.Model.ContinueFromIdentityRevealCommand.Execute(null);
        Assert(!ReferenceEquals(previous, Engine(host.Model)) && host.Model.BattleCues.Count == 0 && host.Layer.ActiveEffectCount == 0,
            "The configured native new game retained a historical weapon result or its transient activation.");
        host.Clock.Advance(.18); host.Layer.InvalidateVisual(); host.Capture(output, $"weapon-{label}-reset-no-replay", 1120);
        Assert(host.Layer.ActiveEffectCount == 0 && !VisibleInk(host, 255, 203, 100).Any(),
            "The reset native match replayed the previous axe or Qinglong source glow.");
    }

    private static void AssertNoCaptionOcclusion(NativeHost host)
    {
        var obstacles = Find<Button>(host.Root).Cast<FrameworkElement>()
            .Concat(Find<TextBlock>(host.Root))
            .Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0)
            .Select(element => (element.Name, Bounds: Bounds(element, host.Layer))).ToArray();
        var captions = Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1, null).Where(shape =>
            shape.Drawing.Brush is SolidColorBrush fill && fill.Color.R == 19 && fill.Color.G == 31 && fill.Color.B == 34 &&
            shape.Opacity * fill.Opacity * fill.Color.A / 255d > .2);
        var covered = captions.SelectMany(shape => obstacles.Where(obstacle => Intersects(shape.Visible, obstacle.Bounds))).ToArray();
        Assert(covered.Length == 0,
            "A real weapon/inquiry caption obscured a portrait, native hand card, command button or visible readout: " +
            string.Join("; ", covered.Select(obstacle => $"{obstacle.Name} {obstacle.Bounds}")));
    }

    private static bool ActualHit(NativeHost host, Rect target) => Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1, null)
        .Any(shape => shape.Drawing.Pen is null && shape.Drawing.Brush is SolidColorBrush brush &&
            brush.Color.R == 255 && brush.Color.G == 91 && brush.Color.B == 62 && shape.Bounds == target &&
            shape.Opacity * brush.Opacity * brush.Color.A / 255d > .001);
    private static IEnumerable<Geometry> VisibleInk(NativeHost host, byte r, byte g, byte b) =>
        Shapes(VisualTreeHelper.GetDrawing(host.Layer), Matrix.Identity, 1, null).Where(shape => shape.Opacity > .001 &&
            (IsColor(shape.Drawing.Brush, r, g, b) || IsColor(shape.Drawing.Pen?.Brush, r, g, b)))
            .Select(shape => shape.Visible).Where(geometry => !geometry.Bounds.IsEmpty);
    private static bool IsColor(Brush? brush, byte r, byte g, byte b) => brush is SolidColorBrush solid &&
        solid.Color.R == r && solid.Color.G == g && solid.Color.B == b && solid.Color.A > 0;
    private static bool Intersects(Geometry geometry, Rect bounds) => geometry.FillContainsWithDetail(new RectangleGeometry(bounds)) != IntersectionDetail.Empty;
    private static IEnumerable<(GeometryDrawing Drawing, Rect Bounds, Geometry Visible, double Opacity)> Shapes(Drawing? drawing,
        Matrix transform, double opacity, Geometry? clip)
    {
        if (drawing is GeometryDrawing shape)
        {
            var geometry = shape.Geometry.Clone(); geometry.Transform = new MatrixTransform(transform);
            Geometry visible = shape.Brush is null && shape.Pen is not null ? geometry.GetWidenedPathGeometry(shape.Pen) : geometry;
            if (clip is not null) visible = Geometry.Combine(visible, clip, GeometryCombineMode.Intersect, null);
            yield return (shape, geometry.Bounds, visible, opacity);
        }
        if (drawing is DrawingGroup group)
        {
            var next = group.Transform?.Value ?? Matrix.Identity; next.Append(transform);
            var nextClip = clip;
            if (group.ClipGeometry is { } local)
            {
                var actual = local.Clone(); actual.Transform = new MatrixTransform(next);
                nextClip = clip is null ? actual : Geometry.Combine(clip, actual, GeometryCombineMode.Intersect, null);
            }
            foreach (var child in group.Children) foreach (var nested in Shapes(child, next, opacity * group.Opacity, nextClip)) yield return nested;
        }
    }
    private static bool HasImage(Drawing? drawing, ImageSource? source) => source is not null && (drawing is ImageDrawing image &&
        ReferenceEquals(image.ImageSource, source) || drawing is DrawingGroup group && group.Children.Any(child => HasImage(child, source)));
    private static bool HasImage(BattleFeedbackLayer layer, ImageSource? source) => HasImage(VisualTreeHelper.GetDrawing(layer), source);
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var result = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(result, converted.PixelWidth * 4, 0); return result;
    }
    private static Rect SeatBounds(NativeHost host, int seat) => Bounds(Find<Button>(host.Root).Single(button => BattleFeedbackLayer.GetSeatAnchor(button) == seat), host.Layer);
    private static Rect Artwork(Rect portrait) => new(portrait.Left + portrait.Width * .24, portrait.Top + portrait.Height * .2, portrait.Width * .6, portrait.Height * .46);
    private static Rect Bounds(FrameworkElement element, Visual relative) => element.TransformToVisual(relative).TransformBounds(new Rect(element.RenderSize));
    private static int Hp(GameEngine game, int seat) => game.CreateSnapshot(0).Players.Single(player => player.Seat == seat).Hp;
    private sealed record MatchBoundary(long Revision, int Commands, int Events, int Movements, string[] Views);
    private static MatchBoundary Boundary(GameEngine game) => new(game.Revision, game.AcceptedCommands.Count, game.Events.Count,
        game.CardMovements.Count, Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray());
    private static bool Same(GameEngine game, MatchBoundary boundary) => game.Revision == boundary.Revision &&
        game.AcceptedCommands.Count == boundary.Commands && game.Events.Count == boundary.Events && game.CardMovements.Count == boundary.Movements &&
        game.CreateSnapshot(0).Players.Count == boundary.Views.Length &&
        Enumerable.Range(0, boundary.Views.Length).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == boundary.Views[seat]);

    private sealed class NativeHost : IDisposable
    {
        public MainViewModel Model { get; }
        public MainWindow Window { get; }
        public FrameworkElement Root { get; }
        public BattleFeedbackLayer Layer { get; }
        public FrameClock Clock { get; } = new();
        public BattleAudioBeatChecks.RecordingOutput AudioOutput { get; }
        private readonly GameAudioController _audio;
        private readonly nint _handle;
        private const int ExtendedStyle = -20, NoActivate = 0x08000000;
        public NativeHost(MainViewModel model, string audioScenario)
        {
            Model = model; model.IsSoundEnabled = false; model.IsMusicEnabled = false;
            Window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 1120, Height = 820 };
            Root = (FrameworkElement)Window.Content; Layer = (BattleFeedbackLayer)Window.FindName("BattleFeedback"); Layer.Clock = Clock;
            _handle = new WindowInteropHelper(Window).EnsureHandle();
            SetWindowLong(_handle, ExtendedStyle, GetWindowLong(_handle, ExtendedStyle) | NoActivate);
            Assert((GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0,
                "The native weapon fixture must disable activation on its own HWND before loading.");
            var previous = SetParent(_handle, new nint(-3));
            var parentError = Marshal.GetLastPInvokeError();
            Assert((previous != 0 || parentError == 0) && IsMessageOnlyWindow(_handle),
                $"The native weapon fixture could not attach its owned message-only HWND: previous=0x{previous:X}, error={parentError}.");
            Window.Show(); ShowWindow(_handle, 0);
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var loaded = Window.IsLoaded; var visible = Window.IsVisible;
            var nativeVisible = IsWindowVisible(_handle); var foreground = GetForegroundWindow(); var parent = GetParent(_handle);
            var ancestor = GetAncestor(_handle, 1); var style = unchecked((uint)GetWindowLong(_handle, -16));
            var extended = unchecked((uint)GetWindowLong(_handle, ExtendedStyle)); var messageOnly = IsMessageOnlyWindow(_handle);
            Assert(loaded && visible && !nativeVisible && foreground != _handle && messageOnly && (extended & NoActivate) != 0,
                "The real weapon fixture must stay loaded without appearing or activating on the foreground desktop. " +
                $"Scenario={audioScenario}; IsLoaded={loaded}, IsVisible={visible}, IsWindowVisible={nativeVisible}, " +
                $"IsForeground={foreground == _handle}; HWND=0x{_handle:X}, previousParent=0x{previous:X}, " +
                $"GetParent=0x{parent:X}, GA_PARENT=0x{ancestor:X}, foreground=0x{foreground:X}; messageOnly={messageOnly}, " +
                $"style=0x{style:X}, exStyle=0x{extended:X}, WS_CHILD={(style & 0x40000000u) != 0}, WS_POPUP={(style & 0x80000000u) != 0}.");
            // The hidden MainWindow owns an inactive production controller.
            // Dispose it before installing this recording controller so the
            // native layer has exactly one owner of its sound gate.
            ((GameAudioController)typeof(MainWindow).GetField("_audio", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window)!).Dispose();
            model.IsSoundEnabled = true; model.IsMusicEnabled = false; model.SoundVolume = .4;
            AudioOutput = new BattleAudioBeatChecks.RecordingOutput(Clock, audioScenario);
            _audio = new GameAudioController(model, () => AudioOutput, Clock, Layer);
            _audio.SetActive(true);
            Assert(AudioOutput.PrepareCount > 0 && AudioOutput.Events.Count == 0,
                "Native audio activation must prepare effects without replaying the loaded historical equipment or prompt.");
        }
        public void Capture(string output, string label, int width, bool saveImage = true)
        {
            var height = width == 1120 ? 740 : 1080; Root.Width = width; Root.Height = height;
            Window.SizeToContent = SizeToContent.WidthAndHeight; Window.UpdateLayout();
            Render(Root, width, height, saveImage ? Path.Combine(output, label + $"-{width}.png") : null);
            var expected = new Size(width, height);
            Assert(Root.RenderSize == expected, $"Native weapon PNG {label}: expected{expected}, actualroot{Root.RenderSize}.");
            foreach (var element in Find<FrameworkElement>(Root).Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0 &&
                (element.Name is "ResponseStatusBanner" or "DecisionDescriptionText" or "HandViewport" or "PrimaryActionButtons" ||
                 element is Button button && BattleFeedbackLayer.GetSeatAnchor(button) >= 0)))
                Assert(new Rect(expected).Contains(Bounds(element, Root)),
                    $"The actual {width} native weapon PNG clips {element.Name}, bounds{Bounds(element, Root)}.");
            Assert(Window.IsLoaded && Window.IsVisible && Root.IsVisible && IsMessageOnlyWindow(_handle) &&
                (GetWindowLong(_handle, ExtendedStyle) & NoActivate) != 0 && !IsWindowVisible(_handle) && GetForegroundWindow() != _handle,
                "Native weapon rendering exposed or activated its owned hidden host.");
        }
        public RenderTargetBitmap MovieFrame()
        {
            Root.UpdateLayout(); Root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Assert(Root.RenderSize == new Size(1120, 740), "The actual native weapon movie lost its exact compact layout.");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(.65, .65)); dc.DrawRectangle(new VisualBrush(Root), null, new Rect(Root.RenderSize)); dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(728, 481, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
        }
        public void Dispose()
        {
            _audio.Dispose(); BattleAudioBeatChecks.RecordNativeEvidence(AudioOutput);
            Window.Content = null; Window.DataContext = null; Window.Close();
        }
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class BindingListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null && (message.Contains("Error:") || message.Contains("Warning:"))) Errors.Add(message);
        }
    }
    private static bool IsMessageOnlyWindow(nint handle)
    {
        nint cursor = 0;
        for (var index = 0; index < 4096; index++)
        {
            cursor = FindWindowEx(new nint(-3), cursor, null, null);
            if (cursor == handle) return true;
            if (cursor == 0) return false;
        }
        return false;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern nint GetParent(nint child);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint handle, uint flags);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
