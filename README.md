# 自动出牌 / 狂吼耳环

《杀戮尖塔 2》模组：给瓦库（Vakuu）加一个先古遗物「狂吼耳环」——
每回合额外能量，并把你的每一个回合交给瓦库：它按你设定的顺序，替你把手牌打出去。

- 目标游戏版本：v0.113.1（Steam 默认分支，Godot 4）
- 运行时：游戏自带 mod 加载器（**不需要** BepInEx，**不需要**任何前置）
- 当前版本：`0.1.0-beta`，作者 kewu
- 状态：源码完成、构建通过；**尚未实机验证**的部分集中列在 [`docs/release.md`](docs/release.md)，
  请不要把本文当成实机验收报告。

## 玩法

### 怎么拿到

两条路，可以同时开：

1. **开局自带**（`start_with_relic`，默认开）。新开局时每个玩家各拿一个。
   读档开局不补发：这一局里如果已经丢了，就不会再给。
2. **瓦库事件**。它被加进瓦库的初始池（`Vakuu.Pool1`），成为第 4 个候选
   （原版是 3 个），因此约 1/4 概率被抽到。

它**不进任何遗物池**，所以不会出现在遗物图鉴里——只能靠上面两条路获得。

持有它时，瓦库事件的选项里不会再出现它自己，也不会再出现原版【低语耳环】；
不持有时，两者都能正常出现在选项里。

### 持有之后

| 效果 | 说明 |
| --- | --- |
| 每回合额外能量 | 默认 +1（`energy_per_turn`，0..9）。实现方式是提高最大能量上限，和原版【低语耳环】同构 |
| 回合接管 | 每个回合（不只是第一回合）进入自动出牌阶段时，瓦库按设定顺序打出可打出的牌 |
| 出牌上限 | 每回合最多 50 张（`max_cards_per_turn`，1..100） |
| 出牌顺序 | 从左到右（默认）/ 从右到左 / 随机；随机取自联机同步的战斗卡牌选择 RNG |
| 目标选择 | 与原版瓦库的自动出牌一致：可命中敌人列表的第一个、随机一名活着的队友、或自己 |
| 自动结束回合 | 默认关。开启后只在「打满上限」或「没有可打出的牌」时结束，且不可反悔 |
| 出牌间隔 | 默认 0（不停顿）。可加 0..600 毫秒，让连打看得清；只影响观感，不改变出牌结果 |

自动出牌的停止条件：战斗结束/正在结束、回合已经切换、玩家已准备结束回合、打满上限、
没有可打出的牌。只有最后两种会触发「自动结束回合」。

### 和原版【低语耳环】的关系

- 两个可以**同时持有**并各自生效，不做去重屏蔽，两套都会跑；先后由原版钩子/遗物加载顺序决定。
- 原版低语耳环的 13 张上限由**同一条配置**接管：实际取 `max(13, max_cards_per_turn)`，
  所以只会变强不会变弱。这条补丁失败时原版保持 13 张，日志会说明补丁没有生效。
- 「出牌顺序」只影响狂吼耳环，不影响低语耳环。
- 同一个玩家不会拿到第二个狂吼耳环（开局发放时跳过已持有的玩家）。

## 设置

6 项，装了 RitsuLib 可以在游戏内设置页改，否则改配置文件：

| 键 | 默认 | 范围 | 作用 |
| --- | --- | --- | --- |
| `energy_per_turn` | `1` | 0..9 | 每回合额外能量 |
| `max_cards_per_turn` | `50` | 1..100 | 每回合自动出牌上限；同时决定低语耳环的上限 `max(13, 本值)` |
| `play_delay_ms` | `0` | 0..600 | 两张自动打出的牌之间额外停顿的毫秒数；`0` = 不停顿 |
| `play_order` | `left_to_right` | `left_to_right` / `right_to_left` / `random` | 自动出牌顺序 |
| `auto_end_turn` | `false` | true / false | 打满或没牌可出时自动结束回合（不可反悔） |
| `start_with_relic` | `true` | true / false | 新开局给每个玩家发一个 |

