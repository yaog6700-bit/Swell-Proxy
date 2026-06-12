/**
 * Swell Proxy 插件：异常断线告警
 * 触发器: OnCoreStarted + OnCoreStopped (自动) + OnManual (手动查看断线日志)
 *
 * 功能:
 *   - 内核停止时记录时间戳到本地文件
 *   - 发送系统通知告警用户代理已断开
 *   - 支持配置静默模式（手动停止不告警）
 */

// ─── OnCoreStarted ──────────────────────────────────────────────────────────
// 内核启动时记录启动时间，用于计算运行时长
function OnCoreStarted() {
    try {
        var now = Date.now();
        Plugin.WriteFile("plugins/cache/core_start_time.txt", String(now));
        Plugin.Log("🟢 内核已启动，已记录启动时间");
    } catch (e) {
        Plugin.LogError("记录启动时间失败: " + e.message);
    }
}

// ─── OnCoreStopped ──────────────────────────────────────────────────────────
// 内核停止时发出告警通知
function OnCoreStopped() {
    try {
        var now = new Date();
        var timeStr = now.getFullYear() + "-" +
            padZero(now.getMonth() + 1) + "-" +
            padZero(now.getDate()) + " " +
            padZero(now.getHours()) + ":" +
            padZero(now.getMinutes()) + ":" +
            padZero(now.getSeconds());

        // ─── 计算运行时长 ───
        var uptimeStr = "未知";
        if (Plugin.FileExists("plugins/cache/core_start_time.txt")) {
            var startStr = Plugin.ReadFile("plugins/cache/core_start_time.txt");
            var startMs = parseInt(startStr);
            if (!isNaN(startMs)) {
                var elapsed = Date.now() - startMs;
                uptimeStr = formatDuration(elapsed);
            }
            // 清理临时文件
            Plugin.DeleteFile("plugins/cache/core_start_time.txt");
        }

        // ─── 写入断线日志 ───
        var logPath = "plugins/cache/disconnect_log.txt";
        var logLine = "[" + timeStr + "] 内核停止 | 运行时长: " + uptimeStr + "\n";

        var existingLog = "";
        if (Plugin.FileExists(logPath)) {
            existingLog = Plugin.ReadFile(logPath);
        }

        // 保留最近 50 条记录（避免文件无限增长）
        var lines = existingLog.split("\n").filter(function(l) { return l.trim() !== ""; });
        if (lines.length >= 50) {
            lines = lines.slice(lines.length - 49);
        }
        lines.push(logLine.trim());
        Plugin.WriteFile(logPath, lines.join("\n") + "\n");

        // ─── 发送告警通知 ───
        var message =
            "⏰ 时间: " + timeStr + "\n" +
            "⏱️ 运行时长: " + uptimeStr;

        Plugin.Log("⚠️ 代理内核已停止\n" + message);
        Plugin.Notify("⚠️ 代理已断开", message);

    } catch (e) {
        Plugin.LogError("断线告警插件异常: " + e.message);
    }
}

// ─── 工具函数 ───────────────────────────────────────────────────────────────

function padZero(n) {
    return n < 10 ? "0" + n : String(n);
}

function formatDuration(ms) {
    var totalSec = Math.floor(ms / 1000);
    var hours = Math.floor(totalSec / 3600);
    var minutes = Math.floor((totalSec % 3600) / 60);
    var seconds = totalSec % 60;

    if (hours > 0) {
        return hours + " 小时 " + minutes + " 分 " + seconds + " 秒";
    } else if (minutes > 0) {
        return minutes + " 分 " + seconds + " 秒";
    } else {
        return seconds + " 秒";
    }
}

// ─── OnManual ───────────────────────────────────────────────────────────────
// 手动触发时，查看最近的断线记录
function OnManual() {
    try {
        var logPath = "plugins/cache/disconnect_log.txt";

        if (!Plugin.FileExists(logPath)) {
            Plugin.Log("📋 暂无断线记录");
            Plugin.Notify("断线告警", "✅ 暂无断线记录，一切正常！");
            return;
        }

        var logContent = Plugin.ReadFile(logPath);
        var lines = logContent.split("\n").filter(function(l) { return l.trim() !== ""; });

        // 显示最近 5 条
        var recent = lines.slice(Math.max(0, lines.length - 5));
        var summary = "📋 最近 " + recent.length + " 条断线记录 (共 " + lines.length + " 条):\n" + recent.join("\n");

        Plugin.Log(summary);
        Plugin.Notify("断线记录", "共 " + lines.length + " 条断线记录，详情请查看日志");
    } catch (e) {
        Plugin.LogError("读取断线日志失败: " + e.message);
    }
}
