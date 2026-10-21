# 单骑无双将池实现核对

核对日期：2026-10-21。依据用户提供的十二张游戏内单骑无双将池截图，以及本地成功构建后加载的全局内容注册表。

共记录截图中完整可读的 241 名武将：已实现 208 名，待实现或待完善 33 名（其中 3 名实现中）。部分滚动页底部没有显示完整名称，这份清单不是完整服务器白名单；没有出现在截图中的武将不会因此被自动禁用。

本地不套用账号的“未开通”、招募、强制开启或个人禁将状态。全局已实现内容保持可用；单骑的明确辅助型、护甲机制和商品限制单独管理。标准包、风包不整包禁用，吕蒙、孙权、观虚、攻心保留。

“已实现”表示精确同名版本已注册且 IsGeneralPlayable 返回 true，不等于本次逐将完成实战验收。OL 后缀及麴／麹的明确异体字用于名字匹配；国战版、移动版不充当对应 OL 版本。数据见 [逐将结果](content/wushuang-roster-audit.json) 和 [截图识别名单](content/sources/wushuang-roster-observed-2026-10-21.json)。

魔司马懿、魔曹操、OL 谋小乔在本工作区并行开发中，本次将其标为“实现中”，未计入已实现数量；这些条目不代表被单骑模式禁用。后续以成功注册和验证结果更新状态。

## 待实现或待完善

| 分类 | 武将 | 状态 | 其他版本 |
|---|---|---|---|
| 界一将 | 界关平 | 待实现 |  |
| 文德武备 | 王元姬 | 待实现 |  |
| 文德武备 | 司马师 | 待实现 |  |
| 文德武备 | 司马昭 | 待实现 |  |
| 文德武备 | 李肃 | 待实现 |  |
| 文德武备 | 杨芷 | 待实现 |  |
| 璀璨星河 | 张芝 | 待实现 |  |
| 璀璨星河 | 夏侯玄 | 待实现 |  |
| 璀璨星河 | 卢氏 | 待实现 |  |
| 璀璨星河 | 彭羕 | 待实现 |  |
| 璀璨星河 | 吕伯奢 | 待实现 |  |
| 璀璨星河 | 陆凯 | 待实现 |  |
| 璀璨星河 | 董翓 | 待实现 |  |
| 璀璨星河 | 刘晔 | 待实现 |  |
| 璀璨星河 | 逄纪 | 待实现 |  |
| 璀璨星河 | 张曼成 | 待实现 |  |
| 璀璨星河 | 乔国老 | 待实现 |  |
| 璀璨星河 | 董子安 | 待实现 |  |
| 谋 | 谋姜维 | 待实现 | mou:jiang-wei-mobile |
| 谋 | 谋董卓 | 待实现 |  |
| 谋 | 谋庞统 | 待实现 | mou:pang-tong-mobile |
| 谋 | 谋袁术 | 待实现 |  |
| 谋 | 谋田丰 | 待实现 |  |
| 谋 | 谋贾诩 | 待实现 |  |
| 谋 | 谋张让 | 待实现 |  |
| 谋 | 谋小乔 | 实现中 |  |
| 谋 | 谋鲁肃 | 待实现 |  |
| 谋 | 谋许攸 | 待实现 |  |
| 谋 | 谋戏志才 | 待实现 |  |
| 谋 | 谋程昱 | 待实现 |  |
| 谋 | 谋祝融 | 待实现 | mou:zhu-rong-mobile |
| 魔 | 魔司马懿 | 实现中 |  |
| 魔 | 魔曹操 | 实现中 |  |

## 全部核对结果

