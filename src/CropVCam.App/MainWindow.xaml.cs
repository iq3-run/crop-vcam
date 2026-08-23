using System.Windows;

namespace CropVCam.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += (_, _) =>
        {
            _viewModel.SaveSettings();
            _viewModel.Dispose();
        };
    }
}
