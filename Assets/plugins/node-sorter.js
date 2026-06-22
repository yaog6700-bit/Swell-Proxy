/**
 * Swell Proxy 插件：节点排序
 * 触发器: OnSubscribe (订阅更新时自动触发) + OnManual (手动运行预览)
 *
 * 功能:
 *   - 按照指定的规则对节点列表进行排序
 *   - 默认按照 "地区 → 协议 → 名称" 进行排序
 *
 * 用户配置变量:
 *   SortBy    - 排序维度: region (默认) | protocol | name
 *   SortOrder - 排序方向: asc (默认)   | desc
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
 * 获取字符串的地区权重。
 * 修复：遍历所有匹配项，取最小权重，避免 for...in 遍历顺序不稳定
 * 导致同一节点名包含多个地区关键词时结果不确定的问题。
 */
function getRegionWeight(name) {
    if (!name) return 999;
    var upperName = name.toUpperCase();
    var minWeight = 999;

    for (var key in REGION_WEIGHTS) {
        if (REGION_WEIGHTS.hasOwnProperty(key)) {
            if (upperName.indexOf(key.toUpperCase()) !== -1) {
                if (REGION_WEIGHTS[key] < minWeight) {
                    minWeight = REGION_WEIGHTS[key];
                }
            }
        }
    }
    return minWeight;
}

/**
 * 获取协议权重（不区分大小写）
 */
function getProtocolWeight(protocol) {
    if (!protocol) return 999;
    var searchProto = protocol.toLowerCase();

    for (var key in PROTOCOL_WEIGHTS) {
        if (PROTOCOL_WEIGHTS.hasOwnProperty(key)) {
            if (key.toLowerCase() === searchProto) {
                return PROTOCOL_WEIGHTS[key];
            }
        }
    }
    return 999;
}

/**
 * 稳定的字符串比较函数。
 * 修复：不使用 localeCompare（Jint 环境下行为不稳定），
 * 改用标准字典序比较，对中文节点名也能正常工作。
 */
function compareStrings(a, b) {
    if (a < b) return -1;
    if (a > b) return 1;
    return 0;
}

/**
 * 按地区维度比较两个节点：地区 → 协议 → 名称
 */
function compareByRegion(a, b) {
    var nameA = a.Name || "";
    var nameB = b.Name || "";

    var rwA = getRegionWeight(nameA);
    var rwB = getRegionWeight(nameB);
    if (rwA !== rwB) return rwA - rwB;

    var pwA = getProtocolWeight(a.Protocol);
    var pwB = getProtocolWeight(b.Protocol);
    if (pwA !== pwB) return pwA - pwB;

    return compareStrings(nameA, nameB);
}

/**
 * 按协议维度比较两个节点：协议 → 地区 → 名称
 */
function compareByProtocol(a, b) {
    var nameA = a.Name || "";
    var nameB = b.Name || "";

    var pwA = getProtocolWeight(a.Protocol);
    var pwB = getProtocolWeight(b.Protocol);
    if (pwA !== pwB) return pwA - pwB;

    var rwA = getRegionWeight(nameA);
    var rwB = getRegionWeight(nameB);
    if (rwA !== rwB) return rwA - rwB;

    return compareStrings(nameA, nameB);
}

/**
 * 节点比较函数（入口）
 * 修复：将各维度逻辑拆分为独立函数，避免同一作用域内变量命名混乱。
 */
function compareNodes(a, b, sortBy) {
    if (sortBy === "region") {
        return compareByRegion(a, b);
    } else if (sortBy === "protocol") {
        return compareByProtocol(a, b);
    } else {
        // 默认按名称排序 (A-Z)
        return compareStrings(a.Name || "", b.Name || "");
    }
}

/**
 * 核心排序逻辑
 * 修复：移除从未使用的 originalLength 变量。
 */
