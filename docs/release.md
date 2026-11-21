# 发布说明（0.2.1-beta）

## 版本与产物

| 项 | 值 |
| --- | --- |
| 模组 id | `sts2-roaring-earring` |
| 显示名 | 自动出牌 / 狂吼耳环 |
| 版本 | `0.2.1-beta` |
| 作者 | kewu |
| 最低游戏版本 | `0.113.1` |
| 发布形态 | 仅 DLL（`has_pck: false`、`has_dll: true`） |
| 依赖 | 无（`dependencies` 为空；RitsuLib 只是可选的设置页） |
| `affects_gameplay` | `true` |

以上字段以 `mod/sts2-roaring-earring.json` 为准。

## 本版包含什么

- 先古遗物「狂吼耳环」（`ROARING_EARRING`）：每回合额外能量，并由瓦库接管每个回合自动出牌。
- 瓦库事件初始池的第 4 个候选（原版 3 个 + 本模组 1 个，约 1/4）。
- 8 项参数（本版新增 `skip_play_animation`：跳过飞牌动画与 0.25–0.35 秒等待；`auto_play_anim_speed`：
  只在自动出牌期间把 Spine 动画加速 1–3 倍。上一版新增 `play_delay_ms`）、cfg 读写、开局快照（局内改设置不影响本局）。
- 联机参数同步：房主广播、客户端采纳并冻结、客户端不写本机文件。
- 遗物图标/大图内嵌在 dll 里；解码失败只留一条日志并保持原版图标，不会让模组加载失败。
  三个 getter 改为 prefix 覆盖，拿到图就跳过原版 `res://` 查找，因此 `Missing sprite 'roaring_earring'` 警告不再出现。
- 可选的 RitsuLib 设置页（不装也能玩）。

## 构建

```powershell
dotnet build -c Release
```

- 需要 `GameDataDir` 指向游戏程序集目录，默认值是本机路径，换机器时用
  `-p:GameDataDir="...\data_sts2_windows_x86_64"` 覆盖。
- 构建会把 `sts2-roaring-earring.dll` 与 `sts2-roaring-earring.json` 落到
  `dist/sts2-roaring-earring/`（`sts2-roaring-earring.csproj:82-86`）。
- RitsuLib 设置页默认参与编译；`-p:EnableRitsuLib=false`（或本机找不到 RitsuLib 的
  `RitsuLib.References.props`）会排除 `src/Integration/**`，产出「只认 JSON」的版本
  （`sts2-roaring-earring.csproj:15-43`）。

发布 zip 的布局必须是这样一层：

```
sts2-roaring-earring/
  sts2-roaring-earring.dll
  sts2-roaring-earring.json
```

不要把 `*.pdb`、`*.zip`、`obj/`、`bin/` 打进包。压缩包条目名用 `/`，不要用反斜杠。

## 验证状态

### 已经做过的

- **源码核对**：本仓库文档里出现的每个默认值、范围、日志行、路径，都能在源码里找到对应位置
  （见文末「事实来源」）。
- **构建**：本轮改动之后跑过 2 次 `dotnet build -c Release`（均为 0 警告 0 错误，dll 70144 B）。
  没有启动游戏（0 次）。

### 未实机验证（请如实对待）

下面八项都只有源码依据，没有实机记录：

1. **真实联机收发与消息 id** —— 两条消息（请求/配置）在真实双端之间是否按预期送达，
   `message type ... id=<n>` 两端是否一致。
2. **局内改设置不影响本局的实际表现** —— 快照逻辑已实现，但没有在真实对局里观察过改设置前后的数值。
3. **cfg 首次写入** —— `mods/config/` 目录自动创建、首次写盘的权限与结果。
4. **RitsuLib 设置页真实观感** —— 页面能否正常显示、滑条范围与文案是否正确。
5. **客户端采纳后再冻结的时序** —— 客户端收到房主配置、采纳、冻结、补发遗物这一串动作的先后。
6. **两端版本不一致时的线格式兼容** —— 消息没有版本号，只能追加字段；旧版/新版互连未验证。
7. **动画加速与跳过出牌动画的真实观感** —— 两个新参数只做过源码与构建核对，没有在真实对局里比较过加速前后，
   以及跳过后是否留下视觉残留。
8. **设置页两个新控件的显示与写盘** —— 1–3 倍滑条与「跳过出牌动画」开关的文案、默认值没有在游戏内看过。

## 兼容性与线格式

