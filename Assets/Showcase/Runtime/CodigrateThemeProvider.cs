using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Showcase.Runtime
{
    // Fetches Codigrate theme metadata + palette JSONs over HTTPS, with a
    // bundled mirror in Resources/CodigrateThemes/ as a CORS-friendly fallback.
    //
    // Why the fallback exists:
    //   codigrate.com (Cloudflare-fronted on Heroku) does not set
    //   `Access-Control-Allow-Origin`, so any browser-context fetch — i.e.
    //   every WebGL build — fails the preflight and never receives the
    //   palette JSON. UnityWebRequest cannot bypass the browser's CORS
    //   enforcement. We therefore bundle a frozen copy of list.json plus the
    //   12 palette JSONs as TextAsset resources; the runtime resolves to
    //   bundled by default on WebGL, and falls back to bundled on network
    //   failure for the Editor and Standalone builds (where the live fetch
    //   normally succeeds and lets the showcase pick up upstream tweaks).
    //
    // Why UnityWebRequest's `completed` event (not a coroutine):
    //   The static-class lifecycle has no MonoBehaviour to host StartCoroutine,
    //   and `UnityWebRequestAsyncOperation.completed` is the documented
    //   non-coroutine completion hook on every platform Unity 6 ships, web
    //   included.
    public static class CodigrateThemeProvider
    {
        // Public roots for the asset bundle. Tracking the path layout here so a
        // future move to a sibling bucket only needs editing in one place.
        const string ROOT_URL      = "https://codigrate.com";
        const string LIST_URL      = ROOT_URL + "/assets/themes/list.json";
        // Codigrate's catalog of theme variants (JetBrains plugins, Chrome
        // theme, Ghostty, etc.) is fronted on plugins.jetbrains.com rather
        // than the codigrate.com root — but the user-facing brand link should
        // point at codigrate.com itself per their instructions, so people
        // landing there can browse the maker's own site.
        public const string SHOWCASE_URL = ROOT_URL;

        const string BUNDLED_LIST_RES   = "CodigrateThemes/list";
        const string BUNDLED_THEME_DIR  = "CodigrateThemes/";

        // WebGL deterministically fails the live fetch (CORS), so we skip the
        // network round-trip there. Editor / Standalone go live so an upstream
        // edit on codigrate.com lands without rebuilding the project.
        static bool PreferBundled =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        // Cached after the first successful list fetch so repeat opens of the
        // theme dropdown don't re-hit the network. Cleared only on domain
        // reload, which matches the lifetime of the showcase scene.
        static List<ThemeListing> _cachedList;
        static readonly Dictionary<string, ThemePalette> _cachedPalettes = new Dictionary<string, ThemePalette>();

        public sealed class ThemeListing
        {
            public string Name;
            public string IconUrl;
            public string PaletteUrl;
            // Bundled fallback path under Resources/. Set when the slug is
            // derivable from the upstream `json` path. Null entries simply
            // skip the fallback — live fetch is still attempted.
            public string PaletteResource;
        }

        public sealed class ThemePalette
        {
            public string Name;
            public string Key;              // sequoia, tokyo, etc.
            public string Appearance;       // "light" | "dark"
            public InterfaceTokens Interface;
        }

        public sealed class InterfaceTokens
        {
            public Color Surface;
            public Color WindowBackground;
            public Color AlternateBackground;
            public Color EditorBackground;
            public Color AccentColor;
            public Color PrimaryForeground;
            public Color SecondaryForeground;
            public Color Error;
            public Color Warning;
            public Color WarningFocused;
            public Color Info;
            public Color Success;
        }

        public static List<ThemeListing> CachedList => _cachedList;

        public static void FetchList(Action<List<ThemeListing>, string> done)
        {
            if (_cachedList != null) { done?.Invoke(_cachedList, null); return; }
            if (PreferBundled) { done?.Invoke(LoadBundledList(), null); return; }

            var req = UnityWebRequest.Get(LIST_URL);
            var op  = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        // Live fetch failed (typically CORS in a WebGL editor
                        // preview, or transient network on Standalone). Use
                        // the bundled mirror if we can — it's a frozen-in-
                        // time snapshot, but every theme in it is functional.
                        var bundled = LoadBundledList();
                        if (bundled != null && bundled.Count > 0)
                        {
                            done?.Invoke(bundled, null);
                            return;
                        }
                        done?.Invoke(null, req.error);
                        return;
                    }
                    var list = ParseList(req.downloadHandler.text);
                    _cachedList = list;
                    done?.Invoke(list, null);
                }
                catch (Exception e)
                {
                    done?.Invoke(null, e.Message);
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        public static void FetchPalette(ThemeListing listing, Action<ThemePalette, string> done)
        {
            if (listing == null) { done?.Invoke(null, "listing was null"); return; }
            string cacheKey = listing.PaletteUrl ?? listing.PaletteResource;
            if (cacheKey != null && _cachedPalettes.TryGetValue(cacheKey, out var hit))
            {
                done?.Invoke(hit, null);
                return;
            }

            if (PreferBundled)
            {
                var bundled = LoadBundledPalette(listing);
                if (bundled != null) { _cachedPalettes[cacheKey] = bundled; done?.Invoke(bundled, null); return; }
                done?.Invoke(null, "bundled palette missing: " + listing.PaletteResource);
                return;
            }

            var req = UnityWebRequest.Get(listing.PaletteUrl);
            var op  = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        var bundled = LoadBundledPalette(listing);
                        if (bundled != null) { _cachedPalettes[cacheKey] = bundled; done?.Invoke(bundled, null); return; }
                        done?.Invoke(null, req.error);
                        return;
                    }
                    var palette = ParsePalette(req.downloadHandler.text);
                    _cachedPalettes[cacheKey] = palette;
                    done?.Invoke(palette, null);
                }
                catch (Exception e)
                {
                    done?.Invoke(null, e.Message);
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        // --- Bundled fallback -------------------------------------------------
        static List<ThemeListing> LoadBundledList()
        {
            var ta = Resources.Load<TextAsset>(BUNDLED_LIST_RES);
            if (ta == null) { Debug.LogWarning($"[CodigrateThemeProvider] Bundled list missing at Resources/{BUNDLED_LIST_RES}"); return new List<ThemeListing>(); }
            var list = ParseList(ta.text);
            _cachedList = list;
            return list;
        }

        static ThemePalette LoadBundledPalette(ThemeListing listing)
        {
            if (string.IsNullOrEmpty(listing.PaletteResource)) return null;
            var ta = Resources.Load<TextAsset>(listing.PaletteResource);
            if (ta == null) return null;
            return ParsePalette(ta.text);
        }

        // --- JSON parsing ------------------------------------------------------
        // The list endpoint returns a top-level array, which JsonUtility refuses
        // to parse. Wrapping it in `{"items":[…]}` keeps us on the built-in
        // serializer (no Newtonsoft dependency in the OSS demo).
        static List<ThemeListing> ParseList(string raw)
        {
            string wrapped = "{\"items\":" + raw + "}";
            var dto = JsonUtility.FromJson<ListWrapper>(wrapped);
            var result = new List<ThemeListing>();
            if (dto?.items == null) return result;
            foreach (var item in dto.items)
            {
                if (string.IsNullOrEmpty(item?.name) || string.IsNullOrEmpty(item.json)) continue;
                result.Add(new ThemeListing
                {
                    Name            = item.name,
                    IconUrl         = JoinUrl(ROOT_URL, item.icon),
                    PaletteUrl      = JoinUrl(ROOT_URL, item.json),
                    PaletteResource = BundledResourceFor(item.json),
                });
            }
            return result;
        }

        // Maps the upstream `json` field
        //   "assets/themes/nature/sequoia-theme/sequoia.palette.json"
        // onto the bundled-resource path
        //   "CodigrateThemes/sequoia"
        // by lifting the basename and stripping `.palette.json`. The naming
        // convention is uniform across all 12 themes; if Codigrate ever ships
        // a theme whose JSON filename diverges, the bundled mirror just
        // misses that one entry and the live fetch handles it.
        static string BundledResourceFor(string upstreamJsonPath)
        {
            if (string.IsNullOrEmpty(upstreamJsonPath)) return null;
            int lastSlash = upstreamJsonPath.LastIndexOf('/');
            string file = lastSlash >= 0 ? upstreamJsonPath.Substring(lastSlash + 1) : upstreamJsonPath;
            const string suffix = ".palette.json";
            if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - suffix.Length);
            return BUNDLED_THEME_DIR + file;
        }

        static ThemePalette ParsePalette(string raw)
        {
            var dto = JsonUtility.FromJson<PaletteDto>(raw);
            if (dto?.tokens?.@interface == null) throw new Exception("palette JSON missing tokens.interface");
            var t = dto.tokens.@interface;
            return new ThemePalette
            {
                Name        = dto.metadata?.name,
                Key         = dto.metadata?.key,
                Appearance  = (dto.metadata?.appearance ?? "dark").ToLowerInvariant(),
                Interface   = new InterfaceTokens
                {
                    Surface             = ToColor(t.surface),
                    WindowBackground    = ToColor(t.windowBackground),
                    AlternateBackground = ToColor(t.alternateBackground),
                    EditorBackground    = ToColor(t.editorBackground),
                    AccentColor         = ToColor(t.accentColor),
                    PrimaryForeground   = ToColor(t.primaryForeground),
                    SecondaryForeground = ToColor(t.secondaryForeground),
                    Error               = ToColor(t.error),
                    Warning             = ToColor(t.warning),
                    WarningFocused      = ToColor(t.warningFocused),
                    Info                = ToColor(t.info),
                    Success             = ToColor(t.success),
                },
            };
        }

        static Color ToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        static string JoinUrl(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return relative;
            if (relative.StartsWith("/")) return root + relative;
            return root + "/" + relative;
        }

        // --- DTOs that mirror the codigrate JSON shape -------------------------
        // JsonUtility can't see properties, only public fields. The shape below
        // matches the keys in the upstream files verbatim — sequoia.palette.json
        // and tokyo.palette.json are the reference points (kept in lockstep so a
        // future field addition surfaces in both).
        [Serializable] class ListWrapper { public List<ListItemDto> items; }
        [Serializable] class ListItemDto { public string name; public string icon; public string html; public string json; }

        [Serializable] class PaletteDto
        {
            public string version;
            public MetadataDto metadata;
            public TokensDto tokens;
        }
        [Serializable] class MetadataDto
        {
            public string id;
            public string name;
            public string key;
            public string category;
            public string appearance;
        }
        [Serializable] class TokensDto
        {
            // `interface` is a C# keyword — JsonUtility maps onto a verbatim
            // field, so we escape it with @.
            public InterfaceDto @interface;
        }
        [Serializable] class InterfaceDto
        {
            public string surface;
            public string windowBackground;
            public string alternateBackground;
            public string editorBackground;
            public string accentColor;
            public string primaryForeground;
            public string secondaryForeground;
            public string error;
            public string warning;
            public string warningFocused;
            public string info;
            public string success;
        }
    }
}
