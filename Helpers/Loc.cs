using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Thin MRT Core wrapper backed by <see cref="ResourceManager"/> + <see cref="ResourceContext"/>.
    /// Works identically in unpackaged multi-file builds and <c>PublishSingleFile=true</c> self-extracting builds
    /// (where <c>Swell Proxy.pri</c> resides in <see cref="AppContext.BaseDirectory"/>).
    /// </summary>
    public static class Loc
    {
        private static readonly ResourceMap? _map;
        private static readonly ResourceContext? _context;

        static Loc()
        {
            try
            {
                ResourceManager manager;
                string priPath = Path.Combine(AppContext.BaseDirectory, "Swell Proxy.pri");
                if (File.Exists(priPath))
                {
                    manager = new ResourceManager(priPath);
                }
                else
                {
                    manager = new ResourceManager();
                }

                _context = manager.CreateResourceContext();
                _context.QualifierValues["Language"] = LanguageHelper.EffectiveLanguage;
                _map = manager.MainResourceMap.TryGetSubtree("Resources");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Loc] Failed to initialize ResourceManager: {ex.Message}");
            }
        }

        public static string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            try
            {
                if (_map != null && _context != null)
                {
                    var candidate = _map.TryGetValue(key, _context);
                    if (candidate != null && !string.IsNullOrEmpty(candidate.ValueAsString))
                    {
                        return candidate.ValueAsString;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Loc] Lookup failed for '{key}': {ex.Message}");
            }

            return key;
        }

        public static string Format(string key, params object?[] args)
            => string.Format(GetString(key), args);
    }
}