- 两条消息：`RoaringConfigRequestMessage`（客户端 → 房主，无字段）、
  `RoaringConfigMessage`（房主 → 客户端，8 个字段按固定顺序序列化：
  energy、cards、order、auto_end、start、delay、skip、anim）。
- **消息里没有版本号**。字段顺序就是线格式，只能追加，不能重排或删除
  （`src/Net/RoaringConfigMessage.cs:91-113`）。
- 消息 id 由类型名决定；类型名一旦发布就不能改，否则等于换了 id
  （`src/Net/RoaringConfigMessage.cs:8-12`）。
- 任一端升级后字段有增删、另一端没升级 —— 这是上面第 6 条未验证项。
- 目标游戏版本 `0.113.1`。游戏更新后，低语耳环上限补丁的目标是编译器生成的状态机，
  最容易失效；失效时它只留 WARN 并保持原版 13 张，不会崩
  （`src/Patches/WhisperingEarringCapPatch.cs:46-81`）。

## 已知边界

- 遗物**不进任何遗物池** ⇒ 不进遗物图鉴；只能靠 `start_with_relic` 或瓦库事件获得。
- `start_with_relic` 开启时每个玩家开局各拿一个；同一玩家不会拿第二个；读档不补发。
- 自动结束回合触发后不可反悔（`canBackOut: false`）。
- `max_cards_per_turn` 对原版低语耳环的改动是**全局副作用**：只要补丁生效，
  本局任何持有低语耳环的人都按 `max(13, 本值)` 跑。
- 客户端的配置自愈每层最多一次、每局封顶 5 次；用满后本局沿用本机值。

## 回滚

- 删除 `<游戏目录>/mods/sts2-roaring-earring/`，或退订创意工坊条目。
- 配置文件 `<游戏目录>/mods/config/sts2-roaring-earring.cfg` 可以留着：
  不装模组时没有代码读它。想彻底清干净就一并删除。
- 模组没有写入存档结构（`[SavedProperty]` 一类），回滚不会污染已有存档。
  已经拿到遗物的一局在回滚后遗物会消失。

## 发布前检查表

- [ ] `mod/sts2-roaring-earring.json` 的 `version`、`workshop/changelog.txt` 顶部、
      `workshop/description.bbcode` 第二行，三处版本号一致。
- [ ] `dependencies` 仍是 `[]`，`affects_gameplay` 仍是 `true`。
- [ ] 发布内容只有 dll 与 json，没有 pdb/zip/obj/bin。
- [ ] `workshop/title.txt` ≤ 128 字符、`workshop/description.bbcode` ≤ 8000 字符，
      正文没有 `[color=...]`。
- [ ] 工坊正文与 README 里的「未实机验证」段落没有被删掉或改写成「已测试」。
- [ ] 本机只保留一处同 id 的模组（本地版或工坊版），避免自测测错版本。

## 事实来源

| 事实 | 位置 |
| --- | --- |
| 遗物稀有度、能量、接管、出牌顺序、出牌间隔、目标、自动结束 | `src/Models/RoaringEarring.cs:26`、`:62-68`、`:77-155`、`:159-179`、`:182-193`、`:155` |
| 每人最多一个 | `src/Models/RoaringEarring.cs:38-46` |
| 自动出牌动画作用域 / 两个 Spine prefix | `src/Models/RoaringEarring.cs:103-108`、`src/Patches/AutoPlayAnimScope.cs:12-41` |
| 瓦库池第 4 个候选 / 持有后隐藏低语耳环 | `src/Patches/VakuuPool1Patch.cs:29`、`:69-83` |
| 低语耳环上限 `max(13, 配置)` | `src/Patches/WhisperingEarringCapPatch.cs:43-44` |
| 8 个键、默认值、范围 | `src/Config/RoaringConfig.cs:21-51`、`:79-92` |
| 配置文件路径与读写 | `src/Config/RoaringConfigStore.cs:78-94`、`:96-114` |
| 开局快照 | `src/Config/RoaringRuntimeConfig.cs:27`、`:33-51` |
| 房主广播 / 客户端采纳 / 补请上限 | `src/Net/RoaringConfigSync.cs:18`、`:78-122`、`:130-171` |
| 开局发遗物（楼层门限、每人一个） | `src/RoaringEarringMod.cs:146-169` |
| RitsuLib 可选 | `sts2-roaring-earring.csproj:15-43`、`src/Integration/RitsuLibSettingsPage.cs:26-60` |
