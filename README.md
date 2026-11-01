<p align="center">
  <img src="assets/logo.png" width="96" alt="Tunnel Manager logo">
</p>

<h1 align="center">Tunnel Manager</h1>

<p align="center">在 Windows 上查看 Cloudflare Tunnel 状态，管理已发布的应用。</p>

用 Cloudflare 账号登录后，所有隧道和应用会显示在一张线路图上。不用手动创建 API Token，也不用打开后台就能添加应用；连接 TCP 应用时，也不用记 `cloudflared access` 命令。

## 功能

- **隧道状态：** 查看全部隧道的在线状态、最近上线时间和 cloudflared 版本，每 30 秒刷新一次。
- **管理应用：** 添加、修改或删除已发布的应用，可设置主机名、路径和 Service URL，并同步 DNS 记录。
- **应用设置：** 编辑 HTTP、TLS、连接和 Access 设置，与 Cloudflare 后台的“其他应用程序设置”对应。
- **DNS 检查：** 标出缺失或指向其他目标的记录，缺失时可以一键修复。主机名已有其他记录时不会覆盖。
- **TCP 连接：** 一键运行 `cloudflared access tcp`，每个主机名固定一个本地端口，已保存的 RDP、SSH 配置可以继续使用。
- **HTTP 应用：** 在浏览器中打开。
- **托盘：** 最小化到系统托盘，后台继续刷新，连接断开时弹出提醒。
- **配色：** 深色、浅色，或跟随 Windows 自动切换。
- **单实例：** 重复启动时回到已打开的窗口。

## 环境要求

- Windows 10 或 Windows 11
- 服务端已安装并连上 Cloudflare 的 cloudflared，隧道由网页后台管理
- 域名托管在同一个 Cloudflare 账号下
- 连接 TCP 应用时，本机需要安装 [cloudflared](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/)
- 本机 8976 端口空闲，用于接收登录回调
- 从源码运行需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)

用本地配置文件管理的隧道能看到状态，但不能查看或修改应用。

## 快速开始

### 1. 运行

```powershell
git clone https://github.com/lim0513/cf-tunnel-manager.git
cd cf-tunnel-manager
dotnet run --project src/CfTunnelManager
```

### 2. 创建 OAuth 客户端

程序不内置客户端，每个 Cloudflare 账号只需创建一次。这一步需要账号的管理员权限。

1. 打开 Cloudflare 后台的 [OAuth clients](https://dash.cloudflare.com/?to=/:account/oauth-clients)，选择 **Create client**。
2. 按下表填写：

   | 字段 | 值 |
   |---|---|
   | Response type | `code` |
   | Grant type | `authorization_code`、`refresh_token` |
   | Token authentication method | `none` |
   | Redirect URLs | `http://127.0.0.1:8976/callback` |

3. 权限选择以下四项，全部保持“必需”：
   - Cloudflare Tunnel Write
   - DNS Write
   - Zone Read
   - Account Settings Read
4. 创建后复制 **Client ID**。

客户端保持默认的私有即可，账号成员都能登录。不需要 Client Secret。改成公开后无法改回私有。

### 3. 登录

在应用的登录页点“查看配置步骤”或右上角的问号，填入 Client ID 并保存。关闭弹窗后点“登录 Cloudflare”，在浏览器里完成授权。

## 项目结构

```text
src/CfTunnelManager.Core   OAuth、Cloudflare API、路由和 DNS 逻辑
src/CfTunnelManager        WPF 界面、托盘、cloudflared 进程管理
tests/                     核心库的单元测试
tools/generate-icon.ps1    生成应用图标
```

## 开发

```powershell
dotnet build
dotnet test
```

## 本地数据

保存在 `%LOCALAPPDATA%\CfTunnelManager`：

| 文件 | 内容 |
|---|---|
| `tokens.bin` | 用 Windows DPAPI 加密的登录令牌 |
| `client-id.txt` | OAuth Client ID，不是密钥 |
| `ports.json` | 每个主机名使用的本地端口 |
| `collapsed.json` | 已收起的隧道分组 |
| `theme.txt` | 配色选择 |

应用只与 Cloudflare 通信，退出登录时会撤销令牌。

## 已知限制

- 账号下有多个 Cloudflare 账号时，只使用第一个。
- 本机没有 cloudflared 时只提示安装，不会自动下载。
- 只管理已发布的应用，不包括专用网络路由、WARP 和 Access 策略。
- 隧道在线不代表后端服务可用。

## 许可证

[MIT](LICENSE)

按钮图标来自 Google [Material Icons](https://github.com/google/material-design-icons)，使用 Apache License 2.0。
