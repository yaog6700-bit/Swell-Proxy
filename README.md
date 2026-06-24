<div align="center">

<img src="Assets/output.ico" width="96" height="96" alt="Swell Proxy Logo"/>

# Swell Proxy

**基于 sing-box 内核的 Windows 原生代理客户端**

[![Release](https://img.shields.io/github/v/release/yaog6700-bit/Swell-Proxy?style=flat-square&logo=github&label=最新版本)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/yaog6700-bit/Swell-Proxy/total?style=flat-square&label=总下载量)](https://github.com/yaog6700-bit/Swell-Proxy/releases)
[![Platform](https://img.shields.io/badge/平台-Windows%2010%2B-0078D4?style=flat-square&logo=windows11)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![Core](https://img.shields.io/badge/Core-sing--box-00A98F?style=flat-square)](https://github.com/SagerNet/sing-box)

</div>

---

## 简介

Swell Proxy 是一款使用 **WinUI 3** 构建的 Windows 原生代理管理客户端，底层由 [sing-box](https://github.com/SagerNet/sing-box) 驱动。它面向日常代理、规则分流、订阅管理和网络状态观测场景，提供现代化界面、系统代理 / TUN 模式、连接与流量面板、AI 服务可用性检测，以及基于 JavaScript 的轻量插件系统。

项目目标是让 Windows 上的 sing-box 使用体验更直观：导入节点、选择策略、观察连接、调整路由、更新规则和扩展自动化能力，都可以在一个原生客户端里完成。

## 功能特性

| 功能 | 说明 |
| --- | --- |
| 多协议节点 | 支持 VLESS、VMess、Shadowsocks、Trojan、Hysteria 2、TUIC、WireGuard、SOCKS5、HTTP、NaiveProxy、AnyTLS、Snell 等常见协议 |
| 多种代理模式 | 支持系统代理、TUN 虚拟网卡和仅手动代理，适配不同使用习惯 |
| 路由分流 | 支持规则分流、全局代理、直连模式，并内置中国域名 / 中国 IP / 广告拦截规则集 |
| 订阅与节点管理 | 支持订阅链接、节点分享链接、多行批量导入、延迟测试、节点编辑和协议颜色自定义 |
| 实时观测 | 提供仪表盘、连接列表、流量图表、日志面板和网络拓扑视图 |
| AI 解锁检测 | 内置 OpenAI、Claude、Gemini 等服务的可用性检测能力 |
| Tailscale Endpoint | 可将 sing-box 作为 Tailscale 节点接入私有网络，支持官方 Tailscale 或 Headscale 控制端 |
| 插件系统 | 支持 JavaScript 插件监听启动、订阅更新、内核启动前后、手动运行等生命周期事件 |
| 自动更新 | 支持客户端更新检查，并可拉取最新 sing-box 内核版本 |

## 预览

![Swell Proxy Dashboard](https://cdn.nodeimage.com/i/TU0t63tT5daa13T4xZhKFEMyIZX9BHlY.webp)
![Swell Proxy Servers](https://cdn.nodeimage.com/i/Cd5ZrMyjLNK6vMNrOCfTLdLX7YUKCrmp.webp)
![Swell Proxy Routing](https://cdn.nodeimage.com/i/NoddMyUtzgHRckl5O4XfFLclOCqcDPHO.webp)
![Swell Proxy Traffic](https://cdn.nodeimage.com/i/xIEmWEiUWe7Azbr9Zkpf650aEYDZCIs3.webp)
![Swell Proxy Connections](https://cdn.nodeimage.com/i/MmdmlYUtqjLBYClYTaQY8QxxPpo6jpaG.webp)
![Swell Proxy Settings](https://cdn.nodeimage.com/i/DYvbJyBcC0cQXNniEcH3bxfwnCedKVZu.webp)

## 下载与安装

前往 [Releases 页面](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest) 下载最新版本：

| 架构 | 适用设备 | 下载文件 |
| --- | --- | --- |
| x64 | 主流 Intel / AMD Windows PC | `Swell-win-x64.zip` |
| ARM64 | 骁龙 X / Copilot+ PC 等 ARM64 设备 | `Swell-win-arm64.zip` |

安装步骤：

1. 下载对应架构的压缩包。
2. 解压到任意目录，例如 `D:\Apps\SwellProxy`。
3. 运行 `Swell Proxy.exe`。
4. 如需使用 TUN 模式，请以管理员权限启动，或按系统提示授权。

> 系统要求：Windows 10 版本 1809 (17763) 或更高版本，推荐 Windows 11。

## 快速开始

1. 打开「服务器」页面，粘贴订阅链接或节点分享链接。
2. 在节点列表中选择一个可用节点，可先进行延迟测试。
3. 在主界面选择代理模式：系统代理、TUN 或手动代理。
4. 根据需要切换路由模式：规则分流、全局代理或直连。
5. 打开「连接」「流量」「日志」页面观察实时运行状态。

## 插件系统

Swell Proxy 内置基于 JavaScript 的轻量插件系统，适合做节点整理、订阅过滤、出口检测、连通性巡检、断线通知等自动化任务。

插件可监听这些事件：

| 触发器 | 说明 |
| --- | --- |
| `OnStartup` | 应用启动并完成初始化后触发 |
| `OnShutdown` | 应用准备退出时触发 |
| `OnBeforeCoreStart` | sing-box 内核启动前触发，可修改即将下发的配置 JSON |
| `OnCoreStarted` | sing-box 内核成功启动后触发 |
| `OnBeforeCoreStop` | sing-box 内核即将停止或重启前触发 |
| `OnCoreStopped` | sing-box 内核停止后触发 |
| `OnSubscribe` | 订阅解析完成后触发，可过滤、重命名或排序节点 |
| `OnManual` | 用户在插件页面手动点击运行时触发 |

内置示例插件位于 `Assets/plugins/`，插件 API 说明见 [插件开发指南](Assets/plugins/README.md)。

## 数据目录

用户数据默认存储在：

```text
%LOCALAPPDATA%\SwellProxy\
├─ nodes_config.json              # 节点、订阅列表和部分用户配置
├─ local_settings.json            # 界面偏好、窗口状态等本地设置
├─ singbox_config.json            # 运行时生成的 sing-box 配置，启动时自动覆盖
├─ geosite-cn.srs                 # 中国域名规则集
├─ geoip-cn.srs                   # 中国 IP 规则集
├─ geosite-category-ads-all.srs   # 广告拦截规则集
└─ Updates\                       # 客户端升级临时目录，启动时自动清理
```

## 常见问题

**TUN 模式无法启动怎么办？**

请确认客户端具备管理员权限，并检查系统安全软件或防火墙是否阻止虚拟网卡相关操作。

**系统代理已开启但浏览器没有走代理怎么办？**

可以尝试重启浏览器，或在「设置」中确认本地混合代理端口没有被其他程序占用。

**订阅更新后节点没有变化怎么办？**

请查看「日志」页面是否有订阅解析错误；如果启用了插件，也可以暂时停用订阅类插件后重试。

**规则集下载失败怎么办？**

检查当前网络环境是否可以访问 GitHub raw 资源，也可以在可用网络下重新下载规则集。

## 致谢

- [SagerNet/sing-box](https://github.com/SagerNet/sing-box)：通用代理平台
- [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml)：WinUI 3 原生 UI 框架
- [CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows)：Windows Community Toolkit
- [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite) 和 [SagerNet/sing-geoip](https://github.com/SagerNet/sing-geoip)：路由规则数据
- [PhoenixNil/XrayUI-dev](https://github.com/PhoenixNil/XrayUI-dev)：设计参考
- [dododook/FlowZ](https://github.com/dododook/FlowZ)：设计参考
- [NodePassProject/Anywhere](https://github.com/NodePassProject/Anywhere)：设计参考

## 许可证

开源许可证请以仓库中的许可证文件为准。
