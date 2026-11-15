using System;
using System.Collections.Generic;
using Eto.Drawing;

namespace ObjectManager.Outliner
{
    internal sealed class OutlinerIconFactory
    {
        private const int IconSize = 16;
        private readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>();
        private bool _darkMode;

        public OutlinerIconFactory()
        {
            _darkMode = Rhino.Runtime.HostUtils.RunningInDarkMode;
        }

        public bool RefreshTheme()
        {
            var darkMode = Rhino.Runtime.HostUtils.RunningInDarkMode;
            if (_darkMode == darkMode)
                return false;

            _darkMode = darkMode;
            _cache.Clear();
            return true;
        }

        public Image GetLayerIcon(System.Drawing.Color layerColor)
        {
            var key = "layer:" + layerColor.ToArgb();
            Image image;
            if (_cache.TryGetValue(key, out image))
                return image;

            var color = Color.FromArgb(layerColor.R, layerColor.G, layerColor.B, layerColor.A);
            image = CreateLayerIcon(color);
            _cache.Add(key, image);
            return image;
        }

        public Image GetObjectIcon(OutlinerIconKind kind, bool muted)
        {
            var key = "object:" + kind + ":" + muted + ":" + _darkMode;
            Image image;
            if (_cache.TryGetValue(key, out image))
                return image;

            image = LoadRhinoObjectIcon(kind, muted, _darkMode) ??
                CreateObjectIcon(kind, muted);
            _cache.Add(key, image);
            return image;
        }

        public Image GetGroupIcon()
        {
            return GetOrCreate(
                "relationship:group",
                () => LoadRhinoAssetIcon(
                    "Rhino.UI.Resources.svg.ObjectManager_Group.svg") ??
                    CreateGroupIcon());
        }

        public Image GetFavoriteIcon(bool favorite, bool disabled = false)
        {
            return GetOrCreate(
                "state:favorite:" + favorite + ":" + disabled,
                () => CreateFavoriteIcon(favorite, disabled));
        }

        public Image GetNoteIcon(bool hasNote)
        {
            return GetOrCreate("note:" + hasNote + ":" + _darkMode, () =>
            {
                var native = LoadRhinoAssetIcon("Rhino.UI.Resources.svg." +
                    (hasNote ? "pencil.svg" : "pencil_gray.svg"));
                if (native != null) return native;
                var bitmap = NewBitmap();
                using (var g = new Graphics(bitmap))
                using (var pen = new Pen(hasNote ? Colors.Orange : Color.FromGrayscale(_darkMode ? 0.65f : 0.45f), 1.4f))
                {
                    g.AntiAlias = true;
                    g.DrawLines(pen, new[] { new PointF(2, 14), new PointF(3, 10),
                        new PointF(11, 2), new PointF(14, 5), new PointF(6, 13), new PointF(2, 14) });
                    if (hasNote) g.DrawLine(pen, 5, 11, 12, 4);
                }
                return bitmap;
            });
        }

        public Image GetDeleteNoteIcon()
        {
            return GetOrCreate("note-trash:" + _darkMode, () =>
            {
                var bitmap = NewBitmap();
                using (var g = new Graphics(bitmap))
                using (var pen = new Pen(Colors.IndianRed, 2))
                {
                    g.DrawLine(pen, 4, 4, 12, 12);
                    g.DrawLine(pen, 12, 4, 4, 12);
                }
                return bitmap;
            });
        }

        public Image GetZoomIcon()
        {
            return GetOrCreate("zoom:" + _darkMode, () =>
            {
                var bitmap = NewBitmap();
                using (var g = new Graphics(bitmap))
                using (var pen = new Pen(Color.FromGrayscale(_darkMode ? 0.86f : 0.24f), 1.5f))
                {
                    g.DrawEllipse(pen, 2, 2, 8, 8);
                    g.DrawLine(pen, 9, 9, 14, 14);
                }
                return bitmap;
            });
        }

        public Image GetCurrentLayerIcon()
        {
            return GetOrCreate(
                "state:current",
                () => LoadOfficialIcon("current") ?? CreateCurrentLayerIcon());
        }

        public Image GetVisibilityIcon(bool visible, bool inheritedHidden = false)
        {
            var resourceName = !visible
                ? "hidden"
                : inheritedHidden ? "visible_gray" : "visible";
            return GetOrCreate(
                "state:visible:" + visible + ":" + inheritedHidden,
                () => LoadOfficialIcon(resourceName) ?? CreateVisibilityIcon(visible));
        }

