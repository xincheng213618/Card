# BWIKI 规则参考数据

`data/reference/bwiki` 保存来自三国杀 BWIKI 公共 MediaWiki API 的规则参考快照，供人物版本核对和人工审计。它不参与运行时执行，页面模板也不作为可执行代码加载。

## 文件与证据

| 文件 | 内容 |
| --- | --- |
| `category-generals.json`、`category-skills.json` | 分类成员索引 |
| `generals.json`、`skills.json` | 提取的武将、技能及同页版本记录 |
| `pages/<pageid>.json` | 保留规则字段的单页审计记录 |
| `manifest.json` | 页数、解析状态、覆盖、关联告警与当前 pageid |
| `last-attempt.json` | 最近一次同步尝试与发布状态 |

记录保存 pageid、revid、revision 时间、页面地址、版本、规则字段和源文本 SHA-256。`source` 是裁剪后重建的规则模板片段，`sourceSha256` 对应完整源文本，两者不能直接互算比较。传记、故事、台词、配音和音频不进入发布的规则记录，图片元数据只定位来源。

读取方以索引和 manifest 的 `currentPageids` 确定当前快照范围，不能把目录中所有 page 文件当成仍在分类里的记录。同页不同版本分别记录，记录数量不等于独立技能数量。

`complete` 表示 API 获取完整，`rulesTemplateCoverageComplete` 表示规则模板解析覆盖；关联缺项和未支持模板仍需查看明细。当前 manifest 标记 `reconstructedFromPreservedSnapshot`，其中 `collectedAt` 是清单重建时间；原始请求时间、continuation token 与请求计数不可用的字段保留为空，不能据此声明数据新鲜度。

## 同步工具

在仓库根目录运行：

```powershell
python -B .\tools\sync_bwiki_reference.py
python -B .\tools\sync_bwiki_reference.py --refresh
```

默认复用按完整请求参数建立的原始响应缓存；`--refresh` 重新请求远端 revision。脚本只访问 `wiki.biligame.com/sgs/api.php`，跟随分类 continuation，每批最多读取 50 页，并使用有限重试和请求间隔。

整批抓取、解析和索引先在暂存区完成，获取不完整时写入 `last-attempt.json` 并返回非零，不发布该批结果。成功时发布页面、索引，最后替换 manifest。发布不是整个目录原子操作；若发布中途失败，须结合 `publishedSnapshotPreserved` 与 `publicationStarted` 检查一致性或重新运行完成发布。

原始响应缓存包含未经规则裁剪的页面资料，属于忽略的工具缓存。规则迁移应同时核对人物的具体产品线、所选技能版本与当前代码；参考快照完整不等于项目已实现其中所有内容。
