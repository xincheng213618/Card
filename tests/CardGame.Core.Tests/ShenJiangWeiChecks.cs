using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenJiangWeiChecks
{
    private const string General = "anniversary:shen-jiang-wei", Driver = "fixture:shen-jiang-wei-driver", Quiet = "fixture:shen-jiang-wei-quiet", Pause = "fixture:shen-jiang-wei-pause", Mode = "identity:classic-shen-jiang-wei-fixture";
    public static void NativeTianrenDiscardsAndMaximumThreshold()
    {
        var (g, registry) = Start(); Fuel(g); var max = V(g).MaxHp;
        Need(!g.GetHumanLegalActions().Any(a => a.SkillId == "anniversary:pingxiang"), "The limited skill cannot offer an unpaid nine-maximum-HP cost.");
        UseKind(g, CardKind.Slash, [1]); Play(g);
        Need(Mark(g) == 0 && E<ShenJiangWeiMarkReceivedEvent>(g).Length == 0, "A physical Slash discarded by its actual native Use never counts for Tianren.");
        DiscardKind(g, CardKind.Crossbow); Play(g); Need(Mark(g) == 0, "An actual discarded equipment entity is outside Tianren's basic/ordinary-trick categories.");
        for (var i = 0; i < max - 1; i++) { DiscardBasic(g); Play(g); }
        Need(Mark(g) == max - 1 && V(g).MaxHp == max, "Every real non-use basic discard gives exactly one public Tianren mark.");
        DiscardBasic(g); Reach(g, p => IsPause(p,"draw-paid"));
        Need(V(g).MaxHp == max + 1 && Mark(g) == 0 && E<ShenJiangWeiGrowthPaidEvent>(g).Single() is { Removed: > 0 } paid && paid.Removed == max && E<ShenJiangWeiDrawReturnedEvent>(g).Single().ActualCount == 2,
            "The threshold consumes the old maximum's exact marks, grows maximum HP once and uses the native two-card draw.");
        g = Cold(g, registry); Continue(g); Play(g);
        Skill(g, Driver, "grow", []); Play(g); var before = V(g).MaxHp; var reduced = before - 9;
        for (var i = 0; i < reduced + 1; i++) { DiscardBasic(g); Play(g); }
        Need(Mark(g) == reduced + 1 && V(g).MaxHp == before, "A stock below the current maximum waits until the actual maximum changes.");
        Skill(g, "anniversary:pingxiang", "nine-fire-slashes", []); Reach(g, p => IsPause(p,"maximum-paid"));
        Need(E<ShenJiangWeiPingxiangPaidEvent>(g).Single() is { } cost && cost.MaximumBefore == before && cost.MaximumAfter == reduced,
            "Pingxiang records an actual nine-point maximum-HP payment before its native health children.");
        g = Cold(g, registry); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:pingxiang");
        Need(V(g).MaxHp == reduced + 1 && Mark(g) == 1 && E<ShenJiangWeiGrowthPaidEvent>(g).Length == 2, "Lowering maximum HP rechecks the remaining Tianren stock before the first offered FireSlash.");
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "stop"); Play(g);
        Need(V(g).HandLimit == V(g).MaxHp && V(g).Skills?.Any(s => s.Id == "anniversary:jiufa") == false, "Choosing zero of the at-most-nine Slashes still performs the actual Jiufa loss and persistent maximum-HP hand limit.");
        _ = Cold(g, registry);
    }

    public static void NativeJiufaRanksAndPingxiangFireSlashes()
    {
        var (g, registry) = Start(); Fuel(g);
        Skill(g, Driver, "duel-pair", [1,0]); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var response = P(g)!.Choices.First(c => c.Cards.Count == 1); var responseId = response.Cards.Single();
        Answer(g, c => c.Id == response.Id); Play(g);
        Need(E<ShenJiangWeiNameRecordedEvent>(g).Single() is { Name: CardKind.Slash, DistinctCount: 1 } &&
            g.CardMovements.Where(m => m.CardId == responseId && m.To == CardLocation.DiscardPile).Any(m => E<ShenJiangWeiMarkReceivedEvent>(g).Any(e => e.MovementSequence == m.Sequence)),
            "A genuinely played Duel-response Slash records its completed normalized name and its non-use physical discard counts for Tianren.");
        foreach (var kind in new[] { CardKind.Crossbow, CardKind.BaguaFormation, CardKind.RenwangShield, CardKind.QinggangSword, CardKind.OffensiveHorse, CardKind.DefensiveHorse, CardKind.JadeSeal, CardKind.Slash })
        { UseKind(g, kind, kind == CardKind.Slash ? [1] : null); Play(g); }
        Need(E<ShenJiangWeiNameRecordedEvent>(g).Last().DistinctCount == 8, "Seven equipment names and the played/used elemental-normalized Slash retain eight names; reusing Slash does not consume another name.");
        UseKind(g, CardKind.IronChain, [1,3]); Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:jiufa");
        Need(E<ShenJiangWeiNameRecordedEvent>(g).Last().DistinctCount == 9, "A native multi-target IronChain completes the ninth distinct name exactly once.");
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "reveal"); Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:jiufa" && p.Choices.Any(c => c.Cards.Count == 1));
        var revealed = E<ProgramCardsRevealedEvent>(g).Single(e => e.SkillId == "anniversary:jiufa");
        var expected = revealed.Cards.GroupBy(c => c.Rank).Where(group => group.Count() > 1).Select(group => group.Key).Order().ToArray();
        Need(revealed.Cards.Count == 9 && expected.Length > 0, "Jiufa publicly reveals nine actual top-deck entities before selecting one from each repeated rank.");
        g = Cold(g, registry);
        for (var i = 0; i < expected.Length; i++)
        {
            Need(P(g)!.Choices.All(c => c.Cards.Count == 1 && revealed.Cards.Single(card => card.Id == c.Cards[0]).Rank == expected[i]), "Each current Jiufa choice belongs to the next frozen repeated-rank group.");
            Answer(g, _ => true);
            if (i + 1 < expected.Length) Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:jiufa");
        }
        Reach(g, p => IsPause(p,"gain-paid"));
        Need(E<ShenJiangWeiRankChosenEvent>(g).Length == expected.Length && E<ShenJiangWeiMovementPaidEvent>(g).Single(e => e.Stage == ShenJiangWeiStage.GainChildren).Count == expected.Length,
            "Exactly one actual chosen entity per duplicate rank moves to hand before the real gain child; leftovers have not been paid twice.");
        g = Cold(g, registry); Continue(g); Play(g);
        var chosen = E<ShenJiangWeiRankChosenEvent>(g).Select(e => e.CardId).ToHashSet();
        Need(revealed.Cards.Where(c => !chosen.Contains(c.Id)).All(c => g.CardMovements.Any(m => m.CardId == c.Id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.anniversary:jiufa.discard")), "All unchosen original revealed entities actually enter the discard pile.");
        Skill(g, Driver, "grow", []); Play(g); var hp1 = V(g,1).Hp; var hp3 = V(g,3).Hp;
        Skill(g, "anniversary:pingxiang", "nine-fire-slashes", []); Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:pingxiang");
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => IsPause(p,"fire-issued"));
        Need(g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.ShenJiangWeiPingxiang is not null) is { CardKind: CardKind.FireSlash, PhysicalCardIds.Count: 0 }, "Each Pingxiang attack is an actual zero-material FireSlash use with its own native window.");
        g = Cold(g, registry); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:pingxiang");
        Need(V(g,1).Hp == hp1 - 1 && V(g,3).Hp == hp3 - 1 && E<ShenJiangWeiSlashReturnedEvent>(g).Length == 1,
            "Cold restore returns through the whole native FireSlash, including real elemental chain damage, before offering the next Slash.");
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => IsPause(p,"fire-issued")); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == "anniversary:pingxiang");
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "stop"); Play(g);
        Need(E<ShenJiangWeiSlashIssuedEvent>(g).Length == 2 && E<ShenJiangWeiSlashReturnedEvent>(g).Length == 2 && E<ShenJiangWeiPingxiangCompletedEvent>(g).Single().SlashCount == 2 && V(g).HandLimit == V(g).MaxHp,
            "Two separately returned native FireSlashes ignore the ordinary Slash quota and stop before applying the final persistent hand rule.");
        Need(!g.GetHumanLegalActions().Any(a => a.SkillId == "anniversary:pingxiang"), "The real limited use remains consumed after finishing its original parent.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int s = 0) => g.CreateSnapshot(s).Players.Single(p => p.Seat == s);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0,4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static int Mark(GameEngine g) => V(g).Markers?.Where(m => m.Kind == PlayerMarkerKind.Tianren).Sum(m => m.Count) ?? 0;
    private static bool IsPause(PendingDecision p, string trigger) => p.SkillPrompt?.SkillId == Pause && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == trigger || c.Id.Value.Contains(trigger, StringComparison.Ordinal));
    private static void Fuel(GameEngine g) { Skill(g, Driver,"draw",[]); Play(g); Skill(g,Driver,"draw",[]); Play(g); }
    private static void Skill(GameEngine g, string skill, string activation, int[] targets, int[]? cards = null) => Accept(g,new UseProgramSkillCommand(0,skill,activation,cards ?? [],targets,g.Revision,P(g)!.PromptId));
    private static void DiscardKind(GameEngine g, CardKind kind) => Skill(g,Driver,"discard",[],[V(g).Hand.First(c => c.Kind == kind).Id]);
    private static void DiscardBasic(GameEngine g)
    {
        if (!V(g).Hand.Any(c => c.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.Duel)) { Skill(g,Driver,"draw",[]); Play(g); }
        Skill(g,Driver,"discard",[],[V(g).Hand.First(c => c.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.Duel).Id]);
    }
    private static void UseKind(GameEngine g, CardKind kind, int[]? targets)
    {
        var ids = V(g).Hand.Where(c => c.Kind == kind).Select(c => c.Id).ToHashSet();
        var a = g.GetHumanLegalActions().First(a => a.CardId is { } id && ids.Contains(id) && (targets is null || a.TargetSeats.SequenceEqual(targets)));
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId) { ConversionSource = a.ConversionSource });
    }
    private static void Answer(GameEngine g, Func<PromptChoice,bool> predicate) { var p=P(g)!; Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
    private static void Play(GameEngine g) => Reach(g,p=>p is { PlayerSeat:0,Kind:DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision,bool> ready)
    {
        for(var i=0;i<250;i++)
        {
            if(P(g) is {} p)
            {
                if(ready(p)) return;
                if(p.Kind==DecisionKind.SelectFaction){Answer(g,_=>true);continue;}
                if(p.SkillPrompt?.SkillId==Pause){Continue(g);continue;}
                if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying){Answer(g,c=>c.Cards.Count==0);continue;}
            }
            Accept(g,new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Jiang Wei missed native boundary: "+JsonSerializer.Serialize(new { Prompt=P(g),Frames=g.ResolutionStack.Select(f=>new{f.Id,Type=f.GetType().Name}) }));
    }
    private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected native Shen Jiang Wei fixture command.");}
    private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"All private views, real pending parent frames, physical movements and native command prefix cold-restore identically.");return restored;}
    private static (GameEngine,ContentRegistry) Start()
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
        Accept(g,new StartGameCommand());Reach(g,p=>p.Kind==DecisionKind.SelectGeneral);Accept(g,new SelectGeneralCommand(0,General,g.Revision,P(g)!.PromptId));Play(g);return(g,r);
    }
    private sealed class Fixture:IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-shen-jiang-wei",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b),General);
            var rules=$$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":9}]},
                {"id":"duel-pair","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Pause}}","revision":1,"triggers":[
                {"id":"draw-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:tianren.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-paid","options":[{"id":"continue"}]}]},
                {"id":"gain-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:jiufa.obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-paid","options":[{"id":"continue"}]}]},
                {"id":"maximum-paid","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"maximumHp"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"maximum-paid","options":[{"id":"continue"}]}]},
                {"id":"fire-issued","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["fireSlash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"fire-issued","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids=new[]{Driver,Quiet,Pause};var cat=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="Small native Shen Jiang Wei fixture.",optionLabels=id==Pause?new Dictionary<string,string>{{"continue","继续"}}:[]})}));
            foreach(var(id,p)in cat.Programs)b.AddSkill(new(id,id,"Native fixture"){Program=p,ProgramPresentation=cat.Presentations[id]});
            for(var s=1;s<4;s++)b.AddGeneral(new($"fixture:shen-jw-peer-{s}","Quiet participant","supporter",Quiet,"wei",20));
            var kinds=new[]{"standard:crossbow","standard:bagua","standard:renwang_shield","standard:qinggang_sword","standard:offensive_horse","standard:defensive_horse","standard:jade_seal","standard:slash","standard:fire_slash","standard:duel","standard:iron_chain","standard:draw_two"};
            b.AddDeck(new("fixture:shen-jw-deck","Real mixed native deck",20,2,[]){PhysicalCards=Enumerable.Range(0,360).Select(i=>new ContentDeckPhysicalCard(kinds[i%kinds.Length],Suit.Club,i%3+1)).ToArray()});
            b.AddMode(new(Mode,"Native Shen Jiang Wei",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:shen-jw-deck",GeneralCandidateCount:4,GeneralPoolIds:[General,"fixture:shen-jw-peer-1","fixture:shen-jw-peer-2","fixture:shen-jw-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==General?d with{AdditionalSkillIds=[..d.AdditionalSkillIds!,Driver,Pause]}:d);
        public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
    }
}
