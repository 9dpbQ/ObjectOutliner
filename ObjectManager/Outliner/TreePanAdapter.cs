using System;
using System.Linq;
using System.Reflection;
using Eto.Forms;

namespace ObjectManager.Outliner
{
    internal interface ITreePanAdapter : IDisposable
    {
        bool HandlesPointerInput { get; }

        bool TryBegin();

        void Pan(float horizontalDrag, float verticalDrag);

        void End();
    }

    internal static class TreePanAdapterFactory
    {
        public static ITreePanAdapter Create(
            TreeGridView tree,
            Action panStarted,
            Action panEnded)
        {
            var handler = tree?.Handler;
            if (handler != null &&
                string.Equals(
                    handler.GetType().Assembly.GetName().Name,
                    "Eto.macOS",
                    StringComparison.Ordinal))
            {
                var adapter = MacTreePanAdapter.TryCreate(
                    handler,
                    panStarted,
                    panEnded);
                if (adapter != null)
                    return adapter;
            }

            return NullTreePanAdapter.Instance;
        }
    }

    internal sealed class NullTreePanAdapter : ITreePanAdapter
    {
        public static readonly NullTreePanAdapter Instance =
            new NullTreePanAdapter();

        private NullTreePanAdapter()
        {
        }

        public bool HandlesPointerInput => false;

        public bool TryBegin()
        {
            return false;
        }

        public void Pan(float horizontalDrag, float verticalDrag)
        {
        }

        public void End()
        {
        }

        public void Dispose()
        {
        }
    }

    internal sealed class MacTreePanAdapter : ITreePanAdapter
    {
        private const int GestureBegan = 1;
        private const int GestureChanged = 2;
        private const int GestureEnded = 3;
        private const int GestureCancelled = 4;
        private const int GestureFailed = 5;

        private readonly object _handler;
        private readonly PropertyInfo _scrollViewProperty;
        private readonly Action _panStarted;
        private readonly Action _panEnded;
        private readonly object[] _pointArguments = new object[2];
        private readonly object[] _singleArgument = new object[1];
        private readonly object[] _viewArgument = new object[1];
        private object _scrollView;
        private object _clipView;
        private object _headerClipView;
        private object _gestureHost;
        private object _gestureRecognizer;
        private Action _gestureAction;
        private MethodInfo _constrainScrollPoint;
        private MethodInfo _scrollToPoint;
        private MethodInfo _headerScrollToPoint;
        private MethodInfo _reflectScrolledClipView;
        private MethodInfo _translationInView;
        private ConstructorInfo _pointConstructor;
        private PropertyInfo _boundsProperty;
        private PropertyInfo _boundsLocationProperty;
        private PropertyInfo _pointXProperty;
        private PropertyInfo _pointYProperty;
        private PropertyInfo _gestureStateProperty;
        private MethodInfo _nativeFloatToDouble;
        private Type _cursorType;
        private object _previousCursor;
        private object _closedHandCursor;
        private object _arrowCursor;
        private bool _panningCursorActive;
        private double _originX;
        private double _originY;
        private double _headerOriginY;
        private bool _nativeGestureActive;
        private bool _disposed;

        private MacTreePanAdapter(
            object handler,
            PropertyInfo scrollViewProperty,
            Action panStarted,
            Action panEnded)
        {
            _handler = handler;
            _scrollViewProperty = scrollViewProperty;
            _panStarted = panStarted;
            _panEnded = panEnded;
            TryInstallNativeGestureRecognizer();
        }

        public bool HandlesPointerInput => _gestureRecognizer != null;

        public static MacTreePanAdapter TryCreate(
            object handler,
            Action panStarted,
            Action panEnded)
        {
            if (handler == null)
                return null;

            var scrollViewProperty = handler
                .GetType()
                .GetProperty(
                    "ScrollView",
                    BindingFlags.Instance | BindingFlags.Public);
            return scrollViewProperty == null
                ? null
                : new MacTreePanAdapter(
                    handler,
                    scrollViewProperty,
                    panStarted,
                    panEnded);
        }