配置文件：`<游戏可执行文件目录>/mods/config/sts2-roaring-earring.cfg`，内容是 JSON。
不存在时用默认值；非法值会被钳到范围内。完整规则、容错和示例见
[`docs/config.md`](docs/config.md)。

**生效时机**：设置页改动会立刻写回文件，但**从下一局开始生效**——开局时这 6 个值会冻结成快照，
本局中途再改不影响本局。手改 cfg 还需要重启游戏：模组只在启动时读一次文件。

RitsuLib 只是**可选**的设置界面增强：不装也能玩，配置照样走 cfg 文件。manifest 的
`dependencies` 是空的，它不是硬依赖。

## 安装

**本地安装**

1. 拿到发布 zip（或自己构建，见下）。
2. 解压到 `<游戏目录>/mods/`。解压后应该是这一层：

   ```
   <游戏目录>/mods/sts2-roaring-earring/
     sts2-roaring-earring.dll
     sts2-roaring-earring.json
   ```

   zip 里已经带好 `sts2-roaring-earring/` 这一层，不要再套一层。
3. 启动游戏。日志 `%APPDATA%\SlayTheSpire2\logs\godot.log` 里应出现
   `[sts2-roaring-earring] initialized; ...`。

**创意工坊安装**：订阅 [自动出牌 / 狂吼耳环 · Auto-Play: Roaring Earring](https://steamcommunity.com/sharedfiles/filedetails/?id=3842516435)（item `3842516435`）即可。发布流程见 [`docs/workshop.md`](docs/workshop.md)。

同一 id 的本地版与工坊版同时存在时，游戏只会启用其中一个。建议只留一处，
否则你测的可能不是你以为的那一份。

## 联机

- **参数以房主为准**：房主在开局时广播自己这一局的 6 个值，客户端采纳后冻结。
- **客户端不写本机 cfg**：客户端上的写入会被拒绝，本机文件保持原样，设置页显示的是房主的值。
- **补请**：客户端开局没收到时会换层重试，每层最多一次、每局封顶 5 次；
  用满后本局沿用本机值，并留一条 WARN。
- **遗物与它的能量属于每个玩家自己**：每人一个，各自结算。
- 两端装同一份模组版本；消息线格式没有版本号，只能追加字段。

双端实测步骤、预期日志与失败判定见 [`docs/mp-test.md`](docs/mp-test.md)。

## 已知限制

- 无池遗物 ⇒ 不进遗物图鉴。
- `start_with_relic` 开启时每个玩家开局各拿一个；读档不补发。
- 自动结束回合一旦触发不可反悔。
- 0.1.0-beta 的未实机验证项见 [`docs/release.md`](docs/release.md)。

## 构建

```powershell
dotnet build -c Release
```

构建把 `sts2-roaring-earring.dll` 与 `sts2-roaring-earring.json` 落到 `dist/sts2-roaring-earring/`。
`GameDataDir` 默认指向本机游戏安装（`...\Slay the Spire 2\data_sts2_windows_x86_64`），
换机器时在命令行覆盖：

```powershell
dotnet build -c Release -p:GameDataDir="D:\path\to\data_sts2_windows_x86_64"
```

RitsuLib 设置页默认参与编译（要求本机有 RitsuLib）；不想引用它时：

```powershell
dotnet build -c Release -p:EnableRitsuLib=false
```

装进游戏并检查日志：

```powershell
.\install.ps1          # 复制 dist/sts2-roaring-earring 到游戏 mods 目录（会先确认）
.\install.ps1 -Yes     # 不询问
.\tools\check-log.ps1  # 只筛出 [sts2-roaring-earring] 开头的日志行
```

## 文档

| 文件 | 内容 |
| --- | --- |
| [`docs/config.md`](docs/config.md) | 6 个键、默认值/范围、容错规则、生效时机 |
| [`docs/release.md`](docs/release.md) | 发布说明、验证状态与未实机项、线格式、打包与回滚 |
| [`docs/mp-test.md`](docs/mp-test.md) | 双端联机实测清单（可执行） |
| [`docs/workshop.md`](docs/workshop.md) | 创意工坊发布手册 |
