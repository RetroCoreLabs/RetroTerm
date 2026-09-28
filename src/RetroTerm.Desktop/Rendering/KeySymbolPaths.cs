using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml;
using Avalonia.Media;
using Avalonia.Platform;
using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Desktop.Rendering
{
    /// <summary>
    /// Loads SVG path data from embedded KeySymbol assets and caches parsed Geometry objects.
    /// Symbol grid positions are read from TDV2200KeyVisualRegistry (single source of truth).
    /// </summary>
    internal static class KeySymbolPaths
    {
        private static readonly Dictionary<string, Geometry> _cache = new Dictionary<string, Geometry>();
        private static readonly HashSet<string> _symbolGridPositions;
        private static bool _loaded;

        static KeySymbolPaths()
        {
            // Build symbol grid position set from visual registry metadata.
            // A key has an SVG symbol if its RenderMode is Symbol (not TextWithOverlay — E14 uses font + overlay).
            _symbolGridPositions = new HashSet<string>(StringComparer.Ordinal);
            var allKeys = TDV2200KeyVisualRegistry.GetAllKeys();
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (allKeys[i].RenderMode == KeyRenderMode.Symbol && allKeys[i].SymbolId != null)
                {
                    _symbolGridPositions.Add(allKeys[i].GridPosition);
                }
            }
        }

        /// <summary>
        /// Ensure all SVG symbols are loaded and cached.
        /// Safe to call multiple times — only loads once.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            // Iterate the HashSet using enumerator (cannot index a HashSet)
            var enumerator = _symbolGridPositions.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var gridPos = enumerator.Current;
                try
                {
                    var uri = new Uri($"avares://RetroTerm.Desktop/Assets/KeySymbols/{gridPos}.svg");
                    using var stream = AssetLoader.Open(uri);
                    if (stream == null) continue;

                    var pathData = ExtractSvgPaths(stream);
                    if (!string.IsNullOrEmpty(pathData))
                    {
                        var geometry = StreamGeometry.Parse(pathData);
                        if (geometry != null)
                        {
                            _cache[gridPos] = geometry;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"KeySymbolPaths: Failed to load {gridPos}.svg: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Try to get the cached geometry for a grid position.
        /// </summary>
        public static bool TryGetSymbol(string gridPosition, [NotNullWhen(true)] out Geometry? geometry)
        {
            EnsureLoaded();
            return _cache.TryGetValue(gridPosition, out geometry);
        }

        /// <summary>
        /// Check if a grid position has an SVG symbol (without loading).
        /// </summary>
        public static bool HasSymbol(string gridPosition)
        {
            return _symbolGridPositions.Contains(gridPosition);
        }

        /// <summary>
        /// Extract all path 'd' attributes from an SVG stream and combine them into a single geometry string.
        /// Also handles circle elements by converting them to arc paths.
        /// </summary>
        private static string? ExtractSvgPaths(Stream stream)
        {
            var pathParts = new List<string>();

            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;

                if (reader.LocalName == "path")
                {
                    var d = reader.GetAttribute("d");
                    if (!string.IsNullOrEmpty(d))
                    {
                        pathParts.Add(d);
                    }
                }
                else if (reader.LocalName == "circle")
                {
                    // Convert <circle cx cy r> to a path arc
                    var cxStr = reader.GetAttribute("cx");
                    var cyStr = reader.GetAttribute("cy");
                    var rStr = reader.GetAttribute("r");
                    if (cxStr != null && cyStr != null && rStr != null)
                    {
                        if (double.TryParse(cxStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double cx)
                            && double.TryParse(cyStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double cy)
                            && double.TryParse(rStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double r))
                        {
                            // Circle as two arcs: move to left, arc to right, arc back
                            var inv = System.Globalization.CultureInfo.InvariantCulture;
                            pathParts.Add(string.Format(inv,
                                "M {0},{1} A {2},{2} 0 1 0 {3},{1} A {2},{2} 0 1 0 {0},{1}",
                                cx - r, cy, r, cx + r));
                        }
                    }
                }
            }

            if (pathParts.Count == 0) return null;
            if (pathParts.Count == 1) return pathParts[0];

            // Combine multiple paths with space separator
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pathParts.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(pathParts[i]);
            }
            return sb.ToString();
        }
    }
}
