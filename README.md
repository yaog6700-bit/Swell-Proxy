<div align="center">

<img src="Assets/output.ico" width="96" height="96" alt="Swell Proxy Logo"/>

# Swell Proxy

**一款基于 sing-box 内核的现代化 Windows 原生代理客户端**

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
- 🛡️ **三种代理模式** — 系统代理 / TUN 虚拟网卡（接管全局流量）/仅手动代理


> **系统要求：** Windows 10 版本 1809 (17763) 或更高，推荐 Windows 11



### 环境依赖
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 或更高（需安装 Windows App SDK / WinUI 3 工作负载）


## 📜 开源协议

本项目基于 [MIT License](LICENSE) 开源。

---

## 🙏 致谢

- [sing-box](https://github.com/SagerNet/sing-box) — 强大的通用代理内核
- [Microsoft WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) — Windows 原生 UI 框架
- [CommunityToolkit.WinUI](https://github.com/CommunityToolkit/Windows) — UI 控件扩展
- [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite) & [sing-geoip](https://github.com/SagerNet/sing-geoip) — 路由规则数据