function sortNodes(nodesJson, subscriptionName) {
    var nodes = JSON.parse(nodesJson);

    // 读取用户配置
    // SortBy: region | protocol | name
    var sortBy = (Plugin.GetConfig("SortBy") || "region").toLowerCase();

    // SortOrder: asc | desc
    var sortOrder = (Plugin.GetConfig("SortOrder") || "asc").toLowerCase();

    // 排序
    var sortedNodes = nodes.slice().sort(function(a, b) {
        return compareNodes(a, b, sortBy);
    });

    // 如果是降序，反转数组
    if (sortOrder === "desc") {
        sortedNodes.reverse();
    }

    Plugin.Log("订阅 [" + (subscriptionName || "未知") + "] 排序完成: 共 " + sortedNodes.length + " 个节点 (模式: " + sortBy + " " + sortOrder + ")");

    return JSON.stringify(sortedNodes);
}

// ─── OnSubscribe ────────────────────────────────────────────────────────────
function OnSubscribe(nodesJson, subscriptionName) {
    try {
        var sortedJson = sortNodes(nodesJson, subscriptionName);
        // 写入缓存，供 OnManual 预览使用
        _writeSortedCache(sortedJson);
        return sortedJson;
    } catch (e) {
        Plugin.LogError("节点排序失败: " + e.message);
        return nodesJson; // 失败时原样返回，不丢弃数据
    }
}

// ─── OnManual ───────────────────────────────────────────────────────────────
/**
 * 修复：原实现依赖一个从未被应用写入的 nodes_config.json 文件，
 * 导致手动触发时永远报错。
 * 现改为读取上次订阅排序后由插件自身缓存的节点快照（sorted_nodes_cache.json），
 * 若缓存不存在则给出清晰提示，引导用户先更新订阅。
 */
function OnManual() {
    try {
        var cacheFile = "plugins/node-sorter-cache.json";

        if (!Plugin.FileExists(cacheFile)) {
            var hint = "尚无节点缓存，请先在订阅页面点击「更新订阅」以触发排序，之后再运行本预览。";
            Plugin.Log(hint);
            Plugin.Notify("节点排序预览", "⚠️ " + hint);
            return;
        }

        var raw = Plugin.ReadFile(cacheFile);
        var nodes = JSON.parse(raw);

        if (!nodes || nodes.length === 0) {
            Plugin.Log("缓存节点列表为空");
            Plugin.Notify("节点排序预览", "⚠️ 缓存节点列表为空");
            return;
        }

        // 读取用户配置
        var sortBy    = (Plugin.GetConfig("SortBy")    || "region").toLowerCase();
        var sortOrder = (Plugin.GetConfig("SortOrder") || "asc").toLowerCase();

        // 排序前 Top5
        var beforeNames = nodes.slice(0, Math.min(5, nodes.length)).map(function(n) { return n.Name || "(无名)"; });

        // 排序
        var sortedNodes = nodes.slice().sort(function(a, b) {
            return compareNodes(a, b, sortBy);
        });
        if (sortOrder === "desc") {
            sortedNodes.reverse();
        }

        // 排序后 Top5
        var afterNames = sortedNodes.slice(0, Math.min(5, sortedNodes.length)).map(function(n) { return n.Name || "(无名)"; });

        var report = "📋 排序预览 (模式: " + sortBy + " " + sortOrder + ")\n\n" +
            "【排序前 Top 5】\n" + beforeNames.join("\n") + "\n\n" +
            "【排序后 Top 5】\n" + afterNames.join("\n");

        Plugin.Log(report);
        Plugin.Notify("节点排序预览", "共 " + nodes.length + " 个节点，详见日志");

    } catch (e) {
        Plugin.LogError("排序预览失败: " + e.message);
    }
}

// ─── OnSubscribe 缓存写入（供 OnManual 预览使用）────────────────────────────
// 注意：此函数在 OnSubscribe 成功后自动调用，将排序后的节点写入缓存文件。
function _writeSortedCache(sortedJson) {
    try {
        Plugin.WriteFile("plugins/node-sorter-cache.json", sortedJson);
    } catch (e) {
        // 缓存写入失败不影响主流程，仅记录警告
        Plugin.Log("警告: 节点缓存写入失败: " + e.message);
    }
}