        public Image GetNewDetailVisibilityIcon(bool visible)
        {
            return GetOrCreate(
                "state:new-detail-visible:" + visible,
                () => LoadOfficialIcon(
                    visible ? "new_detail_on" : "new_detail_off") ??
                    CreateNewDetailVisibilityIcon(visible));
        }

        public Image GetModelVisibilityIcon(
            bool visible,
            bool inheritedHidden = false)
        {
            var resourceName = !visible
                ? "model_off"
                : inheritedHidden ? "model_visible_persist" : "model_on";
            return GetOrCreate(
                "state:model-visible:" + visible + ":" + inheritedHidden,
                () => LoadOfficialIcon(resourceName) ??
                    CreateModelVisibilityIcon(visible));
        }

        public Image GetIsolateTargetIcon()
        {
            return GetOrCreate(
                "state:isolate:target",
                () => CreateIsolateIcon(true));
        }

        public Image GetIsolateSuppressedIcon()
        {
            return GetOrCreate(
                "state:isolate:suppressed",
                () => CreateIsolateIcon(false));
        }

        public Image GetIsolateLockTargetIcon()
        {
            return GetOrCreate(
                "state:isolate-lock:target",
                () => CreateIsolateLockIcon(true));
        }

        public Image GetIsolateLockSuppressedIcon()
        {
            return GetOrCreate(
                "state:isolate-lock:suppressed",
                () => CreateIsolateLockIcon(false));
        }

        public Image GetLockIcon(bool locked, bool inheritedLocked = false)
        {
            if (inheritedLocked && !locked)
            {
                return GetOrCreate(
                    "state:locked:inherited",
                    CreateInheritedLockIcon);
            }

            return GetOrCreate(
                "state:locked:" + locked,
                () => LoadOfficialIcon(locked ? "locked" : "unlocked") ??
                      CreateLockIcon(locked));
        }

        public Image GetMaterialSwatch(bool assigned)
        {
            return GetOrCreate(
                "material:" + assigned,
                () => CreateMaterialSwatch(assigned));
        }

        public Image GetColorSwatch(System.Drawing.Color layerColor)
        {
            return GetOrCreate(
                "swatch:" + layerColor.ToArgb(),
                () => CreateColorSwatch(Color.FromArgb(
                    layerColor.R,
                    layerColor.G,
                    layerColor.B,
                    layerColor.A)));
        }

        public Image GetAddLayerIcon()
        {
            return GetOrCreate(
                "toolbar:add-layer",
                () => LoadRhinoAddLayerIcon() ??
                    CreateAddLayerIcon());
        }

        public Image GetAddSublayerIcon()
        {
            return GetOrCreate(
                "toolbar:add-sublayer",
                () => LoadRhinoAssetIcon(
                    "Rhino.UI.Resources.LayerSubLayer.svg") ??
                    CreateAddSublayerIcon());
        }

        public Image GetDeleteLayerIcon()
        {
            return GetOrCreate(
                "toolbar:delete-layer",
                () => LoadRhinoAssetIcon(
                    "Rhino.UI.Resources.svg.named_item_delete_hot.svg") ??
                    CreateDeleteLayerIcon());
        }

        public Image GetColumnChooserIcon()
        {
            return GetOrCreate(
                "toolbar:columns",
                () => LoadRhinoAssetIcon(
                    "Rhino.UI.Resources.LayoutMenu.svg") ??
                    CreateColumnChooserIcon());
        }

        public Image GetHistoryStackIcon()
        {
            return GetOrCreate(
                "toolbar:history-stack",
                () => LoadRhinoAssetIcon(
                    "Rhino.UI.Resources.svg.History.svg") ??
                    GetObjectIcon(OutlinerIconKind.Curve, false));
        }

        public Image GetDisplayModeIcon(OutlinerDisplayMode mode)
        {
            return GetOrCreate(
                "toolbar:display-mode:" + mode,
                () => CreateDisplayModeIcon(mode));
        }

        public Image GetLayerFilterIcon(bool active, bool alert = false)
        {
            return GetOrCreate(
                "toolbar:layer-filter:" + active + ":" + alert,
                () => LoadRhinoFilterIcon(active, alert) ??
                    CreateFilterIcon(true, active, alert));
        }