        public bool TryBegin()
        {
            if (_disposed)
                return false;

            try
            {
                _scrollView = _scrollViewProperty.GetValue(_handler);
                if (_scrollView == null)
                    return false;

                var scrollViewType = _scrollView.GetType();
                _clipView = scrollViewType
                    .GetProperty(
                        "ContentView",
                        BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(_scrollView);
                if (_clipView == null)
                    return false;

                var clipViewType = _clipView.GetType();
                var documentView = clipViewType
                    .GetProperty(
                        "DocumentView",
                        BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(_clipView);
                if (documentView == null)
                    return false;

                _boundsProperty = clipViewType.GetProperty(
                    "Bounds",
                    BindingFlags.Instance | BindingFlags.Public);
                _constrainScrollPoint = FindPublicInstanceMethod(
                    clipViewType,
                    "ConstrainScrollPoint",
                    1);
                _scrollToPoint = FindPublicInstanceMethod(
                    clipViewType,
                    "ScrollToPoint",
                    1);
                _reflectScrolledClipView = FindPublicInstanceMethod(
                    scrollViewType,
                    "ReflectScrolledClipView",
                    1);
                if (_boundsProperty == null ||
                    _constrainScrollPoint == null ||
                    _scrollToPoint == null ||
                    _reflectScrolledClipView == null)
                {
                    ReleaseNativeViews();
                    return false;
                }

                var bounds = _boundsProperty.GetValue(_clipView);
                _boundsLocationProperty = bounds?
                    .GetType()
                    .GetProperty(
                        "Location",
                        BindingFlags.Instance | BindingFlags.Public);
                var location = _boundsLocationProperty?.GetValue(bounds);
                if (location == null || !BindPointMembers(location.GetType()))
                {
                    ReleaseNativeViews();
                    return false;
                }

                _originX = ReadCoordinate(location, _pointXProperty);
                _originY = ReadCoordinate(location, _pointYProperty);
                BindHeaderClipView();
                FindPublicInstanceMethod(scrollViewType, "FlashScrollers", 0)
                    ?.Invoke(_scrollView, null);
                return true;
            }
            catch
            {
                ReleaseNativeViews();
                return false;
            }
        }

        public void Pan(float horizontalDrag, float verticalDrag)
        {
            if (_scrollView == null || _clipView == null)
                return;

            TryScrollTo(
                _originX - horizontalDrag,
                _originY - verticalDrag,
                out _,
                out _);
        }

        private void BeginPanningCursor()
        {
            if (_disposed || _cursorType == null)
                return;

            if (_panningCursorActive)
            {
                ReapplyPanningCursor();
                return;
            }

            try
            {
                _closedHandCursor = GetCursor("ClosedHandCursor");
                _arrowCursor = GetCursor("ArrowCursor");
                _previousCursor = _cursorType.GetProperty(
                    "CurrentCursor",
                    BindingFlags.Static | BindingFlags.Public)
                    ?.GetValue(null);
                _panningCursorActive = TrySetCursor(_closedHandCursor);
            }
            catch
            {
                _previousCursor = null;
                _closedHandCursor = null;
                _arrowCursor = null;
                _panningCursorActive = false;
            }
        }

        private void ReapplyPanningCursor()
        {
            if (_panningCursorActive)
                TrySetCursor(_closedHandCursor);
        }

        private void EndPanningCursor()
        {
            var restoreCursor = _previousCursor ?? _arrowCursor ??
                GetCursor("ArrowCursor");
            _previousCursor = null;
            _closedHandCursor = null;
            _arrowCursor = null;
            _panningCursorActive = false;
            TrySetCursor(restoreCursor);
        }

        private object GetCursor(string propertyName)
        {
            return _cursorType?.GetProperty(
                propertyName,
                BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null);
        }

        private static bool TrySetCursor(object cursor)
        {
            if (cursor == null)
                return false;

            try
            {
                var set = FindPublicInstanceMethod(
                    cursor.GetType(),
                    "Set",
                    0);
                if (set == null)
                    return false;
                set.Invoke(cursor, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void End()
        {
            var notifyEnded = _nativeGestureActive;
            _nativeGestureActive = false;
            EndPanningCursor();
            ReleaseNativeViews();
            if (notifyEnded)
                _panEnded?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            End();
            _disposed = true;
            if (_gestureHost != null && _gestureRecognizer != null)
            {
                try
                {
                    FindPublicInstanceMethod(
                        _gestureHost.GetType(),
                        "RemoveGestureRecognizer",
                        1)?.Invoke(
                            _gestureHost,
                            new[] { _gestureRecognizer });
                }
                catch
                {
                }
            }

            if (_gestureRecognizer is IDisposable disposable)
                disposable.Dispose();
            _gestureRecognizer = null;
            _gestureHost = null;
            _gestureAction = null;
        }

        private void TryInstallNativeGestureRecognizer()
        {
            try
            {
                _gestureHost = _scrollViewProperty.GetValue(_handler);
                if (_gestureHost == null)
                    return;

                var macAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(
                        assembly.GetName().Name,
                        "Microsoft.macOS",
                        StringComparison.Ordinal)) ??
                    FindAssemblyInTypeHierarchy(
                        _gestureHost.GetType(),
                        "Microsoft.macOS");
                var recognizerType = macAssembly?
                    .GetType("AppKit.NSPanGestureRecognizer", false);
                _cursorType = macAssembly?
                    .GetType("AppKit.NSCursor", false);
                var constructor = recognizerType?
                    .GetConstructor(new[] { typeof(Action) });
                if (constructor == null)
                    return;

                _gestureAction = HandleNativePanGesture;
                _gestureRecognizer = constructor.Invoke(
                    new object[] { _gestureAction });
                recognizerType.GetProperty(
                    "ButtonMask",
                    BindingFlags.Instance | BindingFlags.Public)
                    ?.SetValue(_gestureRecognizer, new UIntPtr(4));
                recognizerType.GetProperty(
                    "DelaysOtherMouseButtonEvents",
                    BindingFlags.Instance | BindingFlags.Public)
                    ?.SetValue(_gestureRecognizer, true);
                _gestureStateProperty = recognizerType.GetProperty(
                    "State",
                    BindingFlags.Instance | BindingFlags.Public);
                _translationInView = FindPublicInstanceMethod(
                    recognizerType,
                    "TranslationInView",
                    1);
                var addGesture = FindPublicInstanceMethod(
                    _gestureHost.GetType(),
                    "AddGestureRecognizer",
                    1);
                if (_gestureStateProperty == null ||
                    _translationInView == null ||
                    addGesture == null)
                {
                    DisposeGestureRecognizer();
                    _gestureRecognizer = null;
                    return;
                }

                addGesture.Invoke(
                    _gestureHost,
                    new[] { _gestureRecognizer });
            }
            catch
            {
                DisposeGestureRecognizer();
                _gestureRecognizer = null;
                _gestureHost = null;
                _gestureAction = null;
            }
        }

        private void HandleNativePanGesture()
        {
            if (_disposed || _gestureRecognizer == null)
                return;

            try
            {
                var state = Convert.ToInt32(
                    _gestureStateProperty.GetValue(_gestureRecognizer));
                switch (state)
                {
                    case GestureBegan:
                        if (!TryBegin())
                            return;
                        _nativeGestureActive = true;
                        _panStarted?.Invoke();
                        BeginPanningCursor();
                        break;

                    case GestureChanged:
                        if (!_nativeGestureActive)
                            return;
                        var translation = ReadGesturePoint(
                            _translationInView,
                            out var translationX,
                            out var translationY);
                        if (translation)
                        {
                            Pan(
                                (float)translationX,
                                (float)translationY);
                        }
                        ReapplyPanningCursor();
                        break;

                    case GestureEnded:
                        CompleteNativeGesture();
                        break;

                    case GestureCancelled:
                    case GestureFailed:
                        CompleteNativeGesture();
                        break;
                }
            }
            catch
            {
                CompleteNativeGesture();
            }
        }

        private void CompleteNativeGesture()
        {
            if (!_nativeGestureActive)
            {
                EndPanningCursor();
                return;
            }

            _nativeGestureActive = false;
            EndPanningCursor();
            _panEnded?.Invoke();
            ReleaseNativeViews();
        }

        private bool ReadGesturePoint(
            MethodInfo method,
            out double x,
            out double y)
        {
            x = 0;
            y = 0;
            if (method == null || _gestureHost == null)
                return false;

            _viewArgument[0] = _gestureHost;
            var point = method.Invoke(_gestureRecognizer, _viewArgument);
            if (point == null)
                return false;

            if (_pointXProperty == null ||
                _pointXProperty.DeclaringType != point.GetType())
            {
                if (!BindPointMembers(point.GetType()))
                    return false;
            }

            x = ReadCoordinate(point, _pointXProperty);
            y = ReadCoordinate(point, _pointYProperty);
            return true;
        }

        private void BindHeaderClipView()
        {
            _headerClipView = null;
            _headerScrollToPoint = null;
            _headerOriginY = 0;
            try
            {
                var table = _handler
                    .GetType()
                    .GetProperty(
                        "Table",
                        BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(_handler);
                var headerView = table?
                    .GetType()
                    .GetProperty(
                        "HeaderView",
                        BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(table);
                _headerClipView = headerView?
                    .GetType()
                    .GetProperty(
                        "Superview",
                        BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(headerView);
                if (_headerClipView == null)
                    return;

                _headerScrollToPoint = FindPublicInstanceMethod(
                    _headerClipView.GetType(),
                    "ScrollToPoint",
                    1);
                if (!TryReadViewOrigin(
                    _headerClipView,
                    out _,
                    out _headerOriginY))
                {
                    _headerClipView = null;
                    _headerScrollToPoint = null;
                }
            }
            catch
            {
                _headerClipView = null;
                _headerScrollToPoint = null;
            }
        }

        private bool TryScrollTo(
            double requestedX,
            double requestedY,
            out double actualX,
            out double actualY)
        {
            actualX = 0;
            actualY = 0;
            if (_scrollView == null || _clipView == null)
                return false;

            try
            {
                var requested = CreatePoint(requestedX, requestedY);
                _singleArgument[0] = requested;
                var constrained = _constrainScrollPoint.Invoke(
                    _clipView,
                    _singleArgument);
                if (constrained == null)
                    return false;

                actualX = ReadCoordinate(constrained, _pointXProperty);
                actualY = ReadCoordinate(constrained, _pointYProperty);
                _singleArgument[0] = constrained;
                _scrollToPoint.Invoke(_clipView, _singleArgument);
                SynchronizeHeader(actualX);
                _singleArgument[0] = _clipView;
                _reflectScrolledClipView.Invoke(
                    _scrollView,
                    _singleArgument);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SynchronizeHeader(double x)
        {
            if (_headerClipView == null || _headerScrollToPoint == null)
                return;

            var headerPoint = CreatePoint(x, _headerOriginY);
            _singleArgument[0] = headerPoint;
            _headerScrollToPoint.Invoke(
                _headerClipView,
                _singleArgument);
        }

        private bool TryReadViewOrigin(
            object view,
            out double x,
            out double y)
        {
            x = 0;
            y = 0;
            if (view == null)
                return false;

            var bounds = view
                .GetType()
                .GetProperty(
                    "Bounds",
                    BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(view);
            var location = bounds?
                .GetType()
                .GetProperty(
                    "Location",
                    BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(bounds);
            if (location == null)
                return false;

            x = ReadCoordinate(location, _pointXProperty);
            y = ReadCoordinate(location, _pointYProperty);
            return true;
        }

        private object CreatePoint(double x, double y)
        {
            _pointArguments[0] = x;
            _pointArguments[1] = y;
            return _pointConstructor.Invoke(_pointArguments);
        }

        private bool BindPointMembers(Type pointType)
        {
            _pointConstructor = pointType.GetConstructor(
                new[] { typeof(double), typeof(double) });
            _pointXProperty = pointType.GetProperty(
                "X",
                BindingFlags.Instance | BindingFlags.Public);
            _pointYProperty = pointType.GetProperty(
                "Y",
                BindingFlags.Instance | BindingFlags.Public);
            var nativeFloatType = _pointXProperty?.PropertyType;
            _nativeFloatToDouble = nativeFloatType?
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == "op_Implicit" &&
                    method.ReturnType == typeof(double) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == nativeFloatType);
            return _pointConstructor != null &&
                _pointXProperty != null &&
                _pointYProperty != null &&
                _nativeFloatToDouble != null;
        }

        private double ReadCoordinate(
            object point,
            PropertyInfo coordinateProperty)
        {
            var value = coordinateProperty.GetValue(point);
            return (double)_nativeFloatToDouble.Invoke(
                null,
                new[] { value });
        }

        private void ReleaseNativeViews()
        {
            _scrollView = null;
            _clipView = null;
            _headerClipView = null;
            _singleArgument[0] = null;
            _viewArgument[0] = null;
        }

        private static MethodInfo FindPublicInstanceMethod(
            Type type,
            string name,
            int parameterCount)
        {
            return type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == name &&
                    method.GetParameters().Length == parameterCount);
        }

        private static Assembly FindAssemblyInTypeHierarchy(
            Type type,
            string assemblyName)
        {
            while (type != null)
            {
                var assembly = type.Assembly;
                if (string.Equals(
                    assembly.GetName().Name,
                    assemblyName,
                    StringComparison.Ordinal))
                {
                    return assembly;
                }
                type = type.BaseType;
            }
            return null;
        }

        private void DisposeGestureRecognizer()
        {
            if (_gestureRecognizer is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
