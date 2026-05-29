<div align="center">

<img src="Assets/output.ico" width="96" height="96" alt="Swell Proxy Logo"/>

# Swell Proxy

**一个基于 sing-box 内核的现代化 Windows 原生代理客户端**

[![Release](https://img.shields.io/github/v/release/yaog6700-bit/Swell-Proxy?style=flat-square&logo=github)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/yaog6700-bit/Swell-Proxy/total?style=flat-square)](https://github.com/yaog6700-bit/Swell-Proxy/releases)
[![License](https://img.shields.io/github/license/yaog6700-bit/Swell-Proxy?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-blue?style=flat-square&logo=windows)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)

</div>

---

## ✨ 简介

Swell Proxy 是一款基于 **WinUI 3** 构建的 Windows 原生代理管理客户端，以 [sing-box](https://github.com/SagerNet/sing-box) 作为底层代理引擎。它提供了精心设计的现代化界面，支持 Mica / Acrylic 窗口材质、深色/浅色主题，以及丰富的路由分流和 DNS 配置能力。

---

## 🚀 功能特性

### 代理核心
- 🔌 **多协议支持** — VLESS、VMess、Shadowsocks、Trojan、Hysteria 2、TUIC、WireGuard、SOCKS5、HTTP、Naive、AnyTLS、Snell
- 🌐 **三种路由模式** — 规则分流 (Rule) / 全局代理 (Global) / 全部直连 (Direct)
- 🛡️ **两种代理模式** — 系统代理 / TUN 虚拟网卡（接管全局流量）
- 📡 **Reality / ShadowTLS** 支持，完整配置传输层参数

### 节点管理
- 📋 **订阅管理** — 支持添加多个订阅链接，一键批量更新
- 🔗 **手动添加节点** — 支持分享链接解析（URI）和手动填写
- 🃏 **卡片 / 列表双视图** — 自由切换节点显示模式
- ⭐ **节点收藏** — 快速定位常用节点
- 🔍 **实时延迟测试** — 一键批量测速，颜色标注优劣
- 🔗 **代理链 (Proxy Chain)** — 支持节点前置级联

### 分流路由
- 🇨🇳 **绕过中国大陆** — 自动识别国内 IP/域名走直连
- 🚫 **广告拦截** — 内置 geosite 广告规则集过滤
- 🧩 **高级分流模块** — 按应用（Google、Telegram、Netflix、YouTube、TikTok、ChatGPT、Claude）、域名、CIDR、进程名称自定义路由规则
- 🔄 **Geo 数据一键更新** — 从上游同步最新的 `geosite-cn.srs` / `geoip-cn.srs` 规则

### DNS 配置
- 🔀 **分离 DNS** — 直连 / 代理出站各自使用独立的 DNS 服务器
- 🧪 **FakeDNS** — TUN 模式下返回虚假 IP 实现真正的透明代理
- 💾 **DNS 缓存** — 大幅提升二次解析速度
- 🛡️ **IPv6 泄露防护** — 屏蔽 AAAA 查询防止地理信息泄露
- 🔃 **自动刷新系统 DNS** — 切换节点时清除 Windows DNS 缓存

### 界面与体验
- 🎨 **WinUI 3 原生界面** — Fluent Design，Mica / Acrylic 毛玻璃背景
- 🌙 **深色 / 浅色 / 跟随系统** 三档主题切换（平滑动画过渡）
- 🪟 **迷你模式** — 折叠为小悬浮窗，节省桌面空间
- 🔒 **隐私保护模式** — 启动时密码验证，防止他人查看配置
- 🏁 **开机自启** — 一键设置 / 取消 Windows 开机自动启动
- 🔔 **系统托盘** — 最小化到托盘，右键快速操作
- 📊 **实时流量图表** — Win2D 绘制的上/下行速率动态折线图
- 🔗 **活动连接** — 查看当前所有代理连接详情
- 📝 **请求日志** — 实时滚动显示 sing-box 引擎日志
- 🎨 **协议标识色** — 自定义各协议节点在卡片中的颜色标签

### 自动更新
- 🔄 **客户端自更新** — 检查 GitHub Release，下载、SHA256 校验、自动重启覆盖安装
- 🔄 **sing-box 内核更新** — 独立检查并替换 sing-box 二进制文件
- 💾 **备份与恢复** — 一键导出/导入全量配置（节点 + 订阅 + 设置）

---

## 📥 下载安装

前往 [Releases 页面](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest) 下载最新版本：

| 架构 | 文件 |
|------|------|
| x64（主流 PC）| `Swell-win-x64.zip` |
| ARM64（骁龙 / Copilot+ PC）| `Swell-win-arm64.zip` |

**安装方式（绿色免安装）：**
1. 解压 ZIP 到任意目录
2. 运行 `Swell Proxy.exe`

> **系统要求：** Windows 10 版本 1809 (17763) 或更高，推荐 Windows 11

---

## 🛠️ 自行构建

### 环境依赖
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 或更高（需安装 Windows App SDK / WinUI 3 工作负载）

### 构建步骤

```bash
git clone https://github.com/yaog6700-bit/Swell-Proxy.git
cd Swell-Proxy

# 调试运行
dotnet build AnywhereWinUI.csproj -c Debug

# 发布（单文件可执行）
dotnet publish AnywhereWinUI.csproj -c Release -r win-x64 \
  -p:SelfContained=true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:WindowsAppSDKSelfContained=true
```

> sing-box 二进制文件需另行获取并放置于 `Assets/sing-box.exe`，可在 [sing-box Releases](https://github.com/SagerNet/sing-box/releases) 下载。

---

## 📁 数据目录

应用数据存储在：

```
%LOCALAPPDATA%\SwellProxy\
├── nodes_config.json      # 节点、订阅、设置
├── local_settings.json    # 本地界面偏好
├── singbox_config.json    # 运行时生成的 sing-box 配置
├── geosite-cn.srs         # 路由规则集（中国域名）
├── geoip-cn.srs           # 路由规则集（中国 IP）
└── Updates\               # 客户端升级暂存（自动清理）
```

---

## 📜 开源协议

本项目基于 [MIT License](LICENSE) 开源。

---

## 🙏 致谢

- [sing-box](https://github.com/SagerNet/sing-box) — 强大的通用代理内核
- [Microsoft WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) — Windows 原生 UI 框架
- [CommunityToolkit.WinUI](https://github.com/CommunityToolkit/Windows) — UI 控件扩展
- [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite) & [sing-geoip](https://github.com/SagerNet/sing-geoip) — 路由规则数据
