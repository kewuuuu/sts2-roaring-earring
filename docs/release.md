# 发布说明（0.3.0-beta）

## 版本与产物

| 项 | 值 |
| --- | --- |
| 模组 id | `sts2-roaring-earring` |
| 显示名 | 自动出牌 / 狂吼耳环 |
| 版本 | `0.3.0-beta` |
| 作者 | kewu |
| 最低游戏版本 | `0.113.1` |
| 发布形态 | 仅 DLL（`has_pck: false`、`has_dll: true`） |
| 依赖 | 无（`dependencies` 为空；RitsuLib 只是可选的设置页） |
| `affects_gameplay` | `true` |

以上字段以 `mod/sts2-roaring-earring.json` 为准。

## 本版包含什么

- 先古遗物「狂吼耳环」（`ROARING_EARRING`）：每回合额外能量，并由瓦库接管每个回合自动出牌。
- 瓦库事件初始池的第 4 个候选（原版 3 个 + 本模组 1 个，约 1/4）。
- 7 项参数（`play_delay_ms` 自 0.2.0-beta 起、`show_toggle` 自 0.3.0-beta 起）、cfg 读写、开局快照（局内改设置不影响本局）。
- 左键点击遗物切换自动出牌（0.3.0-beta）：开关随存档保存，联机由房主权威广播。
- 联机参数同步：房主广播、客户端采纳并冻结、客户端不写本机文件。
- 遗物图标/大图内嵌在 dll 里；解码失败只留一条日志并保持原版图标，不会让模组加载失败。
  三个 getter 改为 prefix 覆盖，拿到图就跳过原版 `res://` 查找，因此 `Missing sprite 'roaring_earring'` 警告不再出现。
- 可选的 RitsuLib 设置页（不装也能玩）。

## 0.3.0-beta 变更（相对 0.2.3-beta）

- **左键点击遗物切换自动出牌**：`src/Patches/RelicToggleClickPatch.cs` 是 `NClickableControl.OnRelease` 的 postfix。
  输入链是 `NClickableControl.cs:504-522` → `:349-364` → `:390-410`（左键 + 可见 + 已聚焦）→ `:441-449` → `:495-497`；
  补丁只认 `NRelicInventoryHolder`（`NRelicInventoryHolder.cs:28`、`:162`），并用 `LocalContext.IsMine`
  排除别人的遗物（`LocalContext.cs:148-155`）。悬停与右键到不了 `OnRelease`。原版的查看遗物界面照旧打开。
- **开关进存档**：`src/Models/RoaringEarring.cs` 新增 `[SavedProperty] public bool AutoPlayEnabled`，默认 `true`；
  老存档缺这个字段 ⇒ 读到 C# 初始值（开，`RelicModel.cs:599-608` 只填存在的属性）。新增存档属性会改变
  模型属性哈希（`ModelIdSerializationCache.cs:157-181`），所以版本号必须升。
- **下一回合生效**：能量读的是回合起点快照。每回合第一次读最大能量发生在 `CombatManager.cs:916-923`
  的 `ResetEnergy()`/`AddMaxEnergyToCurrent()`（`PlayerCombatState.cs:110`、`:182-184`），早于任何模型
  回合开始钩子（`CombatManager.cs:950`），所以快照只能懒取在 `ModifyMaxEnergy` 里，
  用 `(PlayerCombatState 引用, TurnNumber)` 作键（每场战斗新建一份，`Player.cs:807-810`）。
- **联机同步**：新消息 `RoaringToggleMessage`（所有者 NetId + 一个 bool）。客户端只发给房主
  （`NetClientGameService.cs:83-95`），房主校验 `OwnerNetId == senderId`、应用到局面再广播
  （`NetHostGameService.cs:114-130`）；`ShouldBroadcast = false`，避免房主还没校验就被自动转发
  （`NetHostGameService.cs:159-162`）。只同步这一个 bool。
- **配置键 `show_toggle`**（默认 `true`）：`false` 时点击无效、遗物始终按「开」工作。写盘顺序追加在
  `start_with_relic` 之后（`src/Config/RoaringConfigStore.cs`），指纹与 `SameValues` 一并纳入。
- 已知未验证：真实双端的点击切换时序（客户端请求与房主广播的先后）、读档后新字段的缺省、
  点击与悬停/查看界面的交互观感。

## 0.2.3-beta 变更（相对 0.2.2-beta）

- 遗物登记进 `EventRelicPool`：`src/RoaringEarringMod.cs` 的 `Init` 里 `ModHelper.AddModelToPool<EventRelicPool, RoaringEarring>()`。
  该池没有消费者（`RelicGrabBag.cs:70-77` 只读 `SharedRelicPool` + 角色池），所以随机奖励不受影响，
  但 `RelicModel.Pool`（`RelicModel.cs:176`）不再抛 `InvalidOperationException: Sequence contains no matching element`，
  `ModelDb.AllRelics`（`ModelDb.cs:264`）随含它 ⇒ 图鉴（`NRelicCollectionCategory.cs:157`）与控制台 `relic`（`RelicConsoleCmd.cs:68`）都能找到。
