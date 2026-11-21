# 配置

模组只有一份配置文件，8 个键。默认值、允许范围与容错规则都在这里。

## 文件位置

```
<游戏可执行文件目录>/mods/config/sts2-roaring-earring.cfg
```

路径由 `Godot.OS.GetExecutablePath()` 的所在目录加 `mods/config/` 拼出（`src/Config/RoaringConfigStore.cs:78-94`）。
目录不存在时，第一次保存会自动建。扩展名是 `.cfg`，**内容其实是 JSON**，缩进格式。

如果连可执行目录都解析不出来，模组只留一条 WARN（`no config path available, settings are kept in memory only`），
设置只存在于内存里，重启就丢。

## 何时读、何时写

- **启动时读一次**（`src/RoaringEarringMod.cs:26`）。运行期不会回看文件。
  所以**手改 cfg 需要重启游戏**才生效。
- 文件不存在 → 8 个键全部用默认值，日志：
  `[sts2-roaring-earring] no config file found, using defaults`
- 设置页每次改动都会**整份重写**这 8 个键（`src/Config/RoaringConfigStore.cs:96-112`），
  并立刻成为「当前值」；写入失败只留一条 WARN，内存里的值本次会话仍然生效。
- 写回的格式固定是缩进 JSON，键的顺序固定。文件里的**未知键会被忽略**，并在下一次保存后消失。

## 8 个键

| 键 | 类型 | 默认 | 允许范围 | 作用 |
| --- | --- | --- | --- | --- |
| `energy_per_turn` | 整数 | `1` | 0 .. 9 | 持有狂吼耳环时每回合额外能量（提高最大能量上限） |
| `max_cards_per_turn` | 整数 | `50` | 1 .. 100 | 狂吼耳环每回合自动出牌上限 |
| `play_delay_ms` | 整数 | `0` | 0 .. 600 | 两张自动打出的牌之间额外停顿多少毫秒，`0` = 不停顿 |
| `skip_play_animation` | 布尔 | `true` | `true` / `false` | 自动出牌时跳过飞牌补间与引擎的等待；只影响观感 |
| `auto_play_anim_speed` | 整数 | `1` | 1 .. 3 | 仅自动出牌期间把 Spine 时间缩放乘以该倍数；`1` = 不干预 |
| `play_order` | 字符串 | `left_to_right` | `left_to_right` / `right_to_left` / `random` | 自动出牌顺序 |
| `auto_end_turn` | 布尔 | `false` | `true` / `false` | 打满上限或没有可打出的牌时，自动结束回合 |
| `start_with_relic` | 布尔 | `true` | `true` / `false` | 新开局给每个玩家发一个狂吼耳环 |

常量定义在 `src/Config/RoaringConfig.cs:21-51`，钳制在 `Normalized()`（`:79-92`）。

需要注意的几点：

- `energy_per_turn = 0` 表示「不额外给能量，但仍然接管回合」。想只要自动出牌、不要能量时用它。
- `max_cards_per_turn` 是**每回合**的上限，不跨回合累计，每回合重新计数。
- `max_cards_per_turn` **同时**决定原版【低语耳环】的上限：实际值取 `max(13, 本值)`
  （`src/Patches/WhisperingEarringCapPatch.cs:43-44`）。这条补丁是全局的，
  即使本局没人持有狂吼耳环，只要模组装着且补丁生效，低语耳环也按这个上限跑。
  补丁失败时原版保持 13 张。
- `play_order` 只影响狂吼耳环，不影响低语耳环。
- `play_delay_ms` 只是**额外**停顿，加在每张牌打出之后：模组无法让牌打得更快，那由游戏自身的快进模式决定；
  但这一项在快进模式的「即时」下**照样生效**（用的是 `Cmd.Wait` 而不是 `Cmd.CustomScaledWait`），
  因为它是玩家自己设的节奏。战斗结束会立刻中断等待。
  它只影响观感：出哪张牌、能不能打出、结果如何都不变。
- `skip_play_animation` 直接作为 `skipCardPileVisuals` 传给出牌调用：跳过飞牌补间与 0.25–0.35 秒等待，
  只影响观感，不改变结算、不改变联机同步（两端各自按自己的值渲染）。
- `auto_play_anim_speed` 只在**自动出牌循环期间**把 Spine 时间缩放乘以该倍数，循环结束（含异常、取消）
  立刻恢复；手动出牌和其它动画不受影响。`1` 表示完全不干预。
