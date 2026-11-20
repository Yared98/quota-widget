using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using QuotaWidget.Config;
using QuotaWidget.Host;
using QuotaWidget.Ipc;
using QuotaWidget.Localization;
using QuotaWidget.Model;

namespace QuotaWidget;

public partial class MainWindow : Window
{
    private WidgetConfig _config;
    private readonly QuotaManager _quotaManager;
    private TaskbarHost? _taskbarHost;
    private PipeServerHost? _pipeServerHost;
    private DispatcherTimer? _hoverTimer;
    private int _outsideTicks;

    private static readonly SolidColorBrush PanelBgBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.PanelBgHex)));
    private static readonly SolidColorBrush CardBgBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.CardBgHex)));
    private static readonly SolidColorBrush BorderBrushDef = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.BorderHex)));
    private static readonly SolidColorBrush TextPrimaryBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.TextPrimaryHex)));
    private static readonly SolidColorBrush TextSecondaryBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.TextSecondaryHex)));
    private static readonly SolidColorBrush TextMutedBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.TextMutedHex)));
    private static readonly SolidColorBrush GreenBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.GreenHex)));
    private static readonly SolidColorBrush AmberBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.AmberHex)));
    private static readonly SolidColorBrush RedBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.RedHex)));
    private static readonly SolidColorBrush RedLightBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.RedLightHex)));
    private static readonly SolidColorBrush TrackBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.TrackHex)));
    private static readonly SolidColorBrush DangerRowBgBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.DangerRowBgHex)));
    private static readonly SolidColorBrush TagBgBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.TagBgHex)));
    private static readonly SolidColorBrush TagExhaustedBgBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.TagExhaustedBgHex)));
    private static readonly SolidColorBrush RedBorder30Brush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.RedBorder30Hex)));
    private static readonly SolidColorBrush DashedBorderBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorThresholds.DashedBorderHex)));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush GetBrushFromHex(string hex)
    {
        return hex switch
        {
            ColorThresholds.GreenHex => GreenBrush,
            ColorThresholds.AmberHex => AmberBrush,
            ColorThresholds.RedHex => RedBrush,
            ColorThresholds.RedLightHex => RedLightBrush,
            ColorThresholds.TextPrimaryHex => TextPrimaryBrush,
            ColorThresholds.TextSecondaryHex => TextSecondaryBrush,
            ColorThresholds.TextMutedHex => TextMutedBrush,
            ColorThresholds.TrackHex => TrackBrush,
            ColorThresholds.PanelBgHex => PanelBgBrush,
            ColorThresholds.CardBgHex => CardBgBrush,
            _ => Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)))
        };
    }

    private readonly List<BarBlockElements> _barBlocks = new();

    private class BarBlockElements
    {
        public ProviderConfig Config { get; set; } = null!;
        public Border Container { get; set; } = null!;
        public TextBlock Label { get; set; } = null!;
        public TextBlock Percent { get; set; } = null!;
        public ColumnDefinition ThickColFill { get; set; } = null!;
        public ColumnDefinition ThickColEmpty { get; set; } = null!;
        public Border ThickFill { get; set; } = null!;
        public ColumnDefinition ThinColFill { get; set; } = null!;
        public ColumnDefinition ThinColEmpty { get; set; } = null!;
        public Border ThinFill { get; set; } = null!;
    }

    public MainWindow()
    {
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        InitializeComponent();

        _config = ConfigStore.Load();
        _quotaManager = new QuotaManager(_config);
        _quotaManager.QuotasUpdated += OnQuotasUpdated;

        ApplyThemeAndLocalization();
        RebuildBar();
    }

    private string CurrentLanguage => Strings.ResolveLanguage(_config.Language);

    private void ApplyThemeAndLocalization()
    {
        Background = PanelBgBrush;

        string lang = CurrentLanguage;
        MenuRefresh.Header = Strings.GetMenuRefresh(lang);
        MenuOpenConfig.Header = Strings.GetMenuOpenConfig(lang);
        MenuReloadConfig.Header = Strings.GetMenuReloadConfig(lang);
        MenuExit.Header = Strings.GetMenuExit(lang);

        PopupTitle.Text = Strings.GetTitle(lang);
    }

    private void RebuildBar()
    {
        BarContainer.Children.Clear();
        _barBlocks.Clear();

        var visibleProviders = _config.Providers
            .Where(p => p.Enabled && !p.HideOnBar)
            .ToList();

        for (int i = 0; i < visibleProviders.Count; i++)
        {
            var p = visibleProviders[i];
            bool isLast = i == visibleProviders.Count - 1;

            var label = new TextBlock
            {
                Text = p.Label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = TextPrimaryBrush
            };

            var percent = new TextBlock
            {
                Text = "--%",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = TextMutedBrush
            };
            percent.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);

            var headerGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            // two columns so a long label pushes the item wider instead of overlapping the percent
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            percent.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(percent, 1);
            headerGrid.Children.Add(label);
            headerGrid.Children.Add(percent);

            // Thick mini bar (first window of main family)
            var thickFill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = GreenBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var thickGrid = new Grid();
            var thickColFill = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            var thickColEmpty = new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) };
            thickGrid.ColumnDefinitions.Add(thickColFill);
            thickGrid.ColumnDefinitions.Add(thickColEmpty);
            Grid.SetColumn(thickFill, 0);
            thickGrid.Children.Add(thickFill);

            var thickTrack = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = TrackBrush,
                ClipToBounds = true,
                Child = thickGrid
            };

            // Thin mini bar (second window of main family)
            var thinFill = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = GreenBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var thinGrid = new Grid();
            var thinColFill = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            var thinColEmpty = new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) };
            thinGrid.ColumnDefinitions.Add(thinColFill);
            thinGrid.ColumnDefinitions.Add(thinColEmpty);
            Grid.SetColumn(thinFill, 0);
            thinGrid.Children.Add(thinFill);

            var thinTrack = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = TrackBrush,
                ClipToBounds = true,
                Margin = new Thickness(0, 2, 0, 0),
                Child = thinGrid
            };

            var contentStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            contentStack.Children.Add(headerGrid);
            contentStack.Children.Add(thickTrack);
            contentStack.Children.Add(thinTrack);

            var blockBorder = new Border
            {
                MinWidth = 84,
                Margin = new Thickness(0, 0, isLast ? 0 : 6, 0),
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(6),
                Background = CardBgBrush,
                BorderBrush = BorderBrushDef,
                BorderThickness = new Thickness(1),
                Child = contentStack
            };

            BarContainer.Children.Add(blockBorder);

            _barBlocks.Add(new BarBlockElements
            {
                Config = p,
                Container = blockBorder,
                Label = label,
                Percent = percent,
                ThickColFill = thickColFill,
                ThickColEmpty = thickColEmpty,
                ThickFill = thickFill,
                ThinColFill = thinColFill,
                ThinColEmpty = thinColEmpty,
                ThinFill = thinFill
            });
        }

        UpdateLayout();
        _taskbarHost?.CheckAndPosition();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _taskbarHost = new TaskbarHost(this, _config.OffsetX);
        _taskbarHost.Start();

        StartPipeServer();
        UpdateUI();
        _ = _quotaManager.RefreshAsync();
    }

    private void OnQuotasUpdated()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUI();
            if (HoverPopup.IsOpen)
            {
                UpdatePopupContent();
            }
        });
    }

    private void UpdateUI()
    {
        var snapshots = _quotaManager.GetSnapshotsInConfigOrder()
            .ToDictionary(s => s.ProviderId, s => s, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;

        foreach (var block in _barBlocks)
        {
            block.Label.Text = block.Config.Label;

            if (snapshots.TryGetValue(block.Config.Id, out var snapshot))
            {
                var thickWindow = QuotaLogic.GetThickWindow(snapshot, block.Config.MainFamily);
                var thinWindow = QuotaLogic.GetThinWindow(snapshot, block.Config.MainFamily);

                if (thickWindow != null)
                {
                    int p = (int)Math.Round(thickWindow.UsedPercent);
                    block.Percent.Text = $"{p}%";

                    if (p <= 0)
                    {
                        block.Percent.Foreground = TextMutedBrush;
                    }
                    else
                    {
                        block.Percent.Foreground = GetBrushFromHex(ColorThresholds.GetColorHex(thickWindow.UsedPercent));
                    }

                    bool stale = QuotaLogic.IsStale(snapshot, now, _config.RefreshMinutes, block.Config.MainFamily);
                    block.Percent.Opacity = stale ? 0.45 : 1.0;

                    double clamped = Math.Clamp(thickWindow.UsedPercent, 0, 100);
                    block.ThickColFill.Width = new GridLength(clamped, GridUnitType.Star);
                    block.ThickColEmpty.Width = new GridLength(100.0 - clamped, GridUnitType.Star);
                    block.ThickFill.Background = GetBrushFromHex(ColorThresholds.GetColorHex(thickWindow.UsedPercent));

                    if (thickWindow.UsedPercent >= 100.0)
                    {
                        block.Container.BorderBrush = RedBorder30Brush;
                    }
                    else
                    {
                        block.Container.BorderBrush = BorderBrushDef;
                    }
                }
                else
                {
                    block.Percent.Text = "--%";
                    block.Percent.Foreground = TextMutedBrush;
                    block.Percent.Opacity = 0.45;
                    block.ThickColFill.Width = new GridLength(0, GridUnitType.Star);
                    block.ThickColEmpty.Width = new GridLength(100, GridUnitType.Star);
                    block.Container.BorderBrush = BorderBrushDef;
                }

                if (thinWindow != null)
                {
                    double clamped = Math.Clamp(thinWindow.UsedPercent, 0, 100);
                    block.ThinColFill.Width = new GridLength(clamped, GridUnitType.Star);
                    block.ThinColEmpty.Width = new GridLength(100.0 - clamped, GridUnitType.Star);
                    block.ThinFill.Background = GetBrushFromHex(ColorThresholds.GetColorHex(thinWindow.UsedPercent));
                }
                else
                {
                    block.ThinColFill.Width = new GridLength(0, GridUnitType.Star);
                    block.ThinColEmpty.Width = new GridLength(100, GridUnitType.Star);
                }
            }
            else
            {
                block.Percent.Text = "--%";
                block.Percent.Foreground = TextMutedBrush;
                block.Percent.Opacity = 0.45;
                block.ThickColFill.Width = new GridLength(0, GridUnitType.Star);
                block.ThickColEmpty.Width = new GridLength(100, GridUnitType.Star);
                block.ThinColFill.Width = new GridLength(0, GridUnitType.Star);
                block.ThinColEmpty.Width = new GridLength(100, GridUnitType.Star);
                block.Container.BorderBrush = BorderBrushDef;
            }
        }
    }

    private void UpdatePopupContent()
    {
        var now = DateTimeOffset.Now;
        string lang = CurrentLanguage;

        PopupTitle.Text = Strings.GetTitle(lang);

        var snapshots = _quotaManager.GetSnapshotsInConfigOrder()
            .ToDictionary(s => s.ProviderId, s => s, StringComparer.OrdinalIgnoreCase);

        var enabledProviders = _config.Providers.Where(p => p.Enabled).ToList();

        var enabledSnapshots = enabledProviders
            .Select(p => snapshots.TryGetValue(p.Id, out var s) ? s : new QuotaSnapshot(p.Id, p.Label, default))
            .ToList();

        string dotHex = PanelLayout.GetStatusDotColor(enabledSnapshots, now, _config.RefreshMinutes);
        PopupStatusDot.Fill = GetBrushFromHex(dotHex);
        PopupStatusDotGlow.Color = (Color)ColorConverter.ConvertFromString(dotHex);

        var maxReadAt = enabledSnapshots
            .Where(s => s.ReadAt != default)
            .Select(s => (DateTimeOffset?)s.ReadAt)
            .Max();
        PopupHeaderUpdate.Text = maxReadAt.HasValue
            ? maxReadAt.Value.ToLocalTime().ToString("HH:mm")
            : now.ToLocalTime().ToString("HH:mm");

        // ScrollViewer max height 80% of work area
        double maxWorkHeight = SystemParameters.WorkArea.Height * 0.8;
        PopupScrollViewer.MaxHeight = Math.Max(120, maxWorkHeight - 60);

        PopupAccountsContainer.Children.Clear();

        for (int i = 0; i < enabledProviders.Count; i++)
        {
            var pConfig = enabledProviders[i];
            snapshots.TryGetValue(pConfig.Id, out var snapshot);
            snapshot ??= new QuotaSnapshot(pConfig.Id, pConfig.Label, default, Array.Empty<QuotaFamily>());

            var card = CreateProviderCard(pConfig, snapshot, now, lang, isLast: i == enabledProviders.Count - 1);
            PopupAccountsContainer.Children.Add(card);
        }
    }

    private UIElement CreateProviderCard(ProviderConfig pConfig, QuotaSnapshot snapshot, DateTimeOffset now, string lang, bool isLast)
    {
        var cardBorder = new Border
        {
            Background = CardBgBrush,
            BorderBrush = BorderBrushDef,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, isLast ? 0 : 8)
        };

        var cardStack = new StackPanel();

        // Header row: label left, tag pill right
        var headerGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = snapshot.Label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 0);
        headerGrid.Children.Add(titleText);

        bool isExhausted = PanelLayout.IsExhausted(snapshot);
        string? tagText = isExhausted
            ? Strings.GetExhaustedTag(lang)
            : (!string.IsNullOrWhiteSpace(pConfig.Tag) ? pConfig.Tag : null);

        if (!string.IsNullOrEmpty(tagText))
        {
            var tagPill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Background = isExhausted ? TagExhaustedBgBrush : TagBgBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tagLabel = new TextBlock
            {
                Text = tagText,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = isExhausted ? RedLightBrush : TextSecondaryBrush
            };
            tagPill.Child = tagLabel;
            Grid.SetColumn(tagPill, 1);
            headerGrid.Children.Add(tagPill);
        }

        cardStack.Children.Add(headerGrid);

        // Error line under header
        if (snapshot.HasError)
        {
            var errorText = new TextBlock
            {
                Text = snapshot.Error,
                FontSize = 10.5,
                Foreground = RedLightBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            cardStack.Children.Add(errorText);
        }

        // "read X ago" only when stale
        bool isStale = QuotaLogic.IsOld(snapshot, now, _config.RefreshMinutes);
        if (isStale && snapshot.ReadAt != default)
        {
            var staleText = new TextBlock
            {
                Text = Strings.FormatReadAgo(snapshot.ReadAt, now, lang),
                FontSize = 10.5,
                Foreground = TextMutedBrush,
                Margin = new Thickness(0, 0, 0, 4)
            };
            cardStack.Children.Add(staleText);
        }

        // Layout of rows
        var mode = PanelLayout.GetLayoutMode(snapshot);

        if (mode == ProviderLayoutMode.Compact)
        {
            foreach (var family in snapshot.Families)
            {
                if (family.Windows.Count == 0) continue;
                var window = family.Windows[0];
                string period = PanelLayout.ShortenFamilyName(family.Name);
                cardStack.Children.Add(CreateMetricRow(period, window, now, lang));
            }
        }
        else if (mode == ProviderLayoutMode.Single)
        {
            if (snapshot.Families.Count > 0)
            {
                foreach (var window in snapshot.Families[0].Windows)
                {
                    string period = Strings.WindowName(window.Name, lang);
                    cardStack.Children.Add(CreateMetricRow(period, window, now, lang));
                }
            }
        }
        else // Grouped
        {
            for (int g = 0; g < snapshot.Families.Count; g++)
            {
                var family = snapshot.Families[g];
                var groupPanel = new StackPanel();

                if (g > 0)
                {
                    groupPanel.Margin = new Thickness(0, 8, 0, 0);

                    var dashedLine = new System.Windows.Shapes.Line
                    {
                        X1 = 0,
                        X2 = 1,
                        Stretch = Stretch.Fill,
                        Stroke = DashedBorderBrush,
                        StrokeDashArray = new DoubleCollection { 3, 3 },
                        StrokeThickness = 1,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    groupPanel.Children.Add(dashedLine);
                }

                var modelName = new TextBlock
                {
                    Text = family.Name,
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = TextSecondaryBrush,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                groupPanel.Children.Add(modelName);

                foreach (var window in family.Windows)
                {
                    string period = Strings.WindowName(window.Name, lang);
                    groupPanel.Children.Add(CreateMetricRow(period, window, now, lang));
                }

                cardStack.Children.Add(groupPanel);
            }
        }

        cardBorder.Child = cardStack;
        return cardBorder;
    }

    private UIElement CreateMetricRow(string period, QuotaWindow window, DateTimeOffset now, string lang)
    {
        bool isExhausted = window.UsedPercent >= 100.0;

        var rowBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 3, 4, 3),
            Background = isExhausted ? DangerRowBgBrush : Brushes.Transparent
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });

        // Period
        var periodText = new TextBlock
        {
            Text = period,
            FontSize = 10.5,
            Foreground = TextMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(periodText, 0);
        grid.Children.Add(periodText);

        // Bar track and fill
        double clamped = Math.Clamp(window.UsedPercent, 0, 100);
        var fill = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = GetBrushFromHex(ColorThresholds.GetColorHex(window.UsedPercent)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clamped, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100.0 - clamped, GridUnitType.Star) });
        Grid.SetColumn(fill, 0);
        barGrid.Children.Add(fill);

        var track = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = TrackBrush,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Child = barGrid
        };
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);

        // Percent
        var percentText = new TextBlock
        {
            Text = $"{Math.Round(window.UsedPercent):F0}%",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = isExhausted ? RedBrush : TextPrimaryBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        percentText.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        Grid.SetColumn(percentText, 2);
        grid.Children.Add(percentText);

        // Reset info
        string resetStr = Strings.FormatReset(window.ResetsAt, window.IsEstimated, now, lang);
        var resetText = new TextBlock
        {
            Text = resetStr,
            FontSize = 10.5,
            Foreground = isExhausted ? RedLightBrush : TextMutedBrush,
            FontWeight = isExhausted ? FontWeights.Medium : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        resetText.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        Grid.SetColumn(resetText, 3);
        grid.Children.Add(resetText);

        rowBorder.Child = grid;
        return rowBorder;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint p);
    private struct CursorPoint { public int X; public int Y; }

    private void Widget_MouseEnter(object sender, MouseEventArgs e)
    {
        if (HoverPopup.IsOpen)
            return;
        UpdatePopupContent();
        HoverPopup.IsOpen = true;
        _outsideTicks = 0;
        _hoverTimer ??= CreateHoverTimer();
        _hoverTimer.Start();
    }

    private void Widget_MouseLeave(object sender, MouseEventArgs e)
    {
    }

    private DispatcherTimer CreateHoverTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) =>
        {
            if (!HoverPopup.IsOpen) { t.Stop(); return; }
            if (!GetCursorPos(out var p)) return;
            bool inside = IsInside(RootGrid, p) || (HoverPopup.Child is FrameworkElement c && IsInside(c, p));
            _outsideTicks = inside ? 0 : _outsideTicks + 1;
            if (_outsideTicks >= 2) // ~300 ms outside
            {
                HoverPopup.IsOpen = false;
                t.Stop();
            }
        };
        return t;
    }

    private static bool IsInside(FrameworkElement el, CursorPoint p)
    {
        if (!el.IsVisible || PresentationSource.FromVisual(el) == null) return false;
        var tl = el.PointToScreen(new Point(0, 0));
        var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
        return p.X >= tl.X - 2 && p.X <= br.X + 2 && p.Y >= tl.Y - 8 && p.Y <= br.Y + 2;
    }

    private void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = _quotaManager.RefreshAsync();
    }

    private void MenuOpenConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfigStore.EnsureConfigFile();
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigStore.DefaultConfigPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore open error
        }
    }

    private void MenuReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        _config = ConfigStore.Load();
        _quotaManager.UpdateConfig(_config);
        _taskbarHost?.UpdateOffsetX(_config.OffsetX);
        ApplyThemeAndLocalization();
        RebuildBar();
        _ = _quotaManager.RefreshAsync();
    }

    private void StartPipeServer()
    {
        try
        {
            _pipeServerHost = new PipeServerHost(HandlePipeRequestAsync);
            _pipeServerHost.Start();
        }
        catch
        {
            // Ignore if pipe server fails to start
        }
    }

    private async Task<PipeResponse> HandlePipeRequestAsync(PipeRequest request)
    {
        if (string.Equals(request.Cmd, "ping", StringComparison.OrdinalIgnoreCase))
        {
            string ver = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            return new PipeResponse { Ok = true, Version = ver };
        }

        if (string.Equals(request.Cmd, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            var result = _quotaManager.Scheduler.RequestRefresh(request.Ids, request.Force, request.Wait);
            if (request.Wait)
            {
                await result.WaitTask;
                var export = _quotaManager.GetCurrentExport();
                return new PipeResponse
                {
                    Ok = true,
                    Queued = result.Queued.ToList(),
                    Skipped = result.Skipped.ToList(),
                    Quotas = export
                };
            }

            return new PipeResponse
            {
                Ok = true,
                Queued = result.Queued.ToList(),
                Skipped = result.Skipped.ToList()
            };
        }

        return PipeMessageParser.CreateErrorResponse($"Unknown command: '{request.Cmd}'");
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        _taskbarHost?.Stop();
        _pipeServerHost?.Dispose();
        Application.Current.Shutdown();
    }
}
