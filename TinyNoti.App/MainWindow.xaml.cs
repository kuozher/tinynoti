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

        Width = Math.Min(Math.Max(MinWidth, workWidth * 0.60), Math.Max(MinWidth, workWidth - 48));
        Height = Math.Min(Math.Max(MinHeight, workHeight * 0.80), Math.Max(MinHeight, workHeight - 48));
        Left = topLeft.X + (workWidth - Width) / 2;
        Top = topLeft.Y + (workHeight - Height) / 2;
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
}