- `start_with_relic` 只在**新开局**发放（楼层为 0）；读档开局不补发。
- `auto_end_turn` 的结束调用是 `canBackOut: false`，触发后不可反悔；
  战斗结束/正在结束、回合已切换、玩家已准备结束回合这几种情况**不会**触发它。

## 示例

默认值：

```json
{
  "energy_per_turn": 1,
  "max_cards_per_turn": 50,
  "play_delay_ms": 0,
  "play_order": "left_to_right",
  "auto_end_turn": false,
  "start_with_relic": true,
  "skip_play_animation": true,
  "auto_play_anim_speed": 1
}
```

几个常见组合：

```json
{
  "energy_per_turn": 3,
  "max_cards_per_turn": 100,
  "play_delay_ms": 300,
  "play_order": "random",
  "auto_end_turn": true,
  "start_with_relic": false,
  "skip_play_animation": true,
  "auto_play_anim_speed": 2
}
```

（更强、全随机、自动收尾，每张牌之间停 0.3 秒看得清，只在瓦库事件里拿。）

## 容错规则

改动一个坏值不会让游戏报错，规则如下：

- **整份回落**：文件不是 JSON 对象、或 JSON 语法错误 → 8 个键全部用默认值，不保留任何部分值。
- **逐键回落**：单个键缺失或类型不对 → 该项用默认值，其它键照常生效。
- **键名大小写不敏感**（`Energy_Per_Turn` 也能读到）。
- 允许行尾逗号与 JSON 注释。
- 整数接受数字，也接受数字字符串（`"3"`）；布尔接受 `true/false`，也接受字符串 `"true"`。
- 整数越界会被**钳到范围内**（`energy_per_turn` 0..9，`max_cards_per_turn` 1..100，`play_delay_ms` 0..600，
  `auto_play_anim_speed` 1..3）。
  本地文件的钳制是静默的；只有联机时收到房主的越界值，才会留一条 WARN：
  `[sts2-roaring-earring] host config from player <id> was out of range and has been clamped`
- `play_order` 无法识别时回落 `left_to_right`。除了三个标准写法，也接受
  `ltr` / `lefttoright` / `rtl` / `righttoleft`，大小写、连字符、空格都会先被归一化
  （`src/Config/RoaringConfig.cs:128` 起）。

## 生效时机（开局快照）

这 8 个值在**开局时冻结**成一份快照，本局全程用这份快照：

- 单机 / 房主：`RunStarted` 时取当前配置冻结。
- 客户端：等房主的值到了、采纳之后再冻结。
- 本局中途改设置 → 写回文件、改变「当前值」，但**不影响本局**；只影响之后创建的对局（读档开局同理）。
- 上一局的快照在下一局开始前释放。

日志里可以对照这两行：

```
[sts2-roaring-earring] run settings frozen: energy=1 cards=50 delay=0 order=left_to_right end=0 start=1 skip=1 anim=1 h=xxxxxxxx
[sts2-roaring-earring] run settings released; a later run takes the settings as they are then
```

`h=` 是这 8 个值的 FNV-1a 指纹（`src/Config/RoaringRuntimeConfig.cs:119-131`），
两端指纹相同就说明这一局用的是同一组参数。

## 联机

- **以房主为准**：房主广播自己这一局的值；客户端采纳并冻结，页面与行为都用房主的值。
- **客户端不写本机文件**：客户端上的写入会被拒绝，只留一条 WARN
  （`settings write refused: a multiplayer client keeps the host's values and leaves its own JSON untouched`），
  本机 cfg 保持原样。
- 客户端开局没收到时会换层重试，每层最多一次、每局封顶 5 次；用满后本局沿用本机值。
- 客户端回到单机（或自己当房主）时，本机 cfg 重新成为权威。

## 日志对照

| 日志行 | 含义 |
| --- | --- |
| `config loaded from <path>` | 启动时读到了文件 |
| `no config file found, using defaults` | 没有文件，8 个键全默认 |
| `could not read config at <path>: <msg>` | 文件读失败，整份回落默认 |
| `config saved to <path>` | 设置页写盘成功 |
| `could not write config at <path>: <msg>` | 写盘失败，本次会话仍用内存值 |
| `run settings frozen: energy=... h=...` | 这一局冻结的参数 |
| `host config from player <id> was out of range and has been clamped` | 房主发来的值越界，已钳制 |
| `settings write refused: ...` | 联机客户端试图写本机文件，被拒 |
