/**
 * Swell Proxy 插件：IP 欺诈风险检测
 * 触发器: OnManual (点击“运行”按钮时触发)
 */

function checkIpFraudRisk(ip) {
    // 如果没有提供 ip，ip-api.com 会自动检测本机 IP
    var url = "http://ip-api.com/json/" + (ip ? ip : "") + "?fields=query,status,message,country,city,isp,proxy,hosting";
    
    Plugin.Log("正在查询 IP 详情...");
    var response = Plugin.HttpGet(url);
    var body = JSON.parse(response);

    if (body.status !== 'success') {
        Plugin.LogError("查询失败: " + body.message);
        Plugin.Notify("IP 风险检测", "💥 发生错误: " + body.message);
    } else {
        var isProxy = body.proxy ? "是" : "否";
        var isHosting = body.hosting ? "是" : "否";
        
        var riskemoji = (body.proxy || body.hosting) ? '🟠' : '🟢';
        var riskText = (body.proxy || body.hosting) ? "可能为代理或机房 IP" : "正常宽带 IP";

        // body.query 就是当前检测到的实际 IP
        var message = "🌐 IP: " + body.query + "\n" +
                      "📍 位置: " + body.country + " " + body.city + "\n" +
                      "🏢 运营商: " + body.isp + "\n" +
                      "🛡️ 代理池: " + isProxy + "  |  机房: " + isHosting + "\n" +
                      riskemoji + " 风险评估: " + riskText;

        Plugin.Log("查询完成:\n" + message);
        Plugin.Notify("IP 欺诈风险结果", message);
    }
}

// ─── OnManual 触发器（点击面板上的播放按钮执行） ───
function OnManual() {
    try {
        var manualIp = Plugin.GetConfig("TargetIP");
        var ipToQuery = (manualIp && manualIp.trim() !== "") ? manualIp.trim() : "";
        
        if (ipToQuery) {
            Plugin.Log("读取到配置中指定的 IP: " + ipToQuery);
        }

        checkIpFraudRisk(ipToQuery);
    } catch (e) {
        Plugin.LogError("插件执行异常: " + e.message);
    }
}
