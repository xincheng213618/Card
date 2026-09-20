# 可执行技能配置 v7：跨拥有者出牌阶段贡献

规则文件使用 `schemaVersion: 7`，展示文件仍为 `schemaVersion: 1`。执行器标识为 `skill-program-v7`，最低引擎规则版本为 85。schema 1～6 的字段、玩法哈希及 rules 79～84 路径保持不变；schema 7 内容不会进入旧规则存档。

v7 新增 `contributions`，表达“技能属于甲，但出牌阶段入口由符合条件的其他角色乙使用，乙把自己一张实体手牌交给甲”。它解决的是跨拥有者执行资格，不把“主公技”标签解释成主动技种类，也不把使用次数记到技能拥有者身上。

设计依据 [BWIKI 技能概念介绍](https://wiki.biligame.com/sgs/%E6%8A%80%E8%83%BD%E6%A6%82%E5%BF%B5%E4%BB%8B%E7%BB%8D)：主公技是拥有资格标签；状态技、触发技与已开始效果的执行另行建模。[一将成名官网经典张角](https://x.sanguosha.com/hero/33.html)明确经典黄天由其他群势力角色在各自出牌阶段限一次交【闪】或【闪电】；[三国杀 OL 界张角](https://www.sanguosha.com/hero/448)把候选改为【闪】或黑桃手牌。两版因此复用同一种贡献能力，只改变实体牌过滤。

## 配置

```json
{
  "schemaVersion": 7,
  "skills": [
    {
      "id": "example:classic-huangtian",
      "revision": 1,
      "modifiers": [],
      "viewAs": [],
      "activations": [],
      "triggers": [],
      "contributions": [
        {
          "id": "contribute",
          "providerFactions": ["qun"],
          "ownerRole": "lord",
          "cardKinds": ["dodge", "lightning"],
          "cardSuits": [],
          "usesPerPlayPhase": 1
        }
      ]
    },
    {
      "id": "example:boundary-huangtian",
      "revision": 1,
      "modifiers": [],
      "viewAs": [],
      "activations": [],
      "triggers": [],
      "contributions": [
        {
          "id": "contribute",
          "providerFactions": ["qun"],
          "ownerRole": "lord",
          "cardKinds": ["dodge"],
          "cardSuits": ["spade"],
          "usesPerPlayPhase": 1
        }
      ]
    }
  ]
}
```

字段契约：

- `providerFactions` 过滤当前出牌角色的真实势力；至少一项且不可重复。
- `ownerRole` 过滤当前存活且真正拥有此程序的角色。黄天使用 `lord`；拥有者不能同时作为自己的提供者。
- `cardKinds` 与 `cardSuits` 对提供者自己的实体手牌做并集匹配。`["dodge"] + ["spade"]` 是“实体牌名为闪，或实体花色为黑桃”，不是两项同时满足。
- 两个牌过滤数组不能同时为空。候选不读取装备区、木牛粮、他人手牌或转换后的有效牌名。
- `usesPerPlayPhase` 记在“提供者座位、技能拥有者座位、SkillId、ContributionId”四元绑定账本；进入该提供者的新出牌阶段时清理。它不消耗技能拥有者自己的普通 `activations` 次数。
- `contributions[].id` 与同一技能的 `activations[].id` 共用出牌绑定命名空间，重复会在装载时拒绝。

## 命令、牌区与恢复

合法动作同时公开 `ProgramSkillId`、贡献绑定 ID 和 `ProgramSkillOwnerSeat`，目标固定为该拥有者。命令携带独立 `SkillOwnerSeat`；提交时重新核对拥有者仍存活、身份仍符合、程序仍由其拥有、提供者势力、私有候选牌和本阶段账本。伪造拥有者、目标或牌不会产生部分移动。

成功后，所选实体牌从提供者手牌进入 `Processing`，再公开进入技能拥有者手牌；`ProgramSkillContributionResolvedEvent` 分别记录提供者和技能拥有者。Checkpoint 继续以已接受命令前缀重建移动与账本，展示层不能从中文技能名反推任何资格。

WPF 和 AI 复用既有配置技能选牌／选目标入口。动作描述显示接收者，客户端用技能拥有者座位区分同名绑定；AI 只读取提供者自己的过滤后候选和公开接收者关系。

## 本块没有声称完成的能力

- 结构化 `lord`、锁定、限定、觉醒或转换标签；`ownerRole` 只是本条执行入口的资格条件。
- 护驾、激将这类响应询问链，或任意跨拥有者效果图；v7 当前只提供“一张实体手牌公开交给技能拥有者”的贡献能力。
- 本 D3e 交付本身不含正式张角迁移。后续 A51／A52 已分别在经典包 1.65.0／1.66.0 完成两版内容、正式场景与整包注册。
