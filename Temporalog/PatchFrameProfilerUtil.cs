using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HarmonyLib;
using Temporalog.InfluxDB;
using Vintagestory.API.Common;

// ReSharper disable InconsistentNaming

namespace Temporalog;

public class PatchFrameProfilerUtil
{

    public static void Patch(Harmony harmony)
    {
        var original = typeof(FrameProfilerUtil).GetMethod(nameof(FrameProfilerUtil.End));
        var prefix =
            new HarmonyMethod(typeof(PatchFrameProfilerUtil).GetMethod(nameof(Prefix)));
        harmony.Patch(original, prefix: prefix);
    }
    
    public static bool Prefix(FrameProfilerUtil __instance, ProfileEntryRange ___rootEntry,
        // ReSharper disable once IdentifierTypo
        Action<string> ___onLogoutputHandler)
    {
        if (!__instance.Enabled && !__instance.PrintSlowTicks)
        {
            return false;
        }

        __instance.Mark("prefixEnd");
        __instance.Leave();

        __instance.PrevRootEntry = ___rootEntry;

        var ms = (double)___rootEntry.ElapsedTicks / Stopwatch.Frequency * 1000;

        if (!__instance.PrintSlowTicks || !(ms > __instance.PrintSlowTicksThreshold)) return false;
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine($"A tick took {ms:0.##} ms");

        SlowTicksToString(___rootEntry, stringBuilder);

        var message = stringBuilder.ToString();
        if (Temporalog.Config.OverwriteLogTicks == false)
        {
            ___onLogoutputHandler(message);
        }

        var data = PointData.Measurement("logticks").Field("log", message).Field("ms", ms).Timestamp(WritePrecision.Ms);

        Temporalog.Instance?.WritePoint(data, WritePrecision.Ms);

        return false;
    }

    private static void SlowTicksToString(ProfileEntryRange entry, StringBuilder stringBuilder,
        double thresholdMs = 0.35, string indent = "")
    {
        var timeMs = (double)entry.ElapsedTicks / Stopwatch.Frequency * 1000;
        if (timeMs < thresholdMs)
        {
            return;
        }

        if (entry.CallCount > 1)
        {
            stringBuilder.AppendLine(
                $"{indent}{timeMs:0.00}ms, {entry.CallCount:####} calls, avg {timeMs * 1000 / Math.Max(entry.CallCount, 1):0.00} us/call: {entry.Code}"
            );
        }
        else
        {
            stringBuilder.AppendLine(
                $"{indent}{timeMs:0.00}ms, {entry.CallCount:####} call : {entry.Code}"
            );
        }

        var profiles = new List<ProfileEntryRange>();

        if (entry.Marks != null)
        {
            profiles.AddRange(entry.Marks.Select(e => new ProfileEntryRange()
                { ElapsedTicks = e.Value.ElapsedTicks, Code = e.Key, CallCount = e.Value.CallCount }));
        }

        if (entry.ChildRanges != null)
        {
            profiles.AddRange(entry.ChildRanges.Values);
        }

        var orderByDescending = profiles.OrderByDescending((prof) => prof.ElapsedTicks);

        var i = 0;
        foreach (var prof in orderByDescending)
        {
            if (i++ > 8)
            {
                return;
            }

            SlowTicksToString(prof, stringBuilder, thresholdMs, indent + "  ");
        }
    }
}