using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Threading;
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
using Microsoft.Win32;
using Newtonsoft.Json;

namespace mydrive_client
{
    public class ServerFileInfo
    {
        public string file_id { get; set; }
        public string file_name { get; set; }
        public string file_modifieddate { get; set; }
        public long file_size { get; set; }
    }

    public enum TransferType { Download, Upload };
    public enum TransferState { Complete, Uploading, Downloading, Paused, UserPaused };

    public class FileTransfer
    {
        public string file_id { get; set; }
        public string file_localpath { get; set; }
        public TransferType transfer_type { get; set; }
        public TransferState transfer_state { get; set; }
        public long bytes_transfered { get; set; }
    }

    public class WebResult
    {
        public string error { get; set; }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static string BASE_URL = "http://localhost:5000";
        public static string GET_FILE_LIST_URL = BASE_URL + "/mydrive/list";
        public static string INIT_UPLOAD_URL = BASE_URL + "/mydrive/initupload";
        public static string UPLOAD_APPEND_URL = BASE_URL + "/mydrive/appenddata";
        public static string UPLOAD_COMPLETE_URL = BASE_URL + "/mydrive/uploaddone";

        Dictionary<string, FileTransfer> pendingTransferList = new Dictionary<string, FileTransfer>();

        Action cancelUpload;
        Action pauseUpload;
        Action progressUpload;
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() => GetAllFileList());
        }

        private void uploadButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (openFileDialog.ShowDialog() == true)
            {
                // Upload cancellation token
                var cancellationTokenSource = new CancellationTokenSource();
                this.cancelUpload = () =>
                {
                    // We can do some more UI work here
                    cancellationTokenSource.Cancel();
                };

                this.progressUpload = () =>
                {

                };

                var token = cancellationTokenSource.Token;

                Task.Run(() =>  UploadFile(openFileDialog.FileName,
                                token,
                                progressUpload));

            }

        }

        private void GetAllFileList()
        {
            var json_data = "";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GET_FILE_LIST_URL);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                json_data = reader.ReadToEnd();
                var filelist = JsonConvert.DeserializeObject<List<ServerFileInfo>>(json_data);
                foreach(ServerFileInfo item in filelist)
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                        fileListView.Items.Add(item)));
                }
            }

        }

        private int UploadDataToServer(string file_id, byte[] data, int length)
        {
            try
            {
                string url = String.Format("{0}/{1}", UPLOAD_APPEND_URL, Uri.EscapeDataString(file_id));
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.ContentType = "multipart/form-data;";
                request.Method = "POST";
                Stream requeststream = request.GetRequestStream();
                requeststream.Write(data, 0, length);
                requeststream.Close();
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    string json_data = reader.ReadToEnd();
                    WebResult result = JsonConvert.DeserializeObject<WebResult>(json_data);
                    if (result.error == "success")
                    {
                        return 0;
                    }
                }
            }
            catch(Exception)
            { }
            return -1;
        }

        private int UploadFile(string file_id)
        {
            try
            {
                FileTransfer transferobj = pendingTransferList[file_id];
                using (var inFileSteam = new FileStream(transferobj.file_localpath, FileMode.Open))
                {
                    byte[] buffer = new byte[50 * 1024]; // 50KB
                    int bytesRead = inFileSteam.Read(buffer, 0, buffer.Length);

                    while (bytesRead > 0)
                    {
                        // Send data to webserver
                        if (UploadDataToServer(file_id, buffer, bytesRead) == 0)
                        {
                            transferobj.bytes_transfered += bytesRead;
                        }
                        inFileSteam.Seek(transferobj.bytes_transfered, SeekOrigin.Begin);
                        bytesRead = inFileSteam.Read(buffer, 0, buffer.Length);
                    }
                    return 0;
                }
            }
            catch(Exception)
            { }
            return -1;
        }

        private int EndUpload(string file_id)
        {
            string url = String.Format("{0}/{1}", UPLOAD_COMPLETE_URL, Uri.EscapeDataString(file_id));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                string json_data = reader.ReadToEnd();
                WebResult result = JsonConvert.DeserializeObject<WebResult>(json_data);
                if (result.error == "success")
                {
                    return 0;
                }
            }
            return -1;
        }
        private void InitUpload(string localfilepath)
        {
            string fname = System.IO.Path.GetFileName(localfilepath);
            string url = String.Format("{0}/{1}", INIT_UPLOAD_URL, Uri.EscapeDataString(fname));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                string json_data = reader.ReadToEnd();
                FileTransfer transferobj = JsonConvert.DeserializeObject<FileTransfer>(json_data);
                if(string.IsNullOrEmpty(transferobj.file_id) == false)
                {
                    transferobj.file_localpath = localfilepath;
                    transferobj.transfer_state = TransferState.Uploading;
                    pendingTransferList.Add(transferobj.file_id, transferobj);
                    this.Dispatcher.BeginInvoke(new Action(() =>
                            transferListView.Items.Add(transferobj)));
                    if(UploadFile(transferobj.file_id) == 0)
                    {
                        EndUpload(transferobj.file_id);
                    }

                }
            }
        }
        private void UploadFile(string localfilepath, CancellationToken token, Action progressReport)
        {
            InitUpload(localfilepath);
            
        }
    }
}