- 图鉴的「远古 → 瓦库」分类还要求它出现在 `Vakuu.AllPossibleOptions` 里（`NRelicCollectionCategory.cs:197`），
  所以 `src/Patches/VakuuPool1Patch.cs` 在**非局内**的规范实例上追加一个仅用于展示的选项（`onChosen` 为 null ⇒ 锁定，选不了）；
  局内路径不变，仍是 3 个原版 + 本模组 1 个（约 1/4）。
- 已知未验证：图鉴/悬停两条链路只有源码核对与无头启动（`initialized; … relic_pool=EventRelicPool (registry only)`），
  真正打开图鉴看观感仍需实机。

> 历史：0.2.1-beta 的「跳过出牌动画 / 动画倍速」因手牌显示缺陷已在 0.2.2-beta 整体回退。

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
- **构建**：0.3.0-beta 的改动之后跑过 2 次 `dotnet build -c Release`（均为 0 警告 0 错误，
  dll 73728 B）。没有启动游戏（0 次）。

### 未实机验证（请如实对待）

下面六项都只有源码依据，没有实机记录：

1. **真实联机收发与消息 id** —— 三条消息（请求/配置/开关）在真实双端之间是否按预期送达，
   `message type ... id=<n>` 两端是否一致。
2. **局内改设置不影响本局的实际表现** —— 快照逻辑已实现，但没有在真实对局里观察过改设置前后的数值。
3. **cfg 首次写入** —— `mods/config/` 目录自动创建、首次写盘的权限与结果。
4. **RitsuLib 设置页真实观感** —— 页面能否正常显示、滑条范围与文案是否正确。
5. **客户端采纳后再冻结的时序** —— 客户端收到房主配置、采纳、冻结、补发遗物这一串动作的先后。
6. **两端版本不一致时的线格式兼容** —— 消息没有版本号，只能追加字段；旧版/新版互连未验证。
7. **点击切换本身** —— 点击入口、`IsMine` 过滤、下一回合生效的时点都只有源码依据；本机没有启动过游戏。
8. **联机下的点击切换** —— 客户端请求与房主广播的先后、房主自己点击时的本地应用，都没有双端实测。

## 兼容性与线格式

- 三条消息：`RoaringConfigRequestMessage`（客户端 → 房主，无字段）、
  `RoaringConfigMessage`（房主 → 客户端，6 个字段按固定顺序序列化：
  energy、cards、order、auto_end、start、delay）、
  `RoaringToggleMessage`（0.3.0-beta 新增；客户端 → 房主是请求，房主 → 客户端是应用后的值；
  两个字段：owner NetId、bool）。
- **消息里没有版本号**。字段顺序就是线格式，只能追加，不能重排；0.2.2-beta 删除了末尾的
  `skip`、`anim` 两个字段（`src/Net/RoaringConfigMessage.cs:83-103`）。0.3.0-beta 没有动
  `RoaringConfigMessage` 的任何字段，开关走独立消息类型；`show_toggle` 只是 cfg 的键，不进线格式。
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
- 0.3.0-beta 起遗物会写入一个 `[SavedProperty]`（`AutoPlayEnabled`，一个 bool）。回滚后没有人读它，
  不影响其它存档内容；已经拿到遗物的一局在回滚后遗物会消失。

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
| 遗物稀有度、能量、接管、出牌顺序、出牌间隔、目标、自动结束 | `src/Models/RoaringEarring.cs:27`、`:61-67`、`:76-144`、`:147-168`、`:170-181`、`:143` |
| 每人最多一个 | `src/Models/RoaringEarring.cs:37-45` |
| 瓦库池第 4 个候选 / 持有后隐藏低语耳环 | `src/Patches/VakuuPool1Patch.cs:29`、`:69-83` |
| 低语耳环上限 `max(13, 配置)` | `src/Patches/WhisperingEarringCapPatch.cs:43-44` |
| 7 个键、默认值、范围 | `src/Config/RoaringConfig.cs:21-45`、`:74-86` |
| 配置文件路径与读写 | `src/Config/RoaringConfigStore.cs:78-94`、`:96-113` |
| 开局快照 | `src/Config/RoaringRuntimeConfig.cs:27`、`:33-51` |
| 房主广播 / 客户端采纳 / 补请上限 | `src/Net/RoaringConfigSync.cs:18`、`:78-122`、`:130-171` |
| 开局发遗物（楼层门限、每人一个） | `src/RoaringEarringMod.cs:147-170` |
| RitsuLib 可选 | `sts2-roaring-earring.csproj:15-43`、`src/Integration/RitsuLibSettingsPage.cs:26-60` |
| 点击入口 / 本地玩家过滤 | `src/Patches/RelicToggleClickPatch.cs`；`NClickableControl.cs:390-410`、`:441-449`、`:495-497`；`NRelicInventoryHolder.cs:162`；`LocalContext.cs:148-155` |
| 开关存档字段与回合快照 | `src/Models/RoaringEarring.cs`；`RelicModel.cs:588-608`；`CombatManager.cs:916-923`；`PlayerCombatState.cs:110`、`:182-184` |
| 开关联机消息 | `src/Net/RoaringToggleMessage.cs`；`NetClientGameService.cs:83-95`；`NetHostGameService.cs:114-130`、`:159-162` |
| `show_toggle` 键 | `src/Config/RoaringConfig.cs`、`src/Config/RoaringConfigStore.cs`、`src/Config/RoaringRuntimeConfig.cs` |
