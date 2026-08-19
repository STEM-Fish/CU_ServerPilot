using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CU_ServerPilot;

// ============================================================================
//  VersionCheck - 在线版本检查（v0.10.7）
//  ---------------------------------------------------------------------------
//  启动 5 秒后异步请求 GitHub Releases API 的最新版本，与当前版本比较。
//  有新版本时 ModUI 显示提示。网络不可用/超时/解析失败全部静默（不影响游戏）。
// ============================================================================

public static class VersionCheck
{
    public const string CurrentVersion = "0.10.7";
    private const string ApiUrl = "https://api.github.com/repos/STEM-Fish/CU_ServerPilot/releases/latest";

    public static string LatestVersion = "";
    public static bool HasNew { get; private set; }

    private static bool _started;
    private static float _startAt = -1f;
    private static UnityWebRequest _req;

    // 插件 Update 调用
    public static void Update()
    {
        if (_started) return;
        if (_startAt < 0f) { _startAt = Time.time + 5f; return; }   // 启动 5 秒后再查
        if (Time.time < _startAt) return;
        _started = true;

        try
        {
            _req = UnityWebRequest.Get(ApiUrl);
            _req.timeout = 8;
            _req.SendWebRequest();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 版本检查启动失败: {e.Message}");
        }
    }

    // 插件 Update 调用：检查请求是否完成
    public static void Poll()
    {
        if (_req == null) return;
        if (!_req.isDone) return;

        try
        {
            if (_req.result == UnityWebRequest.Result.Success)
            {
                var json = JObject.Parse(_req.downloadHandler.text);
                string tag = (string)json["tag_name"];
                if (!string.IsNullOrEmpty(tag))
                {
                    LatestVersion = tag.TrimStart('v', 'V');
                    HasNew = IsNewer(LatestVersion, CurrentVersion);
                    Plugin.Log.LogInfo($"[CU_ServerPilot] 版本检查: 当前 v{CurrentVersion}, 最新 v{LatestVersion}, {(HasNew ? "发现新版本" : "已是最新")}");
                }
            }
            else
            {
                Plugin.Log.LogInfo($"[CU_ServerPilot] 版本检查失败（网络不可用）: {_req.result}");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[CU_ServerPilot] 版本检查解析失败: {e.Message}");
        }
        finally
        {
            _req.Dispose();
            _req = null;
        }
    }

    // 语义化版本比较：a > b → true
    private static bool IsNewer(string a, string b)
    {
        try
        {
            var pa = Parse(a);
            var pb = Parse(b);
            for (int i = 0; i < 3; i++)
            {
                if (pa[i] != pb[i]) return pa[i] > pb[i];
            }
            return false;
        }
        catch { return false; }
    }

    private static int[] Parse(string v)
    {
        var parts = v.Split('.');
        var r = new int[3];
        for (int i = 0; i < 3; i++)
        {
            int n = 0;
            if (i < parts.Length) int.TryParse(parts[i], out n);
            r[i] = n;
        }
        return r;
    }
}
