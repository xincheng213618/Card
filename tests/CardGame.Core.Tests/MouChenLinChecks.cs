using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouChenLinChecks
{
    private const string Hero = "mou:chen-lin", Plain = "fixture:mcl-plain", Driver = "fixture:mcl-driver", Hold = "fixture:mcl-hold", Deck = "fixture:mcl-deck";
    public static void ActualParticipantGiftsWrittenUsesReturnTopAndCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 1), new(CardKind.Duel, Suit.Club, 2), new(CardKind.BarbarianAssault, Suit.Spade, 3), new(CardKind.Lightning, Suit.Spade, 4), new(CardKind.Dodge, Suit.Heart, 5), new(CardKind.Slash, Suit.Club, 6), new(CardKind.SilverLion, Suit.Club, 7), new(CardKind.Dodge, Suit.Heart, 8), new(CardKind.Dodge, Suit.Heart, 9), new(CardKind.Dodge, Suit.Heart, 10)];
        var setup = new ShanheBattleSetup(7, 1, "mou-chen-lin-native", Hero, 3, 0, 0, 0, 0, [Driver, Hold], hand, [],
            [new(Plain, Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 11)] }, new(Plain, Hp: 12) { StartingHand = [] }],
            [new(Plain, Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 12)] }], new string('d', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        PendingDecision? View(int actor) => game.CreateSnapshot(actor).PendingDecision;
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(View).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        ProgramSkillFrame? Frame() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouChenLin is not null || f.MouChenLinInvitation is not null);
        MouChenLinReceipt? Written() => Frame()?.MouChenLin;
        CardLocation Position(int id) => game.CardMovements.Last(m => m.CardId == id).To;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Written = Written(), Invitation = Frame()?.MouChenLinInvitation, Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.HandCount }), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command) { try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual command rejected."); } catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); } }
        void Answer(int actor, Func<PromptChoice, bool> pick)
        {
            var p = View(actor); Need(p?.PlayerSeat == actor, "The original command is submitted by its actual published actor."); var choice = p!.Choices.FirstOrDefault(pick); Need(choice is not null, "Required actual Chen Lin choice missing."); Submit(new AnswerPromptCommand(actor, p.PromptId, choice!.Id, game.Revision));
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal at " + line);
                if (Prompt()?.ParticipantFrameId is not null) throw new InvalidOperationException("A real participant command is required at " + line + Diagnostic());
                if (Prompt() is not { PlayerSeat: 0 } p) { Submit(new AdvanceOneStepCommand(game.Revision)); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(0, c => c.Cards.Count == 0); continue; }
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(0, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                throw new InvalidOperationException("Unexpected actual human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded actual Chen Lin boundary not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => View(0)?.Kind == DecisionKind.PlayCard);
        void DriverAction(string action, int card, int? target = null) { Submit(new UseProgramSkillCommand(0, Driver, action, [card], target is { } t ? [t] : [], game.Revision, View(0)!.PromptId)); Play(); }
        void UseWritten(int card) { Reach(() => Written()?.Stage == MouChenLinStage.UseChoice && View(0) is not null); Answer(0, c => c.Cards.SequenceEqual([card])); }
        Submit(new StartGameCommand()); Play();
        var initial = Player(0).Hand.ToArray(); int Card(CardKind k, int rank) => initial.Single(c => c.Kind == k && c.Rank == rank).Id;
        var slash = Card(CardKind.Slash, 1); var duel = Card(CardKind.Duel, 2); var global = Card(CardKind.BarbarianAssault, 3); var lightning = Card(CardKind.Lightning, 4); var spare = Card(CardKind.Dodge, 5); var ordinary = Card(CardKind.Slash, 6); var lion = Card(CardKind.SilverLion, 7);
        DriverAction("place", lion, 1);
        foreach (var card in new[] { spare, lightning, global, duel, slash }) DriverAction("top", card);
        Need(Player(0).HandCount >= 5 && Position(lion) == CardLocation.Equipment(1), "The deterministic top-five setup uses actual native movement and the late giver really equips its own material.");
        var donor = game.CreateSnapshot(2).Players[2].Hand.Single().Id;
        Submit(new UseProgramSkillCommand(0, "mou:yaozuo", "invite-authors", [], [], game.Revision, View(0)!.PromptId));
        Reach(() => Frame()?.MouChenLinInvitation?.Stage == MouChenLinInvitationStage.Racing && View(2)?.ParticipantFrameId is not null);
        var invitationFrame = Frame()!.Id; var actor1 = View(1)!; var actor2 = View(2)!; var actor3 = View(3)!;
        Need(View(0) is null && actor1.PromptId == actor2.PromptId && actor2.PromptId == actor3.PromptId && actor1.ParticipantFrameId == invitationFrame && actor1.ValidCardIds.Contains(lion) && !actor2.ValidCardIds.Contains(lion) && actor2.ValidCardIds.SequenceEqual([donor]) && actor3.Choices is [{ Cards.Count: 0 }] && actor1.IsPrivate && actor2.IsPrivate, "The same issued invitation independently projects only each eligible actor's current private HE material.");
        // These are deliberate original commands from separate actors. This is
        // an integration of the race port, not an automatic NPC race scheduler.
        Answer(2, c => c.Cards.SequenceEqual([donor]));
        Reach(() => Frame()?.MouChenLinInvitation is { Stage: MouChenLinInvitationStage.Racing, FirstGiver: 2 });
        var unansweredInvitation = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Answer(1, c => c.Cards.SequenceEqual([lion]));
        Reach(() => Frame()?.MouChenLinInvitation is { Stage: MouChenLinInvitationStage.Racing, Gifts.Count: 2 });
        Answer(3, c => c.Parameters.GetValueOrDefault("branch") == "decline");
        Reach(() => Frame()?.MouChenLinInvitation?.Stage == MouChenLinInvitationStage.ChooseTarget && View(2) is not null);
        Need(Events<MouChenLinFirstGiftEvent>() is [{ Giver: 2 }] && Events<MouChenLinGiftPaidEvent>().Length == 2 && Events<MouChenLinDamageTicketEvent>().Select(e => e.Target).Order().SequenceEqual([1, 3]), "The first successful native Give wins despite its later seat; equipment donation stays a real Give but does not count as handing over a Hand card for the printed damage condition.");
        Answer(2, c => c.Targets.SequenceEqual([3]));
        Reach(() => Written()?.Stage == MouChenLinStage.BranchTarget);
        Need(Written() is { Requested: 5, ForcedTarget: 3 } r && r.Cards.Select(c => c.Id).SequenceEqual([slash, duel, global, lightning, spare]) && r.Source.SkillId == "mou:yaozuo" && View(0)!.Choices.All(c => c.Targets.SequenceEqual([3])), "The successful giver fixes one real target and the invitation reveals exactly the owner's capped actual top-five entities.");
        Answer(0, c => c.Parameters.GetValueOrDefault("branch") == "use"); UseWritten(slash);
        Reach(() => Prompt()?.SkillPrompt?.SkillId == Hold);
        var origin = Written()!.Native!; var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        Need(origin.CardId == slash && Position(slash) == CardLocation.Processing && game.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == origin.UseId && u.MouChenLinWrittenUse == origin && u.Action?.PhysicalCards.Count == 1), "The first written card enters its genuine native physical Use before the committed-use child suspends it.");
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Written()?.Native == origin && Frame()?.MouChenLinInvitation is { FirstGiver: 2, Gifts.Count: 2, Closed: true } && Events<MouChenLinFirstGiftEvent>().Length == 1, "Cold preserves the original winning Give receipts, immutable public queue and exact outstanding physical Use without charging or giving twice.");
        Answer(0, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(() => Written()?.Stage == MouChenLinStage.UseChoice);
        Need(Player(3).Hp == 10 && Events<MouChenLinDamageTicketConsumedEvent>().Count(e => e.Target == 3) == 1, "The declined participant receives its next genuine damage plus one exactly once.");
        UseWritten(duel); Reach(() => Written()?.Stage == MouChenLinStage.UseChoice); Need(Player(3).Hp == 9, "The following real Duel does not reuse the consumed next-damage ticket.");
        UseWritten(global); Play();
        var uses = Events<MouChenLinUseIssuedEvent>().Where(e => e.Origin.FrameId == invitationFrame).Select(e => e.Origin).ToArray();
        Need(uses.Select(e => e.CardId).SequenceEqual([slash, duel, global]) && uses.All(e => Events<CardUseFinishedEvent>().Count(f => f.ResolutionId == e.UseId) == 1 && Events<MouChenLinUseReturnedEvent>().Count(f => f.UseId == e.UseId) == 1 && !Events<CardUseDebitRecordedEvent>().Any(d => d.Debit.CardActionId == e.ActionId)) && Events<GroupCardUsedEvent>().Any(e => e.CardId == global && e.TargetSeats.SequenceEqual([3])) && Player(3).Hp == 8 && Player(1).Hp == 12 && Player(2).Hp == 12, "All three actual physical Uses return in the chosen order; the global attack's real native group is restricted to the fixed target and forced Uses preserve ordinary Slash allowance.");
        Need(Position(lightning) == CardLocation.DrawPile && Position(spare) == CardLocation.DrawPile && Position(lion) == CardLocation.Hand(0) && Position(donor) == CardLocation.Hand(0), "The unsupported self-only Lightning and unused non-damage card truly return to the deck; both donated entities remain actual Hand cards.");
        var normal = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == ordinary && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3])); Need(normal is not null, "The actor retains its genuine ordinary Slash admission after the written chain.");
        Submit(new PlayCardCommand(0, ordinary, [3], game.Revision, View(0)!.PromptId)); Play();
        var ordinaryAction = Events<CardActionAcceptedEvent>().Single(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 0 && e.Action.PhysicalCards.Any(c => c.CardId == ordinary)).Action;
        Need(Player(3).Hp == 7 && Events<CardUseDebitRecordedEvent>().Count(e => e.Debit.CardActionId == ordinaryAction.ActionId) == 1, "The ordinary physical Slash really damages after the forced chain and pays exactly its own ordinary native allowance.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, View(0)!.PromptId));
        Reach(() => View(0)?.Kind == DecisionKind.DiscardCards);
        var discard = View(0)!;
        Need(discard.RequiredCardCount == Player(0).HandCount - 2 - 3 && !discard.ValidCardIds.Contains(lion) && !discard.ValidCardIds.Contains(donor), "The two actually received invitation entities alone are exempt from the real native hand-limit discard count and material selection.");
        Submit(new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision));
        Reach(() => View(0)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == "mou:zhuanwen") == true);
        Answer(0, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => Written()?.Stage == MouChenLinStage.BranchTarget);
        var ending = Written()!; Need(ending.Source.SkillId == "mou:zhuanwen" && ending.Cards.Take(2).Select(c => c.Id).SequenceEqual([lightning, spare]) && ending.Cards.Count == 5, "The actual Ending invocation reveals the same returned physical entities first in their original order.");
        var nonDamage = ending.Cards.Where(c => c.Kind != CardKind.Lightning).Select(c => c.Id).ToArray(); Answer(0, c => c.Parameters.GetValueOrDefault("branch") == "give" && c.Targets.SequenceEqual([0]));
        Reach(() => Events<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Need(nonDamage.All(id => Position(id) == CardLocation.Hand(0)) && Position(lightning) == CardLocation.DrawPile && Position(lion) == CardLocation.Hand(0) && Position(donor) == CardLocation.Hand(0), "The current any-role non-damage branch can truly give to self, return its unused Lightning to the actual deck and retain both successful gift entities after the genuine discard phase.");
        VerifyParticipantHost().GetAwaiter().GetResult();

        async Task VerifyParticipantHost()
        {
            static void Require(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(unansweredInvitation), registry);
            GameSnapshot restoredView;
            await using (var host = ParticipantPromptHost.CreateHeadless(restored))
            {
                await host.RunToBoundaryAsync(deadline.Token);
                restoredView = await host.CreateSnapshotAsync(0);
            }
            // Trusted history and frame inspection occurs only after the host's
            // single reader and every independent worker have been disposed.
            var restoredFacts = restored.Events.Select(e => e.Payload).ToArray();
            var paid = restoredFacts.OfType<MouChenLinGiftPaidEvent>().Where(e => e.FrameId == invitationFrame).ToArray();
            var answers = restoredFacts.OfType<MouChenLinInvitationAnswerEvent>().Where(e => e.FrameId == invitationFrame).ToArray();
            Require(restoredView.PendingDecision?.PlayerSeat == 0 && restored.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == invitationFrame).MouChenLin is { Stage: MouChenLinStage.BranchTarget } && restoredFacts.OfType<MouChenLinInvitationClosedEvent>().Count(e => e.FrameId == invitationFrame) == 1 && answers.Length == 3 && answers.Select(e => e.Actor).Distinct().Count() == 3,
                "The actual headless host reconstructs the outstanding all-NPC participants after Cold, runs their independent private-view brains and reaches the original owner's real written-card choice without manually submitting any remaining NPC answer.");
            Require(paid.Count(e => e.Giver == 2 && e.CardId == donor) == 1 && restoredFacts.OfType<MouChenLinFirstGiftEvent>().Where(e => e.FrameId == invitationFrame).ToArray() is [{ Giver: 2, CardId: var firstCard }] && firstCard == donor && restored.CardMovements.Count(m => m.CardId == donor && m.From == CardLocation.Hand(2) && m.To == CardLocation.Hand(0)) == 1,
                "Host reconstruction does not replay the first completed Give or issue a second winner; only the formerly unanswered NPCs compute and submit their own original commands.");

            // A human participant requires a genuinely different legal layout:
            // Shanhe fixes its human to seat0, so its allied Chen Lin is NPC1.
            var mixedSetup = setup with { NodeId = "mou-chen-lin-human-participant", PlayerGeneralId = Plain, Hp = 12, ExtraSkillIds = [], StartingCards = [new(CardKind.Dodge, Suit.Heart, 13)], AllyRoster = [new(Hero, Hp: 3) { StartingHand = [new(CardKind.Dodge, Suit.Club, 1), new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Club, 3)] }] };
            var mixed = GameEngine.CreateStandard(new() { Seed = mixedSetup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = mixedSetup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            long mixedInvitation = 0; int humanGift = 0;
            await using (var host = ParticipantPromptHost.CreateHeadless(mixed))
            {
                var started = await host.SubmitAsync(new StartGameCommand()); Require(started.Accepted, "The real mixed human/NPC host accepts its native game start.");
                await host.RunToBoundaryAsync(deadline.Token);
                var view = await host.CreateSnapshotAsync(0); Require(view.PendingDecision is { Kind: DecisionKind.PlayCard }, "The human reaches its actual Play through the host queue.");
                var ended = await host.SubmitAsync(new EndPlayPhaseCommand(0, view.Revision, view.PendingDecision!.PromptId)); Require(ended.Accepted, "The human's actual Play ends through the host queue.");
                await host.RunToBoundaryAsync(deadline.Token);
                var accepted = false;
                for (var attempt = 0; attempt < 8 && !accepted; attempt++)
                {
                    view = await host.CreateSnapshotAsync(0);
                    Require(view.PendingDecision is { ParticipantFrameId: not null, PlayerSeat: 0 } && view.PendingDecision.SourceSeat == 1, "The genuine NPC Chen Lin activates Yaozuo and leaves the human's independently projected invitation available alongside NPC workers.");
                    var p = view.PendingDecision!; mixedInvitation = p.ParticipantFrameId!.Value;
                    var choice = p.Choices.First(c => c.Cards.Count == 1); humanGift = choice.Cards.Single();
                    var result = await host.SubmitAsync(new AnswerPromptCommand(0, p.PromptId, choice.Id, view.Revision)); accepted = result.Accepted;
                    Require(accepted || result.Error?.Code == CommandErrorCode.StaleRevision, "A human's actor-view choice is accepted through the same serial host; concurrent NPC commits may require only a fresh revision.");
                    await host.RunToBoundaryAsync(deadline.Token);
                }
                Require(accepted, "The human's actual invitation reply completes without bypassing the host queue.");
            }
            var mixedFacts = mixed.Events.Select(e => e.Payload).ToArray();
            var mixedGifts = mixedFacts.OfType<MouChenLinGiftPaidEvent>().Where(e => e.FrameId == mixedInvitation).ToArray();
            var first = mixedFacts.OfType<MouChenLinFirstGiftEvent>().Single(e => e.FrameId == mixedInvitation);
            Require(mixedGifts.Count(e => e.Giver == 0 && e.CardId == humanGift && e.From == CardLocation.Hand(0)) == 1 && mixedFacts.OfType<MouChenLinInvitationClosedEvent>().Count(e => e.FrameId == mixedInvitation) == 1 && mixedFacts.OfType<MouChenLinInvitationAnswerEvent>().Where(e => e.FrameId == mixedInvitation).Select(e => e.Actor).Order().SequenceEqual([0, 2, 3]) && mixedGifts.OrderBy(e => e.Sequence).First() is var actualFirst && first.Giver == actualFirst.Giver && first.CardId == actualFirst.CardId && first.Sequence == actualFirst.Sequence,
                "The human and real NPC tasks complete one invitation through the original commands; the winner agrees with the actual earliest successful native Give invoice, whichever worker really finishes first.");
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-chen-lin", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Duel, CardKind.BarbarianAssault, CardKind.Lightning, CardKind.Dodge, CardKind.SilverLion }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mcl-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                    {"id":"top","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]}
                  ]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"actual-native-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "真实牌序与安置", description = "原生安置装备并将真实手牌放回牌堆顶。" }, [Hold] = new { name = "真实撰文杀子窗口", description = "真实物理用牌提交后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules, labels); foreach (var id in new[] { Driver, Hold }) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "真实陈琳参与者", "", "standard:none", "wei", 12));
            b.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:mcl-" + CardKind.Dodge, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋陈琳真实参与者与撰文流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
