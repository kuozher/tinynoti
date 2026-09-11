using System.Windows;
using System.ComponentModel;
using System.Windows.Interop;

namespace TinyNoti.App;

public partial class MainWindow : Window
{
    private readonly AppController _controller;

    public MainWindow(AppController controller, MainWindowViewModel viewModel)
    {
        _controller = controller;
        DataContext = viewModel;
        InitializeComponent();
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var workingArea = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
        var workWidth = bottomRight.X - topLeft.X;
        var workHeight = bottomRight.Y - topLeft.Y;

        Width = 680;
        Height = 760;
        Left = topLeft.X + Math.Max(0, (workWidth - Width) / 2);
        Top = topLeft.Y + Math.Max(0, (workHeight - Height) / 2);
    }

    private async void RequestAccess_Click(object sender, RoutedEventArgs e)
    {
        await _controller.RequestAccessAsync();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _controller.ClearAllNotifications();
    }

    private void ShowRecent_Click(object sender, RoutedEventArgs e)
    {
        _controller.ShowRecentNotifications();
    }

    private void Card_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NotificationCardViewModel card)
        {
            _controller.LaunchNotification(card.DisplayId);
        }
    }

    private void NavRecent_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedTab = MainNavTab.RecentNotifications;
        }
    }

    private void NavOverlayPosition_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedTab = MainNavTab.OverlayPosition;
        }
    }

    private void NavFilters_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedTab = MainNavTab.Filters;
        }
    }

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSettingsExpanded = !vm.IsSettingsExpanded;
        }
    }
}
