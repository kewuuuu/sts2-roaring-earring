# 创意工坊发布手册（本项目）

本文件只讲「怎么把 `sts2-roaring-earring` 发到 Steam 创意工坊」，以及失败时先查什么。
平台是 Slay the Spire 2，Steam appid `2868840`；工坊条目的内容根目录里放
`sts2-roaring-earring/sts2-roaring-earring.{dll,json}`。

> 当前状态：**已发布**，条目 id `3842516435`（public），
> 地址 https://steamcommunity.com/sharedfiles/filedetails/?id=3842516435 。
> 2026-11-20 `--create` 首版 0.1.0-beta（4 文件 76544 B）；
> 2026-11-21 `--update` 升到 0.2.0-beta（4 文件 78080 B，dll 67072 B）。
> 0.2.1-beta 已本地构建（两个快速出牌参数 + 图集缺失 sprite 修复），尚未上传。
> 之后改版只需升版本号（manifest + 正文两处）→ `tools\pack-workshop.ps1` → `--update 3842516435`。

## 1. 产物

| 路径 | 作用 |
| --- | --- |
| `dist/workshop/sts2-roaring-earring/sts2-roaring-earring.dll` | 模组本体（0.2.1-beta：70144 B） |
| `dist/workshop/sts2-roaring-earring/sts2-roaring-earring.json` | manifest（374 B） |
| `dist/workshop/roaring_earring_icon.png` | 遗物小图标（128×128，2380 B），随 dll 内嵌使用，不在工坊内容根目录里也行 |
| `dist/workshop/roaring_earring_big.png` | 遗物大图（512×512，8254 B），同上 |
| `workshop/title.txt` | 工坊标题（≤128 字符） |
| `workshop/description.bbcode` | 工坊正文（≤8000 字符） |
| `workshop/changelog.txt` | 更新说明（`--create` 不读它） |

内容根目录（`--content` 指向的那个目录）会被整体交给 Steam，所以它下面**只能**有
`<模组 id>/` 一层。两张 PNG 是否需要一起上传取决于 dll 是否已内嵌；当前 dll 内嵌图标，
它们留在 `dist/workshop/` 顶层只作为美术源文件，`CheckContent` 不要求也不需要它们。

## 2. 一次性准备

1. Steam 客户端以**有该 appid 上传权限**的账号登录（创作者账号或被授权的协作者）。
2. 上传器已经在本仓库：`tools/WorkshopUploader/`（从 sts2-replica 移植，已按第 3 节改好三处常量），
   配套的 `tools/pack-workshop.ps1` 负责构建 + 暂存。
3. 它依赖 Steamworks.NET：`WorkshopUploader.csproj` 的 `GameDataDir` 默认指向本机游戏目录，
   换机器时用 `-p:GameDataDir=...` 覆盖；`steam_api64.dll` 会随构建复制到输出目录。

## 3. 上传器移植时改过的三处（已改，留档备查）

