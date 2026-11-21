
## 2026-11-21 0.2.1-beta：快速自动出牌 + 图集警告修复

- 新配置 skip_play_animation（默认 true）：CardCmd.AutoPlay 传 skipCardPileVisuals: true，跳过飞牌补间与 OnPlayWrapper 里 0.25–0.35 秒等待（CardModel.cs:2020-2035）。
- 新配置 auto_play_anim_speed（1..3，默认 1）：src/Patches/AutoPlayAnimScope.cs 在 MegaAnimationState.SetTimeScale 与 MegaTrackEntry.SetTimeScale 上打 prefix，只在自动出牌循环期间乘时间缩放，try/finally 退出即复位。
- 图标补丁由 postfix 改 prefix（src/Art/RelicIconPatch.cs）：自家遗物直接返回运行时贴图并 return false，不再触发原版 res:// …relic_atlas 的失败查找，每局刷屏的 AtlasResourceLoader: Missing sprite 警告消失。
- 顺带删掉上一版遗留的 [sts2-replica] Icon getter #N 探针（sts2-replica 仓库单独提交 c5b7900）。
- 验收：两仓 build 0 警告 0 错误（狂吼耳环 dll 70144 B）；无头启动 exit=0，initialized 行出现 skip_play_animation=True 与 auto_play_anim_speed=1，零 ERROR 指向本模组；工坊 content 4 文件 81152 B。
