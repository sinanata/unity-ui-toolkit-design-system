#if UNITY_6000_5_OR_NEWER
using UnityEngine;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// Which Unity editors carry the fix for the staged-updater overrun that used to keep
    /// world-space materials off in WebGL players.
    ///
    /// The bug: <c>GpuUpdaterStaged</c> (the UI geometry updater of the WebGL and WebGPU players)
    /// sized its per-frame staging buffer from a data set's dirty count BEFORE consolidating the
    /// ranges it then copied, so a dense one-frame restyle read past its own staging buffer
    /// (<c>GfxDevice::CopyBufferRanges: range reads out of bounds</c>). We root-caused it from the
    /// decompiled module and reported it on 2026-07-17; Unity fixed it as UUM-151098 by
    /// consolidating first and sizing each staging request exactly.
    ///
    /// Fixed in 6000.6.2f1 and 6000.7.0b1. Unity 6000.5 is Won't Fix (its support window closed
    /// before a backport), so <c>Tools/UirStagingPatch</c> stays the answer there. Editors before
    /// 6000.5 have no staged updater and were never affected, but they also have no
    /// <c>style.unityMaterial</c>, which is why this whole namespace starts at 6000.5.
    /// </summary>
    public static class DsUirStagingFix
    {
        public const string IssueId = "UUM-151098";

        public const string IssueUrl =
            "https://issuetracker.unity.com/issues/24430/webgl-crash-when-consolidated-range-is-larger-than-the-sum-of-individual-ranges";

        /// <summary>Does the engine running this code carry Unity's fix?</summary>
        public static bool EngineHasFix => IsFixedIn(Application.unityVersion);

        /// <summary>
        /// True for 6000.6.2f1 and every later 6000.6 patch, for 6000.7.0b1 onward, and for any
        /// later minor or major. False for 6000.5.x, for 6000.6.0f1 and 6000.6.1f1, for 6000.7
        /// alphas, and for a string that does not parse, so an unknown version keeps the safe
        /// default instead of guessing.
        /// </summary>
        public static bool IsFixedIn(string unityVersion)
        {
            if (!TryParse(unityVersion, out int major, out int minor, out int patch, out char stage))
                return false;
            if (major != 6000)
                return major > 6000;
            if (minor >= 8) return true;
            if (minor == 7) return patch > 0 || stage != 'a';   // 6000.7.0b1 and later
            if (minor == 6) return patch >= 2;                  // 6000.6.2f1 and later
            return false;                                       // 6000.5 and earlier
        }

        // "6000.6.7f1" -> 6000, 6, 7, 'f'. Hand-rolled rather than Regex: this ships in WebGL
        // players, where every managed dependency is download size.
        private static bool TryParse(string v, out int major, out int minor, out int patch, out char stage)
        {
            major = minor = patch = 0;
            stage = '\0';
            if (string.IsNullOrEmpty(v))
                return false;
            int i = 0;
            if (!ReadInt(v, ref i, out major) || !Expect(v, ref i, '.')) return false;
            if (!ReadInt(v, ref i, out minor) || !Expect(v, ref i, '.')) return false;
            if (!ReadInt(v, ref i, out patch)) return false;
            if (i >= v.Length || v[i] < 'a' || v[i] > 'z') return false;
            stage = v[i];
            return true;
        }

        private static bool ReadInt(string s, ref int i, out int value)
        {
            value = 0;
            int start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9' && i - start < 9)
                value = value * 10 + (s[i++] - '0');
            return i > start;
        }

        private static bool Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) return false;
            i++;
            return true;
        }
    }
}
#endif