        public Image GetObjectFilterIcon(bool active, bool alert = false)
        {
            return GetOrCreate(
                "toolbar:object-filter:" + active + ":" + alert,
                () => LoadRhinoFilterIcon(active, alert) ??
                    CreateFilterIcon(false, active, alert));
        }

        private Image GetOrCreate(string key, Func<Image> factory)
        {
            Image image;
            if (_cache.TryGetValue(key, out image))
                return image;

            image = factory();
            _cache.Add(key, image);
            return image;
        }

        private static Image LoadOfficialIcon(string name)
        {
            var resourceBaseName = GetOfficialResourceBaseName(name);
            return resourceBaseName == null
                ? null
                : RhinoStandardIconProvider.LoadLayerStateIcon(resourceBaseName);
        }

        private static Image LoadRhinoAssetIcon(string resourceName)
        {
            return RhinoStandardIconProvider.LoadAssetIcon(resourceName, IconSize);
        }

        private static Image LoadRhinoFilterIcon(bool active, bool alert)
        {
            var resourceName = alert
                ? "Rhino.UI.Resources.FilterMenuRed.svg"
                : active
                    ? "Rhino.UI.Resources.FilterMenuGreen.svg"
                    : "Rhino.UI.Resources.FilterMenuBlue.svg";
            return LoadRhinoAssetIcon(resourceName);
        }

        private static Image LoadRhinoAddLayerIcon()
        {
            var layerIcon = LoadRhinoAssetIcon(
                "Rhino.UI.Resources.tabs.TabLayers.svg");
            if (layerIcon == null)
                return null;

            var plusOverlay = LoadRhinoAssetIcon(
                "Rhino.UI.Resources.svg.Plus_overlay_for_panels.svg");
            return plusOverlay == null
                ? layerIcon
                : CreateCompositeImage(layerIcon, plusOverlay);
        }

        private static Image LoadRhinoObjectIcon(
            OutlinerIconKind kind,
            bool muted,
            bool darkMode)
        {
            var resourceName = GetRhinoObjectResourceName(kind, darkMode);
            var image = resourceName == null
                ? null
                : LoadRhinoAssetIcon(resourceName);
            if (image == null || !muted)
                return image;

            return CreateMutedImage(image);
        }

        private static string GetRhinoObjectResourceName(
            OutlinerIconKind kind,
            bool darkMode)
        {
            const string prefix = "Rhino.UI.Resources.svg.ObjectManager_";
            switch (kind)
            {
                case OutlinerIconKind.Point:
                    return prefix + "Point.svg";
                case OutlinerIconKind.PointCloud:
                    return prefix + "Point_set.svg";
                case OutlinerIconKind.Curve:
                    return darkMode
                        ? "Rhino.UI.Resources.svg.Curve_small_auto-darkmode.svg"
                        : "Rhino.UI.Resources.svg.Curve_small.svg";
                case OutlinerIconKind.Surface:
                    return prefix + "Surface.svg";
                case OutlinerIconKind.Polysurface:
                    return prefix + "Polysurface.svg";
                case OutlinerIconKind.Extrusion:
                    return prefix + "Extrusion.svg";
                case OutlinerIconKind.Mesh:
                    return prefix + "Mesh.svg";
                case OutlinerIconKind.SubD:
                    return prefix + "SubD.svg";
                case OutlinerIconKind.Block:
                    return prefix + "BlockInstance.svg";
                case OutlinerIconKind.Annotation:
                    return prefix + "Text.svg";
                case OutlinerIconKind.Light:
                    return prefix + "Light.svg";
                case OutlinerIconKind.Hatch:
                    return prefix + "Hatch.svg";
                default:
                    return prefix + "Object.svg";
            }
        }

        private static Image CreateMutedImage(Image source)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            {
                graphics.Clear(Colors.Transparent);
                graphics.DrawImage(
                    source,
                    new RectangleF(0, 0, IconSize, IconSize));
            }

            for (var y = 0; y < IconSize; y++)
            {
                for (var x = 0; x < IconSize; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    bitmap.SetPixel(
                        x,
                        y,
                        Color.FromArgb(
                            (int)Math.Round(color.R * 255f),
                            (int)Math.Round(color.G * 255f),
                            (int)Math.Round(color.B * 255f),
                            (int)Math.Round(color.A * 255f * 0.42f)));
                }
            }

            return bitmap;
        }

