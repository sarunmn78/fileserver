using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace mydrive_client
{
    public class ServerFileInfo
    {
        public string fileId { get; set; }
        public string fileName { get; set; }
        public string fileModifiedDate { get; set; }
        public long fileSize { get; set; }
    }

    public enum TransferType { Download, Upload };
    public enum TransferState { Complete, Progress, UserPaused };

    public class FileTransfer
    {
        public string transferId { get; set; }
        public string fileId { get; set; }
        public string fileLocalPath { get; set; }
        public TransferType transferType { get; set; }
        public TransferType transferState { get; set; }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void uploadButton_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
