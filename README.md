<div align="center">

<img src="Assets/output.ico" width="96" height="96" alt="Swell Proxy Logo"/>


# Swell Proxy

**基于 sing-box 内核Windows原生代理客户端**

[![Release](https://img.shields.io/github/v/release/yaog6700-bit/Swell-Proxy?style=flat-square&logo=github&label=最新版本)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/yaog6700-bit/Swell-Proxy/total?style=flat-square&label=总下载量)](https://github.com/yaog6700-bit/Swell-Proxy/releases)
[![Platform](https://img.shields.io/badge/平台-Windows%2010%2B-0078D4?style=flat-square&logo=windows11)](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)
[![License](https://img.shields.io/github/license/yaog6700-bit/Swell-Proxy?style=flat-square&label=协议)](LICENSE)

</div>

---

##  简介

Swell Proxy 是基于 **WinUI 3** 构建的 Windows 原生代理管理客户端，系统内存占用低，以 [sing-box](https://github.com/SagerNet/sing-box) 作为底层代理引擎。提供了精心设计的现代化界面，支持 Mica / Acrylic 窗口材质、以及丰富的路由分流、便捷使用链式代理和DNS 配置能力。

---

##  功能特性


| 功能 | 说明 |
|------|------|
| **多协议** | VLESS · VMess · Shadowsocks · Trojan · Hysteria 2 · TUIC · WireGuard · SOCKS5 · HTTP · Naive · AnyTLS · Snell ·Tailscale|
| **路由模式** | 规则分流 (Rule) / 全局代理 (Global) / 直连 (Direct) |
| **代理模式** | 系统代理 / TUN 虚拟网卡/ 仅手动代理 |
| **插件功能** | 

---

##  预览 
<img src="https://cdn.nodeimage.com/i/TU0t63tT5daa13T4xZhKFEMyIZX9BHlY.webp" alt="TU0t63tT5daa13T4xZhKFEMyIZX9BHlY.webp">
![Cd5ZrMyjLNK6vMNrOCfTLdLX7YUKCrmp.webp](https://cdn.nodeimage.com/i/Cd5ZrMyjLNK6vMNrOCfTLdLX7YUKCrmp.webp)
![NoddMyUtzgHRckl5O4XfFLclOCqcDPHO.webp](https://cdn.nodeimage.com/i/NoddMyUtzgHRckl5O4XfFLclOCqcDPHO.webp)
![xIEmWEiUWe7Azbr9Zkpf650aEYDZCIs3.webp](https://cdn.nodeimage.com/i/xIEmWEiUWe7Azbr9Zkpf650aEYDZCIs3.webp)
![MmdmlYUtqjLBYClYTaQY8QxxPpo6jpaG.webp](https://cdn.nodeimage.com/i/MmdmlYUtqjLBYClYTaQY8QxxPpo6jpaG.webp)
![DYvbJyBcC0cQXNniEcH3bxfwnCedKVZu.webp](https://cdn.nodeimage.com/i/DYvbJyBcC0cQXNniEcH3bxfwnCedKVZu.webp)







##  下载安装

前往 **[Releases 页面](https://github.com/yaog6700-bit/Swell-Proxy/releases/latest)** 下载最新版本：

| 架构 | 适用场景 | 下载文件 |
|------|----------|----------|
| **x64** | 主流 Intel / AMD PC | `Swell-win-x64.zip` |
| **ARM64** | 骁龙 X / Copilot+ PC | `Swell-win-arm64.zip` |


> **系统要求：** Windows 10 版本 1809 (17763) 或更高，推荐 Windows 11



##  数据目录

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

##  开源协议

基于 [MIT License](LICENSE) 开源，欢迎 PR 和 Issues。

---

## 🙏 致谢

- [SagerNet/sing-box](https://github.com/SagerNet/sing-box) — 强大的通用代理内核
- [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml) — WinUI 3 原生 UI 框架
- [CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows) — WinUI 控件扩展库
- [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite) & [sing-geoip](https://github.com/SagerNet/sing-geoip) — 路由规则数据集
- [PhoenixNil/XrayUI-dev](https://github.com/PhoenixNil/XrayUI-dev) — 设计参考
- [dododook/FlowZ](https://github.com/dododook/FlowZ) — 设计参考
- [Anywhere](https://github.com/NodePassProject/Anywhere)— 设计参考
- [google/antigravity](https://antigravity.google/)— 本人不懂代码开发，真正开发者属于antigravity，谢谢倾力相助，实现程序梦。
