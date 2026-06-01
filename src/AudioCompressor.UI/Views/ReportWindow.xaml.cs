using System.Windows;
using AudioCompressor.UI.ViewModels;

namespace AudioCompressor.UI.Views;

public partial class ReportWindow : Window
{
    public ReportWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
