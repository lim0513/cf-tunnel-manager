# Tunnel Manager

管理 Cloudflare Tunnel 已发布应用的 Windows 桌面工具。

- 用 Cloudflare 账号登录（OAuth + PKCE），不需要手动创建 API Token
- 查看全部隧道的在线状态和已发布应用
- 添加、删除应用，并同步 DNS 记录，检查 DNS 状态
- 一键运行 `cloudflared access tcp` 连接 TCP 应用，或在浏览器打开 HTTP 应用

服务端的 `cloudflared` 需要已安装好；客户端连接 TCP 应用时，本机也需要安装 `cloudflared`。
只支持网页后台管理的隧道（远程管理模式）。

## 构建

需要 .NET 10 SDK。

```powershell
dotnet test
dotnet run --project src/CfTunnelManager
```

## 本地数据

保存在 `%LOCALAPPDATA%\CfTunnelManager`：

- `tokens.bin`：用 DPAPI 加密的登录令牌
- `ports.json`：每个主机名固定使用的本地端口

## OAuth 客户端

`ClientId` 是公开客户端的标识，不是密钥。客户端需要：

- Token authentication method：`none`
- Redirect URL：`http://127.0.0.1:8976/callback`
- Scopes：`account-settings.read`、`dns.write`、`zone.read`、`argotunnel.write`

当前是私有客户端，只有所属 Cloudflare 账号的成员能登录。