| 位置 | 原值 | 改成 | 不改的后果 |
| --- | --- | --- | --- |
| `tools/WorkshopUploader/Program.cs:19` | `private const string ModId = "sts2-replica";` | `"sts2-roaring-earring"` | `CheckContent` 找 `<content>/sts2-replica/sts2-replica.dll`，直接报 missing |
| `tools/WorkshopUploader/Program.cs:304` | `Path.Combine(directory.FullName, "sts2-replica.csproj")` | `"sts2-roaring-earring.csproj"` | 找不到仓库根，默认路径（dist/workshop、workshop/*.txt）全部回落到当前工作目录 |
| `tools/WorkshopUploader/Program.cs:324` | `good_card_replica_big.png` | `roaring_earring_big.png` | `--preview` 不显式给就会报 preview missing |
| `tools/WorkshopUploader/Program.cs:20` | `MinModDllBytes = 60000` | 保持 | 当前 dll 65536 B 通过；若以后瘦身到 60000 以下，dry-run 会报 `is N bytes (< 60000)` |

其余常量可以不动：`MaxTitleChars = 128`、`MaxDescriptionChars = 8000`、
`MaxPreviewBytes = 1 MiB`（`:21-23`）。

## 4. 发布三步

### 第 1 步：构建并暂存

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-workshop.ps1
```

它做四件事：`dotnet build -c Release` → 逐文件把 `dist/sts2-roaring-earring/` 拷进
`dist/workshop/sts2-roaring-earring/` 并比对每个文件的字节数 → 用 UTF-8 读 manifest 校验 `id` →
把 `assets/icons/roaring_earring_{icon,big}.png` 放到 `dist/workshop/` 顶层。
最后一行是 `content ready: N files, M bytes`。已经构建过就加 `-NoBuild`。

检查内容根目录干净（不能有 pdb / zip / obj / bin）：

```powershell
Get-ChildItem -Recurse "dist\workshop" | Select-Object FullName, Length
```

### 第 2 步：dry-run（不联网、不上传）

```powershell
dotnet run --project tools\WorkshopUploader -c Release -- --dry-run
```

期望看到：标题字符数、正文字符数、preview 字节数、`content 2 files ... bytes`、
以及版本号一致性检查通过。任何一条 `BAD`/`MISSING` 都先修掉再往下。

### 第 3 步：首次发布或更新

首次（创建条目）：

```powershell
dotnet run --project tools\WorkshopUploader -c Release -- --create
```

拿到条目 id 后立刻写进 `dist/workshop-item-id.txt`（一行数字），之后都用它更新：

```powershell
dotnet run --project tools\WorkshopUploader -c Release -- --update <条目 id>
```

更新时 `workshop/changelog.txt` 才会被读取并作为本次更新说明；
`--create` 会忽略该文件并写入「首次发布」。

## 5. 校验规则速查（全部来自上传器源码）

| 规则 | 出处 |
| --- | --- |
| 标题 ≤ 128 字符、正文 ≤ 8000 字符 | `Program.cs:21-22` |
| 预览图存在、非空、≤ 1 MiB | `Program.cs:444-462` |
| 内容根目录存在，列出全部文件与总字节 | `Program.cs:464-487` |
| 内容里不允许 `.pdb` / `.zip` | `Program.cs:489-495` |
| 内容里不允许出现 `obj` / `bin` 目录段 | `Program.cs:497-505` |
| 必须有 `<content>/<id>/<id>.dll` 且 ≥ 60000 B | `Program.cs:508-520` |
| 必须有 `<content>/<id>/<id>.json`，且 `"id"` 等于模组 id | `Program.cs:522-540` |
| 正文里若写了「模组版本 X.Y.Z」必须与 manifest 的 `version` 一致 | `Program.cs:549-591` |
| `--branch-min` 与 `--branch-max` 必须成对出现 | `Program.cs:593-607` |

注意最后两条容易踩：

- 正文写的是 `模组版本 0.2.1-beta`（中英各一处），manifest 的 `version` 也是 `0.2.1-beta`；
  改版本号时**两处一起改**，否则 dry-run 直接报
  `description says mod version ... but the manifest says ...`（上一次 0.2.0-beta 上传时就先被这条拦下一次）。
- 版本号正则只认 `模组版本` / `mod version` 后跟 `数字.数字.数字[-后缀]`；
  随手写「版本：beta」不会被检查，也就会漏掉不一致。

## 6. 文案与文件对应

| 工坊字段 | 文件 | 硬限制 | 备注 |
| --- | --- | --- | --- |
| 标题 | `workshop/title.txt` | 128 字符 | 一行，中文名 + 英文名 |
| 正文 | `workshop/description.bbcode` | 8000 字符 | BBCode 白名单：`h1/h2/h3/b/i/u/hr/list/table/quote/url/code`；**不要用 `[color=#hex]`**，工坊不解析，会原样泄漏成文本 |
| 更新说明 | `workshop/changelog.txt` | 无 | 一版一句，纯文本 |
| 预览图 | `dist/workshop/roaring_earring_big.png` | 1 MiB | 用 `--preview` 显式指定最稳 |

## 7. 发布后核验

1. 打开条目页，确认标题、正文排版、预览图、可见性（公开/仅链接）都正确。
2. 用**干净的游戏目录**（只留工坊版、删掉本地 `mods/sts2-roaring-earring/`）订阅并启动：
   同 id 的本地版与工坊版同时存在时游戏只会启用一个，只留一处才不会测错版本。
3. 进游戏看 `%APPDATA%\SlayTheSpire2\logs\godot.log` 里有没有
   `[sts2-roaring-earring] initialized; ...`；有就说明工坊版被加载。
4. 在工坊页把「所需游戏版本」与 `min_game_version`（`0.113.1`）对一下。

## 8. 常见失败与先查什么

| 现象 | 先查 |
| --- | --- |
| `content <id>/<id>.dll` missing | 是否改过 `Program.cs:19` 的 `ModId`；`dist/workshop/` 下是否真的有一层 `sts2-roaring-earring/` |
| `dll is N bytes (< 60000)` | 是否构建成了 Debug 片段或空壳；确认 `dist/workshop/` 里是 Release 产物 |
| `content contains ... .pdb` | 把 pdb 从内容根目录删掉（构建产物在 `dist/sts2-roaring-earring/`，别整目录复制） |
| `description says mod version ... but the manifest says ...` | 正文与 manifest 的版本号不一致 |
| `preview ... missing` | 没改 `Program.cs:324` 的默认预览图，也没传 `--preview` |
| 卡在 `preparing content` / `uploading content` 不动 | 网络与 Steam 客户端状态；内容体积（当前不到 100 KB，正常应很快）；Steam 客户端是否掉线后重登 |
| `InvalidHandle` 一类 Steamworks 错误 | SteamAPI 是否初始化成功、`--probe` 能否单独跑通；重跑一次 `--probe` 再决定是否重试上传 |
| 上传「成功」但页面没变 | 条目是否被审核/隐藏；`--update` 用的 id 是否是同一个条目 |

## 9. 更新与回滚

- 更新：改代码 → 改 `mod/sts2-roaring-earring.json` 的 `version` → 改
  `workshop/description.bbcode` 里的「模组版本」→ 在 `workshop/changelog.txt` 顶部加一条 →
  重新 build、重新暂存、`--dry-run`、`--update <id>`。
- 回滚：把工坊条目可见性改成隐藏/私有，或上传上一版内容；本地玩家把
  `<游戏目录>/mods/sts2-roaring-earring/` 换成旧版 dll 即可，配置文件向后兼容（多出的键会被忽略）。
- 绝不删除已发布条目的 id 记录：`dist/workshop-item-id.txt` 丢了就只能靠工坊后台找回。
