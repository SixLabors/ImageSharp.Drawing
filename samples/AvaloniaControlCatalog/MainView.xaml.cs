using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ControlCatalog.Models;
using ControlCatalog.ViewModels;
using AvaloniaCalendar = Avalonia.Controls.Calendar;

namespace ControlCatalog
{
    public partial class MainView : DrawerPage
    {
        private readonly TransparentStyles _transparentStyles = new();

        public MainView()
        {
            InitializeComponent();

            Loaded += MainView_Loaded;
            Unloaded += MainView_Unloaded;
        }

        private const double WideBreakpoint = 1008;
        private const double NarrowBreakpoint = 640;

        protected override Type StyleKeyOverride => typeof(MainView);

        private void MainView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext == null)
                return;

            SizeChanged += OnDrawerSizeChanged;
            UpdateAdaptiveLayout();
        }

        private void MainView_Unloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SizeChanged -= OnDrawerSizeChanged;
            _lastAppliedMode = null;
        }

        private SplitViewDisplayMode? _lastAppliedMode;
        private bool _updatingLayout;

        private void OnDrawerSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
                UpdateAdaptiveLayout();
        }

        private void UpdateAdaptiveLayout()
        {
            if (_updatingLayout || DataContext == null)
                return;

            var width = Bounds.Width;
            if (width <= 0)
                return;

            SplitViewDisplayMode targetMode;
            if (width >= WideBreakpoint)
                targetMode = SplitViewDisplayMode.Inline;
            else if (width >= NarrowBreakpoint)
                targetMode = SplitViewDisplayMode.CompactInline;
            else
                targetMode = SplitViewDisplayMode.Overlay;

            if (_lastAppliedMode == targetMode)
                return;

            _updatingLayout = true;
            try
            {
                _lastAppliedMode = targetMode;
                ViewModel.DisplayMode = targetMode;

                if (targetMode == SplitViewDisplayMode.Inline)
                    ViewModel.IsDrawerOpened = true;
                else if (targetMode == SplitViewDisplayMode.Overlay)
                    ViewModel.IsDrawerOpened = false;
            }
            finally
            {
                _updatingLayout = false;
            }
        }

        private void Themes_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is CatalogTheme theme)
            {
                App.SetCatalogThemes(theme);
            }
        }

        private void ThemeVariants_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (Application.Current is { } app && e.AddedItems.Count > 0 && e.AddedItems[0] is ThemeVariant themeVariant)
            {
                app.RequestedThemeVariant = themeVariant;
            }
        }

        private void FlowDirection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel && e.AddedItems.Count > 0 && e.AddedItems[0] is FlowDirection flowDirection)
            {
                topLevel.FlowDirection = flowDirection;
            }
        }

        private void Decorations_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window window && e.AddedItems.Count > 0 && e.AddedItems[0] is WindowDecorations systemDecorations)
            {
                window.WindowDecorations = systemDecorations;
            }
        }

        private void TransparencyLevels_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is { } topLevel && e.AddedItems.Count > 0 && e.AddedItems[0] is WindowTransparencyLevel transparencyLevel)
            {
                topLevel.TransparencyLevelHint = [transparencyLevel];

                if (topLevel.ActualTransparencyLevel != WindowTransparencyLevel.None &&
                    transparencyLevel != WindowTransparencyLevel.None)
                {
                    topLevel.Background = new ImmutableSolidColorBrush(Colors.Gray, 0.2);
                    if (!topLevel.Styles.Contains(_transparentStyles))
                        topLevel.Styles.Add(_transparentStyles);
                }
                else
                {
                    topLevel.Background = null;
                    topLevel.Styles.Remove(_transparentStyles);
                }
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (ViewModel != null)
            {
                ViewModel.Navigator = NavPage;
            }
        }

        internal MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (DataContext == null)
                return;

            UpdateAdaptiveLayout();

            var topLevel = TopLevel.GetTopLevel(this)!;
            if (topLevel is Window window)
                ViewModel.SelectedDecorationIndex = (int)window.WindowDecorations;

            var insets = topLevel.InsetsManager;
            if (insets != null)
            {
                // In real life application these events should be unsubscribed to avoid memory leaks.
                ViewModel.SafeAreaPadding = insets.SafeAreaPadding;
                insets.SafeAreaChanged += (sender, args) =>
                {
                    ViewModel.SafeAreaPadding = insets.SafeAreaPadding;
                };

                ViewModel.DisplayEdgeToEdge = insets.DisplayEdgeToEdgePreference;
                ViewModel.IsSystemBarVisible = insets.IsSystemBarVisible ?? true;

                ViewModel.PropertyChanged += async (sender, args) =>
                {
                    if (args.PropertyName == nameof(ViewModel.DisplayEdgeToEdge))
                    {
                        insets.DisplayEdgeToEdgePreference = ViewModel.DisplayEdgeToEdge;
                    }
                    else if (args.PropertyName == nameof(ViewModel.IsSystemBarVisible))
                    {
                        insets.IsSystemBarVisible = ViewModel.IsSystemBarVisible;
                    }

                    // Give the OS some time to apply new values and refresh the view model.
                    await Task.Delay(100);
                    ViewModel.DisplayEdgeToEdge = insets.DisplayEdgeToEdgePreference;
                    ViewModel.IsSystemBarVisible = insets.IsSystemBarVisible ?? true;
                };
            }

            int startupPageIndex = 0;
            string? startupPage = Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_PAGE");

            if (!string.IsNullOrWhiteSpace(startupPage))
            {
                for (int i = 0; i < ViewModel.Pages.Count; i++)
                {
                    if (string.Equals(ViewModel.Pages[i].Header, startupPage, StringComparison.OrdinalIgnoreCase))
                    {
                        startupPageIndex = i;
                        break;
                    }
                }
            }

            ViewModel.SelectedPageIndex = startupPageIndex;

            string? navigateToPage = Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_NAVIGATE_TO_PAGE");

            if (!string.IsNullOrWhiteSpace(navigateToPage))
            {
                // Test-only hook: changing selection after layout exercises the same dirty-region
                // repaint path as clicking a navigation item in the drawer.
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(350);

                    string[] navigationSequence = navigateToPage.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    foreach (string pageName in navigationSequence)
                    {
                        for (int i = 0; i < ViewModel.Pages.Count; i++)
                        {
                            if (string.Equals(ViewModel.Pages[i].Header, pageName, StringComparison.OrdinalIgnoreCase))
                            {
                                ListBox? navigationList = this.GetVisualDescendants()
                                    .OfType<ListBox>()
                                    .FirstOrDefault(x => ReferenceEquals(x.ItemsSource, ViewModel.Pages));

                                Console.Error.WriteLine($"ControlCatalog navigation atTicks={Stopwatch.GetTimestamp()} page={pageName}");

                                if (navigationList is not null)
                                {
                                    navigationList.SelectedIndex = i;
                                }
                                else
                                {
                                    ViewModel.SelectedPageIndex = i;
                                }

                                if (Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_KEEP_DRAWER_OPEN") == "1")
                                {
                                    ViewModel.IsDrawerOpened = true;
                                }

                                break;
                            }
                        }

                        // Leave enough time for the selected page's layout and ordered flushes to
                        // finish before the next selection measures a distinct navigation.
                        await Task.Delay(1000);
                    }
                }, DispatcherPriority.Loaded);
            }

            if (double.TryParse(
                Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_SCROLL_Y"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double scrollY))
            {
                // Test-only hook: scrolling after layout captures the same dirty-region path that a
                // user scroll exercises, while keeping the sample's normal startup path untouched.
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(250);

                    ScrollViewer? scrollViewer = this.GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .Where(x => x.Extent.Height > x.Viewport.Height && x.Viewport.Height > 0)
                        .OrderByDescending(x => x.Viewport.Width)
                        .ThenByDescending(x => x.Extent.Height - x.Viewport.Height)
                        .FirstOrDefault();

                    if (scrollViewer is not null)
                    {
                        int stepCount = int.TryParse(
                            Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_SCROLL_STEPS"),
                            out int parsedStepCount)
                            ? Math.Max(1, parsedStepCount)
                            : 1;

                        for (int i = 1; i <= stepCount; i++)
                        {
                            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollY * i / stepCount);
                            await Task.Delay(80);
                        }
                    }
                }, DispatcherPriority.Loaded);
            }

            if (int.TryParse(
                Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_CALENDAR_SCROLL_MONTHS"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int calendarScrollMonths))
            {
                // Test-only hook: Calendar navigation invalidates the CalendarItem internals, which
                // exercises a different dirty-region path than scrolling the page viewport.
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(250);

                    AvaloniaCalendar[] calendars = this.GetVisualDescendants()
                        .OfType<AvaloniaCalendar>()
                        .Where(x => x.IsVisible)
                        .ToArray();

                    int calendarStart = 0;
                    int calendarEnd = calendars.Length;

                    if (int.TryParse(
                        Environment.GetEnvironmentVariable("IMAGESHARP_CONTROL_CATALOG_CALENDAR_SCROLL_INDEX"),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int calendarIndex))
                    {
                        calendarStart = calendarIndex;
                        calendarEnd = calendarIndex + 1;
                    }

                    using Pointer pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    PointerPointProperties properties = new(RawInputModifiers.None, PointerUpdateKind.Other);
                    int direction = calendarScrollMonths < 0 ? 1 : -1;
                    int steps = Math.Abs(calendarScrollMonths);

                    for (int i = 0; i < steps; i++)
                    {
                        for (int j = calendarStart; j < calendarEnd; j++)
                        {
                            AvaloniaCalendar calendar = calendars[j];
                            Point position = calendar.TranslatePoint(
                                new Point(calendar.Bounds.Width / 2, calendar.Bounds.Height / 2),
                                topLevel) ?? default;

                            PointerWheelEventArgs args = new(
                                calendar,
                                pointer,
                                topLevel,
                                position,
                                (ulong)Environment.TickCount64,
                                properties,
                                KeyModifiers.None,
                                new Vector(0, direction));

                            calendar.RaiseEvent(args);
                        }

                        await Task.Delay(80);
                    }
                }, DispatcherPriority.Loaded);
            }
        }
    }
}
