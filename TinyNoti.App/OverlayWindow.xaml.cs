using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TinyNoti.App;

public partial class OverlayWindow : Window, INotifyPropertyChanged
{
    private const double CompactCardHeight = 136;
    private const double ImageCardHeight = 216;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private double _overlayHeight = 560;
    private double _maxListHeight = 480;
    private double _contentMinHeight;
    private bool _leftAnchored;
    private bool _isHiding;
    private bool _isOverlayPresented;
    private bool _isEmptyState;
    private CornerRadius _glassCornerRadius = new(16, 16, 0, 0);
    private System.Windows.HorizontalAlignment _headerHorizontalAlignment = System.Windows.HorizontalAlignment.Right;
    private Thickness _headerMargin = new(0, 0, 24, 7);
    private Visibility _bottomFadeVisibility = Visibility.Visible;
    private VerticalAlignment _cardsVerticalAlignment = VerticalAlignment.Top;

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = this;
        Cards.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCards));
            OnPropertyChanged(nameof(IsOverlayChromeVisible));
            OnPropertyChanged(nameof(CountText));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<long>? DismissRequested;

    public event Action<long>? LaunchRequested;

    public event Action? ClearAllRequested;

    public event Action? HideOverlayRequested;

    public ObservableCollection<NotificationCardViewModel> Cards { get; } = [];

    public bool HasCards => Cards.Count > 0;

    public bool IsOverlayChromeVisible => HasCards || _isEmptyState;

    public Visibility EmptyStateVisibility => _isEmptyState ? Visibility.Visible : Visibility.Collapsed;

    public bool IsOverlayPresented => _isOverlayPresented;

    public string CountText => Cards.Count == 1 ? "1 notification" : $"{Cards.Count} notifications";

    public double OverlayHeight
    {
        get => _overlayHeight;
        set => SetField(ref _overlayHeight, value);
    }

    public double MaxListHeight
    {
        get => _maxListHeight;
        set => SetField(ref _maxListHeight, value);
    }

    public double ContentMinHeight
    {
        get => _contentMinHeight;
        set => SetField(ref _contentMinHeight, value);
    }

    public CornerRadius GlassCornerRadius
    {
        get => _glassCornerRadius;
        set => SetField(ref _glassCornerRadius, value);
    }

    public System.Windows.HorizontalAlignment HeaderHorizontalAlignment
    {
        get => _headerHorizontalAlignment;
        set => SetField(ref _headerHorizontalAlignment, value);
    }

    public Thickness HeaderMargin
    {
        get => _headerMargin;
        set => SetField(ref _headerMargin, value);
    }

    public Visibility BottomFadeVisibility
    {
        get => _bottomFadeVisibility;
        set => SetField(ref _bottomFadeVisibility, value);
    }

    public VerticalAlignment CardsVerticalAlignment
    {
        get => _cardsVerticalAlignment;
        set => SetField(ref _cardsVerticalAlignment, value);
    }

    public void PrepareLayoutCapacity(double availableHeight)
    {
        OverlayHeight = availableHeight;
        MaxListHeight = Math.Max(180, availableHeight - HeaderChromeHeight());
        ContentMinHeight = 0;
        GlassCornerRadius = new CornerRadius(16, 16, 0, 0);
        BottomFadeVisibility = Visibility.Visible;
    }

    public void SetCards(IEnumerable<NotificationCardViewModel> cards, bool leftAnchored)
    {
        ConfigureForHorizontalPlacement(leftAnchored);
        SetEmptyState(false);
        ContentMinHeight = 0;
        var shouldAnimateReflow = SystemParameters.ClientAreaAnimation && _isOverlayPresented && !_isHiding;
        var previousPositions = shouldAnimateReflow ? CaptureCardPositions() : new Dictionary<long, double>();

        SyncCards(cards.ToArray());
        CardsVerticalAlignment = VerticalAlignment.Top;

        if (shouldAnimateReflow)
        {
            UpdateLayout();
            AnimateCardsFromPreviousPositions(previousPositions);
        }
    }

    public void SetEmptyRecentState(bool leftAnchored)
    {
        ConfigureForHorizontalPlacement(leftAnchored);
        ContentMinHeight = 0;
        SetEmptyState(true);
        Cards.Clear();
        CardsVerticalAlignment = VerticalAlignment.Top;
    }

    public bool PrepareShowForLayout()
    {
        OverlayRoot.BeginAnimation(OpacityProperty, null);
        var rootTransform = EnsureRootTransform();
        rootTransform.BeginAnimation(TranslateTransform.XProperty, null);

        if (_isOverlayPresented && !_isHiding)
        {
            OverlayRoot.Opacity = 1;
            rootTransform.X = 0;
            return false;
        }

        _isHiding = false;
        _isOverlayPresented = true;
        OverlayRoot.Opacity = 0;
        rootTransform.X = _leftAnchored ? -18 : 18;

        if (!IsVisible)
        {
            Opacity = 1;
            Show();
        }

        return true;
    }

    public void FitToContentHeight(double availableHeight)
    {
        UpdateLayout();

        var headerHeight = HeaderChromeHeight();
        var scrollMargins = CardsScrollViewer.Margin.Top + CardsScrollViewer.Margin.Bottom;
        var maxGlassHeight = Math.Max(180, availableHeight - headerHeight);
        var measuredCardsHeight = MeasureCardsHeight();
        var desiredGlassHeight = Math.Clamp(measuredCardsHeight + scrollMargins, 132, maxGlassHeight);
        var desiredOverlayHeight = Math.Min(availableHeight, headerHeight + desiredGlassHeight);
        var fillsAvailableHeight = desiredOverlayHeight >= availableHeight - 1;

        OverlayHeight = desiredOverlayHeight;
        MaxListHeight = Math.Max(112, desiredGlassHeight - scrollMargins);
        ContentMinHeight = 0;
        GlassCornerRadius = fillsAvailableHeight
            ? new CornerRadius(16, 16, 0, 0)
            : new CornerRadius(16);
        BottomFadeVisibility = fillsAvailableHeight ? Visibility.Visible : Visibility.Collapsed;
    }

    public void FinishShowAnimation(bool shouldAnimate)
    {
        OverlayRoot.BeginAnimation(OpacityProperty, null);
        var rootTransform = EnsureRootTransform();
        rootTransform.BeginAnimation(TranslateTransform.XProperty, null);

        if (!shouldAnimate)
        {
            OverlayRoot.Opacity = 1;
            rootTransform.X = 0;
            return;
        }

        var opacity = new DoubleAnimation(OverlayRoot.Opacity, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var slide = new DoubleAnimation(rootTransform.X, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        opacity.Completed += (_, _) =>
        {
            OverlayRoot.Opacity = 1;
            rootTransform.X = 0;
        };
        OverlayRoot.BeginAnimation(OpacityProperty, opacity);
        rootTransform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    public void HideWithAnimation()
    {
        if (!_isOverlayPresented)
        {
            if (IsParkedOffscreen())
            {
                return;
            }

            _isOverlayPresented = true;
        }

        OverlayRoot.BeginAnimation(OpacityProperty, null);
        var rootTransform = EnsureRootTransform();
        rootTransform.BeginAnimation(TranslateTransform.XProperty, null);
        _isHiding = true;
        var offset = _leftAnchored ? -44 : 44;

        var opacity = new DoubleAnimation(OverlayRoot.Opacity, 0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        var slide = new DoubleAnimation(rootTransform.X, offset, TimeSpan.FromMilliseconds(230))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        slide.Completed += (_, _) =>
        {
            if (!_isHiding)
            {
                return;
            }

            _isHiding = false;
            _isOverlayPresented = false;
            ParkOffscreen();
            Dispatcher.BeginInvoke(() =>
            {
                OverlayRoot.BeginAnimation(OpacityProperty, null);
                rootTransform.BeginAnimation(TranslateTransform.XProperty, null);
                OverlayRoot.Opacity = 1;
                rootTransform.X = 0;
            });
        };

        OverlayRoot.BeginAnimation(OpacityProperty, opacity);
        rootTransform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ClearAllRequested?.Invoke();
    }

    private void HideOverlay_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideOverlayRequested?.Invoke();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is NotificationCardViewModel card)
        {
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = false;
            }

            var cardChrome = FindDismissTarget(sender as DependencyObject);
            if (cardChrome is null)
            {
                DismissRequested?.Invoke(card.DisplayId);
                return;
            }

            AnimateCardDismiss(cardChrome, () => DismissRequested?.Invoke(card.DisplayId));
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NotificationCardViewModel card)
        {
            LaunchRequested?.Invoke(card.DisplayId);
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private double HeaderChromeHeight()
    {
        if (!HasCards)
        {
            return 0;
        }

        var measuredHeight = HeaderActions.ActualHeight > 0 ? HeaderActions.ActualHeight : 28;
        return measuredHeight + HeaderActions.Margin.Top + HeaderActions.Margin.Bottom;
    }

    private void ConfigureForHorizontalPlacement(bool leftAnchored)
    {
        if (_leftAnchored == leftAnchored && HeaderActions.Children.Count == 2)
        {
            return;
        }

        _leftAnchored = leftAnchored;
        HeaderHorizontalAlignment = leftAnchored ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
        HeaderMargin = leftAnchored ? new Thickness(12, 0, 0, 7) : new Thickness(0, 0, 24, 7);
        HideArrow.Data = Geometry.Parse(leftAnchored ? "M 6 1 L 1 6 L 6 11" : "M 1 1 L 6 6 L 1 11");

        HeaderActions.Children.Clear();
        if (leftAnchored)
        {
            HideForNowButton.Margin = new Thickness(0);
            RemoveAllButton.Margin = new Thickness(6, 0, 0, 0);
            HeaderActions.Children.Add(HideForNowButton);
            HeaderActions.Children.Add(RemoveAllButton);
        }
        else
        {
            RemoveAllButton.Margin = new Thickness(0);
            HideForNowButton.Margin = new Thickness(6, 0, 0, 0);
            HeaderActions.Children.Add(RemoveAllButton);
            HeaderActions.Children.Add(HideForNowButton);
        }
    }

    private void SetEmptyState(bool isEmptyState)
    {
        if (_isEmptyState == isEmptyState)
        {
            UpdateHeaderState();
            return;
        }

        _isEmptyState = isEmptyState;
        UpdateHeaderState();
        OnPropertyChanged(nameof(IsOverlayChromeVisible));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    private void UpdateHeaderState()
    {
        RemoveAllButton.Content = _isEmptyState ? "No notifications" : "Clear all";
        RemoveAllButton.ToolTip = _isEmptyState ? "No recent notifications" : "Clear all notifications";
        RemoveAllButton.IsEnabled = !_isEmptyState;
    }

    private void SyncCards(IReadOnlyList<NotificationCardViewModel> nextCards)
    {
        var nextIds = nextCards.Select(static card => card.DisplayId).ToHashSet();
        for (var index = Cards.Count - 1; index >= 0; index--)
        {
            if (!nextIds.Contains(Cards[index].DisplayId))
            {
                Cards.RemoveAt(index);
            }
        }

        for (var index = 0; index < nextCards.Count; index++)
        {
            var nextCard = nextCards[index];
            var currentIndex = IndexOfCard(nextCard.DisplayId);
            if (currentIndex < 0)
            {
                Cards.Insert(index, nextCard);
                continue;
            }

            if (currentIndex != index)
            {
                Cards.Move(currentIndex, index);
            }
        }
    }

    private int IndexOfCard(long displayId)
    {
        for (var index = 0; index < Cards.Count; index++)
        {
            if (Cards[index].DisplayId == displayId)
            {
                return index;
            }
        }

        return -1;
    }

    private double MeasureCardsHeight()
    {
        CardsItems.Measure(new System.Windows.Size(CardsItems.ActualWidth > 0 ? CardsItems.ActualWidth : 384, double.PositiveInfinity));
        var desiredHeight = CardsItems.DesiredSize.Height;
        var estimatedHeight = EstimateCardsHeight();
        var measuredHeight = 0d;
        for (var i = 0; i < Cards.Count; i++)
        {
            if (CardsItems.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                measuredHeight += Math.Max(container.ActualHeight, container.DesiredSize.Height);
            }
        }

        if (measuredHeight > 0 && measuredHeight <= estimatedHeight + 80)
        {
            return measuredHeight;
        }

        if (desiredHeight > 0 && desiredHeight <= estimatedHeight + 80)
        {
            return desiredHeight;
        }

        return estimatedHeight;
    }

    private double EstimateCardsHeight()
    {
        return Cards.Sum(static card => string.IsNullOrWhiteSpace(card.ImageUri) ? CompactCardHeight : ImageCardHeight);
    }

    private Dictionary<long, double> CaptureCardPositions()
    {
        UpdateLayout();
        var positions = new Dictionary<long, double>();
        for (var index = 0; index < Cards.Count; index++)
        {
            if (CardsItems.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
            {
                continue;
            }

            var position = container.TranslatePoint(new System.Windows.Point(0, 0), CardsItems);
            positions[Cards[index].DisplayId] = position.Y;
        }

        return positions;
    }

    private void AnimateCardsFromPreviousPositions(IReadOnlyDictionary<long, double> previousPositions)
    {
        if (previousPositions.Count == 0)
        {
            return;
        }

        for (var index = 0; index < Cards.Count; index++)
        {
            if (!previousPositions.TryGetValue(Cards[index].DisplayId, out var previousY)
                || CardsItems.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
            {
                continue;
            }

            var currentY = container.TranslatePoint(new System.Windows.Point(0, 0), CardsItems).Y;
            var deltaY = previousY - currentY;
            if (Math.Abs(deltaY) < 1)
            {
                continue;
            }

            var transform = EnsureCardTransform(container);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = deltaY;

            var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            slide.Completed += (_, _) => transform.Y = 0;
            transform.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    private static TranslateTransform EnsureCardTransform(FrameworkElement container)
    {
        if (container.RenderTransform is TranslateTransform transform && !transform.IsFrozen)
        {
            return transform;
        }

        transform = new TranslateTransform();
        container.RenderTransform = transform;
        return transform;
    }

    private FrameworkElement? FindDismissTarget(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Border { DataContext: NotificationCardViewModel } border && border.ActualWidth > 300)
            {
                return (FrameworkElement)source;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void AnimateCardDismiss(FrameworkElement target, Action completed)
    {
        target.IsHitTestVisible = false;
        var currentX = target.RenderTransform is TranslateTransform current && !current.IsFrozen ? current.X : 0;
        var transform = new TranslateTransform(currentX, 0);
        target.RenderTransform = transform;

        target.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        var offset = _leftAnchored ? -(target.ActualWidth + 36) : target.ActualWidth + 36;
        var opacity = new DoubleAnimation(0, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        var slide = new DoubleAnimation(offset, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        slide.Completed += (_, _) => completed();
        target.BeginAnimation(OpacityProperty, opacity);
        transform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private TranslateTransform EnsureRootTransform()
    {
        if (OverlayRoot.RenderTransform is TranslateTransform transform && !transform.IsFrozen)
        {
            return transform;
        }

        transform = new TranslateTransform();
        OverlayRoot.RenderTransform = transform;
        return transform;
    }

    private void ParkOffscreen()
    {
        Left = -32000;
        Top = -32000;
    }

    private bool IsParkedOffscreen()
    {
        return Left <= -30000 || Top <= -30000;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
