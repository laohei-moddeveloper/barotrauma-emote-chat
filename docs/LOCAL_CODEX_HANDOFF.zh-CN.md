# 最新：0.2.0 界面与纯图片修复接续

拉取最新 main，使用 v0.2.0 的 EmoteChat-0.2.0-workshop-update.zip。只更新现有工坊 **3833045408**，不创建新条目。沿用第一版封面、双语标题、简介和灵感感谢。包内状态已绑定该 ID。

重点实测：F8 自绘面板的搜索/最近/刷新/选中预览/发送、不同分辨率缩放、GIF 在头顶与展开/折叠/大厅聊天持续动画、纯图片无代码（包括其他聊天模组组合）、距离/电台干扰不泄露损坏标识、普通聊天不变。更新服务器与所有客户端并重载插件，避免旧端继续走旧显示链路。不要中断运行中的游戏或替换游戏 DLL。

使用现有 Publish-New.ps1 读取 publish-state.json 更新并回读双语资料；脚本名称历史遗留，不得移除状态触发新建。完成 Steam 更新后记录回执与实测结果。此云环境没有本机游戏或 Steam 登录态，不能宣称已更新安装或上传工坊。

详情见 docs/FIXES-0.2.0.zh-CN.md；下文为历史记录。

---

# 0.1.1 修复接续（优先于下方历史交接）

工坊已存在，ID **3833045408**，只允许更新这个条目，不得重新 Create。GitHub 拉取最新 main，下载 0.1.1 工坊更新包；包内 publish-state.json 已绑定现有 ID。沿用已选第一版封面、中英文标题及灵感感谢。

本轮修复首次传输、短 GIF 被导成静图、动画纹理上传时机、展开/折叠/大厅聊天布局、原生气泡长标识泄露、导入器所在磁盘依赖，并限制多人分发服务器总出口。详情和验证边界见 docs/FIXES-0.1.1.zh-CN.md。

本地应使用真实 1.13.5.0 + LuaCs 环境复核这些用户场景：两帧短 GIF、现有多帧 GIF、图形缩放/窄聊天窗/折叠提示、头顶与聊天同步动画、无长标识、便携导入 EXE 任意磁盘、同服重连、多收件人。旧的单帧 ECP 无法恢复丢失帧，需要重导原 GIF。服务器和所有客户端一起升级协议 v2。不要中断正在运行的游戏或替换游戏 DLL。

通过后用现有 Publish-New.ps1 入口读取更新包内的现有 publish-state.json，先核对当前 Steam 账号拥有 ID 3833045408，再更新上传和双语回读；入口名称是历史遗留，不能删除状态文件去新建。把实际工坊更新回执、版本与实测结果同步 GitHub。

---

# 给本地 Codex 的接续话术与发布资料

用户已授权开发本模组、同步 GitHub，并新建一个 Steam 创意工坊项目。标题、简介方向和封面已确认，无需再次询问这些偏好。

## 可以直接复制给本地 Codex

