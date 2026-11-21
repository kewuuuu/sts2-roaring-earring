
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
