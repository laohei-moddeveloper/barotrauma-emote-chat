# 0.3.0 工坊更新成功

用户完成 Steam 登录后，继续既有上传目录，已成功更新 https://steamcommunity.com/sharedfiles/filedetails/?id=3833045408 。没有创建新条目。Steam 回执 OK，更新时间 2026-11-04T07:42:01Z（北京时间 15:42:01）。此前登录阻挡记录保留，但现已解决。

原生 API 回读：简中标题「表情包聊天和气泡显示」、英文标题「chat with emote」、两种语言完整简介均匹配本次包文件，状态均 Public。线上封面下载 SHA256 与第一版 A 一致。原生 children 查询确认 LuaCs 2559634234 依赖仍存在；重复添加返回 false 不代表依赖被删除。

包：v0.3.0 / EmoteChat-0.3.0-workshop-update.zip。SHA256 `6e69a3b0c092f7ab6ffdd1fbc7f22f6b30feb8906073947e66910f6095250eb8`，与 GitHub Release 资源 digest 一致。包内 0.3.0 清单与全部 C# 文件和 main cb27cee 一致，状态绑定现有 3833045408，无个人图库、收藏、游戏 DLL 或存档。发布继续使用同一状态文件，没有删除它触发 Create。

上传后真实回下载，发布清单所有文件 SHA256 匹配，额外文件只允许本机个人 Library，未删除或上传用户图片及名字。校验清单是在本次新暂存目录创建，未使用旧版本包。

本机后台验证沿用前一步实际执行结果：客户端、服务器对本机安装目录 Publicized 引用副本编译，均零警告零错误；Python 11 项执行、10 通过、1 因符号链接权限跳过。没有启动游戏、客户端图形界面或专用服，未对真实多人收到/保存/收藏、重启持久化和昵称布局作实机验收；云端 72 核心、99 原生与软件 GPU 结果不冒充本机实测。

未改游戏 DLL、第三方模组或存档，未覆盖运行中的程序。0.3.0 客户端升级后使用新昵称和收到/收藏功能，协议 v2 不变，与 0.2.0 服务端兼容。实际生效仍需用户在安全时机重载插件。

本机证据目录 `D:/Codex/emote-chat-030-update-20261104/`：Release 包、编译与 Python 日志、publish-after-login.log、dependency-verification.json、cover-verification.json；prepared/workshop 内保存状态、双语/API 验证、上传清单与 download-verification.json。未提交凭据或个人文件。