        private static Bitmap LoadOfficialBitmap(string name)
        {
            var resourceBaseName = GetOfficialResourceBaseName(name);
            return resourceBaseName == null
                ? null
                : RhinoStandardIconProvider.LoadLayerStateBitmap(resourceBaseName);
        }

        private static string GetOfficialResourceBaseName(string name)
        {
            switch (name)
            {
                case "current":
                    return "layer_current";
                case "visible":
                    return "layer_visible";
                case "hidden":
                    return "layer_hidden";
                case "visible_gray":
                    return "layer_visible_persist";
                case "locked":
                    return "layer_locked";
                case "unlocked":
                    return "layer_unlocked";
                case "locked_gray":
                    return "layer_unlocked_persist";
                case "new_detail_on":
                    return "layer_new_detail_on";
                case "new_detail_off":
                    return "layer_new_detail_off";
                case "model_on":
                    return "layer_model_on";
                case "model_off":
                    return "layer_model_off";
                case "model_visible_persist":
                    return "layer_model_visible_persist";
                default:
                    return null;
            }
        }

        private static Image CreateInheritedLockIcon()
        {
            return LoadOfficialIcon("locked_gray") ?? CreateLockIcon(true);
        }

        private static Image CreateLayerIcon(Color color)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var border = new Pen(Color.FromGrayscale(0.35f), 1f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.FillRectangle(color, 2, 3, 12, 10);
                graphics.DrawRectangle(border, 2, 3, 12, 10);
            }

