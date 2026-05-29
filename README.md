<div align="center">

<img src="Assets/output.ico" width="96" height="96" alt="Swell Proxy Logo"/>

# Swell Proxy

**一款基于 sing-box 内核的现代化 Windows 原生代理客户端**

[![Release](https://img.shields.io/github/v/release/yaog6700-bit/Swell-Proxy?style=flat-square&logo=github&label=最新版本)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/yaog6700-bit/Swell-Proxy/total?style=flat-square&label=总下载量)](https://github.com/yaog6700-bit/Swell-Proxy/releases)
[![Platform](https://img.shields.io/badge/平台-Windows%2010%2B-0078D4?style=flat-square&logo=windows11)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![License](https://img.shields.io/github/license/yaog6700-bit/Swell-Proxy?style=flat-square&label=协议)](LICENSE)

</div>

---

## ✨ 简介

Swell Proxy 是一款基于 **WinUI 3** 构建的 Windows 原生代理管理客户端，以 [sing-box](https://github.com/SagerNet/sing-box) 作为底层代理引擎。它提供了精心设计的现代化界面，支持 Mica / Acrylic 窗口材质、深色/浅色主题，以及丰富的路由分流和 DNS 配置能力。

---

## 🚀 功能特性

### 🔌 代理核心

| 功能 | 说明 |
|------|------|
| **多协议** | VLESS · VMess · Shadowsocks · Trojan · Hysteria 2 · TUIC · WireGuard · SOCKS5 · HTTP · Naive · AnyTLS · Snell |
| **路由模式** | 规则分流 (Rule) / 全局代理 (Global) / 全部直连 (Direct) |
| **代理模式** | 系统代理 / TUN 虚拟网卡（真全局） / 仅手动代理 |
| **高级传输** | Reality、ShadowTLS、WebSocket、gRPC、XHTTP、H2 等传输层配置 |

### 📋 节点管理

- **订阅** — 支持多订阅链接，一键批量更新，保留收藏与 ID 不变
- **手动节点** — 粘贴分享链接（URI）或手动逐项填写
- **卡片 / 列表** — 双视图自由切换，协议颜色标识一目了然
- **延迟测试** — 批量测速，结果实时上报并按颜色分级展示
- **节点收藏** — 标记常用节点，快速过滤定位
- **代理链** — 节点前置级联（Proxy Chain）
- **QR 码分享** — 生成节点二维码，方便跨设备导入

### 🧩 分流路由

- **绕过中国大陆** — 基于 geosite-cn + geoip-cn 规则集，国内流量自动直连
- **广告拦截** — 内置 geosite-category-ads-all 规则，静默过滤广告
- **高级分流模块** — 按应用策略（Google、Telegram、Netflix、YouTube、TikTok、ChatGPT、Claude）、自定义域名、CIDR、进程名称配置独立出站
- **Geo 一键更新** — 从 SagerNet 上游拉取最新 `.srs` 规则文件

### 🌐 DNS 配置

- **分离 DNS** — 直连出站 / 代理出站使用独立解析器，防止污染
- **FakeDNS** *(实验性)* — TUN 模式下返回虚假 IP，实现真正透明代理
- **DNS 缓存** — 大幅提升二次解析速度
- **IPv6 泄露防护** — 代理环境下屏蔽 AAAA 查询
- **自动刷新 DNS** — 切换节点时调用 Windows API 清除本地缓存

### 🎨 界面与体验

- **WinUI 3 + Fluent Design** — Mica / Acrylic 毛玻璃，跟随系统主题
- **深色 / 浅色 / 跟随系统** — 三档切换，主题过渡带平滑动画
- **迷你悬浮窗** — 一键折叠为小窗，保持桌面整洁
- **系统托盘** — 最小化常驻，右键快速连接 / 断开
- **隐私保护模式** — 启动时密码验证，防止他人查看节点配置
- **开机自启** — 一键注册 / 取消 Windows 开机启动
- **实时流量图表** — Win2D 绘制的上/下行速率动态折线图
- **活动连接** — 实时查看所有代理连接的来源与目标
- **请求日志** — 滚动展示 sing-box 引擎运行日志
- **协议标识色** — 自定义各协议节点在卡片中的颜色标签

### 🔄 更新与备份

- **客户端自更新** — 检查 GitHub Release，下载 + SHA256 校验 + 重启覆盖安装
- **sing-box 内核更新** — 独立检查并热替换内核二进制
- **备份与恢复** — 一键导出/导入全量配置（节点 + 订阅 + 个人设置）为 `.zip` 归档

---

## 📥 下载安装

前往 **[Releases 页面](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)** 下载最新版本：

| 架构 | 适用场景 | 下载文件 |
|------|----------|----------|
| **x64** | 主流 Intel / AMD PC | `Swell-win-x64.zip` |
| **ARM64** | 骁龙 X / Copilot+ PC | `Swell-win-arm64.zip` |

**使用方式（绿色免安装）：**
1. 解压 ZIP 到任意目录
2. 运行 `Swell Proxy.exe`

> **系统要求：** Windows 10 版本 1809 (17763) 或更高，推荐 Windows 11

---

## 🛠️ 自行构建

### 环境依赖

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022+（需安装「Windows 应用程序开发」工作负载）

### 构建命令

```powershell
git clone https://github.com/yaog6700-bit/Swell-Proxy.git
cd Swell-Proxy

# 调试构建
dotnet build AnywhereWinUI.csproj -c Debug

# 发布（单文件自包含）
dotnet publish AnywhereWinUI.csproj -c Release -r win-x64 `
  -p:SelfContained=true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=true `
  -p:WindowsAppSDKSelfContained=true
```

> **注意：** sing-box 可执行文件需单独获取，放置于 `Assets/sing-box.exe`，可在 [sing-box Releases](https://github.com/SagerNet/sing-box/releases) 下载。

---

## 📁 数据目录

所有用户数据存储于：

```
%LOCALAPPDATA%\SwellProxy\
├── nodes_config.json              # 节点、订阅列表及个人设置
├── local_settings.json            # 界面偏好（主题、窗口状态等）
├── singbox_config.json            # 运行时生成的 sing-box 配置（自动覆盖）
├── geosite-cn.srs                 # 路由规则集 — 中国域名
├── geoip-cn.srs                   # 路由规则集 — 中国 IP
├── geosite-category-ads-all.srs   # 广告拦截规则集
└── Updates\                       # 客户端升级暂存（启动时自动清理）
```

---

## 📜 开源协议

本项目基于 [MIT License](LICENSE) 开源，欢迎 PR 和 Issue。

---

## 🙏 致谢

- [SagerNet/sing-box](https://github.com/SagerNet/sing-box) — 强大的通用代理内核
- [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml) — WinUI 3 原生 UI 框架
- [CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows) — WinUI 控件扩展库
- [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite) & [sing-geoip](https://github.com/SagerNet/sing-geoip) — 路由规则数据集
- [PhoenixNil/XrayUI-dev](https://github.com/PhoenixNil/XrayUI-dev) — 设计参考
- [dododook/FlowZ](https://github.com/dododook/FlowZ) — 设计参考
