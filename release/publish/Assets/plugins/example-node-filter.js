/**
 * 示例插件：节点延迟过滤器
 * 
 * 触发器: OnSubscribe
 * 功能: 订阅更新后，过滤掉名称包含特定关键词的节点
 *
 * API 说明:
 *   Plugin.Log(msg)              - 写入日志
 *   Plugin.LogError(msg)         - 写入错误日志  
 *   Plugin.GetConfig(key)        - 读取插件配置值
 *   Plugin.HttpGet(url)          - 发起 HTTP GET 请求
 *   Plugin.HttpPost(url, body)   - 发起 HTTP POST 请求
 *   Plugin.ReadFile(path)        - 读取文件（相对于 SwellProxy 目录）
 *   Plugin.WriteFile(path, text) - 写入文件
 *   Plugin.Notify(title, body)   - 发送系统通知
 */

// ─── OnSubscribe ────────────────────────────────────────────────────────────
// 在订阅节点列表更新后触发
// 参数:
//   nodesJson      - 节点数组的 JSON 字符串 (PersistedNode[])
//   subscriptionName - 订阅名称
// 返回: 过滤/修改后的节点数组 JSON 字符串（或 null 保持不变）
function OnSubscribe(nodesJson, subscriptionName) {
    Plugin.Log("订阅「" + subscriptionName + "」已触发过滤器");

    var nodes = JSON.parse(nodesJson);
    Plugin.Log("过滤前节点数量: " + nodes.length);

    // 读取配置：过滤关键词（逗号分隔）
    var keywords = Plugin.GetConfig("FilterKeywords");
    if (!keywords) {
        Plugin.Log("未配置过滤关键词，跳过过滤");
        return nodesJson;
    }

    var kwList = keywords.split(",").map(function(k) { return k.trim().toLowerCase(); });

    var filtered = nodes.filter(function(node) {
        var name = (node.Name || "").toLowerCase();
        for (var i = 0; i < kwList.length; i++) {
            if (kwList[i] && name.indexOf(kwList[i]) !== -1) {
                Plugin.Log("已过滤节点: " + node.Name + " (匹配关键词: " + kwList[i] + ")");
                return false;
            }
        }
        return true;
    });

    Plugin.Log("过滤后节点数量: " + filtered.length);
    return JSON.stringify(filtered);
}

// ─── OnManual ───────────────────────────────────────────────────────────────
// 手动触发（点击插件页面中的运行按钮）
function OnManual() {
    Plugin.Log("插件示例 - 手动触发成功");
    Plugin.Notify("Swell Proxy 插件", "节点过滤器插件已就绪 ✅");
}

// ─── OnCoreStarted ──────────────────────────────────────────────────────────
// sing-box 内核启动后触发
function OnCoreStarted() {
    Plugin.Log("代理核心已启动，节点过滤器插件就绪");
}
