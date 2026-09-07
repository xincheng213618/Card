using System.Windows;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
