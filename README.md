# 表情包聊天和气泡显示 | chat with emote

本地导入 PNG、JPG 和 GIF，在原版聊天记录和人物头顶显示图片或动画。服务器和客户端都需要安装本模组及 LuaCs，并启用 C# 模组支持。

**当前为 0.1.1 修复版，目标版本为 Barotrauma 1.13.5.0。** 已有自动化测试和参考程序集编译检查，尚未完成真实游戏双客户端、专用服及 GPU/UI 验证。请先在测试服试用；不宣称绝对安全、零开销或已通过实机验收。其他游戏版本会停用此预览版，避免强行修改未知接口。

## 使用

1. 将发布包的 `LocalMods/EmoteChat` 文件夹放到游戏的 `LocalMods` 下；房主/专用服和所有参与者安装并启用同一版本。需要 LuaCs 和 C# 支持；模组不会替你替换游戏 DLL 或修改启动设置。
2. Windows 发布包中，双击 `EmoteChat/Tools/EmoteImporter.exe` 选择图片，或直接将 PNG、JPG、GIF 拖到这个 EXE 上。工具把资源写入同一模组的 `Library`，不会经由批处理脚本转交文件名。首次导入后重新打开面板即可。源码包没有独立 EXE 时，需要 Python 3.12+，先执行 `python -m pip install -r tools/requirements.txt`，也可使用下方命令。
3. 加入服务器，按 **F8**，点击表情缩略图发送。沿用当前聊天频道；首次同步显示进度，之后命中缓存只传标识。`ec` 控制台命令也可打开面板。
4. 面板可关闭表情接收、关闭动画。关闭动画时显示首帧。头顶气泡持续约 6 秒；初次下载完成后才开始计时。

```sh
python tools/import_emotes.py "我的表情.gif"
```

请使用 **0.1.1 随包的新导入器**，它可以复制到任意磁盘后运行。游戏也兼容读取尚在旧模组 `Library` 中的表情；新版统一库位于用户目录，工坊更新不覆盖它。`--library` 仅用于显式导出或开发测试，普通使用无需指定。旧版已经转换成单帧的 GIF，须用新版导入器重新导入原 GIF 一次。工坊中英文标题、简介与第一版封面均已确认，当前等待本地实机验收与 Steam 上传。接续话术和全部资料见 [本地 Codex 交接](docs/LOCAL_CODEX_HANDOFF.zh-CN.md)。

## 安全与性能设计

- 原始 GIF/PNG/JPG 只由本地导入工具处理。游戏联机端只接受 ECP1 受限像素格式，不调用通用图片解码器、不访问图片网址、不执行资源中的任何代码。
- 服务器按真实连接识别发送者。通过原版聊天选出的收件人才能请求相应资源；不另行全服广播，保留距离、无线电、死亡和私聊规则。被距离效果破坏的标识不会解析成图片。
- 尺寸、帧数、时长、压缩体积、解压后体积和 SHA-256 均检查。解析在受控后台任务中完成；服务器验证时不保存完整解压帧。
- 每个发送端上限 128 KiB/s（仍受总预算限制），模组应用层全局出口上限 128 KiB/s；32 块窗口、重试、超时及拥塞回压。服务器只保留一份压缩资源，多名收件人共享该数据并轮流分发；只向原版聊天实际收件人供图。实际速度还取决于延迟、丢包、原版网络负载和其他玩家的下载队列。
- 客户端资源缓存最多 24 MiB、32 项；服务器压缩缓存最多 8 MiB、64 项。远端资源不写入磁盘。缓存淘汰后的旧聊天图片可显示占位符，重新发送时恢复。
- 每个资源共用一张纹理，只在可见并且帧变化时上传。每次更新最多两次纹理上传；不在每帧读盘或解码 GIF。最多同时显示 16 个人物气泡。

导入限制：源文件 ≤8 MiB、源图最长边 ≤1024、源动画 ≤240 帧。输出 ≤256 KiB、最长边 ≤128、≤48 帧、总时长 ≤10 秒；短 GIF 保留多帧和原始延时（最低 20ms）。为控制开销，长动画会截取前 10 秒，高帧率动画会采样，过大的动画会逐级缩小。文件名只在本地用于面板标签，不发给其他玩家。详细边界见 [安全说明](docs/SECURITY.md)。

## 开发与验证

```sh
python -m pip install -r tools/requirements.txt
python -m unittest discover -s tests -p 'test_*.py' -v
python tests/make_fixtures.py
dotnet run --project tests/CoreTests.csproj -- .test-fixtures/still.ecp .test-fixtures/motion.ecp .test-fixtures/short.ecp
dotnet build tools/CompileCheck.csproj -p:Flavor=Client -p:RefsDir=/path/to/refs
dotnet build tools/CompileCheck.csproj -p:Flavor=Server -p:RefsDir=/path/to/refs
python tools/package.py
```

需要 .NET 8 SDK。编译使用 LuaCs 官方发布的参考程序集；仓库与发布包不包含游戏 DLL。CI 校验参考包 SHA-256，依赖变化时停止构建，待重新核对接口。CI 还会构建并试运行 Windows 独立导入工具，最后生成白名单发布包。

验证结果及未完成事项见 [验证记录](docs/VALIDATION.md)。请勿把格式解析微基准理解为整局游戏的 FPS、延迟或多人安全保证。

## 反馈与致谢

反馈请提供游戏/LuaCs 版本、客户端还是专用服、表情尺寸与帧数、是否首次发送，以及可复现步骤。不要公开上传存档、原始聊天记录、他人私密图片或凭据。

特别感谢[《东方潜渊行动组》及其作者团队](https://steamcommunity.com/sharedfiles/filedetails/?id=2941124988)提供灵感！喜欢东方角色、特色装束、职业与事件的船员，推荐体验这个模组。

模组原创代码使用 MIT 许可证。本地导入工具依赖 Pillow，其许可证随相应依赖分发。导入和分享的图片版权由使用者自行管理。

0.1.1 的修复和验证范围见 [本轮修复记录](docs/FIXES-0.1.1.zh-CN.md)。服务器与客户端必须一起升级到 0.1.1（协议 v2）；已有工坊条目只更新，不新建。
