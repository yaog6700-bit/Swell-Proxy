# Swell Proxy 插件开发指南

Swell Proxy 2.0 引入了基于 JavaScript (ECMAScript 5.1/部分 ES6) 的轻量级插件系统。通过插件，开发者可以监听和干预核心生命周期、动态修改配置、以及与系统/网络进行交互。

## 1. 插件结构

每个插件主要由两个部分组成，这两部分信息在首次添加插件时会被记录在 `plugins.json` 运行时清单中：

1. **基本元数据**：如 `name`, `version`, `description`, `author` 等。
2. **触发器 (Triggers)**：插件希望监听的应用事件列表，如 `["OnBeforeCoreStart", "OnManual"]` 等。
3. **入口代码**：包含 JavaScript 代码的 `.js` 文件或者一个可拉取远程代码的 URL。
4. **用户配置变量**：允许用户在 UI 面板上输入的变量，供插件通过 `Plugin.GetConfig("Key")` 读取。

## 2. API 对象 (`Plugin` 全局对象)

在您的 JavaScript 脚本执行时，Swell Proxy 已经向全局环境中注入了一个名为 `Plugin` 的对象。您可以通过该对象直接调用 C# 的能力。

### 📌 属性 (Properties)
- `Plugin.Id` `(String)`: 获取当前插件的唯一识别码。
- `Plugin.Name` `(String)`: 获取插件显示名称。
- `Plugin.Version` `(String)`: 获取插件的版本号字符串。

### 📌 日志与通知 (Logging & Notify)
- `Plugin.Log(message)`
  将一条普通信息写入 Swell Proxy 的主控制台日志。
- `Plugin.LogError(message)`
  将一条错误信息（带 ❌ 图标）写入日志面板。
- `Plugin.Notify(title, message)`
  触发一条 Windows 系统的 Toast 右下角弹窗通知。

### 📌 获取用户配置 (Configuration)
- `Plugin.GetConfig(key) -> String`
  读取用户在 UI 插件配置界面填写的自定义变量值。如果变量不存在，则返回空字符串 `""`。

### 📌 网络请求 (HTTP)
> **注意**: 所有的 HTTP 请求均为**同步阻塞**。如果有大文件下载，界面会有对应的等待时间，或者推荐放入独立的异步逻辑/外部调用中。
- `Plugin.HttpGet(url) -> String`
  发起 HTTP GET 请求，返回请求体的内容字符串。
- `Plugin.HttpPost(url, body, contentType = "application/json") -> String`
  发起 HTTP POST 请求，可指定 Body 和 Content-Type。
- `Plugin.DownloadFile(url, destPath)`
  用于直接将文件下载到本地磁盘（避免载入内存），常用于测速文件或订阅拉取。`destPath` 可为绝对路径，或相对于 `LocalAppdata\SwellProxy` 的相对路径。

### 📌 文件系统 (File I/O)
> 相对路径均相对于 `C:\Users\{UserName}\AppData\Local\SwellProxy` 目录。
- `Plugin.ReadFile(path) -> String`
  读取本地文件，返回文件内容。
- `Plugin.WriteFile(path, content)`
  写入字符串到本地文件，如果目录不存在则会自动创建。
- `Plugin.FileExists(path) -> Boolean`
  判断指定的文件是否存在。
- `Plugin.DeleteFile(path)`
  删除指定的文件（如果存在）。


## 3. 生命周期钩子 (Triggers / Hooks)

您的 JavaScript 代码中只需定义与触发器同名的**全局函数**，当触发器被激活时，Swell Proxy 就会自动调用该函数。

### 💡 `OnStartup()`
- **触发时机**：应用启动并完成初始化时触发。
- **用途**：可以用于设置定时任务、初始化插件缓存等。

### 💡 `OnShutdown()`
- **触发时机**：应用准备关闭退出时。
- **用途**：清理临时文件。

### 💡 `OnBeforeCoreStart(configJson) -> String`
- **触发时机**：代理内核 (sing-box) 启动前触发。
- **参数**：`configJson` `(String)` —— 包含将要传递给内核的完整 JSON 配置文件字符串。
- **返回值**：必须返回修改后的 JSON 字符串（通常通过 `JSON.parse` 处理后，再用 `JSON.stringify` 返回）。如果你不想修改，可直接原样返回。
- **用途**：动态修改代理内核配置（如注入特殊的出站规则、修改 DNS 设定等）。

### 💡 `OnCoreStarted()`
- **触发时机**：代理内核成功启动并运行后。
- **用途**：启动网络监控、发送成功通知等。

### 💡 `OnBeforeCoreStop()`
- **触发时机**：代理内核即将被停止或重启前。

### 💡 `OnCoreStopped()`
- **触发时机**：代理内核已经停止。

### 💡 `OnSubscribe(nodesJson, subscriptionName) -> String`
- **触发时机**：每次成功解析订阅链接并生成节点列表后触发。
- **参数**：
  - `nodesJson` `(String)`: 解析出的节点列表 JSON 字符串数组。
  - `subscriptionName` `(String)`: 该订阅的任务名或分组名。
- **返回值**：可返回经过滤、修改后的节点 JSON 字符串。
- **用途**：节点过滤（去重、删减特定国家节点）、重命名节点前缀等。

### 💡 `OnManual()`
- **触发时机**：用户在客户端界面「插件管理」页面中，手动点击了“运行”按钮。
- **用途**：用于执行脚本性质的插件，如网络测速工具、IP 归属地查询等。


## 4. 示例：IP 风险查询插件

这是一个仅使用 `OnManual` 手动触发器的完整脚本示例：

```javascript
function checkIpFraudRisk(ip) {
    var url = "http://ip-api.com/json/" + (ip ? ip : "") + "?fields=query,status,message,country,city,isp,proxy,hosting";
    
    Plugin.Log("正在查询 IP 详情...");
    var response = Plugin.HttpGet(url);
    var body = JSON.parse(response);

    if (body.status !== 'success') {
        Plugin.LogError("查询失败: " + body.message);
    } else {
        var isProxy = body.proxy ? "是" : "否";
        var riskemoji = (body.proxy || body.hosting) ? '🟠' : '🟢';
        var riskText = (body.proxy || body.hosting) ? "代理池或机房" : "正常宽带 IP";

        var message = "🌐 IP: " + body.query + "\n" +
                      "📍 位置: " + body.country + " " + body.city + "\n" +
                      "🏢 运营商: " + body.isp + "\n" +
                      riskemoji + " 风险评估: " + riskText;

        Plugin.Log("查询完成:\n" + message);
        Plugin.Notify("IP 风险查询", message);
    }
}

// 定义钩子函数
function OnManual() {
    try {
        // 读取用户可能配置的 "TargetIP" 参数
        var manualIp = Plugin.GetConfig("TargetIP");
        checkIpFraudRisk(manualIp);
    } catch (e) {
        Plugin.LogError("插件执行异常: " + e.message);
    }
}
```

## 5. 注意事项
1. JavaScript 执行环境由 [Jint](https://github.com/sebastienros/jint) 驱动，支持标准的 ECMAScript 规范，但不支持浏览器专用的 DOM API (如 `window`, `document`)，也不支持 Node.js 专用的 API (如 `require('fs')`)。请务必使用 `Plugin.*` 进行系统交互。
2. 全局环境仅在应用生命周期内保持。
3. `try-catch` 推荐：如果插件逻辑复杂或涉及网络请求，请包裹在 `try-catch` 块中，并使用 `Plugin.LogError(e.message)` 输出错误。未捕获的错误会被主程序拦截并打印警告，但不会导致客户端崩溃。
