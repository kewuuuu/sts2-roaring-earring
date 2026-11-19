# 双端联机实测清单

目标：确认这 5 个参数在双端一致、以房主为准、客户端不写盘、补请有上限、遗物各归各。
预计 30–45 分钟，需要两台机器（或两份游戏目录 + 两个 Steam 账号）。

每条用例都写清楚「期望日志」和「失败判定」。日志只筛 `[sts2-roaring-earring]` 开头的行。

## 0. 前置

两端各自确认：

- [ ] 游戏版本 `0.113.1`（主菜单右下角或启动日志）。
- [ ] 同一份 `sts2-roaring-earring.dll`（比较文件大小或 SHA-256；两端必须同版本）。
- [ ] `<游戏目录>/mods/sts2-roaring-earring/` 下有 dll 与 json（或订阅同一工坊条目）。
- [ ] 本机只保留一处同 id 的模组：本地版与工坊版同时存在时游戏只启用一个，
      自测前先删掉多余的那份，避免测错版本。
- [ ] 记录两端 cfg 的路径与内容（客户端第 4 节要证明它没被改写）：
      `<游戏目录>/mods/config/sts2-roaring-earring.cfg`。
- [ ] RitsuLib 装或不装都可以：它只加设置页，不参与参数同步。两端可以不装在同一状态。

日志位置：`%APPDATA%\SlayTheSpire2\logs\godot.log`。开两个 PowerShell 窗口常驻：

```powershell
Get-Content "$env:APPDATA\SlayTheSpire2\logs\godot.log" -Wait -Tail 50 |
  Select-String -Pattern '\[sts2-roaring-earring\]'
```

只看同步相关：

```powershell
Select-String -Path "$env:APPDATA\SlayTheSpire2\logs\godot.log" -Pattern '\[sts2-roaring-earring\].*(config|frozen|adopt|request|refused|message type)'
```

## 1. 单机基线（每端各做一次）

先证明模组本身在两端都能加载，再谈同步。

1. 启动游戏，新开一局。
2. 期望出现的行（顺序大致如下）：

   ```
   [sts2-roaring-earring] initialized; energy_per_turn=1, max_cards_per_turn=50, play_order=left_to_right, auto_end_turn=False, start_with_relic=True
   [sts2-roaring-earring] no config file found, using defaults        # 第一次启动、没有 cfg 时
   [sts2-roaring-earring] whispering earring: transpiler attached to <...>.MoveNext
   [sts2-roaring-earring] run settings frozen: energy=1 cards=50 order=left_to_right end=0 start=1 h=xxxxxxxx
   ```

3. 遗物栏里应有一个「狂吼耳环」；每回合开始自动出牌。
4. 失败判定：
   - 没有 `initialized` 行 → 模组没加载（看路径、manifest id、游戏版本）。
   - 有 `harmony patching failed` → 补丁失败，先停下，别继续联机测试。
   - 有 `no nested type of WhisperingEarring ... vanilla 13-card behaviour is untouched`
     → 低语耳环补丁没挂上（游戏版本可能变了），本用例记为「已知不适用」，其它用例照做。

## 2. 建立联机：两端参数一致

1. 房主建局，客户端加入，一起进入第一场战斗。
2. 房主期望：

   ```
   [sts2-roaring-earring] config sync handlers registered; this peer is the Host
   [sts2-roaring-earring] message type RoaringConfigMessage id=<n>
   [sts2-roaring-earring] host: broadcast config energy=... cards=... order=... end=... start=... h=xxxxxxxx
   [sts2-roaring-earring] run settings frozen: energy=... h=xxxxxxxx
   ```

3. 客户端期望：

   ```
   [sts2-roaring-earring] config sync handlers registered; this peer is the Client
   [sts2-roaring-earring] message type RoaringConfigMessage id=<n>
   [sts2-roaring-earring] client: asked the host for its config
   [sts2-roaring-earring] adopting host config from player <房主 id>: energy=... h=xxxxxxxx
   [sts2-roaring-earring] run settings frozen: energy=... h=xxxxxxxx
   ```

4. **对账**：两端 `run settings frozen` 行里 `h=` 必须**完全相同**，`energy=`/`cards=`/`order=`/
   `end=`/`start=` 也必须相同。把两行原文抄进记录表。
5. **消息 id 对账**：两端 `message type RoaringConfigMessage id=` 的 `<n>` 应相同
   （两端装同一份模组清单时）。不同 → 两端模组清单不一致，先统一再测。
6. 失败判定：
   - 客户端只有 `client: asked...` 而始终没有 `adopting...` → 同步没到（记录是否同时出现
     `host config missing at floor ...`，见第 5 节）。
   - 两端 `h=` 不同 → 本用例失败，附两行原文。

## 3. 房主改设置：本局不变、下一局生效

1. 本局进行中，房主打开设置页（装 RitsuLib）把 `energy_per_turn` 从 1 改成 3。
   没装 RitsuLib 的话，手改 cfg 需要重启游戏，重启会结束本局 —— 那种情况请直接跳到「新开一局」，
   并记录「手改需要重启」。
2. 本局期望：
   - 房主：`settings write energy_per_turn` → `config saved to <path>`。
   - 本局**不应**出现新的 `run settings frozen` 行。
   - 本局实际能量仍是 1（改进前记一次每回合初始能量，改完再记一次，应相同）。
   - 客户端本局不应有任何变化（房主只在开局和被请求时广播）。
3. 双方结束本局，**新开一局**：
   - 房主：`run settings frozen: energy=3 ...`。
   - 客户端：`adopting host config from player ...: energy=3 ...` → `run settings frozen: energy=3 ...`。
   - 两端 `h=` 仍然相同。
