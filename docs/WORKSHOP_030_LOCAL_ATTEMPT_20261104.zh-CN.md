# 0.3.0 本机上传接续

已拉取 main 至 cb27cee；本机工作区原先干净，无覆盖。下载 v0.3.0 的 EmoteChat-0.3.0-workshop-update.zip，SHA256 `6e69a3b0c092f7ab6ffdd1fbc7f22f6b30feb8906073947e66910f6095250eb8` 与 GitHub 资源摘要一致，ZIP 完整性通过。包内版本 0.3.0，所有 C# 文件与当前分支一致，不含个人图库、收藏或游戏 DLL。更新状态始终绑定现有 3833045408，不会创建新条目。

本机后台客户端、服务器编译均通过，零警告零错误；使用本机安装目录 Publicized 引用的独立副本。Python 11 项执行、10 通过、1 符号链接权限跳过。本轮没有启动游戏、修改游戏 DLL、安装或操作模组，也未将云端 72 核心/99 原生检查记为本轮本机实测。

已执行更新入口，但官方 SDK 检查 SteamClient.IsLoggedOn 未通过，报错 `Expected logged-on Barotrauma Steam session`。Steam 进程正在运行，不代表已登录在线。脚本在上传前停止，没有提交内容、修改线上资料或重新创建条目。0.3.0 工坊更新仍待登录后继续，不能把此前 0.2.0 的验证结果当成此次成功。

本机已准备目录 `D:/Codex/emote-chat-030-update-20261104/prepared/workshop/`；账号在官方客户端登录在线后，继续执行该目录的 Publish-New.ps1，使用真实游戏目录 D:/steam/steamapps/common/Barotrauma。保留 publish-state.json，不得删除以触发 Create。上传后还须完成双语/封面/公开状态回读与实际发布文件回下载校验；个人 Library 数据应保留、不上传。

编译、Python 日志、ZIP、发布错误日志仅留本机 `D:/Codex/emote-chat-030-update-20261104/`。不索取或提交账号密码、Steam Guard 验证码或令牌。
