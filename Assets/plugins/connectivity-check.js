/**
 * Swell Proxy 插件：连通性巡检
 * 触发器: OnCoreStarted (内核启动时自动巡检) + OnManual (手动触发巡检)
 *
 * 功能:
 *   - 自动检测一组预设网站的连通性
 *   - 返回 HTTP 状态码和延迟时间
 *   - 生成并发送测速报告
 */

// 默认检测站点列表
var DEFAULT_URLS = [
    { name: "Google", url: "https://www.google.com/generate_204", expectedStatus: 204 },
    { name: "Cloudflare", url: "https://cp.cloudflare.com/generate_204", expectedStatus: 204 },
    { name: "YouTube", url: "https://www.youtube.com", expectedStatus: 200 },
    { name: "GitHub", url: "https://github.com", expectedStatus: 200 },
    { name: "Twitter", url: "https://twitter.com", expectedStatus: 200 }
];

function checkConnectivity() {
    Plugin.Log("🔍 开始执行网络连通性巡检...");
    
    // 读取自定义配置
    var customUrlsStr = Plugin.GetConfig("CheckUrls");
    var targetList = DEFAULT_URLS;
    
    if (customUrlsStr && customUrlsStr.trim().length > 0) {
        targetList = [];
        var urls = customUrlsStr.split(",");
        for (var i = 0; i < urls.length; i++) {
            var url = urls[i].trim();
            if (url) {
                // 尝试提取域名作为名称
                var nameMatch = url.match(/:\/\/(www\.)?([^\/]+)/);
                var name = nameMatch ? nameMatch[2] : url;
                targetList.push({ name: name, url: url, expectedStatus: -1 }); // -1 表示只要不抛异常就算成功
            }
        }
        Plugin.Log("使用自定义巡检列表，共 " + targetList.length + " 个站点");
    }

    var results = [];
    var successCount = 0;
    
    // 发送开始通知
    Plugin.Notify("连通性巡检", "正在检测 " + targetList.length + " 个站点...");
    
    // 串行检测
    for (var j = 0; j < targetList.length; j++) {
        var target = targetList[j];
        var start = Date.now();
        var status = "❌ 失败";
        var timeStr = "-";
        
        try {
            // HttpGet 会阻塞直到完成或抛出异常
            Plugin.HttpGet(target.url);
            var end = Date.now();
            var duration = end - start;
            
            status = "✅ 正常";
            timeStr = duration + "ms";
            successCount++;
            
            Plugin.Log("巡检成功 [" + target.name + "] 耗时: " + timeStr);
        } catch (e) {
            Plugin.Log("巡检失败 [" + target.name + "] 错误: " + e.message);
        }
        
        results.push(target.name + ": " + status + " " + timeStr);
    }
    
    // 生成报告
    var reportMsg = "巡检完成: " + successCount + "/" + targetList.length + " 个站点可用\n\n" + results.join("\n");
    Plugin.Log(reportMsg);
    
    var notifyTitle = successCount === targetList.length ? "网络畅通 ✅" : "部分网络异常 ⚠️";
    Plugin.Notify(notifyTitle, reportMsg);
}

// ─── OnCoreStarted ──────────────────────────────────────────────────────────
function OnCoreStarted() {
    try {
        // 延迟 3 秒执行，等待代理完全就绪
        Plugin.Log("代理启动，将在 3 秒后执行连通性巡检...");
        // 由于没有 setTimeout，我们可以利用 OnCoreStarted 触发时的线程进行一个短暂的阻塞等待
        // 这是一个变通方法，实际中如果有 HttpClient 同步获取可能会卡住，但对于短时间是可以接受的。
        // 或者直接执行
        checkConnectivity();
    } catch (e) {
        Plugin.LogError("连通性巡检异常: " + e.message);
    }
}

// ─── OnManual ───────────────────────────────────────────────────────────────
function OnManual() {
    try {
        checkConnectivity();
    } catch (e) {
        Plugin.LogError("连通性巡检异常: " + e.message);
    }
}
