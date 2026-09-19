# BWIKI 规则参考数据

`data/reference/bwiki` 是从三国杀 BWIKI 公共 MediaWiki API 得到的规则参考快照，供后续逐项迁移和人工审计使用。它不是运行时代码，也不应执行或展开页面中的模板。

数据源为 `分类:武将图鉴测试` 和 `分类:技能`。脚本跟随 `categorymembers` continuation 读取实际分类规模，再以最多 50 页一批获取当前 revision。分类中非主命名空间的模板/文档页仍计入分类总数和 `excludedNamespacePages`，但不会混入技能记录。`generals.json` 和 `skills.json` 保留 pageid、revid、revision timestamp、页面 URL、原始中文字段名、模式或版本、技能描述、武将关联、规则模板片段及源文本 SHA-256。同页历史版本各自成为独立记录。`pages/<pageid>.json` 是剔除传记、故事、台词、配音和音频后的单页审计记录。旧页面文件可能在分类更新后继续留在目录中，读取方应以索引和 manifest 的 `currentPageids` 为准。`manifest.json` 记录分页、总数、成功与失败、未解析页面、结构异常、未支持模板和关联缺项；`complete` 表示 API 抓取完整，`rulesTemplateCoverageComplete` 另行表示规则模板是否全部可解析。

2026-09-20 的实际快照包含分类武将 730 页、技能 1589 页，得到 729 条武将模板记录和 2493 条技能模板/历史版本记录（不能等同于 2493 个独立技能或逐模式拆分记录）。API 抓取失败为 0；`面杀抽将` 没有武将模板，技能分类中的 2 个模板命名空间页面被排除，另有 16 条关联告警，涉及 `写满技能的天书`、`引路标记效果`、`爻袁术` 三个不在武将分类索引中的关联值。所有缺项均在 manifest 中逐条列出，因此该快照不能称为 100% 规则模板覆盖。

默认命令会复用 `.cache` 中已经成功取得的原始 API 响应，适合中断后重跑。缓存键包含完整请求参数（包括 continuation 游标），文件内记录取得时间；缓存不做模板裁剪，以保证首次抓取和缓存重跑输入完全一致。`.cache` 被本目录的 `.gitignore` 排除，不属于可提交的规则资料，可能含页面中未发布到规则资料库的传记、台词等原文：

```powershell
python .\tools\sync_bwiki_reference.py
```

需要检查远端最新 revision 时显式刷新：

```powershell
python .\tools\sync_bwiki_reference.py --refresh
```

抓取器只访问 `wiki.biligame.com/sgs/api.php`，请求间隔默认随机 0.4 至 0.8 秒，超时或 429 会有限退避重试。任何批次失败都会写入 `last-attempt.json` 并使进程返回非零，最后成功发布的 manifest 和索引保持不变；缺少关联字段不会被猜成“无技能”。

脚本在 `.staging` 完成整批抓取、解析和索引后才发布文件，manifest 最后替换。抓取或解析失败只写 `last-attempt.json`，不会覆盖最后一次成功快照。文件发布不是整个目录的原子替换；若磁盘异常发生在发布中途，`publishedSnapshotPreserved` 会为 false，需要核对索引与 page 记录或重跑完成发布。读取方应检查 `last-attempt.json` 了解最近一次更新尝试。

记录中的 `source` 是保留规则字段后重建的模板片段；`sourceSha256` 对应原页面完整源文本，不能用裁剪后的片段重新计算并比较。图片元数据只用于定位来源，不自动下载图鉴全部图片。

当前 `manifest.json` 不是首次成功抓取时的原文件：2026-09-20 清理旧缓存后的更新尝试收到 HTTP 567，旧脚本当时覆盖了 manifest。现有 manifest 依据保留下来的 category、index 和 page 文件以及首次成功运行统计离线重建，并明确标记 `reconstructedFromPreservedSnapshot`。原始 API 请求时间、continuation token 和请求计数无法恢复，均保持空值或不可用标记；`last-attempt.json` 保留这次 HTTP 567 失败事实。
