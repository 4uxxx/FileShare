using System.Windows;
using FileShare.ViewModels;

namespace FileShare.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
