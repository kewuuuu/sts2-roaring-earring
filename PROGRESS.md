
## 2026-11-21 0.3.1-beta：自动结束回合保留按钮、可手动撤回

- 用户报告（多人局）：自动结束回合后结束回合按钮变灰消失。
- 根因：`src/Models/RoaringEarring.cs:222` 原来传 `canBackOut: false`。`CombatManager.SetReadyToEndTurn`（CombatManager.cs:973-976）只在 canBackOut 为假时把玩家记进 `PlayersThatCannotUnEndTurn`；`NEndTurnButton.AfterPlayerEndedTurn`（NEndTurnButton.cs:462-469）只在 canBackOut 为真、且还有别的玩家没结束时，才把按钮设为 Enabled 并把文案换成「取消结束回合」，否则一律 Disabled。
- 修法：改成 `canBackOut: true`。玩家点「取消结束回合」走原版按钮逻辑（NEndTurnButton.cs:593）发出联网的 `UndoEndPlayerTurnAction`（UndoEndPlayerTurnAction.cs:40 → CombatManager.TryUndoReadyToEndTurn），所以多人撤回也是同步的；单人对局没有别人要等，全员就绪立刻切边，行为不变。
- 验收：dotnet build 0 警告 0 错误（dll 73728 B，只有常量变化故字节数不变）；ilspycmd 反编译确认产物里是 `PlayerCmd.EndTurn(((RelicModel)this).Owner, true, (Func<Task>)null)`；装进 mods 后无头启动 exit=0，日志 `initialized; … auto_end_turn=True`、模组列表 (0.3.1-beta)、无指向本模组的 ERROR。
- 发布：正文压到 7970 UTF-8 字节后 `--update 3842516435` 成功（file_size 84736）。另修 docs/workshop.md 把「≤8000 字符」改成「≤8000 UTF-8 字节」。
## 2026-11-21 0.3.0-beta：点击遗物切换自动出牌

- 模型加 `[SavedProperty] public bool AutoPlayEnabled`（默认 true，老存档缺字段读初始值）；回合起点按 `(PlayerCombatState 引用, TurnNumber)` 懒快照，`ModifyMaxEnergy` 与 `AfterAutoPrePlayPhaseEnteredLate` 都读同一快照 ⇒ 点击只影响下一回合。
- 配置第 7 键 `show_toggle`（默认 true）：为 false 时点击不生效、遗物始终按开工作（`EffectiveAutoPlayEnabled`）。
- 联机：新 INetMessage `RoaringToggleMessage`（OwnerNetId + Enabled，Reliable，ShouldBroadcast=false）；客户端只向房主发请求，房主校验 `OwnerNetId == senderId` 后应用并广播已应用值，客户端只接受房主广播。
- 点击入口：Harmony postfix 打在 `NClickableControl.OnRelease`（NClickableControl.cs:441-449 → :495-497 protected virtual；NButton 与 NRelicInventoryHolder 都未 override），只认 `NRelicInventoryHolder`，`LocalContext.IsMine` 过滤他人遗物，整段 try/catch。
- 验收：dotnet build 0 警告 0 错误（dll 73728 B）；dll 内 UTF-16LE 命中 show_toggle / toggle_default / 中英新描述 / 下一回合生效，UTF-8 #Strings 命中 RoaringToggleMessage / RoaringToggleSync / RelicToggleClickPatch / AutoPlayEnabled / AutoPlayActiveThisTurn。
## 2026-11-21 0.2.3-beta：遗物登记进 EventRelicPool（图鉴/控制台/悬停）

- 根因：遗物不在任何遗物池里 ⇒ RelicModel.Pool（RelicModel.cs:176）抛 Sequence contains no matching element，悬停它时经 EnergyIconHelper.GetPool 就崩；ModelDb.AllRelics（ModelDb.cs:264）不含它 ⇒ 图鉴缺位（NRelicCollectionCategory.cs:157）、控制台 relic 找不到（RelicConsoleCmd.cs:68）。
- 修法：Init 里 ModHelper.AddModelToPool<EventRelicPool, RoaringEarring>()（纯登记表，RelicGrabBag.cs:70-77 只读 SharedRelicPool + 角色池，随机奖励不受影响）。
- 图鉴「远古 → 瓦库」另需 Vakuu.AllPossibleOptions 含它（NRelicCollectionCategory.cs:197），故 VakuuPool1Patch 在非局内的规范实例上追加仅展示的锁定选项；局内路径不变。
- 验收：dotnet build 0 警告 0 错误（dll 67584 B）；无头启动 exit=0，initialized 行含 relic_pool=EventRelicPool (registry only)，零 ERROR 指向本模组。

## 2026-11-21 0.2.2-beta：回退快速自动出牌

- 用户实测多人局出现「牌都在手里」并刷 NPlayerHand duplicate card ERROR；根因是 0.2.1-beta 的 skip_play_animation 把 skipCardPileVisuals: true 传给 CardCmd.AutoPlay，CardPileCmd.cs:522 的 !skipVisuals 分支被跳过 ⇒ 手牌 UI 节点永不移除（CardPileCmd.cs:699 → NPlayerHand.cs:674/:725），下回合抽到同一实例时 NPlayerHand.cs:653 报 duplicate 并丢弃新节点。
- 回退 11 个路径：删 src/Patches/AutoPlayAnimScope.cs；配置删 skip_play_animation 与 auto_play_anim_speed；RoaringEarring 恢复 CardCmd.AutoPlay(..., skipXCapture: true)；联机消息尾部两字段一并删除（本版线格式回到 6 字段）。
- 保留 play_delay_ms 与图标 prefix 覆盖（图集缺失 sprite 警告的修复不受影响）。
- 验收：dotnet build -c Release 0 警告 0 错误，dll 67072 B；src/ 下 7 个旧标识符 grep 0 命中。

## 2026-11-21 0.2.1-beta：快速自动出牌 + 图集警告修复

- 新配置 skip_play_animation（默认 true）：CardCmd.AutoPlay 传 skipCardPileVisuals: true，跳过飞牌补间与 OnPlayWrapper 里 0.25–0.35 秒等待（CardModel.cs:2020-2035）。
- 新配置 auto_play_anim_speed（1..3，默认 1）：src/Patches/AutoPlayAnimScope.cs 在 MegaAnimationState.SetTimeScale 与 MegaTrackEntry.SetTimeScale 上打 prefix，只在自动出牌循环期间乘时间缩放，try/finally 退出即复位。
- 图标补丁由 postfix 改 prefix（src/Art/RelicIconPatch.cs）：自家遗物直接返回运行时贴图并 return false，不再触发原版 res:// …relic_atlas 的失败查找，每局刷屏的 AtlasResourceLoader: Missing sprite 警告消失。
- 顺带删掉上一版遗留的 [sts2-replica] Icon getter #N 探针（sts2-replica 仓库单独提交 c5b7900）。
- 验收：两仓 build 0 警告 0 错误（狂吼耳环 dll 70144 B）；无头启动 exit=0，initialized 行出现 skip_play_animation=True 与 auto_play_anim_speed=1，零 ERROR 指向本模组；工坊 content 4 文件 81152 B。
