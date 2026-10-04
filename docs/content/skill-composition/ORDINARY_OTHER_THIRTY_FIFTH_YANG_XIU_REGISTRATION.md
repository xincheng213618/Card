# 统一整合登记清单

NEW module `OrdinaryYangXiuContent.Register`（StandardClassicGeneralPackage已有窄patch，唯一登记）；`ol:yang-xiu` / portrait key `ol-yang-xiu`，ordinary / sanguosha-ol，图库显式 other（窄patch）。Embedded JSON 通配自动收 bundle，无 csproj 增项。

现 Core runner 新增以下四个现有方法注册；第一个 display-prefix 同时作一条 routine，不能添加武将专属 runner。

| Method | Suggested display name |
| --- | --- |
| OrdinaryYangXiuChecks.ActualMultiTargetTrickWaitsForDrawAndUniqueBorrowedHolderDoesNotOffer | Yang Xiu multi-target trick owns draw children and unique Borrowed Sword is excluded |
| OrdinaryYangXiuChecks.DeclaredCategoryBlocksOwnHandMaterialsButForeignDiscardAndEquipmentRemainLegal | Yang Xiu categories block own hand uses responses and costs without protecting foreign discards |
| OrdinaryYangXiuChecks.HandLimitCountsProtectedCardsAndDiscardsOnlyAvailableOverflow | Yang Xiu protected hand still counts toward limit and only legal overflow is discarded |
| OrdinaryYangXiuChecks.DrawGainDamageDyingAndPaidSourceLossReturnToSameUseOnce | Yang Xiu paid draw returns through real damage dying wine and source loss |

Routine prefix: `Yang Xiu multi-target trick`。WPF 无新交互，复用 ProgramTrigger/SkillChoices 与真实弃牌提示，不新增 WPF 检查。

将 artAdoption 原始 cover 字节复制到 `src/CardGame.Wpf/Assets/official-ol-yang-xiu.png`，按 manifest 提供的 catalogEntry 新增唯一 `ol-yang-xiu`；GeneralArt 已优先读嵌入 catalog，因此不新增 portrait dictionary branch。原图一次 GET，PNG signature/IHDR 静态核750x950，未解码/渲染图片。Source 与 HTTP raw/headers/request 的建议 destination 在 supportFiles 中，不能把 metadata 性别工程判断伪装为 API 字段。
