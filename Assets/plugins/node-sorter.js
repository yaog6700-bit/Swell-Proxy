/**
 * Swell Proxy 插件：节点排序
 * 触发器: OnSubscribe (订阅更新时自动触发) + OnManual (手动运行预览)
 *
 * 功能:
 *   - 按照指定的规则对节点列表进行排序
 *   - 默认按照 "地区 → 协议 → 名称" 进行排序
 */

// ─── 地区权重映射 ───────────────────────────────────────────────────────────
// 权重越小越靠前，未匹配的节点默认权重为 999
var REGION_WEIGHTS = {
    // 常用亚洲节点优先
    "香港": 10, "HK": 10,
    "台湾": 20, "TW": 20,
    "日本": 30, "JP": 30,
    "新加坡": 40, "SG": 40,
    "韩国": 50, "KR": 50,
    
    // 北美其次
    "美国": 100, "US": 100,
    "加拿大": 110, "CA": 110,

    // 其他地区
    "英国": 200, "UK": 200, "GB": 200,
    "德国": 210, "DE": 210,
    "法国": 220, "FR": 220,
    "澳大利亚": 300, "AU": 300,
    "俄罗斯": 400, "RU": 400
};

// ─── 协议权重映射 ───────────────────────────────────────────────────────────
var PROTOCOL_WEIGHTS = {
    "Shadowsocks": 10,
    "VMess": 20,
    "VLESS": 30,
    "Trojan": 40,
    "Hysteria2": 50,
    "TUIC": 60,
    "WireGuard": 70,
    "SOCKS": 80,
    "HTTP": 90,
    "AnyTLS": 100
};

/**
 * 获取字符串的地区权重
 */
function getRegionWeight(name) {
    if (!name) return 999;
    var upperName = name.toUpperCase();
    
    // 遍历键值对匹配
    for (var key in REGION_WEIGHTS) {
        if (REGION_WEIGHTS.hasOwnProperty(key)) {
            if (upperName.indexOf(key.toUpperCase()) !== -1) {
                return REGION_WEIGHTS[key];
            }
        }
    }
    return 999; // 未匹配
}

/**
 * 获取协议权重
 */
function getProtocolWeight(protocol) {
    if (!protocol) return 999;
    
    // 不区分大小写匹配
    var searchProto = protocol.toLowerCase();
    for (var key in PROTOCOL_WEIGHTS) {
        if (PROTOCOL_WEIGHTS.hasOwnProperty(key)) {
            if (key.toLowerCase() === searchProto) {
                return PROTOCOL_WEIGHTS[key];
            }
        }
    }
    return 999; // 未匹配
}

/**
 * 节点比较函数
 */
function compareNodes(a, b, sortBy) {
    var nameA = a.Name || "";
    var nameB = b.Name || "";
    
    if (sortBy === "region") {
        // 先按地区排，地区相同按协议，协议相同按名称
        var rwA = getRegionWeight(nameA);
        var rwB = getRegionWeight(nameB);
        if (rwA !== rwB) return rwA - rwB;
        
        var pwA = getProtocolWeight(a.Protocol);
        var pwB = getProtocolWeight(b.Protocol);
        if (pwA !== pwB) return pwA - pwB;
        
        return nameA.localeCompare(nameB);
    } 
    else if (sortBy === "protocol") {
        // 先按协议排，协议相同按地区，地区相同按名称
        var pwA2 = getProtocolWeight(a.Protocol);
        var pwB2 = getProtocolWeight(b.Protocol);
        if (pwA2 !== pwB2) return pwA2 - pwB2;
        
        var rwA2 = getRegionWeight(nameA);
        var rwB2 = getRegionWeight(nameB);
        if (rwA2 !== rwB2) return rwA2 - rwB2;
        
        return nameA.localeCompare(nameB);
    }
    else {
        // 默认按名称排序 (A-Z)
        return nameA.localeCompare(nameB);
    }
}

/**
 * 核心排序逻辑
 */
function sortNodes(nodesJson, subscriptionName) {
    var nodes = JSON.parse(nodesJson);
    var originalLength = nodes.length;
    
    // 读取用户配置
    // SortBy: region, protocol, name
    var sortBy = Plugin.GetConfig("SortBy") || "region";
    sortBy = sortBy.toLowerCase();
    
    // SortOrder: asc, desc
    var sortOrder = Plugin.GetConfig("SortOrder") || "asc";
    sortOrder = sortOrder.toLowerCase();

    // 复制数组并排序
    var sortedNodes = nodes.slice().sort(function(a, b) {
        return compareNodes(a, b, sortBy);
    });

    // 如果是降序，反转数组
    if (sortOrder === "desc") {
        sortedNodes.reverse();
    }

    Plugin.Log("订阅 [" + (subscriptionName || "手动") + "] 排序完成: 共 " + sortedNodes.length + " 个节点 (模式: " + sortBy + " " + sortOrder + ")");

    return JSON.stringify(sortedNodes);
}

// ─── OnSubscribe ────────────────────────────────────────────────────────────
function OnSubscribe(nodesJson, subscriptionName) {
    try {
        return sortNodes(nodesJson, subscriptionName);
    } catch (e) {
        Plugin.LogError("节点排序失败: " + e.message);
        return nodesJson; // 失败时返回原数据
    }
}

// ─── OnManual ───────────────────────────────────────────────────────────────
function OnManual() {
    try {
        if (!Plugin.FileExists("nodes_config.json")) {
            Plugin.Log("未找到节点配置文件");
            Plugin.Notify("节点排序", "❌ 未找到节点配置文件");
            return;
        }

        var raw = Plugin.ReadFile("nodes_config.json");
        var config = JSON.parse(raw);
        var nodes = config.Nodes || [];
        
        if (nodes.length === 0) {
            Plugin.Log("节点列表为空");
            return;
        }

        // 读取用户配置
        var sortBy = Plugin.GetConfig("SortBy") || "region";
        var sortOrder = Plugin.GetConfig("SortOrder") || "asc";

        // 提取前几个节点名称展示排序前状态
        var beforeNames = nodes.slice(0, Math.min(5, nodes.length)).map(function(n) { return n.Name; });

        // 排序
        var sortedNodes = nodes.slice().sort(function(a, b) {
            return compareNodes(a, b, sortBy.toLowerCase());
        });
        if (sortOrder.toLowerCase() === "desc") {
            sortedNodes.reverse();
        }

        // 提取前几个节点名称展示排序后状态
        var afterNames = sortedNodes.slice(0, Math.min(5, sortedNodes.length)).map(function(n) { return n.Name; });

        var report = "📋 排序预览模式 (模式: " + sortBy + " " + sortOrder + ")\n\n" +
            "【排序前 Top 5】\n" + beforeNames.join("\n") + "\n\n" +
            "【排序后 Top 5】\n" + afterNames.join("\n");

        Plugin.Log(report);
        Plugin.Notify("节点排序预览", "共处理 " + nodes.length + " 个节点，详见日志");

    } catch (e) {
        Plugin.LogError("排序预览失败: " + e.message);
    }
}
