using System.Windows;
using D365ERAnalyzer.ViewModels;

namespace D365ERAnalyzer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