4. 失败判定：
   - 本局能量变成 3，或本局出现新的 `frozen` 行 → 快照没生效。
   - 新一局客户端仍是 1 → 新对局没有重新同步。

## 4. 客户端改设置被拒

1. 客户端在本局内拨动设置页任一项（装了 RitsuLib 才有页面）。
2. 期望日志（每条只报一次）：

   ```
   [sts2-roaring-earring] settings write <key>
   [sts2-roaring-earring] settings write refused: a multiplayer client keeps the host's values and leaves its own JSON untouched
   [sts2-roaring-earring] settings page is read-only this run: a multiplayer client follows the host's config
   ```

3. 对比客户端 cfg 的**修改时间与内容**：应完全不变（用 `Get-FileHash` 记前后哈希）。
4. 失败判定：
   - 客户端 cfg 被改写或哈希变化。
   - 客户端本局数值发生变化。
   - 页面上显示的值被改成客户端自己拨的值（页面应继续显示房主的值）。

## 5. 补请路径（观察项，难稳定构造）

只有「客户端开局没收到房主配置」时才会走这条路。正常情况下不该出现这些行；
一旦出现，记录层号与次数，用来验证上限：

```
[sts2-roaring-earring] host config missing at floor <n>; re-requested (attempt <k>/5, local values in effect until it arrives)
[sts2-roaring-earring] giving up on the host config: 5 re-requests used this run; this run keeps the local values
```

判定：
- `attempt` 的 `<k>` 在同一层不应出现两次（每层最多补请一次）。
- `<k>` 不应超过 5；第 5 次之后只应看到 `giving up` 一次。
- 出现补请后，客户端最终仍应打出 `adopting host config` 与 `frozen` 两行。

## 6. 遗物归属：每人一个

1. 新开局后，两端各自检查遗物栏：**各自**有一个狂吼耳环。
2. 客户端可能比房主晚一个往返拿到（客户端要等房主配置采纳后才发放），属预期。
3. 期望日志（客户端）：`adopting host config ...` 之后 `frozen`，随后遗物出现。
4. 失败判定：
   - 只有房主有遗物、客户端没有。
   - 某一端有两个狂吼耳环（同一玩家不应拿到第二个）。
   - 客户端在采纳配置**之前**就拿到了遗物（顺序反了，记录时间戳）。

## 7. 与低语耳环共存

1. 让其中一端同时持有狂吼耳环与原版【低语耳环】（瓦库事件里选，或用正常流程拿）。
2. 期望：两套都跑，不崩、不互相屏蔽；战斗里能连续自动出牌。
3. 上限验证：
   - 把 `max_cards_per_turn` 设为 10（< 13），新开一局，确认低语耳环仍能打到 13 张。
   - 再设为 20，新开一局，确认低语耳环的上限变成 20。
4. 失败判定：低语耳环被压到 10 张 → `max(13, 配置)` 逻辑坏了；
   两套同时持有时崩或卡住 → 记录崩溃日志与当时手牌。

## 8. 出牌顺序与目标

1. 分别用 `left_to_right`、`right_to_left`、`random` 各开一局，记录每回合**第一张**打出的牌。
2. 期望：前两者分别是最左/最右的可打出牌；`random` 不可预测，但同一场战斗在两端应表现一致
   （取自联机同步的战斗卡牌选择 RNG）。
3. 失败判定：设置改成 `right_to_left` 后仍从最左出牌 → 设置没生效（先确认这一局是改设置**之后**开的）。

## 9. 记录模板

| # | 用例 | 房主 h= | 客户端 h= | 通过 | 日志原文 / 备注 |
| --- | --- | --- | --- | --- | --- |
| 1 | 单机基线 | — | — |  |  |
| 2 | 两端参数一致 |  |  |  |  |
| 3 | 房主改设置（本局/下一局） |  |  |  |  |
| 4 | 客户端写入被拒 |  |  |  |  |
| 5 | 补请上限 |  |  |  |  |
| 6 | 每人一个遗物 |  |  |  |  |
| 7 | 与低语耳环共存 |  |  |  |  |
| 8 | 顺序与目标 |  |  |  |  |

## 10. 失败判定汇总

| 现象 | 先看哪行 | 结论 |
| --- | --- | --- |
| 联机后两端数值不同 | 两端 `run settings frozen` 的 `h=` | 同步失败；若客户端有 `adopting` 行则说明采纳后又被改，记录时间顺序 |
| 客户端始终没有 `adopting` | `client: asked...`、`host config missing at floor ...` | 消息没到；补请是否触发、是否用满 5 次 |
| 客户端 cfg 被改写 | `settings write refused` 是否出现 | 拒绝逻辑没生效（联机判定失败） |
| 本局数值跟着设置变 | 本局是否新增 `frozen` 行 | 快照没生效 |
| 客户端没有遗物 | `adopting` / `frozen` 之后有没有 `run start hook failed` | 发放失败，抄异常原文 |
| 低语耳环上限没变 | `whispering earring: transpiler ... vanilla cap=` | 补丁没挂上或被 `INERT` 回滚（日志会写明） |
| 两端消息 id 不同 | `message type RoaringConfigMessage id=` | 两端模组清单/版本不一致 |

## 11. 测试后要交回的内容

- 记录表（第 9 节）填满的原文。
- 两端完整日志里所有 `[sts2-roaring-earring]` 行。
- 两端 cfg 的路径、内容、前后哈希。
- 明确写出：哪些用例通过、哪些没通过、哪些因为环境原因没跑。
