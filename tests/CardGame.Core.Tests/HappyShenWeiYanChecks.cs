using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenWeiYanChecks
{
    private const string Hero = "happy:shen-wei-yan", Zigu = "happy:zigu", Driver = "fixture:happy-wy-driver", Watch = "fixture:happy-wy-watch", Quiet = "fixture:happy-wy-quiet", Deck = "fixture:happy-wy-deck";
    public static void RealBoneSlotFirstEquipmentAndNativeChildrenCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(Options(), registry); Accept(game, new StartGameCommand()); Play(game);
        Need(View(game).Hp == 6 && View(game).MaxHp == 6, "The formal Happy general starts at six actual HP and maximum HP.");
        var bone = View(game).Hand.Single(c => c.Kind == CardKind.Dodge); var hp = View(game, 1).Hp;
        Use(game, "poke", [1]); Offer(game); Answer(game, c => Action(c) == "activate");
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.Hand); Answer(game, c => c.Cards.SequenceEqual([bone.Id]));
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.Slot); Answer(game, c => Option(c) == "slot:Weapon");
        Reach(game, () => Pending(game)?.SkillPrompt?.SkillId == Watch);
        var root = Root(game)!; var frameId = root.Id;
        Need(root.HappyWeiYan is { Stage: HappyWeiYanStage.Placing, Paid.Count: 1 } receipt && receipt.Paid[0].CardId == bone.Id &&
            receipt.Paid[0].From == CardLocation.Hand(0) && receipt.Paid[0].To == CardLocation.Equipment(1) &&
            Equipment(game, 1).Single().Id == bone.Id && Equipment(game, 1).Single().Kind == CardKind.Dodge && View(game, 1).Hp == hp - 1,
            "A real Dodge retains its printed Basic category while occupying the selected weapon slot; the original damage's bone placement is already paid before its movement observer returns.");
        Need(game.CreateSnapshot(0).Players[1].Equipment.Single() is { IsBone: true, Kind: CardKind.Dodge, OccupiedEquipmentSlot: EquipmentSlot.Weapon } &&
            new[] { 1, 2, 3 }.All(viewer => game.CreateSnapshot(viewer).Players[1].Equipment.Single() is { IsBone: true, OccupiedEquipmentSlot: EquipmentSlot.Weapon } shown && shown.Kind == CardSnapshot.UndisclosedKind && shown.Suit == bone.Suit && shown.Rank == bone.Rank),
            "Only the issuing Wei Yan sees the original bone category; its own holder and every other viewer see the occupied weapon slot and public suit/rank without a fake equipment identity.");
        game = Cold(game, registry); Continue(game); Play(game);
        Need(Events<HappyWeiYanBonePlacedEvent>(game).Count(e => e.FrameId == frameId && e.Holder == 1 && e.Slot == EquipmentSlot.Weapon) == 1 &&
            game.CardMovements.Count(m => m.CardId == bone.Id && m.Reason.Value == "skill-program.happy:zigu.bone-enter") == 1,
            "Cold return does not repeat or reinterpret the original real bone entity placement.");
        var before = View(game).HandCount; hp = View(game, 1).Hp;
        Equip(game, CardKind.Crossbow); Offer(game); Answer(game, c => Action(c) == "activate");
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.DamageTarget); Answer(game, c => c.Targets.SequenceEqual([1])); Play(game);
        Need(View(game, 1).Hp == hp - 1 && View(game).HandCount == before && Equipment(game, 0).Any(c => c.Kind == CardKind.Crossbow) &&
            Events<HappyWeiYanStartedEvent>(game).Count(e => e.Source.BindingId == "first-equipment") == 1 &&
            game.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.happy:zigu.damage-card") == 1,
            "The first actual equipment Use deals native damage before entering its ordinary slot and obtains one real damage card from the deck.");
        Equip(game, CardKind.BaguaFormation); Play(game);
        Need(Events<HappyWeiYanFirstEquipmentEvent>(game).Count(e => e.Actor == 0 && e.ActualTurn == game.State.TurnNumber) == 1 &&
            Events<HappyWeiYanStartedEvent>(game).Count(e => e.Source.BindingId == "first-equipment") == 1,
            "A second accepted equipment Use cannot recreate the first-equipment opportunity in the same actual turn.");
        Need(State(game) == State(GameReplay.Replay(Options(), game.AcceptedCommands, registry)), "The command journal replays real slot occupation, native damage, the first-use opportunity and paid movement child cold return.");
    }
    public static void EzhiPhysicalUseGujingDestinationAndDiscardedNativeUses()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(Options(true), registry); Accept(game, new StartGameCommand()); Play(game);
        var peach = View(game).Hand.Single(c => c.Kind == CardKind.Peach);
        var slash = View(game).Hand.First(c => c.Kind == CardKind.Slash);
        var armor = View(game).Hand.Single(c => c.Kind == CardKind.BaguaFormation);
        var crossbow = View(game).Hand.Single(c => c.Kind == CardKind.Crossbow);
        var lion = View(game).Hand.Single(c => c.Kind == CardKind.SilverLion);
        var ox = View(game).Hand.Single(c => c.Kind == CardKind.WoodenOx);
        Accept(game, new UseProgramSkillCommand(0, "happy:gujing", "bone-path", [], [1], game.Revision, Pending(game)!.PromptId));
        Place(game, peach.Id, EquipmentSlot.Treasure);
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.GujingSelection);
        Answer(game, c => Option(c) == "abolish:" + peach.Id);
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.GujingSelection); Answer(game, c => Option(c) == "finish"); Play(game);
        Need(View(game, 1).EquipmentSlotCapacities![EquipmentSlot.Treasure] == 0 && Events<HappyWeiYanGujingIssuedEvent>(game).Length == 1 &&
            game.CardMovements.Count(m => m.CardId == peach.Id && m.From == CardLocation.Equipment(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.happy:gujing.discard") == 1,
            "The limited native Gujing really places and discards a physical bone, abolishes its selected treasure slot and issues its permanent replacement exactly once.");
        Accept(game, new UseProgramSkillCommand(0, Driver, "top-and-draw", [ox.Id], [1], game.Revision, Pending(game)!.PromptId)); Play(game);
        Need(View(game).Hand.Any(c => c.Id == ox.Id) && View(game, 1).Hand.All(c => c.Id != ox.Id) && game.CardMovements.Count(m => m.CardId == ox.Id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1 &&
            game.CardMovements.All(m => m.CardId != ox.Id || m.To != CardLocation.Hand(1)),
            "A real draw by the abolished-slot holder is redirected before ownership changes; there is no transient gain by that holder and no later theft.");
        var retainedMovements = game.CardMovements.Count; var retainedEvents = game.Events.Count; var retainedLogs = game.Log.Count;
        var retainedOwnerHand = View(game).Hand.Select(c => c.Id).Order().ToArray();
        var retainedRecipientHand = View(game, 1).Hand.Select(c => c.Id).Order().ToArray();
        Accept(game, new UseProgramSkillCommand(0, Driver, "give", [ox.Id], [1], game.Revision, Pending(game)!.PromptId)); Play(game);
        var retainedFacts = game.Events.Skip(retainedEvents).Select(e => e.Payload).ToArray();
        var retainedStart = retainedFacts.OfType<ProgramSkillStartedEvent>().Single(e => e.OwnerSeat == 0 && e.SkillId == Driver && e.ActivationId == "give");
        Need(View(game).Hand.Select(c => c.Id).Order().SequenceEqual(retainedOwnerHand) &&
            View(game, 1).Hand.Select(c => c.Id).Order().SequenceEqual(retainedRecipientHand) &&
            View(game).Hand.Any(c => c.Id == ox.Id) && View(game, 1).Hand.All(c => c.Id != ox.Id) &&
            game.CardMovements.Count == retainedMovements && !retainedFacts.OfType<CardMovedEvent>().Any() &&
            !retainedFacts.OfType<NativeDrawInvocationRecordedEvent>().Any() && !retainedFacts.OfType<NativeCardsGivenEvent>().Any() &&
            !retainedFacts.OfType<ProgramBindingStartedEvent>().Any(e => e.Window is SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained) &&
            game.Log.Skip(retainedLogs).All(e => e.Type != "CardsDrawn") &&
            retainedFacts.OfType<ProgramSkillResolvedEvent>().Count(e => e.FrameId == retainedStart.FrameId && e.SkillId == Driver && e.ActivationId == "give" && e.Completed) == 1,
            "Giving the original Wooden Ox to the abolished treasure-slot holder is replaced by its original owner's gain: both actual hands stay unchanged, no entity leaves Hand0 or enters Processing, no movement/draw/give/gain fact is invented, and the real GiveSelected program completes once.");
        var contestEvents = game.Events.Count;
        Accept(game, new UseProgramSkillCommand(0, "ol:jianshu", "limited-black-hand-recipient-contest", [ox.Id], [1], game.Revision, Pending(game)!.PromptId)); Play(game);
        var contestFacts = game.Events.Skip(contestEvents).Select(e => e.Payload).ToArray();
        var contestStart = contestFacts.OfType<ProgramSkillStartedEvent>().Single(e => e.OwnerSeat == 0 && e.SkillId == "ol:jianshu");
        Need(ox.Suit == Suit.Club && View(game).Hand.Select(c => c.Id).Order().SequenceEqual(retainedOwnerHand) &&
            View(game, 1).Hand.Select(c => c.Id).Order().SequenceEqual(retainedRecipientHand) && View(game, 1).HandCount == 0 &&
            game.CardMovements.Count == retainedMovements && !contestFacts.OfType<CardMovedEvent>().Any() &&
            !contestFacts.OfType<NativeCardsGivenEvent>().Any() && !contestFacts.OfType<NativeDrawInvocationRecordedEvent>().Any() &&
            !contestFacts.OfType<ProgramBindingStartedEvent>().Any(e => e.Window is SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained) &&
            contestFacts.OfType<BlackGiftContestGiftPaidEvent>().Single(e => e.FrameId == contestStart.FrameId) is { } contestPaid && contestPaid.Before == contestPaid.After &&
            !contestFacts.OfType<BlackGiftContestStartedEvent>().Any() &&
            contestFacts.OfType<ProgramSkillResolvedEvent>().Count(e => e.FrameId == contestStart.FrameId && e.SkillId == "ol:jianshu" && e.Completed) == 1,
            "The real legacy Jianshu producer settles its black equipment gift from the original retained-entity receipt, completes exactly once without fake movement, draw or actual Give facts, and starts no Pindian because the actual recipient still has no hand cards.");
        Use(game, "enable-hongyuan", []); Play(game); Use(game, "gain-two", [1]);
        Reach(game, () => Pending(game)?.SkillPrompt?.SkillId == "ol:hongyuan" && Pending(game)!.Choices.Any(c => c.Cards.Contains(ox.Id) && c.Targets.SequenceEqual([1])));
        var hongyuan = game.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == "ol:hongyuan");
        var hongyuanFrame = hongyuan.Id;
        Need(hongyuan.WindowContext?.MovementBatch is { Movements.Count: 2 } gainBatch &&
            gainBatch.Movements.All(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) && View(game, 1).HandCount == 0,
            "The real Hongyuan opportunity comes from one atomic two-card foreign-hand gain, leaving the abolished-slot recipient genuinely empty before the retained equipment gift.");
        var hongyuanEvents = game.Events.Count; var hongyuanMoves = game.CardMovements.Count;
        var hongyuanHand = View(game).Hand.Select(c => c.Id).Order().ToArray();
        Answer(game, c => c.Cards.SequenceEqual([ox.Id]) && c.Targets.SequenceEqual([1]));
        Reach(game, () => Pending(game)?.SkillPrompt?.SkillId == "ol:hongyuan");
        Need(Pending(game)!.Choices.All(c => !c.Cards.Contains(ox.Id) && !c.Targets.Contains(1)) &&
            View(game).Hand.Select(c => c.Id).Order().SequenceEqual(hongyuanHand) && View(game, 1).HandCount == 0 && game.CardMovements.Count == hongyuanMoves &&
            game.Events.Skip(hongyuanEvents).All(e => e.Payload is not CardMovedEvent and not NativeCardsGivenEvent and not NativeDrawInvocationRecordedEvent) &&
            Events<GainGiftPaidEvent>(game).Single(e => e.ProgramFrameId == hongyuanFrame) is { TargetSeat: 1 } hongyuanPaid && hongyuanPaid.Before == hongyuanPaid.After,
            "The old Hongyuan producer settles the retained equipment request without a physical gain and excludes both its selected entity and first recipient from the same request's remaining gift menu.");
        Answer(game, c => c.Cards.Count == 0); Play(game);
        Need(Events<GainGiftPhaseIssuedEvent>(game).Count(e => e.ProgramFrameId == hongyuanFrame) == 1 &&
            Events<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == hongyuanFrame && e.SkillId == "ol:hongyuan" && e.Completed) == 1,
            "The retained Hongyuan request consumes its original actual-phase quota and returns the original binding exactly once.");

        Use(game, "poke", [2]); Offer(game); Answer(game, c => Action(c) == "activate"); Place(game, slash.Id, EquipmentSlot.Armor); Play(game);
        Use(game, "force-equipment", [2]); SelectGift(game, crossbow.Id);
        Reach(game, () => Pending(game)?.SkillPrompt?.SkillId == Watch && Root(game)?.HappyWeiYan is { Operation: SkillProgramEffectOp.ResolveHappyWeiYanEzhi, Stage: HappyWeiYanStage.DrawChildren });
        var ezhiId = Root(game)!.Id; var hand = View(game).HandCount;
        Need(Root(game)!.HappyWeiYan is { Draw: { RequestedCount: 2, ActualDrawIds.Count: 2 }, SelectedBone: null } && Equipment(game, 2).Any(c => c.Id == slash.Id),
            "Ezhi has already paid its native two-card draw and waits in the original committed equipment Use before the other role discards its bone.");
        game = Cold(game, registry); Continue(game); Play(game);
        Need(Events<HappyWeiYanEzhiPaidEvent>(game).Single(e => e.FrameId == ezhiId) is { Canceled: false, Actor: 2, Kind: CardKind.Crossbow, BoneKind: CardKind.Slash } &&
            View(game).HandCount == hand && Equipment(game, 2).Any(c => c.Id == crossbow.Id) && !Equipment(game, 2).Any(c => c.Id == slash.Id) &&
            DirectedDistance(game, 2) && !DirectedDistance(game, 3),
            "A Basic bone differs from the original Equipment Use, so that use installs normally and grants only its actor a genuine directed unlimited-distance entitlement without redrawing after cold return.");

        Use(game, "poke", [3]); Offer(game); Answer(game, c => Action(c) == "activate"); Place(game, armor.Id, EquipmentSlot.Weapon); Play(game);
        Need(!EquipmentEffect(game, "HasBagua", 3), "An actual Bagua entity used as a weapon-slot bone supplies no ordinary armor ability.");
        Use(game, "force-equipment", [3]); SelectGift(game, lion.Id); Play(game);
        var canceled = Events<HappyWeiYanEzhiPaidEvent>(game).Single(e => e.Actor == 3);
        Need(canceled is { Canceled: true, Kind: CardKind.SilverLion, BoneKind: CardKind.BaguaFormation } && Equipment(game, 3).Count == 0 &&
            game.CardMovements.Count(m => m.CardId == lion.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.happy:ezhi.cleanup") == 1,
            "An Equipment bone keeps that printed category in the weapon slot, cancels the original equipment Use after real discard and prevents the canceled Silver Lion from entering any equipment slot.");
        var targetHp = View(game, 2).Hp;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Pending(game)!.PromptId));
        Reach(game, () => Root(game)?.HappyWeiYan is { Operation: SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn, Stage: HappyWeiYanStage.ReturnChoice } && Pending(game)!.Choices.Any(c => c.Cards.Contains(slash.Id)));
        Answer(game, c => c.Cards.Contains(slash.Id) && c.Targets.SequenceEqual([2]));
        Reach(game, () => Root(game)?.HappyWeiYan is { Operation: SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn, Stage: HappyWeiYanStage.ReturnChoice } && Pending(game)!.Choices.Any(c => c.Cards.Contains(armor.Id)));
        Need(View(game, 2).Hp == targetHp - 1 && game.CardMovements.Count(m => m.CardId == slash.Id && m.From == CardLocation.DiscardPile && m.To == CardLocation.Processing) == 1,
            "The first usable discarded bone is the original Slash entity, directly issued from the discard pile against its otherwise out-of-range actor through the same-turn directed entitlement.");
        Answer(game, c => c.Cards.Contains(armor.Id) && Option(c)!.StartsWith("use:", StringComparison.Ordinal));
        Offer(game); Answer(game, c => Action(c) == "activate");
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.DamageTarget); Answer(game, c => c.Targets.SequenceEqual([3]));
        Reach(game, () => Equipment(game, 0).Any(c => c.Id == armor.Id) && Events<HappyWeiYanBoneUseReturnedEvent>(game).Length == 2);
        Need(Equipment(game, 0).Single(c => c.Id == armor.Id).Kind == CardKind.BaguaFormation && EquipmentEffect(game, "HasBagua", 0) &&
            Events<HappyWeiYanBoneUseIssuedEvent>(game).Select(e => e.CardId).SequenceEqual([slash.Id, armor.Id]) &&
            Events<HappyWeiYanStartedEvent>(game).Count(e => e.Source.BindingId == "first-equipment") == 1 &&
            game.CardMovements.All(m => m.CardId != slash.Id || m.From != CardLocation.DiscardPile || m.To != CardLocation.Hand(0)),
            "Chronological native returns skip the unusable Peach, use the Slash before the armor, restore the armor's ordinary equipment ability after its bone identity ends and trigger the first own equipment opportunity even inside actual turn-end settlement.");
        Need(State(game) == State(GameReplay.Replay(Options(true), game.AcceptedCommands, registry)), "All accepted commands replay limited destination replacement, conditional original-Use cancellation, paid native draw cold return and both original discarded-entity uses.");
    }
    private static GameOptions Options(bool full = false)
    {
        var setup = new ShanheBattleSetup(7, 1, "happy-wei-yan", Hero, 6, 0, 0, 0, 0, full ? [Driver, Watch, "ol:jianshu"] : [Driver, Watch],
            full ? [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.BaguaFormation, Suit.Spade, 2), new(CardKind.Slash, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.SilverLion, Suit.Club, 5), new(CardKind.WoodenOx, Suit.Club, 6)] :
                [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.BaguaFormation, Suit.Spade, 2), new(CardKind.Slash, Suit.Heart, 3), new(CardKind.Dodge, Suit.Diamond, 4)], [],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "shu" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int seat) => "fixture:happy-wy-peer-" + seat;
    private static PlayerSnapshot View(GameEngine game, int seat = 0) => game.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? Pending(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HappyWeiYan is not null);
    private static T[] Events<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option");
    private static void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Happy Wei Yan command."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var pending = Pending(game)!; var choice = pending.Choices.FirstOrDefault(predicate); Need(choice is not null, "Missing exact Happy Wei Yan choice: " + JsonSerializer.Serialize(pending));
        Accept(game, new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId, choice!.Id, game.Revision));
    }
    private static void Use(GameEngine game, string id, int[] targets) => Accept(game, new UseProgramSkillCommand(0, Driver, id, [], targets, game.Revision, Pending(game)!.PromptId));
    private static void Place(GameEngine game, int cardId, EquipmentSlot slot)
    {
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.Hand); Answer(game, c => c.Cards.SequenceEqual([cardId]));
        Reach(game, () => Root(game)?.HappyWeiYan?.Stage == HappyWeiYanStage.Slot); Answer(game, c => Option(c) == "slot:" + slot);
    }
    private static void SelectGift(GameEngine game, int cardId)
    {
        Reach(game, () => Pending(game)?.SkillPrompt?.SkillId == Driver && Pending(game)!.Choices.Any(c => Action(c) == "select-and-move-owned-card"));
        Answer(game, c => c.Cards.SequenceEqual([cardId]));
    }
    private static void Equip(GameEngine game, CardKind kind)
    {
        var action = game.GetHumanLegalActions().First(a => a.CardId is { } id && View(game).Hand.Any(c => c.Id == id && c.Kind == kind));
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, Pending(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    }
    private static void Offer(GameEngine game) => Reach(game, () => Pending(game)?.SkillPrompt?.SkillId == Zigu && Pending(game)!.Choices.Any(c => Action(c) == "activate"));
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine game) => Reach(game, () => Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 180; step++)
        {
            if (ready()) return;
            if (Pending(game) is { } pending)
            {
                if (pending.Kind == DecisionKind.SelectFaction) { Answer(game, _ => true); continue; }
                if (pending.SkillPrompt?.SkillId == Watch) { Continue(game); continue; }
                if (pending.SkillPrompt?.SkillId == Zigu && pending.Choices.Any(c => Action(c) == "skip")) { Answer(game, c => Action(c) == "skip"); continue; }
                throw new InvalidOperationException("Unexpected Happy Wei Yan boundary: " + JsonSerializer.Serialize(new { Prompt = pending, Root = Root(game)?.HappyWeiYan }));
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Happy Wei Yan missed the bounded native boundary.");
    }
    private static IReadOnlyList<Card> Equipment(GameEngine game, int seat) => (IReadOnlyList<Card>)typeof(GameEngine).GetMethod("GetEquipment", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(int)], null)!.Invoke(game, [seat])!;
    private static object Player(GameEngine game, int seat) => ((System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!)[seat]!;
    private static bool EquipmentEffect(GameEngine game, string method, int seat) => (bool)typeof(GameEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(CharacterState)], null)!.Invoke(game, [Player(game, seat)])!;
    private static bool DirectedDistance(GameEngine game, int target) => (bool)typeof(GameEngine).GetMethod("HappyEzhiDistance", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, [0, target])!;
    private static GameEngine Cold(GameEngine game, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Need(State(game) == State(restored), "Cold restore preserves all player views, original bone placement receipt and native child return."); return restored; }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-wei-yan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero, "ol:sp-jia-xu", "ol:zhuge-jin");
            foreach (var kind in new[] { CardKind.SilverLion, CardKind.WoodenOx })
            {
                var card = CardCatalog.Get(kind);
                builder.AddCard(new("fixture:happy-wy-" + kind, card.DisplayName, card.CategoryName, card.Description, kind));
            }
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"enable-hongyuan","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:hongyuan"]}]},
                {"id":"gain-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2},{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"gain-pair"},{"op":"moveBoundCards","target":"owner","sourceBind":"gain-pair","destination":"ownerHand","awaitMovementTriggers":true}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
                {"id":"force-equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                  {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"resultBind":"gift","revealBeforeMove":true,"awaitMovementTriggers":true},
                  {"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"gift"}]},
                {"id":"top-and-draw","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true},{"op":"draw","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"bone-paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.happy:zigu.bone-enter"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"wait","options":[{"id":"continue"}]}]},
                {"id":"ezhi-draw-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.happy:ezhi.draw"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-wait","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实伤害", description = "恣骨原生伤害入口。" }, [Watch] = new { name = "已付骨移动", description = "真实骨入装备区之后的子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [Quiet] = new { name = "安静同行", description = "跳过出牌。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new(Peer(seat), "真实骨目标", "supporter", Quiet, "wei", 8));
            builder.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(i => new ContentDeckPhysicalCard("standard:slash", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实骨占槽", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
