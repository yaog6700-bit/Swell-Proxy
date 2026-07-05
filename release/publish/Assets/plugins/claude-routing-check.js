/**
 * Swell Proxy 插件：高级分流测试与 AI 解锁检测 (类似 ip.skk.moe)
 * 触发器: OnManual (手动触发)
 *
 * 功能:
 *   - 测试多平台（淘宝、百度、B站、Cloudflare、Google等）的出口 IP 和归属地
 *   - 判断国内/国外分流状态是否正常
 *   - 检测主流 AI 服务 (Claude, ChatGPT, Gemini) 解锁状态
 */

// ─── 辅助函数：提取信息 ──────────────────────────────────────────────────
function extractJsonField(str, field) {
    try {
        var obj = JSON.parse(str);
        return obj[field];
    } catch (e) {
        return null;
    }
}

function extractTaobaoIp(str) {
    var match = str.match(/ipCallback\(\{ip:"([^"]+)"\}\)/);
    return match ? match[1] : null;
}

function extractSohuIp(str) {
    var match = str.match(/"cip":\s*"([^"]+)"/);
    return match ? match[1] : null;
}

function extractCfTrace(str) {
    var ipMatch = str.match(/ip=([^\n]+)/);
    var locMatch = str.match(/loc=([^\n]+)/);
    return {
        ip: ipMatch ? ipMatch[1] : "Unknown",
        loc: locMatch ? locMatch[1] : "Unknown"
    };
}

// ─── 核心检测逻辑 ──────────────────────────────────────────────────────────
function runAdvancedCheck() {
    var report = [];
    var results = {}; // 存储获取到的IP用于对比

    Plugin.Log("🚀 正在执行高级分流与解锁检测，请稍候...");

    // ========================================================================
    // 1. 国内平台检测 (应该走直连)
    // ========================================================================
    report.push("📍 【国内服务出口】 (预期: 本地 IP)");

    // 搜狐 (Sohu)
    try {
        var sohuRes = Plugin.HttpGet("http://pv.sohu.com/cityjson");
        var sohuIp = extractSohuIp(sohuRes);
        results.sohu = sohuIp;
        report.push("  - 搜狐 (Sohu):   " + (sohuIp || "获取失败"));
    } catch (e) { report.push("  - 搜狐 (Sohu):   超时/失败"); }

    // B站 (Bilibili)
    try {
        var biliRes = Plugin.HttpGet("https://api.bilibili.com/x/web-interface/zone");
        var obj = JSON.parse(biliRes);
        var loc = obj.data ? (obj.data.country + " " + obj.data.province) : "未知";
        report.push("  - B站 (Bilibili): 识别区域 -> " + loc);
    } catch (e) { report.push("  - B站 (Bilibili): 超时/失败"); }


    // ========================================================================
    // 2. 国际平台检测 (应该走代理)
    // ========================================================================
    report.push("\n🌎 【国际服务出口】 (预期: 节点 IP)");

    // Cloudflare
    try {
        var cfRes = Plugin.HttpGet("https://cloudflare.com/cdn-cgi/trace");
        var cfInfo = extractCfTrace(cfRes);
        results.cloudflare = cfInfo.ip;
        report.push("  - Cloudflare:  " + cfInfo.ip + " (区域: " + cfInfo.loc + ")");
    } catch (e) { report.push("  - Cloudflare:  超时/失败"); }

    // Google (domains.google.com)
    try {
        var ggIp = Plugin.HttpGet("https://domains.google.com/checkip").trim();
        results.google = ggIp;
        report.push("  - Google:      " + (ggIp || "获取失败"));
    } catch (e) { report.push("  - Google:      超时/失败"); }

    // ipify
    try {
        var ipifyRes = Plugin.HttpGet("https://api.ipify.org?format=json");
        var ipifyIp = extractJsonField(ipifyRes, "ip");
        results.ipify = ipifyIp;
        report.push("  - Ipify:       " + (ipifyIp || "获取失败"));
    } catch (e) { report.push("  - Ipify:       超时/失败"); }


    // ========================================================================
    // 3. 分流状态评估
    // ========================================================================
    var domesticIp = results.sohu;
    var foreignIp = results.google || results.cloudflare || results.ipify;

    report.push("\n⚖️ 【分流状态诊断】");
    if (domesticIp && foreignIp) {
        if (domesticIp === foreignIp) {
            report.push("  ⚠️ 警告: 国内外出口 IP 完全一致 (" + domesticIp + ")。");
            report.push("  可能原因: 当前为【全局代理】模式，或【全局直连】，或分流规则失效。");
        } else {
            report.push("  ✅ 正常: 国内外出口 IP 不一致。");
            report.push("  (国内: " + domesticIp + " / 国外: " + foreignIp + ")");
        }
    } else {
        report.push("  ⚠️ 无法准确诊断: 未能同时获取国内外 IP。");
    }


    // ========================================================================
    // 4. AI 服务解锁检测
    // ========================================================================
    report.push("\n🤖 【AI 服务可用性】");

    // Claude
    try {
        Plugin.HttpGet("https://claude.ai/login");
        report.push("  - Claude AI: ✅ 可用 (返回 200 OK)");
    } catch (e) {
        if (e.message.indexOf("403") !== -1) {
            report.push("  - Claude AI: ❌ 封锁 (该地区不支持 - 403 Forbidden)");
        } else {
            report.push("  - Claude AI: ⚠️ 异常 (" + e.message + ")");
        }
    }

    // ChatGPT
    try {
        Plugin.HttpGet("https://chatgpt.com/");
        report.push("  - ChatGPT:   ✅ 可用 (网络连通)");
    } catch (e) {
        if (e.message.indexOf("403") !== -1) {
            report.push("  - ChatGPT:   ❌ 封锁 (拒绝访问 - 403)");
        } else {
            report.push("  - ChatGPT:   ⚠️ 异常 (" + e.message + ")");
        }
    }

    // Gemini (Google)
    try {
        Plugin.HttpGet("https://gemini.google.com/");
        report.push("  - Gemini:    ✅ 可用 (网络连通)");
    } catch (e) {
        report.push("  - Gemini:    ⚠️ 异常 (" + e.message + ")");
    }

    // 输出最终报告
    var finalReport = report.join("\n");
    Plugin.Log("========== 分流与解锁高级报告 ==========\n" + finalReport);
    Plugin.Notify("分流检测完成", "已完成多平台出口分析，请查看日志了解详情。");
}

// ─── OnManual ───────────────────────────────────────────────────────────────
function OnManual() {
    try {
        runAdvancedCheck();
    } catch (e) {
        Plugin.LogError("高级检测执行期间发生错误: " + e.message);
    }
}
