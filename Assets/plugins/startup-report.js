/**
 * Swell Proxy 插件：开机日报 / 状态通知
 * 触发器: OnStartup (应用启动时自动触发) + OnManual (手动运行)
 *
 * 功能:
 *   - 统计当前节点总数、协议分布、收藏节点数
 *   - 统计订阅源数量
 *   - 统计已安装插件数量
 *   - 推送一条系统通知，展示启动摘要
 */

function OnStartup() {
    try {
        Plugin.Log("📋 正在生成开机日报...");

        // ─── 1. 读取节点信息 ───
        var nodeCount = 0;
        var favoriteCount = 0;
        var protocols = {};
        var selectedNodeName = "未知";

        if (Plugin.FileExists("nodes_config.json")) {
            var raw = Plugin.ReadFile("nodes_config.json");
            var config = JSON.parse(raw);
            var nodes = config.Nodes || [];
            nodeCount = nodes.length;

            for (var i = 0; i < nodes.length; i++) {
                var node = nodes[i];

                // 统计收藏
                if (node.IsFavorite) {
                    favoriteCount++;
                }

                // 统计协议分布
                var proto = node.Protocol || "Unknown";
                if (protocols[proto]) {
                    protocols[proto]++;
                } else {
                    protocols[proto] = 1;
                }

                // 找到当前选中的节点
                if (config.SelectedNodeId && node.Id === config.SelectedNodeId) {
                    selectedNodeName = node.Name || "未命名";
                }
            }

            // 统计订阅源数量
            var subCount = (config.Subscriptions || []).length;
        }

        // ─── 2. 构建协议分布摘要 ───
        var protocolParts = [];
        for (var key in protocols) {
            if (protocols.hasOwnProperty(key)) {
                protocolParts.push(key + "×" + protocols[key]);
            }
        }
        var protocolSummary = protocolParts.length > 0 ? protocolParts.join("  ") : "无";

        // ─── 3. 读取插件信息 ───
        var pluginCount = 0;
        var enabledPluginCount = 0;

        if (Plugin.FileExists("plugins.json")) {
            var pluginsRaw = Plugin.ReadFile("plugins.json");
            var plugins = JSON.parse(pluginsRaw);
            pluginCount = plugins.length;

            for (var j = 0; j < plugins.length; j++) {
                if (!plugins[j].disabled) {
                    enabledPluginCount++;
                }
            }
        }

        // ─── 4. 构建日报 ───
        var report =
            "📊 节点: " + nodeCount + " 个  |  ⭐ 收藏: " + favoriteCount + " 个\n" +
            "🔗 协议分布: " + protocolSummary + "\n" +
            "🎯 当前节点: " + selectedNodeName + "\n" +
            "📦 订阅源: " + (subCount || 0) + " 个  |  🧩 插件: " + enabledPluginCount + "/" + pluginCount + " 启用";

        Plugin.Log("开机日报:\n" + report);
        Plugin.Notify("Swell Proxy 启动就绪 ✅", report);

    } catch (e) {
        Plugin.LogError("开机日报生成失败: " + e.message);
    }
}

// ─── OnManual ───────────────────────────────────────────────────────────────
// 手动触发时，执行与开机日报相同的逻辑
function OnManual() {
    OnStartup();
}
