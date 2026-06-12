/**
 * Swell Proxy 插件：节点按地区分组重命名
 * 触发器: OnSubscribe (订阅更新时自动触发) + OnManual (手动运行)
 *
 * 功能:
 *   - 根据节点名称中的地区关键字，自动添加国旗 emoji 前缀
 *   - 标准化节点命名格式
 *   - 支持自定义前缀格式
 */

// ─── 地区关键字 → 国旗 emoji 映射表 ─────────────────────────────────────────
var REGION_MAP = [
    // 亚洲
    { keys: ["香港", "HK", "Hong Kong", "HongKong", "🇭🇰"],       flag: "🇭🇰", region: "香港" },
    { keys: ["台湾", "TW", "Taiwan", "🇹🇼"],                      flag: "🇹🇼", region: "台湾" },
    { keys: ["日本", "JP", "Japan", "Tokyo", "Osaka", "🇯🇵"],     flag: "🇯🇵", region: "日本" },
    { keys: ["韩国", "KR", "Korea", "Seoul", "🇰🇷"],              flag: "🇰🇷", region: "韩国" },
    { keys: ["新加坡", "SG", "Singapore", "🇸🇬"],                  flag: "🇸🇬", region: "新加坡" },
    { keys: ["印度", "IN", "India", "Mumbai", "🇮🇳"],              flag: "🇮🇳", region: "印度" },
    { keys: ["马来西亚", "MY", "Malaysia", "🇲🇾"],                  flag: "🇲🇾", region: "马来西亚" },
    { keys: ["泰国", "TH", "Thailand", "🇹🇭"],                     flag: "🇹🇭", region: "泰国" },
    { keys: ["菲律宾", "PH", "Philippines", "🇵🇭"],                flag: "🇵🇭", region: "菲律宾" },
    { keys: ["印尼", "ID", "Indonesia", "🇮🇩"],                    flag: "🇮🇩", region: "印尼" },
    { keys: ["越南", "VN", "Vietnam", "🇻🇳"],                      flag: "🇻🇳", region: "越南" },

    // 北美
    { keys: ["美国", "US", "USA", "United States", "America", "Los Angeles", "San Jose", "Seattle", "Dallas", "🇺🇸"], flag: "🇺🇸", region: "美国" },
    { keys: ["加拿大", "CA", "Canada", "Toronto", "🇨🇦"],          flag: "🇨🇦", region: "加拿大" },

    // 欧洲
    { keys: ["英国", "UK", "GB", "Britain", "London", "🇬🇧"],     flag: "🇬🇧", region: "英国" },
    { keys: ["德国", "DE", "Germany", "Frankfurt", "🇩🇪"],         flag: "🇩🇪", region: "德国" },
    { keys: ["法国", "FR", "France", "Paris", "🇫🇷"],              flag: "🇫🇷", region: "法国" },
    { keys: ["荷兰", "NL", "Netherlands", "Amsterdam", "🇳🇱"],     flag: "🇳🇱", region: "荷兰" },
    { keys: ["俄罗斯", "RU", "Russia", "Moscow", "🇷🇺"],           flag: "🇷🇺", region: "俄罗斯" },
    { keys: ["土耳其", "TR", "Turkey", "Türkiye", "Istanbul", "🇹🇷"], flag: "🇹🇷", region: "土耳其" },
    { keys: ["意大利", "IT", "Italy", "🇮🇹"],                      flag: "🇮🇹", region: "意大利" },
    { keys: ["西班牙", "ES", "Spain", "🇪🇸"],                      flag: "🇪🇸", region: "西班牙" },
    { keys: ["瑞士", "CH", "Switzerland", "🇨🇭"],                  flag: "🇨🇭", region: "瑞士" },
    { keys: ["波兰", "PL", "Poland", "🇵🇱"],                       flag: "🇵🇱", region: "波兰" },
    { keys: ["爱尔兰", "IE", "Ireland", "🇮🇪"],                    flag: "🇮🇪", region: "爱尔兰" },

    // 大洋洲
    { keys: ["澳大利亚", "AU", "Australia", "Sydney", "🇦🇺"],     flag: "🇦🇺", region: "澳大利亚" },
    { keys: ["新西兰", "NZ", "New Zealand", "🇳🇿"],                flag: "🇳🇿", region: "新西兰" },

    // 南美
    { keys: ["巴西", "BR", "Brazil", "🇧🇷"],                       flag: "🇧🇷", region: "巴西" },
    { keys: ["阿根廷", "AR", "Argentina", "🇦🇷"],                  flag: "🇦🇷", region: "阿根廷" },

    // 中东 / 非洲
    { keys: ["以色列", "IL", "Israel", "🇮🇱"],                     flag: "🇮🇱", region: "以色列" },
    { keys: ["阿联酋", "AE", "UAE", "Dubai", "🇦🇪"],              flag: "🇦🇪", region: "阿联酋" },
    { keys: ["南非", "ZA", "South Africa", "🇿🇦"],                 flag: "🇿🇦", region: "南非" }
];

/**
 * 检测节点名称中的地区并返回匹配结果
 */