            return bitmap;
        }

        private static Image CreateObjectIcon(OutlinerIconKind kind, bool muted)
        {
            var color = GetCategoryColor(kind, muted ? 120 : 230);
            var fill = GetCategoryColor(kind, muted ? 30 : 70);
            var bitmap = NewBitmap();

            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(color, 1.4f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);

                switch (kind)
                {
                    case OutlinerIconKind.Point:
                        graphics.FillEllipse(color, 5, 5, 6, 6);
                        break;
                    case OutlinerIconKind.PointCloud:
                        DrawPointCloud(graphics, color);
                        break;
                    case OutlinerIconKind.Curve:
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(2, 11),
                            new PointF(5, 5),
                            new PointF(9, 4),
                            new PointF(14, 10)
                        });
                        break;
                    case OutlinerIconKind.Surface:
                        DrawSurface(graphics, pen, fill);
                        break;
                    case OutlinerIconKind.Polysurface:
                        DrawPolysurface(graphics, pen, fill);
                        break;
                    case OutlinerIconKind.Extrusion:
                        DrawExtrusion(graphics, pen, fill);
                        break;
                    case OutlinerIconKind.Mesh:
                        DrawMesh(graphics, pen, fill);
                        break;
                    case OutlinerIconKind.SubD:
                        graphics.FillEllipse(fill, 2, 3, 12, 10);
                        graphics.DrawEllipse(pen, 2, 3, 12, 10);
                        graphics.DrawLine(pen, 4, 11, 12, 5);
                        break;
                    case OutlinerIconKind.Block:
                        graphics.DrawRectangle(pen, 2, 5, 8, 8);
                        graphics.DrawRectangle(pen, 6, 2, 8, 8);
                        break;
                    case OutlinerIconKind.Annotation:
                        graphics.DrawLine(pen, 3, 4, 13, 4);
                        graphics.DrawLine(pen, 3, 8, 11, 8);
                        graphics.DrawLine(pen, 3, 12, 9, 12);
                        break;
                    case OutlinerIconKind.Light:
                        graphics.DrawEllipse(pen, 5, 5, 6, 6);
                        graphics.FillEllipse(fill, 6, 6, 4, 4);
                        graphics.DrawLine(pen, 8, 1, 8, 3);
                        graphics.DrawLine(pen, 8, 13, 8, 15);
                        graphics.DrawLine(pen, 1, 8, 3, 8);
                        graphics.DrawLine(pen, 13, 8, 15, 8);
                        break;
                    case OutlinerIconKind.Hatch:
                        graphics.DrawRectangle(pen, 2, 2, 12, 12);
                        graphics.DrawLine(pen, 2, 7, 7, 2);
                        graphics.DrawLine(pen, 2, 13, 13, 2);
                        graphics.DrawLine(pen, 8, 14, 14, 8);
                        break;
                    default:
                        graphics.DrawEllipse(pen, 3, 3, 10, 10);
                        graphics.FillEllipse(color, 7, 7, 2, 2);
                        break;
                }
            }

            return bitmap;
        }

        private static Image CreateGroupIcon()
        {
            var bitmap = NewBitmap();
            var stroke = Color.FromGrayscale(0.55f, 0.95f);
            var fill = Color.FromGrayscale(0.55f, 0.22f);
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(stroke, 1.2f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawLine(pen, 4.5f, 5.5f, 11.5f, 5.5f);
                graphics.DrawLine(pen, 4.5f, 5.5f, 8f, 11.5f);
                graphics.DrawLine(pen, 11.5f, 5.5f, 8f, 11.5f);
                DrawGroupNode(graphics, pen, fill, 4.5f, 5.5f);
                DrawGroupNode(graphics, pen, fill, 11.5f, 5.5f);
                DrawGroupNode(graphics, pen, fill, 8f, 11.5f);
            }

            return bitmap;
        }

        private static Image CreateFavoriteIcon(bool favorite, bool disabled)
        {
            var bitmap = NewBitmap();
            var outline = disabled
                ? Color.FromGrayscale(0.55f, 0.7f)
                : favorite
                    ? Color.FromArgb(181, 112, 0)
                    : Color.FromGrayscale(0.48f, 0.9f);
            var fill = disabled
                ? Color.FromGrayscale(0.6f, favorite ? 0.35f : 0.08f)
                : favorite
                    ? Color.FromArgb(255, 184, 32)
                    : Color.FromGrayscale(0.6f, 0.08f);
            var points = new PointF[10];
            const float centerX = 8f;
            const float centerY = 8f;
            for (var index = 0; index < points.Length; index++)
            {
                var angle = -Math.PI / 2 + index * Math.PI / 5;
                var radius = index % 2 == 0 ? 6f : 2.7f;
                points[index] = new PointF(
                    centerX + (float)Math.Cos(angle) * radius,
                    centerY + (float)Math.Sin(angle) * radius);
            }

            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(outline, 1.1f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.FillPolygon(fill, points);
                graphics.DrawPolygon(pen, points);
            }

            return bitmap;
        }

        private static void DrawGroupNode(
            Graphics graphics,
            Pen pen,
            Color fill,
            float centerX,
            float centerY)
        {
            const float radius = 2.2f;
            graphics.FillEllipse(
                fill,
                centerX - radius,
                centerY - radius,
                radius * 2,
                radius * 2);
            graphics.DrawEllipse(
                pen,
                centerX - radius,
                centerY - radius,
                radius * 2,
                radius * 2);
        }

        private static Bitmap NewBitmap()
        {
            return new Bitmap(IconSize, IconSize, PixelFormat.Format32bppRgba);
        }

        private static Image CreateCurrentLayerIcon()
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(Color.FromGrayscale(0.16f), 1.8f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawLines(pen, new[]
                {
                    new PointF(3, 8),
                    new PointF(6.5f, 11.5f),
                    new PointF(13, 4)
                });
            }

            return bitmap;
        }

        private static Image CreateVisibilityIcon(bool visible)
        {
            var bitmap = NewBitmap();
            var color = visible
                ? Color.FromArgb(238, 190, 28)
                : Color.FromGrayscale(0.55f);
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(color, 1.2f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawEllipse(pen, 4, 2, 8, 9);
                graphics.DrawLine(pen, 6, 12, 10, 12);
                graphics.DrawLine(pen, 6.5f, 14, 9.5f, 14);
                if (visible)
                    graphics.FillEllipse(Color.FromArgb(255, 214, 48), 6, 4, 4, 5);
                else
                    graphics.DrawLine(pen, 2, 14, 14, 2);
            }

            return bitmap;
        }

        private static Image CreateNewDetailVisibilityIcon(bool visible)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(
                visible
                    ? Color.FromArgb(238, 190, 28)
                    : Color.FromGrayscale(0.55f),
                1.2f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawLine(pen, 5, 8, 11, 8);
                graphics.DrawLine(pen, 8, 5, 8, 11);
                graphics.DrawLine(pen, 3.5f, 3.5f, 5.5f, 5.5f);
                graphics.DrawLine(pen, 10.5f, 10.5f, 12.5f, 12.5f);
                DrawViewportFrame(graphics, pen);
            }

            return bitmap;
        }

        private static Image CreateModelVisibilityIcon(bool visible)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(
                visible
                    ? Color.FromArgb(238, 190, 28)
                    : Color.FromGrayscale(0.55f),
                1.1f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawEllipse(pen, 5, 3, 6, 7);
                graphics.DrawLine(pen, 6.5f, 11, 9.5f, 11);
                graphics.DrawLine(pen, 7, 13, 9, 13);
                DrawViewportFrame(graphics, pen);
            }

            return bitmap;
        }

        private static void DrawViewportFrame(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 1.5f, 4.5f, 1.5f, 1.5f);
            graphics.DrawLine(pen, 1.5f, 1.5f, 4.5f, 1.5f);
            graphics.DrawLine(pen, 11.5f, 1.5f, 14.5f, 1.5f);
            graphics.DrawLine(pen, 14.5f, 1.5f, 14.5f, 4.5f);
            graphics.DrawLine(pen, 1.5f, 11.5f, 1.5f, 14.5f);
            graphics.DrawLine(pen, 1.5f, 14.5f, 4.5f, 14.5f);
            graphics.DrawLine(pen, 11.5f, 14.5f, 14.5f, 14.5f);
            graphics.DrawLine(pen, 14.5f, 11.5f, 14.5f, 14.5f);
        }

        private static Image CreateCompositeImage(Image background, Image overlay)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            {
                graphics.Clear(Colors.Transparent);
                graphics.DrawImage(
                    background,
                    new RectangleF(0, 0, IconSize, IconSize));
                graphics.DrawImage(
                    overlay,
                    new RectangleF(0, 0, IconSize, IconSize));
            }

            return bitmap;
        }

        private static Image CreateIsolateIcon(bool target)
        {
            var source = LoadOfficialBitmap(target ? "visible" : "visible_gray") ??
                CreateVisibilityIcon(target) as Bitmap;
            var bitmap = NewBitmap();
            using (source)
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(
                target
                    ? Color.FromArgb(235, 126, 24)
                    : Color.FromGrayscale(0.48f),
                1.2f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                if (source != null)
                    graphics.DrawImage(source, new RectangleF(0, 0, IconSize, IconSize));

                graphics.DrawLine(pen, 1.5f, 4.5f, 1.5f, 1.5f);
                graphics.DrawLine(pen, 1.5f, 1.5f, 4.5f, 1.5f);
                graphics.DrawLine(pen, 11.5f, 1.5f, 14.5f, 1.5f);
                graphics.DrawLine(pen, 14.5f, 1.5f, 14.5f, 4.5f);
                graphics.DrawLine(pen, 1.5f, 11.5f, 1.5f, 14.5f);
                graphics.DrawLine(pen, 1.5f, 14.5f, 4.5f, 14.5f);
                graphics.DrawLine(pen, 11.5f, 14.5f, 14.5f, 14.5f);
                graphics.DrawLine(pen, 14.5f, 11.5f, 14.5f, 14.5f);
            }

            return bitmap;
        }

        private static Image CreateLockIcon(bool locked)
        {
            var bitmap = NewBitmap();
            var color = locked
                ? Color.FromArgb(225, 169, 19)
                : Color.FromGrayscale(0.48f);
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(color, 1.3f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawRectangle(pen, 4, 7, 9, 7);
                if (locked)
                {
                    graphics.DrawArc(pen, 5.5f, 2, 6, 8, 180, 180);
                }
                else
                {
                    graphics.DrawArc(pen, 1.5f, 2, 7, 8, 180, 180);
                    graphics.DrawLine(pen, 1.5f, 6, 1.5f, 8);
                }
            }

            return bitmap;
        }

        private static Image CreateIsolateLockIcon(bool target)
        {
            var source = LoadOfficialBitmap(target ? "unlocked" : "locked_gray") ??
                CreateLockIcon(!target) as Bitmap;
            var bitmap = NewBitmap();
            using (source)
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(
                target
                    ? Color.FromArgb(235, 126, 24)
                    : Color.FromGrayscale(0.48f),
                1.2f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                if (source != null)
                    graphics.DrawImage(source, new RectangleF(0, 0, IconSize, IconSize));

                graphics.DrawLine(pen, 1.5f, 4.5f, 1.5f, 1.5f);
                graphics.DrawLine(pen, 1.5f, 1.5f, 4.5f, 1.5f);
                graphics.DrawLine(pen, 11.5f, 1.5f, 14.5f, 1.5f);
                graphics.DrawLine(pen, 14.5f, 1.5f, 14.5f, 4.5f);
                graphics.DrawLine(pen, 1.5f, 11.5f, 1.5f, 14.5f);
                graphics.DrawLine(pen, 1.5f, 14.5f, 4.5f, 14.5f);
                graphics.DrawLine(pen, 11.5f, 14.5f, 14.5f, 14.5f);
                graphics.DrawLine(pen, 14.5f, 11.5f, 14.5f, 14.5f);
            }

            return bitmap;
        }

        private static Image CreateColorSwatch(Color color)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var border = new Pen(Color.FromGrayscale(0.35f), 1f))
            {
                graphics.Clear(Colors.Transparent);
                graphics.FillRectangle(color, 2, 2, 12, 12);
                graphics.DrawRectangle(border, 2, 2, 12, 12);
            }

            return bitmap;
        }

        private static Image CreateMaterialSwatch(bool assigned)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var border = new Pen(Color.FromGrayscale(0.45f), 1f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.FillEllipse(
                    assigned ? Color.FromGrayscale(0.72f) : Colors.White,
                    2,
                    2,
                    12,
                    12);
                graphics.DrawEllipse(border, 2, 2, 12, 12);
            }

            return bitmap;
        }

        private static Image CreateAddLayerIcon()
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(Color.FromGrayscale(0.25f), 1.4f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawRectangle(pen, 2, 7, 8, 6);
                graphics.DrawLine(pen, 12, 2, 12, 8);
                graphics.DrawLine(pen, 9, 5, 15, 5);
            }

            return bitmap;
        }

        private static Image CreateAddSublayerIcon()
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(Color.FromGrayscale(0.25f), 1.3f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawLine(pen, 2, 3, 2, 12);
                graphics.DrawLine(pen, 2, 7, 6, 7);
                graphics.DrawRectangle(pen, 6, 9, 7, 5);
                graphics.DrawLine(pen, 11, 1, 11, 7);
                graphics.DrawLine(pen, 8, 4, 14, 4);
            }

            return bitmap;
        }

        private static Image CreateDeleteLayerIcon()
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(Color.FromGrayscale(0.3f), 1.3f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawLine(pen, 4, 5, 12, 5);
                graphics.DrawLine(pen, 6, 3, 10, 3);
                graphics.DrawRectangle(pen, 5, 6, 6, 8);
                graphics.DrawLine(pen, 7, 8, 7, 12);
                graphics.DrawLine(pen, 9, 8, 9, 12);
            }

            return bitmap;
        }

        private static Image CreateColumnChooserIcon()
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(Color.FromGrayscale(0.3f), 1f))
            {
                graphics.Clear(Colors.Transparent);
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                        graphics.DrawRectangle(pen, 2 + column * 4, 2 + row * 4, 3, 3);
                }
            }

            return bitmap;
        }

        private static Image CreateDisplayModeIcon(OutlinerDisplayMode mode)
        {
            var bitmap = NewBitmap();
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(Color.FromGrayscale(0.3f), 1.2f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                graphics.DrawRectangle(pen, 1.5f, 2, 3, 3);
                graphics.DrawLine(pen, 6, 3.5f, 14, 3.5f);
                if (mode == OutlinerDisplayMode.LayersOnly)
                    return bitmap;

                graphics.DrawEllipse(pen, 4.5f, 7, 3, 3);
                graphics.DrawLine(pen, 9, 8.5f, 14, 8.5f);
                if (mode == OutlinerDisplayMode.Structure)
                    return bitmap;

                graphics.DrawLine(pen, 8, 13, 14, 13);
            }

            return bitmap;
        }

        private static Image CreateFilterIcon(
            bool includeLayers,
            bool active,
            bool alert)
        {
            var bitmap = NewBitmap();
            var color = active
                ? Color.FromArgb(235, 126, 24)
                : Color.FromGrayscale(0.35f);
            using (var graphics = new Graphics(bitmap))
            using (var pen = new Pen(color, 1.4f))
            {
                graphics.AntiAlias = true;
                graphics.Clear(Colors.Transparent);
                var offset = includeLayers ? 3f : 0f;
                if (includeLayers)
                {
                    graphics.DrawLine(pen, 1, 3, 5, 3);
                    graphics.DrawLine(pen, 1, 7, 5, 7);
                    graphics.DrawLine(pen, 1, 11, 5, 11);
                }
                graphics.DrawLines(pen, new[]
                {
                    new PointF(2 + offset, 2),
                    new PointF(14, 2),
                    new PointF(10, 7),
                    new PointF(10, 13),
                    new PointF(7, 15),
                    new PointF(7, 7),
                    new PointF(2 + offset, 2)
                });
                if (alert)
                {
                    graphics.FillEllipse(
                        Color.FromArgb(235, 126, 24),
                        11,
                        11,
                        5,
                        5);
                }
            }
            return bitmap;
        }

        private static Color GetCategoryColor(OutlinerIconKind kind, int alpha)
        {
            switch (kind)
            {
                case OutlinerIconKind.Point:
                    return Color.FromArgb(222, 172, 46, alpha);
                case OutlinerIconKind.PointCloud:
                    return Color.FromArgb(32, 157, 146, alpha);
                case OutlinerIconKind.Curve:
                    return Color.FromArgb(45, 132, 197, alpha);
                case OutlinerIconKind.Surface:
                    return Color.FromArgb(53, 154, 91, alpha);
                case OutlinerIconKind.Polysurface:
                    return Color.FromArgb(224, 126, 44, alpha);
                case OutlinerIconKind.Extrusion:
                    return Color.FromArgb(213, 84, 73, alpha);
                case OutlinerIconKind.Mesh:
                    return Color.FromArgb(103, 116, 130, alpha);
                case OutlinerIconKind.SubD:
                    return Color.FromArgb(173, 79, 146, alpha);
                case OutlinerIconKind.Block:
                    return Color.FromArgb(64, 103, 187, alpha);
                case OutlinerIconKind.Annotation:
                    return Color.FromArgb(185, 74, 74, alpha);
                case OutlinerIconKind.Light:
                    return Color.FromArgb(218, 167, 34, alpha);
                case OutlinerIconKind.Hatch:
                    return Color.FromArgb(105, 117, 91, alpha);
                default:
                    return Color.FromArgb(112, 112, 112, alpha);
            }
        }

        private static void DrawPointCloud(Graphics graphics, Color color)
        {
            graphics.FillEllipse(color, 2, 3, 3, 3);
            graphics.FillEllipse(color, 9, 2, 3, 3);
            graphics.FillEllipse(color, 6, 7, 3, 3);
            graphics.FillEllipse(color, 11, 11, 3, 3);
            graphics.FillEllipse(color, 2, 11, 3, 3);
        }

        private static void DrawSurface(Graphics graphics, Pen pen, Color fill)
        {
            var points = new[]
            {
                new PointF(2, 5),
                new PointF(12, 2),
                new PointF(14, 11),
                new PointF(4, 14)
            };
            graphics.FillPolygon(fill, points);
            graphics.DrawPolygon(pen, points);
        }

        private static void DrawPolysurface(Graphics graphics, Pen pen, Color fill)
        {
            var front = new[]
            {
                new PointF(2, 6),
                new PointF(9, 4),
                new PointF(9, 12),
                new PointF(2, 14)
            };
            var back = new[]
            {
                new PointF(7, 3),
                new PointF(14, 1),
                new PointF(14, 9),
                new PointF(9, 12),
                new PointF(9, 4)
            };
            graphics.FillPolygon(fill, back);
            graphics.DrawPolygon(pen, back);
            graphics.FillPolygon(fill, front);
            graphics.DrawPolygon(pen, front);
        }

        private static void DrawExtrusion(Graphics graphics, Pen pen, Color fill)
        {
            graphics.FillRectangle(fill, 2, 6, 8, 8);
            graphics.DrawRectangle(pen, 2, 6, 8, 8);
            graphics.DrawRectangle(pen, 6, 2, 8, 8);
            graphics.DrawLine(pen, 2, 6, 6, 2);
            graphics.DrawLine(pen, 10, 6, 14, 2);
            graphics.DrawLine(pen, 10, 14, 14, 10);
        }

        private static void DrawMesh(Graphics graphics, Pen pen, Color fill)
        {
            var points = new[]
            {
                new PointF(2, 13),
                new PointF(8, 2),
                new PointF(14, 13)
            };
            graphics.FillPolygon(fill, points);
            graphics.DrawPolygon(pen, points);
            graphics.DrawLine(pen, 5, 8, 11, 8);
            graphics.DrawLine(pen, 8, 2, 8, 13);
        }
    }
}
