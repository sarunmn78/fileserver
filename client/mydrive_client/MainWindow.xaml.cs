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

    public enum TransferStatus { CHUNK_TRANSFER_SUCCESS, FILE_TRANSFER_COMPLETE, FILE_ID_NOT_EXIST, SEVRER_NOTREACHABLE, INVALID_ERROR};
    
    public class FileTransfer
    {
        public string file_id { get; set; }
        public string file_localpath { get; set; }
        public TransferType transfer_type { get; set; }
        public TransferState transfer_state { get; set; }
        public long bytes_transfered { get; set; }
        public long total_size { get; set; }
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
        public static string DOWNLOAD_DATA_URL = BASE_URL + "/mydrive/download";

        Dictionary<string, FileTransfer> pendingTransferList = new Dictionary<string, FileTransfer>();

        Action cancelUpload;
        Action pauseUpload;
        Action progressUpload;

        bool thread_running_ = true;
        private AutoResetEvent newTransferEvent = new AutoResetEvent(false);
        public MainWindow()
        {
            InitializeComponent();
            Task.Run(() => GetAllFileList());
            Thread newThread = new Thread(backgroundWorker_ProcessTransfers);
            newThread.Start();
        }

        private void backgroundWorker_ProcessTransfers(object data)
        {
            while(thread_running_)
            {
                newTransferEvent.WaitOne(5000);
                while (pendingTransferList.Count > 0)
                {
                    foreach (KeyValuePair<string, FileTransfer> entry in pendingTransferList)
                    {
                        TransferStatus status = TransferStatus.INVALID_ERROR;
                        FileTransfer transferobj = entry.Value;
                        if(transferobj.transfer_type == TransferType.Upload)
                        {
                            status = UploadFileChunk(transferobj.file_id);
                            if (status == TransferStatus.FILE_TRANSFER_COMPLETE)
                            {
                                // File transfer complete. remove item from the list and start 
                                // a new enumeration
                                RemoveItemFromPendingTransferList(transferobj.file_id);
                                break;
                            }
                        }
                        else if(transferobj.transfer_type == TransferType.Download)
                        {
                            if(DownloadFileChunk(transferobj.file_id) != 0)
                            {
                                // Download chunk failed Handle it
                                RemoveItemFromPendingTransferList(transferobj.file_id);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void AddItemToPendingTransferList(string file_id, FileTransfer item)
        {
            pendingTransferList.Add(file_id, item);
            this.Dispatcher.BeginInvoke(new Action(() =>
                            transferListView.Items.Add(item)));
        }

        private void RemoveItemFromTransferListView(string file_id)
        {
            foreach (FileTransfer item in transferListView.Items)
            {
                if (item.file_id == file_id)
                {
                    transferListView.Items.Remove(item);
                    break;
                }
            }
        }
        private void RemoveItemFromPendingTransferList(string file_id)
        {
            pendingTransferList.Remove(file_id);
            this.Dispatcher.BeginInvoke(new Action(() => RemoveItemFromTransferListView(file_id)));
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

                Task.Run(() => UploadFile(openFileDialog.FileName,
                                token
                                ));

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
                foreach (ServerFileInfo item in filelist)
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
            catch (Exception)
            { }
            return -1;
        }

        private TransferStatus UploadFileChunk(string file_id)
        {
            try
            {
                FileTransfer transferobj = pendingTransferList[file_id];
                using (var inFileSteam = new FileStream(transferobj.file_localpath, FileMode.Open))
                {
                    inFileSteam.Seek(transferobj.bytes_transfered, SeekOrigin.Begin);
                    byte[] buffer = new byte[50 * 1024]; // 50KB
                    int bytesRead = inFileSteam.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        // Send data to webserver
                        if (UploadDataToServer(file_id, buffer, bytesRead) == 0)
                        {
                            transferobj.bytes_transfered += bytesRead;
                            // Check if we have completed the transfer
                            if(transferobj.bytes_transfered >= transferobj.total_size)
                            {
                                if(EndUpload(file_id) == 0)
                                {
                                    return TransferStatus.FILE_TRANSFER_COMPLETE;
                                }
                            }
                        }
                    }
                    return TransferStatus.CHUNK_TRANSFER_SUCCESS;
                }
            }
            catch (Exception)
            { }
            return TransferStatus.SEVRER_NOTREACHABLE;
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
            //long length = new System.IO.FileInfo(localfilepath).Length;
            string url = String.Format("{0}/{1}", INIT_UPLOAD_URL, Uri.EscapeDataString(fname));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                string json_data = reader.ReadToEnd();
                FileTransfer transferobj = JsonConvert.DeserializeObject<FileTransfer>(json_data);
                if (string.IsNullOrEmpty(transferobj.file_id) == false)
                {
                    transferobj.file_localpath = localfilepath;
                    transferobj.total_size = new System.IO.FileInfo(localfilepath).Length;
                    transferobj.transfer_type = TransferType.Upload;
                    transferobj.transfer_state = TransferState.Uploading;
                    AddItemToPendingTransferList(transferobj.file_id, transferobj);
                    
                    //if (UploadFile(transferobj.file_id) == 0)
                    //{
                    //    EndUpload(transferobj.file_id);
                    //}

                }
            }
        }
        private void UploadFile(string localfilepath, CancellationToken token)
        {
            InitUpload(localfilepath);
        }

        private int DownloadDataFromServer(string file_id, ref byte[] data, long offset, int bytes_to_read, ref int bytes_read)
        {
            try
            {
                string url = String.Format("{0}/{1}/{2}/{3}", DOWNLOAD_DATA_URL, Uri.EscapeDataString(file_id), offset, bytes_to_read);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(data, bytes_read, (bytes_to_read - bytes_read));
                        if (count > 0)
                            bytes_read += count;
                        else if (count == 0)
                            return 0;

                    } while (bytes_read < bytes_to_read);
                    return 0;
                }
            }
            catch (Exception)
            { }
            return -1;
        }

        private TransferStatus DownloadFileChunk(string file_id)
        {
            try
            {
                FileTransfer transferobj = pendingTransferList[file_id];
                byte[] buffer = new byte[50 * 1024]; // 50KB
                int bytesToRead = 50 * 1024;
                int bytesRead = 0;
                if (DownloadDataFromServer(file_id, ref buffer, transferobj.bytes_transfered, bytesToRead, ref bytesRead) == 0)
                {
                    transferobj.bytes_transfered += bytesRead;
                    if (bytesRead > 0)
                    {
                        using (var outFileSteam = new FileStream(transferobj.file_localpath, FileMode.Append))
                        {
                            outFileSteam.Write(buffer, 0, bytesRead);
                        }
                    }

                    if(transferobj.bytes_transfered >= transferobj.total_size)
                    {
                        return TransferStatus.FILE_TRANSFER_COMPLETE;
                    }

                    return TransferStatus.CHUNK_TRANSFER_SUCCESS;
                }
            }
            catch (Exception)
            { }
            return TransferStatus.SEVRER_NOTREACHABLE;
        }
        private void DownloadFile(string file_id, string file_name, long file_size)
        {
            FileTransfer transferobj = new FileTransfer();
            transferobj.file_id = file_id;
            transferobj.total_size = file_size;
            transferobj.bytes_transfered = 0;
            string local_folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyDrive");
            System.IO.Directory.CreateDirectory(local_folder);
            transferobj.file_localpath = System.IO.Path.Combine(local_folder, file_name);
            transferobj.transfer_type = TransferType.Download;
            transferobj.transfer_state = TransferState.Downloading;
            AddItemToPendingTransferList(transferobj.file_id, transferobj);
        }

        private void DownloadContextMenu_OnClick(object sender, RoutedEventArgs e)
        {
            if (fileListView.SelectedIndex > -1)
            {
                ServerFileInfo serverFileInfo = new ServerFileInfo();
                serverFileInfo = (ServerFileInfo)fileListView.SelectedItem; // casting the list view 
                DownloadFile(serverFileInfo.file_id, serverFileInfo.file_name, serverFileInfo.file_size);
            }
        }
   }
}