function detectRegion(name) {
    var upperName = name.toUpperCase();

    for (var i = 0; i < REGION_MAP.length; i++) {
        var entry = REGION_MAP[i];
        for (var j = 0; j < entry.keys.length; j++) {
            var keyword = entry.keys[j].toUpperCase();
            if (upperName.indexOf(keyword) !== -1) {
                return entry;
            }
        }
    }
    return null;
}

/**
 * 去除名称中已有的国旗 emoji（避免重复添加）
 * Unicode 国旗 emoji 范围：\uD83C\uDDE6-\uD83C\uDDFF 组合
 */
function stripExistingFlags(name) {
    // 移除已有的国旗 emoji（区域指示符号组合）
    var result = "";
    var i = 0;
    while (i < name.length) {
        var code = name.charCodeAt(i);
        // 检测 surrogate pair (高代理 0xD83C + 低代理 0xDDE6-0xDDFF)
        if (code === 0xD83C && i + 1 < name.length) {
            var next = name.charCodeAt(i + 1);
            if (next >= 0xDDE6 && next <= 0xDDFF) {
                // 跳过这个国旗字符对
                i += 2;
                continue;
            }
        }
        result += name.charAt(i);
        i++;
    }
    return result.trim();
}

/**
 * 核心重命名逻辑
 */
function renameNodes(nodesJson, subscriptionName) {
    var nodes = JSON.parse(nodesJson);
    var renamedCount = 0;
    var regionStats = {};

    // 读取用户配置
    var enableStripOldFlags = Plugin.GetConfig("StripOldFlags") !== "false"; // 默认开启

    for (var i = 0; i < nodes.length; i++) {
        var node = nodes[i];
        var originalName = node.Name || "";
        var region = detectRegion(originalName);

        if (region) {
            // 统计地区分布
            if (regionStats[region.region]) {
                regionStats[region.region]++;
            } else {
                regionStats[region.region] = 1;
            }

            // 去除旧国旗（如果开启）
            var cleanName = enableStripOldFlags ? stripExistingFlags(originalName) : originalName;

            // 检查是否已经有正确的国旗前缀（避免重复）
            if (cleanName.indexOf(region.flag) === 0) {
                continue;
            }

            // 添加国旗前缀
            node.Name = region.flag + " " + cleanName;
            renamedCount++;
        }
    }

    // 输出统计
    var statParts = [];
    for (var key in regionStats) {
        if (regionStats.hasOwnProperty(key)) {
            statParts.push(key + "×" + regionStats[key]);
        }
    }

    var logMsg = "订阅 [" + (subscriptionName || "手动") + "] 重命名完成: " +
        renamedCount + "/" + nodes.length + " 个节点已添加国旗前缀\n" +
        "地区分布: " + (statParts.length > 0 ? statParts.join("  ") : "无匹配");

    Plugin.Log(logMsg);

    return JSON.stringify(nodes);
}

// ─── OnSubscribe ────────────────────────────────────────────────────────────
function OnSubscribe(nodesJson, subscriptionName) {
    try {
        return renameNodes(nodesJson, subscriptionName);
    } catch (e) {
        Plugin.LogError("节点重命名失败: " + e.message);
        return nodesJson;
    }
}

// ─── OnManual ───────────────────────────────────────────────────────────────
function OnManual() {
    try {
        // 手动运行时，读取本地节点配置文件进行预览（不会真正修改）
        if (!Plugin.FileExists("nodes_config.json")) {
            Plugin.Log("未找到节点配置文件");
            Plugin.Notify("节点重命名", "❌ 未找到节点配置文件");
            return;
        }

        var raw = Plugin.ReadFile("nodes_config.json");
        var config = JSON.parse(raw);
        var nodes = config.Nodes || [];

        Plugin.Log("📋 预览模式 - 扫描 " + nodes.length + " 个节点...");

        var regionStats = {};
        var examples = [];

        for (var i = 0; i < nodes.length; i++) {
            var node = nodes[i];
            var region = detectRegion(node.Name || "");
            if (region) {
                if (regionStats[region.region]) {
                    regionStats[region.region]++;
                } else {
                    regionStats[region.region] = 1;
                    // 记录每个地区的第一个示例
                    var cleanName = stripExistingFlags(node.Name || "");
                    examples.push(region.flag + " " + cleanName);
                }
            }
        }

        var statParts = [];
        for (var key in regionStats) {
            if (regionStats.hasOwnProperty(key)) {
                statParts.push(key + "×" + regionStats[key]);
            }
        }

        var unmatched = nodes.length;
        for (var k in regionStats) {
            if (regionStats.hasOwnProperty(k)) {
                unmatched -= regionStats[k];
            }
        }

        var report = "📊 节点地区分布:\n" +
            (statParts.length > 0 ? statParts.join("  ") : "无匹配") + "\n" +
            "❓ 未匹配: " + unmatched + " 个\n" +
            "📝 重命名示例:\n" + examples.slice(0, 5).join("\n");

        Plugin.Log(report);
        Plugin.Notify("节点重命名预览", "共识别 " + (nodes.length - unmatched) + "/" + nodes.length + " 个节点地区");

    } catch (e) {
        Plugin.LogError("预览失败: " + e.message);
    }
}
