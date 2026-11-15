using System;
using System.Collections.Generic;
using System.Reflection;
using Eto.Drawing;

namespace ObjectManager.Outliner
{
    internal static class RhinoStandardIconProvider
    {
        private const string AssemblyName = "Rhino.UI";
        private const string AssetsTypeName = "Rhino.Resources.Assets";
        private const string LayerImageTypeName = "Rhino.UI.DialogPanels.LayerImage";
        private const string ResourcePrefix = "Rhino.UI.Resources.";
        private static readonly object AssemblyLock = new object();
        private static readonly object AssetIconLock = new object();
        private static readonly object LayerImageLock = new object();
        private static readonly Dictionary<string, Image> AssetIcons =
            new Dictionary<string, Image>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Image> LayerImages =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static Assembly _rhinoUiAssembly;
        private static bool _assemblyResolved;
        private static object _assetIconCache;
        private static MethodInfo _assetIconTryGet;
        private static bool _assetIconCacheResolved;
        private static Type _layerImageType;
        private static bool _layerImageTypeResolved;

        public static Image LoadAssetIcon(string resourceName, int size = 16)
        {
            if (string.IsNullOrWhiteSpace(resourceName) || size < 1)
                return null;

            var cacheKey = resourceName + ":" + size;
            lock (AssetIconLock)
            {
                Image cached;
                if (AssetIcons.TryGetValue(cacheKey, out cached))
                    return cached;

                if (!ResolveAssetIconCache())
                    return null;

                try
                {
                    var image = _assetIconTryGet.Invoke(
                        _assetIconCache,
                        new object[] { resourceName, new Size(size, size) }) as Image;
                    if (image != null)
                        AssetIcons[cacheKey] = image;
                    return image;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static Image LoadLayerStateIcon(string resourceBaseName)
        {
            var layerPanelImage = LoadLayerPanelImage(resourceBaseName);
            if (layerPanelImage != null)
                return layerPanelImage;

            var assembly = GetRhinoUiAssembly();
            if (assembly == null || string.IsNullOrWhiteSpace(resourceBaseName))
                return null;

            try
            {
                var frames = new List<IconFrame>();
                for (var scale = 1; scale <= 3; scale++)
                {
                    var resourceName = GetResourceName(resourceBaseName, scale);
                    if (!HasResource(assembly, resourceName))
                        continue;

                    frames.Add(IconFrame.FromResource(scale, resourceName, assembly));
                }

                return frames.Count == 0 ? null : new Icon(frames);
            }
            catch
            {
                return null;
            }
        }

        public static Bitmap LoadLayerStateBitmap(
            string resourceBaseName,
            int scale = 1)
        {
            var layerPanelImage = LoadLayerPanelImage(resourceBaseName);
            if (layerPanelImage != null)
            {
                var size = 16 * Math.Max(1, scale);
                Bitmap bitmap = null;
                try
                {
                    bitmap = new Bitmap(size, size, PixelFormat.Format32bppRgba);
                    using (var graphics = new Graphics(bitmap))
                    {
                        graphics.Clear(Colors.Transparent);
                        graphics.DrawImage(
                            layerPanelImage,
                            new RectangleF(0, 0, size, size));
                    }

                    return bitmap;
                }
                catch
                {
                    bitmap?.Dispose();
                }
            }

            var assembly = GetRhinoUiAssembly();
            if (assembly == null || string.IsNullOrWhiteSpace(resourceBaseName))
                return null;

            var resourceName = GetResourceName(resourceBaseName, scale);
            try
            {
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                    return stream == null ? null : new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        private static Image LoadLayerPanelImage(string resourceBaseName)
        {
            if (string.IsNullOrWhiteSpace(resourceBaseName))
                return null;

            lock (LayerImageLock)
            {
                Image cached;
                if (LayerImages.TryGetValue(resourceBaseName, out cached))
                    return cached;

                var propertyName = GetLayerImagePropertyName(resourceBaseName);
                var layerImageType = GetLayerImageType();
                if (propertyName == null || layerImageType == null)
                    return null;

                try
                {
                    var property = layerImageType.GetProperty(
                        propertyName,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Static);
                    var image = property?.GetValue(null, null) as Image;
                    if (image != null)
                        LayerImages[resourceBaseName] = image;
                    return image;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static Type GetLayerImageType()
        {
            if (_layerImageTypeResolved)
                return _layerImageType;

            var assembly = GetRhinoUiAssembly();
            if (assembly == null)
                return null;

            _layerImageType = assembly.GetType(
                LayerImageTypeName,
                throwOnError: false,
                ignoreCase: false);
            _layerImageTypeResolved = true;
            return _layerImageType;
        }

        private static bool ResolveAssetIconCache()
        {
            if (_assetIconCacheResolved)
                return _assetIconCache != null && _assetIconTryGet != null;

            var assembly = GetRhinoUiAssembly();
            if (assembly == null)
                return false;

            try
            {
                var flags = BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static;
                var assetsType = assembly.GetType(
                    AssetsTypeName,
                    throwOnError: false,
                    ignoreCase: false);
                var rhinoAssets = assetsType?
                    .GetProperty("Rhino", flags)?
                    .GetValue(null, null);
                var etoAssets = assetsType?
                    .GetProperty("Eto", flags)?
                    .GetValue(rhinoAssets, null);
                _assetIconCache = etoAssets?
                    .GetType()
                    .GetProperty("Icons", flags)?
                    .GetValue(etoAssets, null);

                if (_assetIconCache != null)
                {
                    foreach (var method in _assetIconCache.GetType().GetMethods(flags))
                    {
                        if (!string.Equals(
                            method.Name,
                            "TryGet",
                            StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var parameters = method.GetParameters();
                        if (parameters.Length == 2 &&
                            parameters[0].ParameterType == typeof(string) &&
                            parameters[1].ParameterType == typeof(Size))
                        {
                            _assetIconTryGet = method;
                            break;
                        }
                    }
                }
            }
            catch
            {
                _assetIconCache = null;
                _assetIconTryGet = null;
            }

            _assetIconCacheResolved = true;
            return _assetIconCache != null && _assetIconTryGet != null;
        }

        private static string GetLayerImagePropertyName(string resourceBaseName)
        {
            switch (resourceBaseName)
            {
                case "layer_current":
                    return "Current";
                case "layer_visible":
                    return "On";
                case "layer_hidden":
                    return "Off";
                case "layer_visible_persist":
                    return "OffPersist";
                case "layer_locked":
                    return "Lock";
                case "layer_unlocked":
                    return "Unlock";
                case "layer_unlocked_persist":
                    return "LockPersist";
                case "layer_new_detail_on":
                    return "NewDetailOn";
                case "layer_new_detail_off":
                    return "NewDetailOff";
                case "layer_model_on":
                    return "ViewportOn";
                case "layer_model_off":
                    return "ViewportOff";
                case "layer_model_visible_persist":
                    return "ViewportOffPersist";
                default:
                    return null;
            }
        }

        private static Assembly GetRhinoUiAssembly()
        {
            if (_assemblyResolved)
                return _rhinoUiAssembly;

            lock (AssemblyLock)
            {
                if (_assemblyResolved)
                    return _rhinoUiAssembly;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(
                        assembly.GetName().Name,
                        AssemblyName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _rhinoUiAssembly = assembly;
                        break;
                    }
                }

                if (_rhinoUiAssembly == null)
                {
                    try
                    {
                        _rhinoUiAssembly = Assembly.Load(AssemblyName);
                    }
                    catch
                    {
                        _rhinoUiAssembly = null;
                    }
                }

                _assemblyResolved = true;
                return _rhinoUiAssembly;
            }
        }

        private static bool HasResource(Assembly assembly, string resourceName)
        {
            try
            {
                return assembly.GetManifestResourceInfo(resourceName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetResourceName(string resourceBaseName, int scale)
        {
            var scaleSuffix = scale <= 1 ? string.Empty : "@" + scale + "x";
            return ResourcePrefix + resourceBaseName + scaleSuffix + ".png";
        }
    }
}
