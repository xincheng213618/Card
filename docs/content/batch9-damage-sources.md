# Batch 9 damage and HP-loss generals

This batch uses the following official OL descriptions. Only skills with complete rules programs should be enabled; the other skills remain explicitly pending common runtime capabilities.

| General | Faction / HP | Official rule source | Implemented program | Pending rule |
| --- | --- | --- | --- | --- |
| `boundary:huang-gai` | Wu / 4 | [OL general page](https://www.sanguosha.com/hero/307), [2014 boundary announcement](https://www.sanguosha.com/news/20140318_6743_5540) | `boundary:kurou`: once per turn during Play, discard one hand or equipment card, then lose 1 HP | `boundary:zhajiang`: after each point of HP loss draw three; if during own Play, grant one extra Slash and red Slash distance/response restrictions through turn end |
| `boundary:wei-yan` | Shu / 4 | [OL general page](https://www.sanguosha.com/hero/456), [2020 release notice](https://www.sanguosha.com/news/20200715_9068_1122) | `boundary:kuanggu`: after each point of damage to a character at distance at most 1, optionally recover 1 HP or draw one card | `boundary:qimou`: limited Play activation with chosen HP loss X, X draws, distance −X and Slash limit +X until turn end |
| `classic:dong-zhuo` | Qun / 8 | [OL Dong Zhuo rules](https://www.sanguosha.com/act/dongzhuo), [OL classic card notice](https://www.sanguosha.com/news/20170605_9524_4909) | none in this damage bundle | `classic:jiuchi`, `classic:roulin`, `classic:benghuai`, `classic:baonue`; the shared alcohol conversion, gender-bound response count, lowest-HP check and third-party lord judgment are being integrated separately |

The distance fact used by `boundary:kuanggu` is frozen when the damage is applied, so later death or equipment movement cannot change whether that point qualifies. `boundary:kurou` uses the existing activation card payment and HP-loss effects. Neither skill uses a general-ID branch in the engine.
