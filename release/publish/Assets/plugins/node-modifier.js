/**
 * Swell Proxy 插件：节点去重与序号格式化
 * 触发器: OnSubscribe (在每次订阅更新时自动触发)
 */

function OnSubscribe(nodesJson, subscriptionName) {
    Plugin.Log("正在处理订阅: " + subscriptionName);

    var nodes = JSON.parse(nodesJson);
    var originalCount = nodes.length;
    
    // 读取用户配置
    // 是否开启去重 (填 "true" 开启)
    var enableDedup = Plugin.GetConfig("Deduplicate") === "true";
    // 是否添加序号 (填 "true" 开启)
    var enableOrdinal = Plugin.GetConfig("ShowOrdinal") === "true";

    if (!enableDedup && !enableOrdinal) {
        Plugin.Log("去重和序号功能均未开启，跳过处理");
        return nodesJson;
    }

    var resultNodes = nodes;

    // ─── 1. 节点去重 (按 服务器地址:端口) ───
    if (enableDedup) {
        var uniqueKeys = {};
        resultNodes = [];
        var dropCount = 0;

        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];
            var key = node.Address + ":" + node.Port;
            
            if (!uniqueKeys[key]) {
                uniqueKeys[key] = true;
                resultNodes.push(node);
            } else {
                dropCount++;
            }
        }
        Plugin.Log("去重完毕: 移除了 " + dropCount + " 个重复节点");
    }

    // ─── 2. 名称添加序号 ───
    if (enableOrdinal) {
        var totalCount = resultNodes.length;
        // 计算序号需要的总位数，例如 100 个节点就是 3 位 (001, 002...)
        var width = String(totalCount).length;

        for (var j = 0; j < resultNodes.length; j++) {
            var seq = String(j + 1);
            // 补齐前导 0
            while (seq.length < width) {
                seq = "0" + seq;
            }
            // 修改节点名称
            resultNodes[j].Name = resultNodes[j].Name + " - " + seq;
        }
        Plugin.Log("序号格式化完毕");
    }

    Plugin.Log("订阅 [" + subscriptionName + "] 处理完成，最终节点数: " + resultNodes.length + " (原始: " + originalCount + ")");
    return JSON.stringify(resultNodes);
}
