using System.Windows;

namespace RiseFlashTool
{
    public partial class AboutEnterpriseWindow : Window
    {
        public AboutEnterpriseWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}