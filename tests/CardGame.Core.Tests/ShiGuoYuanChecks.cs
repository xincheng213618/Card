using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiGuoYuanChecks
{
    private const string Hero = "mobile:shi-guo-yuan", Qingdao = "mobile:shi-qingdao", Xiugeng = "mobile:shi-xiugeng", Chenshe = "mobile:shi-chenshe",
        Driver = "fixture:shi-guo-yuan-driver", Watch = "fixture:shi-guo-yuan-watch", Quiet = "fixture:shi-guo-yuan-quiet", Aggressor = "fixture:shi-guo-yuan-aggro",
        Peer = "fixture:shi-guo-yuan-peer", Foe = "fixture:shi-guo-yuan-foe", Deck = "fixture:shi-guo-yuan-deck";
    public static void FrozenComparisonPersistentLimitAndLatestRecordCold()
    {
        var s = Scene.Start(); s.Offer(Xiugeng); s.Activate(); s.Record([0, 1]); s.Play();
        var first = s.E<ShiGyRecordCommittedEvent>().Single();
        s.Need(s.E<ShiGyComparedEvent>().Single(e => e.Target == 0) is { ActualHand: 5, RecordedHand: 5, DrawTwo: true, RaiseLimit: true } &&
            s.V(0).HandCount == 7 && s.E<ShiGyHandLimitRaisedEvent>().Count(e => e.Target == 0) == 1,
            "Equality freezes both branches before drawing two real cards; the added cards cannot erase the hand-limit benefit.");
        s.End(); s.Reach(() => s.E<ShiGyComparedEvent>().Any(e => e.Target == 1) && s.V(1).HandCount == 3);
        s.Need(s.E<ShiGyComparedEvent>().Single(e => e.Target == 1) is { ActualHand: 1, RecordedHand: 1, DrawTwo: true, RaiseLimit: true } &&
            s.V(1).HandCount == 3 && s.V(1).HandLimit == s.V(1).Hp + 1,
            "The exact recorded other player receives both effects at their real draw-phase start.");
        // TurnBeforeStart belongs to upcoming turn five; the public counter is
        // still four until the native prelude completes and TurnStarted commits.
        s.Reach(() => s.G.ResolutionStack.OfType<BeiMiHuTurnPreludeFrame>().Any(f => f.Proof.UpcomingTurnNumber == 5 && f.Proof.UpcomingOwnerSeat == 0) &&
            s.P?.SkillPrompt?.SkillId == Xiugeng && s.P.Choices.Any(c => Key(c) == "activate"));
        s.Activate(); s.Record([2]); s.Play();
        s.Need(s.E<ShiGyRecordCommittedEvent>().Last() is { Count: 1 } replacement && replacement.FrameId != first.FrameId &&
            s.E<ShiGyHandRecordedEvent>().Where(e => e.FrameId == replacement.FrameId).Select(e => e.Target).SequenceEqual([2]) &&
            s.E<ShiGyComparedEvent>().Count(e => e.Target == 0) == 1,
            "A new one-person record replaces the full previous list, so the former self target no longer triggers.");
        var limit = s.V(0).HandLimit; s.Skill("drop-xiugeng"); s.Play(); s.Cold();
        s.Need(s.V(0).HandLimit == limit && s.E<ShiGyHandLimitRaisedEvent>().Count(e => e.Target == 0) == 1,
            "An already applied permanent hand-limit increase remains after Xiugeng is lost through the shared mutation layer."); s.Replay();
    }
    public static void CompletedDamageUseOwnDamageFactAndOriginalHandNoDistanceCold()
    {
        var s = Scene.Start(aggressor: true); s.Play();
        var slash = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.CardId == slash && a.TargetSeats.SequenceEqual([2])), "The original Slash cannot normally reach seat two.");
        s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.Count == 1); s.Offer(Qingdao); s.Activate(); s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "use") == true);
        s.Need(s.V(0).Hp == 3 && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiGuoYuan is not null).ShiGuoYuan is { Damaged: false },
            "Dodging the actual completed enemy Slash selects the undamaged branch from this card's own damage history.");
        s.Answer(c => Branch(c) == "use"); s.Reach(() => s.P?.Choices.Any(c => c.Cards.SequenceEqual([slash]) && c.Targets.SequenceEqual([2])) == true);
        s.Cold(); var hp = s.V(2).Hp; s.Answer(c => c.Cards.SequenceEqual([slash]) && c.Targets.SequenceEqual([2]));
        s.Reach(() => s.E<ShiGyNativeReturnedEvent>().Length == 1);
        var native = s.E<ShiGyNativeIssuedEvent>().Single(); var actual = s.E<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == native.ActionId).Action;
        s.Need(s.V(2).Hp == hp - 1 && actual is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, ConversionChain.Count: 0, PhysicalCards.Count: 1 } &&
            actual.PhysicalCards.Single().CardId == slash && actual.PhysicalCards.Single().From == CardLocation.Hand(0) &&
            s.E<CardUseDebitRecordedEvent>().All(e => e.Debit.CardActionId != native.ActionId),
            "The exact original Hand entity performs a native use across distance two and completes its full child without consuming another player's Play quota."); s.Cold(); s.Replay();

        var hit = Scene.Start(aggressor: true); hit.Play(); hit.End(); hit.Reach(() => hit.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        hit.Answer(c => c.Cards.Count == 0); hit.Offer(Qingdao); hit.Activate(); hit.Reach(() => hit.P?.Choices.Any(c => Branch(c) == "obtain") == true);
        hit.Need(hit.V(0).Hp == 2 && hit.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiGuoYuan is not null).ShiGuoYuan is { Damaged: true } &&
            !hit.P!.Choices.Any(c => Branch(c) == "use"), "Actual damage to Guo Yuan selects Dodge/field discard, not the undamaged hand-use branch.");
        hit.Cold(); hit.Answer(c => Branch(c) == "obtain"); hit.Reach(() => hit.E<ShiGyBenefitObtainedEvent>().Length == 1);
        var obtained = hit.E<ShiGyBenefitObtainedEvent>().Single();
        hit.Need(obtained is { RequestedKind: CardKind.Dodge, CardId: { } id } && hit.V(0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Dodge) &&
            hit.G.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)),
            "The benefit obtains an existing real Dodge from the deck and records its actual movement."); hit.Replay();

        var wine = Scene.Start(aggressor: true); wine.Play(); wine.End(); wine.Reach(() => wine.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        wine.Answer(c => c.Cards.Count == 1); wine.Offer(Qingdao); wine.Activate(); wine.Reach(() => wine.P?.Choices.Any(c => Branch(c) == "use") == true);
        wine.Answer(c => Branch(c) == "use"); wine.Reach(() => wine.P?.Choices.Any(c => c.Cards is [var card] && wine.V(0).Hand.Any(h => h.Id == card && h.Kind == CardKind.Alcohol)) == true);
        wine.Cold(); wine.Answer(c => c.Cards is [var card] && wine.V(0).Hand.Any(h => h.Id == card && h.Kind == CardKind.Alcohol));
        wine.Reach(() => wine.E<ShiGyNativeReturnedEvent>().Length == 1);
        var wineUse = wine.E<ShiGyNativeIssuedEvent>().Single();
        wine.Need(wine.V(0).HasAlcoholEffect && wine.E<JianjiOrdinaryAlcoholPaidEvent>().Single(e => e.NativeUseId == wineUse.UseId).JianjiProgramFrameId is > 0,
            "A real off-turn Wine retains its native ordinary-use quota invoice and the exact Qingdao program parent.");
        wine.Cold(); wine.Reach(() => wine.E<TurnEndedEvent>().Any(e => e.ActorSeat == 1));
        wine.Need(!wine.V(0).HasAlcoholEffect, "Unused off-turn Wine expires with the actual turn in which Qingdao used it, rather than leaking into Guo Yuan's next turn."); wine.Replay();
    }
    public static void OrderedRepeatedSourceDiscardPaidChildrenAndSuccessfulSkillLossCold()
    {
        var s = Scene.Start(); s.Play(); s.Skill("damage-eight", [1]); s.Offer(Chenshe); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "discard") == true);
        var victim = s.V(1).Hp;
        s.Need(victim == 0 && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiGuoYuan is not null).ShiGuoYuan!.Seats.SequenceEqual([0, 1, 0]),
            "The actual damage source is also Guo Yuan, but the prescribed owner/victim/source occurrences remain three ordered payments.");
        s.Cold(); s.Answer(c => c.Cards is [var id] && s.V(0).Hand.Any(h => h.Id == id && h.Suit == Suit.Heart));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold();
        s.Need(s.E<ShiGyDiscardPaidEvent>().Length == 1 && s.E<ShiGyRescueIssuedEvent>().Length == 0 && s.V(1).Hp == victim,
            "The first cost is genuinely paid once, while its native movement observer blocks subsequent costs and recovery.");
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "discard" && c.Parameters.GetValueOrDefault("card-owner-seat") == "1") == true);
        s.Need(s.P!.Choices.All(c => c.Cards.Count == 0), "Foreign hidden Hand choices reveal only an opaque slot, never the suit needed for the rescue.");
        s.Cold(); s.Answer(_ => true);
        s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "discard" && c.Parameters.GetValueOrDefault("card-owner-seat") == "0") == true);
        s.Answer(c => c.Cards is [var id] && s.V(0).Hand.Any(h => h.Id == id && h.Suit == Suit.Heart));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.E<ShiGyChensheLostEvent>().Length == 1); s.Play();
        var paid = s.E<ShiGyDiscardPaidEvent>().Select(e => e.Card).ToArray();
        s.Need(paid.Select(p => p.OwnerSeat).SequenceEqual([0, 1, 0]) && paid.Select(p => p.Suit).Distinct().SequenceEqual([Suit.Heart]) && paid.Select(p => p.CardId).Distinct().Count() == 3 &&
            s.V(1).Hp == s.V(1).MaxHp && s.E<ShiGyRescueIssuedEvent>().Length == 1 && s.G.GetHumanLegalActions().All(a => a.SkillId != Chenshe) &&
            s.V(0).SkillRuntimeStates!.All(r => r.SkillId != Chenshe),
            "All three physical payments retain their occurrence and effective suit, recover the original victim to maximum exactly once, and lose Chenshe through shared skill changes."); s.Cold(); s.Replay();
    }

    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Skill(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Offer(string id) => Reach(() => P?.SkillPrompt?.SkillId == id && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() { Answer(c => Key(c) == "activate"); }
        public void Record(int[] seats)
        {
            Reach(() => P?.Choices.Any(c => Branch(c) == "record") == true);
            foreach (var seat in seats) Answer(c => Branch(c) == "record" && c.Targets.SequenceEqual([seat]));
            Cold(); Answer(c => Branch(c) == "finish");
        }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact original native choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 350; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Guo Yuan native prompt. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "Bounded native fixture remains alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missing bounded actual native boundary.");
        }
        public void Cold()
        { var expected = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(expected == State(), "Cold recovery preserves original record, native use and already-paid rescue children."); }
        public void Replay()
        { var expected = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(expected == State(), "The accepted native command journal reproduces all physical payments and frame returns."); }
        public void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Guo Yuan command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Compared = E<ShiGyComparedEvent>(), Paid = E<ShiGyDiscardPaidEvent>(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start(bool aggressor = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.Alcohol, Suit.Club, 6)];
            var foes = Enumerable.Range(1, 3).Select(seat => new ShanheCombatantDefinition(aggressor && seat == 1 ? Foe : Peer, Hp: 8)
            { StartingHand = [new(aggressor && seat == 1 ? CardKind.Slash : CardKind.Peach, Suit.Heart, 7)] }).ToArray();
            var setup = new ShanheBattleSetup(631, 1, "shi-guo-yuan-native", Hero, 3, 0, 0, 0, 0, [Driver, Watch], hand, [], foes, [], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-guo-yuan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],
               "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],
               "activations":[
                 {"id":"damage-eight","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":8}]},
                 {"id":"drop-xiugeng","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Xiugeng}}"],"sourceBind":"{{Quiet}}"}]}
               ]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-guo-yuan.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Aggressor}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "真实濒死与技能变更驱动", description = "真实伤害与共享技能变更。" },
                [Watch] = new { name = "付款观察", description = "真实弃牌子结算。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [Quiet] = new { name = "安静同桌", description = "保留真实回合。" }, [Aggressor] = new { name = "原生进攻者", description = "实际AI出牌。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实同桌", "", Quiet, "wei", 8)); b.AddGeneral(new(Foe, "真实原生进攻者", "", Aggressor, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势国渊真实记录与顺序救援", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer, Foe], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