```text
请继续完成《潜渊症》模组「表情包聊天和气泡显示 | chat with emote」的本地实机验证和创意工坊发布。

GitHub：https://github.com/laohei-moddeveloper/barotrauma-emote-chat
先拉取 main，阅读 README.md、docs/RESULTS.txt、docs/SECURITY.md 和本交接文件，查看最近一次 GitHub Actions 是否通过。
如果有 v0.1.0-preview 发布，下载其中的 EmoteChat-0.1.0-workshop-publish.zip；否则从通过验证的 Actions 下载同名产物。不要使用旧的失败构建。

这些配置已确定，请直接沿用：
- 游戏版本 1.13.5.0，双方及服务器需要 LuaCs 与 C# 支持。
- 中文工坊名：表情包聊天和气泡显示
- 英文工坊名：chat with emote（保持这个小写写法）
- 封面采用用户选择的第一版 A，对应仓库 workshop/cover.jpg，不再生成其他版本。
- 中英文简介分别在 workshop/description.zh-CN.txt、workshop/description.en.txt。
- 简介以两句反问开头，再接「老实打字聊天，很枯燥吧？」；介绍功能、用法和具体安全措施，别扩写无意义免责声明或承诺绝对安全。
- 感谢灵感模组《东方潜渊行动组》及作者团队，保留简短推荐和链接 https://steamcommunity.com/sharedfiles/filedetails/?id=2941124988 。这是致谢推荐，不是本模组运行依赖。

先识别本机真实游戏路径、版本和 LuaCs/C# 状态，不要使用项目旧电脑默认路径或缓存 DLL 冒充本机环境。不要替换游戏 DLL，不要中断正在运行的游戏。
验证 F8 面板、导入静图/GIF、透明与动画、聊天记录、人物头顶气泡；至少测试真实双客户端，能用专用服也一起验证。覆盖普通/无线电/私聊/死亡频道与距离规则、首次下载、缓存、断线换服、关闭表情/动画和卸载清理。记录帧时间、显存/内存和传输行为，不能把格式微基准当整局帧率保证。
发现问题直接修复，跑相关检查并同步 GitHub，保持原生聊天权限与资源上限。

实机通过后，用当前用户已登录的 Steam 账号新建本模组工坊条目。确认账号身份、app 602960，以及不存在同名/同元数据的本模组条目。不要修改其他模组的工坊项目。
优先使用上传包的 workshop/Publish-New.ps1（PowerShell 7.2+），参数 -GameDir 指向真实游戏目录。它依次新建私有草稿、保存新 ID、上传内容与封面、设置公开、写入并原生回读中英文元数据。
只创建一次：如果 workshop/publish-state.json 已有 ID，恢复/更新该 ID，不要重新 Create。SDK 调用中断或条款待同意时也必须保留这个 ID。
遇到 Steam 未登录或 Steam 实际返回需要接受工坊条款，只让用户完成那一步；其他已经授权的步骤继续执行，不要再次要求确认标题/封面/简介。

完成后核实：当前账号拥有条目、游戏 app 正确、公开可见、文件/封面存在、中英文标题和简介原生回读完全匹配、LuaCs 依赖正确，并检查下载后的模组可加载。
把新工坊 ID、链接、公开状态、双方语言验证结果、实际游戏测试结论与校验和写回 docs/PUBLICATION.json 和验证记录，同步 GitHub。排除个人表情、names.json、游戏 DLL、凭据、存档和私人报告。
最后给我 GitHub 和工坊链接；如果仍有真实阻塞，明确说是哪一步，不要把准备好上传包说成已发布。
```

## 当前事实与边界

- 本地导入工具、受限像素格式、资源传输/缓存、F8 缩略图面板、聊天图片和头顶气泡代码已实现。
- 本地验证：7 项 Python 测试、39 项 C# 检查；近上限资源在 0ms/800ms RTT、丢包、重复与乱序模拟下完成；11,000 个随机/结构化畸形格式输入。
- 客户端和服务器参考程序集编译通过。实际下载的 1.13.5.0 运行时程序集已在隔离进程中安装/卸载全部补丁，客户端 11 项、服务器 9 项检查通过。
- 这里没有用户的游戏图形会话或 Steam 登录态。没有真实游戏双客户端/UI/GPU/专用服验收，也没有实际创建工坊 ID。工坊状态仍是待本地发布。
- Windows 独立导入 EXE 由 Actions 构建；以最新成功的工作流结果为准。第一轮曾因非中文系统控制台编码失败，已修复并加中文文件名回归检查。

## 文件位置

| 用途 | 仓库路径 |
| --- | --- |
| 模组源代码与清单 | `LocalMod/EmoteChat/` |
| Windows 双击/拖入导入器源码 | `tools/import_emotes.py` |
| Mod 发布包生成 | `tools/package.py` |
| 工坊上传包生成 | `tools/package_workshop.py` |
| 已选封面 | `workshop/cover.jpg` |
| 两种语言标题与简介 | `workshop/title.*.txt`、`workshop/description.*.txt` |
| 一次性创建并继续发布入口 | `workshop/Publish-New.ps1` |
| 上传、所有权核对、ID 持久化 | `workshop/Publish-Workshop.ps1` |
| 双语提交与真实语言回读 | `workshop/Update-Localizations.ps1` |
| 持续更新的发布状态 | `docs/PUBLICATION.json` |

在解压后的上传包中运行（示例路径需自行替换）：

```powershell
pwsh -File .\workshop\Publish-New.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\Barotrauma'
```

直接从源码执行脚本前，必须先准备 `workshop/content/EmoteChat` 的白名单内容；不要指向包含个人表情的已安装模组目录。推荐使用 Actions 产物中的完整上传包。
