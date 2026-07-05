/**
 * Swell Proxy 插件：网络测速 (Ping & 下载速度)
 * 触发器: OnManual (点击“运行”按钮时触发)
 */

function OnManual() {
    var starttime = Date.now();

    // 从配置读取测速文件大小 (MB)，如果没填默认测试 1MB (为了让您快点看到结果，我把它从50改成了1)
    var mbStr = Plugin.GetConfig("testFileSize");
    var mb = parseInt(mbStr);
    if (isNaN(mb) || mb <= 0) {
        mb = 10;  // 默认 10MB，测速更准确；可在插件配置 testFileSize 中自定义
    }

    var bytes = mb * 1024 * 1024;
    var url = "https://speed.cloudflare.com/__down?bytes=" + bytes;
    var path = "plugins/cache/speedtest.file";

    var pingurl = "http://connectivitycheck.gstatic.com/generate_204";

    Plugin.Log("开始测速，目标大小: " + mb + " MB");
    Plugin.Notify("测速中", "正在测试网络延迟，请稍候...");

    // ─── 1. 测试延迟 (Ping) ───
    var pingduration;
    var pingstart = Date.now();

    try {
        Plugin.HttpGet(pingurl);
        var pingend = Date.now();
        var pingDurationMs = pingend - pingstart;

        if (pingDurationMs > 10000) {
            pingduration = "Error (>10s)";
            Plugin.LogError("延迟测试超时");
        } else {
            pingduration = pingDurationMs + " ms";
            Plugin.Log("延迟测试成功: " + pingduration);
        }
    } catch (error) {
        pingduration = "Error";
        Plugin.LogError("延迟测试失败");
    }

    Plugin.Notify("测速中", "正在测试下行速度 (" + mb + " MB)，请稍候...");

    // ─── 2. 测试下行速度 ───
    var end;
    var speed;
    var duration;
    var fileExists = false;

    var start = Date.now();

    try {
        // 使用我们刚刚为 C# 引擎新增的 DownloadFile 方法，直接将大文件写入硬盘，防止撑爆内存
        Plugin.Log("开始下载 " + mb + " MB 测速文件，请耐心等待...");
        Plugin.DownloadFile(url, path);
        end = Date.now();
        fileExists = true;
    } catch (error) {
        fileExists = false;
        speed = "Error";
        duration = "Error";
        Plugin.LogError("下行速度测试失败: " + error);
    }

    if (fileExists) {
        var durationSec = (end - start) / 1000;
        var speedMb = mb / durationSec;

        duration = durationSec.toFixed(2) + " s";
        speed = speedMb.toFixed(2) + " MB/s";

        // 测速完成，删除临时文件
        Plugin.DeleteFile(path);
        Plugin.Log("下行速度测试完成: " + speed);
    }

    // ─── 3. 汇总结果 ───
    var endtime = Date.now();
    var totalTime = ((endtime - starttime) / 1000).toFixed(2) + " s";

    var text0 = "⚡ 延迟: " + pingduration;
    var text1 = "💨 下行速度: " + speed;
    var text2 = "⏳ 测试耗时: " + totalTime;

    var message = text0 + "\n" + text1 + "\n" + text2;

    Plugin.Log("最终测速结果:\n" + message);
    Plugin.Notify("测速结果", message);
}