| 分类 | 武将 | 本地状态 | 本地可执行版本 |
|---|---|---|---|
| 标准版 | 关羽 | 已实现 | classic:guan-yu |
| 标准版 | 张飞 | 已实现 | classic:zhang-fei |
| 标准版 | 诸葛亮 | 已实现 | classic:zhuge-liang |
| 标准版 | 赵云 | 已实现 | classic:zhao-yun |
| 标准版 | 马超 | 已实现 | classic:ma-chao |
| 标准版 | 黄月英 | 已实现 | classic:huang-yueying |
| 标准版 | 孙权 | 已实现 | classic:sun-quan |
| 标准版 | 甘宁 | 已实现 | classic:gan-ning |
| 标准版 | 吕蒙 | 已实现 | classic:lu-meng |
| 标准版 | 黄盖 | 已实现 | classic:huang-gai |
| 标准版 | 周瑜 | 已实现 | classic:zhou-yu |
| 标准版 | 陆逊 | 已实现 | classic:lu-xun |
| 标准版 | 曹操 | 已实现 | classic:cao-cao |
| 标准版 | 司马懿 | 已实现 | classic:sima-yi |
| 标准版 | 夏侯惇 | 已实现 | classic:xiahou-dun |
| 标准版 | 张辽 | 已实现 | classic:zhang-liao |
| 标准版 | 许褚 | 已实现 | classic:xu-chu |
| 标准版 | 郭嘉 | 已实现 | classic:guo-jia |
| 标准版 | 甄姬 | 已实现 | classic:zhen-ji |
| 标准版 | 吕布 | 已实现 | classic:lu-bu |
| 标准版 | 孙尚香 | 已实现 | classic:sun-shangxiang |
| 神话再临 | 神甘宁 | 已实现 | ol:shen-gan-ning |
| 神话再临 | 许攸 | 已实现 | classic:xu-you |
| 神话再临 | 诸葛瞻 | 已实现 | classic:zhuge-zhan |
| 神话再临 | 魏延 | 已实现 | classic:wei-yan |
| 神话再临 | 夏侯渊 | 已实现 | classic:xiahou-yuan |
| 神话再临 | 典韦 | 已实现 | classic:dian-wei |
| 神话再临 | 袁绍 | 已实现 | classic:yuan-shao |
| 神话再临 | 姜维 | 已实现 | classic:jiang-wei |
| 神话再临 | 孙策 | 已实现 | classic:sun-ce |
| 神话再临 | 黄忠 | 已实现 | classic:huang-zhong |
| 神话再临 | 神吕蒙 | 已实现 | classic:shen-lu-meng |
| 神话再临 | 神周瑜 | 已实现 | classic:shen-zhou-yu、ol:shen-zhou-yu |
| 神话再临 | 神赵云 | 已实现 | classic:shen-zhao-yun、ol:shen-zhao-yun |
| 神话再临 | 张角 | 已实现 | classic:zhang-jiao |
| 神话再临 | 神刘备 | 已实现 | ol:shen-liu-bei |
| 神话再临 | 神陆逊 | 已实现 | ol:shen-lu-xun |
| 神话再临 | 神孙权 | 已实现 | ol:shen-sun-quan |
| 神话再临 | 神张角 | 已实现 | ol:shen-zhang-jiao |
| 神话再临 | 神典韦 | 已实现 | ol:shen-dian-wei |
| 神话再临 | 神黄忠 | 已实现 | ol:shen-huang-zhong |
| 神话再临 | 王基 | 已实现 | classic:wang-ji |
| 一将成名 | 马谡 | 已实现 | classic:ma-su |
| 一将成名 | 王异 | 已实现 | classic:wang-yi |
| 一将成名 | 秦宓 | 已实现 | classic:qin-mi |
| 一将成名 | 凌统 | 已实现 | classic:ling-tong |
| 一将成名 | 徐盛 | 已实现 | classic:xu-sheng |
| 一将成名 | 于禁 | 已实现 | classic:yu-jin |
| 一将成名 | 张春华 | 已实现 | classic:zhang-chun-hua |
| 一将成名 | 马岱 | 已实现 | classic:ma-dai |
| 一将成名 | 朱然 | 已实现 | classic:zhu-ran |
| 一将成名 | 吴懿 | 已实现 | classic:wu-yi |
| 一将成名 | 朱桓 | 已实现 | classic:zhu-huan |
| 一将成名 | 沮授 | 已实现 | classic:ju-shou |
| 一将成名 | 孙资刘放 | 已实现 | classic:sun-zi-liu-fang |
| 一将成名 | 辛宪英 | 已实现 | classic:xin-xianying |
| 一将成名 | 嵇康 | 已实现 | classic:ji-kang |
| 一将成名 | 薛综 | 已实现 | classic:xue-zong |
| 界限突破 | 界黄忠 | 已实现 | boundary:huang-zhong |
| 界限突破 | 界关羽 | 已实现 | boundary:guan-yu |
| 界限突破 | 界黄盖 | 已实现 | boundary:huang-gai |
| 界限突破 | 界诸葛亮 | 已实现 | boundary:zhuge-liang |
| 界限突破 | 界孙权 | 已实现 | boundary:sun-quan |
| 界限突破 | 界魏延 | 已实现 | boundary:wei-yan |
| 界限突破 | 界姜维 | 已实现 | boundary:jiang-wei |
| 界限突破 | 界典韦 | 已实现 | boundary:dian-wei |
| 界限突破 | 界张飞 | 已实现 | boundary:zhang-fei |
| 界限突破 | 界赵云 | 已实现 | boundary:zhao-yun |
| 界限突破 | 界马超 | 已实现 | boundary:ma-chao |
| 界限突破 | 界甘宁 | 已实现 | boundary:gan-ning |
| 界限突破 | 界周瑜 | 已实现 | boundary:zhou-yu |
| 界限突破 | 界陆逊 | 已实现 | boundary:lu-xun |
| 界限突破 | 界曹操 | 已实现 | boundary:cao-cao |
| 界限突破 | 界司马懿 | 已实现 | boundary:sima-yi |
| 界限突破 | 界夏侯惇 | 已实现 | boundary:xiahou-dun |
| 界限突破 | 界张辽 | 已实现 | boundary:zhang-liao |
| 界限突破 | 界许褚 | 已实现 | boundary:xu-chu |
| 界限突破 | 界郭嘉 | 已实现 | boundary:guo-jia |
| 界限突破 | 界吕布 | 已实现 | boundary:lu-bu |
| 界限突破 | 界黄月英 | 已实现 | boundary:huang-yueying |
| 界限突破 | 界甄姬 | 已实现 | boundary:zhen-ji |
| 界限突破 | 界华雄 | 已实现 | boundary:hua-xiong |
| 界限突破 | 界张角 | 已实现 | boundary:zhang-jiao |
| 界限突破 | 界孙策 | 已实现 | boundary:sun-ce |
| 界限突破 | 界小乔 | 已实现 | boundary:xiao-qiao |
| 界限突破 | 界庞德 | 已实现 | boundary:pang-de |
| 界限突破 | 界太史慈 | 已实现 | boundary:taishi-ci |
| 界限突破 | 界贾诩 | 已实现 | boundary:jia-xu |
| 界限突破 | 界颜良文丑 | 已实现 | boundary:yan-liang-wen-chou |
| 界一将 | 界徐盛 | 已实现 | boundary:xu-sheng |
| 界一将 | 界凌统 | 已实现 | boundary:ling-tong |
| 界一将 | 界虞翻 | 已实现 | boundary:yu-fan |
| 界一将 | 界高顺 | 已实现 | boundary:gao-shun |
| 界一将 | 界王异 | 已实现 | boundary:wang-yi |
| 界一将 | 界张春华 | 已实现 | boundary:zhang-chun-hua |
| 界一将 | 界曹冲 | 已实现 | boundary:cao-chong |
| 界一将 | 界曹植 | 已实现 | boundary:cao-zhi |
| 界一将 | 界廖化 | 已实现 | boundary:liao-hua |
| 界一将 | 界李儒 | 已实现 | boundary:li-ru |
| 界一将 | 界郭淮 | 已实现 | boundary:guo-huai |
| 界一将 | 界韩当 | 已实现 | boundary:han-dang |
| 界一将 | 界关兴张苞 | 已实现 | boundary:guan-xing-zhang-bao |
| 界一将 | 界马岱 | 已实现 | boundary:ma-dai |
| 界一将 | 界郭皇后 | 已实现 | boundary:guo-huang-hou |
| 界一将 | 界沮授 | 已实现 | boundary:ju-shou |
| 界一将 | 界夏侯氏 | 已实现 | boundary:xiahou-shi |
| 界一将 | 界张松 | 已实现 | boundary:zhang-song |
| 界一将 | 界于禁 | 已实现 | boundary:yu-jin |
| 界一将 | 界关平 | 待实现 |  |
| 界一将 | 界步练师 | 已实现 | boundary:bu-lian-shi |
| 文德武备 | 王元姬 | 待实现 |  |
| 文德武备 | 杜预 | 已实现 | ol:du-yu |
| 文德武备 | 杨艳 | 已实现 | ol:yang-yan |
| 文德武备 | 钟琰 | 已实现 | ol:zhong-yan |
| 文德武备 | 成济成倅 | 已实现 | ol:cheng-ji-cheng-cui |
| 文德武备 | 司马师 | 待实现 |  |
| 文德武备 | 司马昭 | 待实现 |  |
| 文德武备 | 石苞 | 已实现 | ol:shi-bao |
| 文德武备 | 李肃 | 待实现 |  |
| 文德武备 | 司马伷 | 已实现 | ol:sima-zhou |
| 文德武备 | 杨芷 | 待实现 |  |
| 文德武备 | 贾充 | 已实现 | ol:jia-chong |
| 文德武备 | 王祥 | 已实现 | ol:wang-xiang |
| 璀璨星河 | 王荣 | 已实现 | ol:wang-rong |
| 璀璨星河 | 麴义 | 已实现 | classic:qu-yi |
| 璀璨星河 | 李傕 | 已实现 | ol:li-jue |
| 璀璨星河 | 丁原 | 已实现 | ol:ding-yuan |
| 璀璨星河 | 清河公主 | 已实现 | ol:qinghe-gongzhu |
| 璀璨星河 | 诸葛恪 | 已实现 | ol:zhuge-ke |
| 璀璨星河 | 夏侯霸 | 已实现 | ol:xiahou-ba |
| 璀璨星河 | 乐进 | 已实现 | ol:yue-jin |
| 璀璨星河 | 曹昂 | 已实现 | classic:cao-ang |
| 璀璨星河 | 诸葛瑾 | 已实现 | ol:zhuge-jin |
| 璀璨星河 | 潘凤 | 已实现 | ol:pan-feng |
| 璀璨星河 | 马良 | 已实现 | ol:ma-liang |
| 璀璨星河 | 程昱 | 已实现 | ol:cheng-yu |
| 璀璨星河 | 张鲁 | 已实现 | ol:zhang-lu |
| 璀璨星河 | 邓芝 | 已实现 | ol:deng-zhi |
| 璀璨星河 | 董白 | 已实现 | ol:dong-bai |
| 璀璨星河 | 诸葛果 | 已实现 | ol:zhuge-guo |
| 璀璨星河 | 鲁芝 | 已实现 | ol:lu-zhi-wei |
| 璀璨星河 | 沙摩柯 | 已实现 | classic:sha-mo-ke |
| 璀璨星河 | 吕旷吕翔 | 已实现 | ol:lv-kuang-lv-xiang |
| 璀璨星河 | 花鬘 | 已实现 | ol:hua-man |
| 璀璨星河 | 张陵 | 已实现 | ol:zhang-ling |
| 璀璨星河 | 周鲂 | 已实现 | ol:zhou-fang |
| 璀璨星河 | 曹性 | 已实现 | ol:cao-xing |
| 璀璨星河 | 黄祖 | 已实现 | ol:huang-zu |
| 璀璨星河 | 刘辩 | 已实现 | ol:liu-bian |
| 璀璨星河 | 杨仪 | 已实现 | ol:yang-yi |
| 璀璨星河 | 陈登 | 已实现 | ol:chen-deng |
| 璀璨星河 | 辛毗 | 已实现 | ol:xin-pi |
| 璀璨星河 | 杨婉 | 已实现 | ol:yang-wan |
| 璀璨星河 | 滕芳兰 | 已实现 | ol:teng-fang-lan |
| 璀璨星河 | 芮姬 | 已实现 | ol:rui-ji |
| 璀璨星河 | 郭槐 | 已实现 | ol:guo-huai |
| 璀璨星河 | 邓忠 | 已实现 | ol:deng-zhong |
| 璀璨星河 | 霍峻 | 已实现 | ol:huo-jun |
| 璀璨星河 | 张芝 | 待实现 |  |
| 璀璨星河 | 文鸯 | 已实现 | ol:wen-yang |
| 璀璨星河 | 夏侯玄 | 待实现 |  |
| 璀璨星河 | 马日磾 | 已实现 | ol:ma-mi-di |
| 璀璨星河 | 阎柔 | 已实现 | ol:yan-rou |
| 璀璨星河 | 朱儁 | 已实现 | ol:zhu-jun |
| 璀璨星河 | 卢氏 | 待实现 |  |
| 璀璨星河 | 张翼 | 已实现 | ol:zhang-yi |
| 璀璨星河 | 张华 | 已实现 | ol:zhang-hua |
| 璀璨星河 | 曹羲 | 已实现 | ol:cao-xi |
| 璀璨星河 | 孟达 | 已实现 | ol:meng-da |
| 璀璨星河 | 彭羕 | 待实现 |  |
| 璀璨星河 | 吕伯奢 | 待实现 |  |
| 璀璨星河 | 张燕 | 已实现 | ol:zhang-yan |
| 璀璨星河 | 陆凯 | 待实现 |  |
| 璀璨星河 | 向宠 | 已实现 | ol:xiang-chong |
| 璀璨星河 | 胡金定 | 已实现 | ol:hu-jin-ding |
| 璀璨星河 | 凌操 | 已实现 | ol:ling-cao |
| 璀璨星河 | 刘辟 | 已实现 | ol:liu-pi |
| 璀璨星河 | 郭图 | 已实现 | ol:guo-tu |
| 璀璨星河 | 孙茹 | 已实现 | ol:sun-ru |
| 璀璨星河 | 刘焉 | 已实现 | ol:liu-yan |
| 璀璨星河 | 王匡 | 已实现 | ol:wang-kuang |
| 璀璨星河 | 轲比能 | 已实现 | ol:ke-bi-neng |
| 璀璨星河 | 秦朗 | 已实现 | ol:qin-lang |
| 璀璨星河 | 陶谦 | 已实现 | ol:tao-qian |
| 璀璨星河 | 武安国 | 已实现 | ol:wu-an-guo |
| 璀璨星河 | 袁姬 | 已实现 | ol:yuan-ji |
| 璀璨星河 | 牛辅 | 已实现 | ol:niu-fu |
| 璀璨星河 | 董翓 | 待实现 |  |
| 璀璨星河 | 阮慧 | 已实现 | ol:ruan-hui |
| 璀璨星河 | 袁涣 | 已实现 | ol:yuan-huan |
| 璀璨星河 | 吴景 | 已实现 | ol:wu-jing |
| 璀璨星河 | 马元义 | 已实现 | ol:ma-yuan-yi |
| 璀璨星河 | 刘晔 | 待实现 |  |
| 璀璨星河 | 逄纪 | 待实现 |  |
| 璀璨星河 | 裴元绍 | 已实现 | ol:pei-yuan-shao |
| 璀璨星河 | 刘璋 | 已实现 | ol:liu-zhang |
| 璀璨星河 | 李丰 | 已实现 | ol:li-feng |
| 璀璨星河 | 张曼成 | 待实现 |  |
| 璀璨星河 | 管亥 | 已实现 | ol:guan-hai |
| 璀璨星河 | 孙鸢 | 已实现 | ol:sun-yuan |
| 璀璨星河 | 乔国老 | 待实现 |  |
| 璀璨星河 | 阮瑀 | 已实现 | ol:ruan-yu |
| 璀璨星河 | 鲍信 | 已实现 | ol:bao-xin |
| 璀璨星河 | 环淑君 | 已实现 | ol:huan-shu-jun |
| 璀璨星河 | 崔琰 | 已实现 | ol:cui-yan |
| 璀璨星河 | 唐咨 | 已实现 | ol:tang-zi |
| 璀璨星河 | 董子安 | 待实现 |  |
| 璀璨星河 | 段煨 | 已实现 | ol:duan-wei |
| 璀璨星河 | 谢灵毓 | 已实现 | ol:xie-ling-yu |
| 谋 | 谋姜维 | 待实现 |  |
| 谋 | 谋关羽 | 已实现 | mou:guan-yu-ol |
| 谋 | 谋邓艾 | 已实现 | mou:deng-ai-ol |
| 谋 | 谋太史慈 | 已实现 | mou:taishi-ci-ol |
| 谋 | 谋董卓 | 待实现 |  |
| 谋 | 谋孙坚 | 已实现 | mou:sun-jian-ol |
| 谋 | 谋黄月英 | 已实现 | mou:huang-yueying-ol |
| 谋 | 谋庞统 | 待实现 | mou:pang-tong-mobile |
| 谋 | 谋华雄 | 已实现 | mou:hua-xiong-ol |
| 谋 | 谋袁术 | 待实现 |  |
| 谋 | 谋公孙瓒 | 已实现 | mou:gongsun-zan-ol |
| 谋 | 谋田丰 | 待实现 |  |
| 谋 | 谋贾诩 | 待实现 |  |
| 谋 | 谋文丑 | 已实现 | mou:wen-chou-ol |
| 谋 | 谋张绣 | 已实现 | mou:zhang-xiu-ol |
| 谋 | 谋卢植 | 已实现 | mou:lu-zhi-ol |
| 谋 | 谋张让 | 待实现 |  |
| 谋 | 谋小乔 | 实现中 |  |
| 谋 | 谋诸葛亮 | 已实现 | mou:zhuge-liang |
| 谋 | 谋鲁肃 | 待实现 |  |
| 谋 | 谋许攸 | 待实现 |  |
| 谋 | 谋戏志才 | 待实现 |  |
| 谋 | 谋程昱 | 待实现 |  |
| 谋 | 谋董昭 | 已实现 | mou:dong-zhao-ol |
| 谋 | 谋祝融 | 待实现 |  |
| 谋 | 谋周瑜 | 已实现 | mou:zhou-yu-ol |
| 魔 | 魔司马懿 | 实现中 |  |
| 魔 | 魔孙权 | 已实现 | ol:mo-sun-quan |
| 魔 | 魔曹操 | 实现中 |  |
| 魔 | 魔吕布 | 已实现 | ol:mo-lu-bu |
| 魔 | 魔貂蝉 | 已实现 | ol:mo-diao-chan |
| 魔 | 魔张飞 | 已实现 | ol:mo-zhang-fei |

