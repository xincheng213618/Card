# 网页游戏皮肤名称、品质与动态资源

2026-09-26，配合用户 4399 三国杀游戏截图做只读资源核对。当前已发布的本机包实现静态原图、皮肤原名、选中详情和皮肤切换；尚未集成真实动态播放或通用品质分级。

## 实际样本

- 金盔界关羽的资源编号为 `30001`。目标 Chrome 缓存同时出现其静态与动态资源键；静态原图与用户图片目视对应。
- 静态原图：`https://web.sanguosha.com/220/h5_2/res/runtime/pc/general/big/static/30001.png`。
- 动态根目录：`https://web.sanguosha.com/220/h5_2/res/runtime/pc/general/big/dynamic/30001/`。
- 动态文件为 `xingxiang.json`、`xingxiang.atlas`、`xingxiang.png`、`xingxiang_2.png`、`xingxiang_3.png`，以及 `beijing.json`、`beijing.atlas`、`beijing.png`，合计 9,931,004 字节。
- 两套骨骼 JSON 都声明 Spine `4.0.56`，动画名为 `play`。这是人物与背景骨骼动画及纹理，不是 GIF 或视频，也不能通过晃动静态 PNG 还原。
- 本机样本和逐文件 URL / SHA-256：`.artifacts/portrait-refresh/dynamic-sample/source-index.json`。样本未加入程序包。

[Spine 官方说明](https://us.esotericsoftware.com/spine-runtimes)说明 JSON / atlas 需要对应运行时加载，通用 C# 运行时不负责渲染。后续应先验证匹配版本的动画渲染集成、人物与背景合成、裁剪、性能和关闭时释放，再添加真实动静切换。保留低性能设备的静态回退，不把静态包标为动态已支持。

## 名称与品质

皮肤名字不同于武将名字或品质。例如[官方鲁肃页](https://x.sanguosha.com/hero/50.html)列出「独断远略*鲁肃」。当前素材目录保留下载页给出的原始皮肤名。

[官方活动](https://www.sanguosha.com/news/20260203_7076_2713)列有「动态水晶-单刀赴会*界关羽」。同款名称跨产品、年份、版本的品质标注不一定一致，不能直接把历史公告品质批量套到用户当前游戏。用户截图中的品质仅作为该截图的显示证据。

公开游戏配置 `Config_w.sgs` 是 ZIP，其 `cha_gs_dbs_fs_skininfo.sgs` 成员为不可直接读取的加密二进制；本次未解密，也未从中验证全量品质或静动对应表。未确认的信息不填充为稀有 / 史诗 / 传说。
