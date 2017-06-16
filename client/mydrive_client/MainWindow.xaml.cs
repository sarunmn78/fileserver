using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;
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
        public string error { get; set; }
    }

    public enum TransferType { Download, Upload };
    public enum TransferState { Complete, Uploading, Downloading, Paused, UserPaused };

    public enum TransferStatus { CHUNK_TRANSFER_SUCCESS, FILE_TRANSFER_COMPLETE, FILE_ID_NOT_EXIST, SEVRER_NOTREACHABLE, INVALID_ERROR};
    
    public class FileTransfer : INotifyPropertyChanged
    {
        public string file_id { get; set; }
        public string file_localpath { get; set; }
        public TransferType transfer_type { get; set; }
        public long bytes_transfered { get; set; }
        public long total_size { get; set; }

        private int transfer_progress_;
        public int TransferProgress
        {
            get
            {
                return transfer_progress_;
            }
            set
            {

                transfer_progress_ = value;
                NotifyPropertyChanged("TransferProgress");
            }
        }

        private string transfer_message_;
        public string TransferMessage
        {
            get
            {
                return transfer_message_;
            }
            set
            {

                transfer_message_ = value;
                NotifyPropertyChanged("TransferMessage");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(String propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (null != handler)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
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
        public static string GET_PENDING_FILEINFO_URL = BASE_URL + "/mydrive/pendingfileinfo";
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
            LoadTransferList();
            while (thread_running_)
            {
                newTransferEvent.WaitOne(5000);
                while (pendingTransferList.Count > 0)
                {
                    try
                    {
                        bool server_connection_error = false;
                        foreach (KeyValuePair<string, FileTransfer> entry in pendingTransferList)
                        {
                            TransferStatus status = TransferStatus.INVALID_ERROR;
                            FileTransfer transferobj = entry.Value;
                            if (transferobj.transfer_type == TransferType.Upload)
                            {
                                status = UploadFileChunk(transferobj.file_id);
                                if (status == TransferStatus.FILE_TRANSFER_COMPLETE)
                                {
                                    // File transfer complete. remove item from the list and start 
                                    // a new enumeration
                                    RemoveItemFromPendingTransferList(transferobj.file_id);
                                    // Refresh the main file list
                                    Task.Run(() => GetAllFileList());
                                    break;
                                }
                            }
                            else if (transferobj.transfer_type == TransferType.Download)
                            {
                                status = DownloadFileChunk(transferobj.file_id);
                                if (status == TransferStatus.FILE_TRANSFER_COMPLETE)
                                {
                                    // Download chunk failed Handle it
                                    RemoveItemFromPendingTransferList(transferobj.file_id);
                                    break;
                                }
                            }

                            if (status == TransferStatus.SEVRER_NOTREACHABLE)
                            {
                                transferobj.TransferMessage = "Paused";
                                server_connection_error = true;
                                break;
                            }
                        }
                        // Stop any pending transaction and wait for some time for server to be up
                        if (server_connection_error == true)
                        {
                            server_connection_error = false;
                            break;
                        }
                    }
                    catch(InvalidOperationException) { }
                }
            }
        }

        private void AddItemToPendingTransferList(string file_id, FileTransfer item)
        {
            pendingTransferList.Add(file_id, item);
            this.Dispatcher.BeginInvoke(new Action(() =>
                            transferListView.Items.Add(item)));
        }

        private FileTransfer GetItemFromTransferListView(string file_id)
        {
            foreach (FileTransfer item in transferListView.Items)
            {
                if (item.file_id == file_id)
                {
                    return item;
                }
            }
            return null;
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
            SerializeTransferList();
            this.Dispatcher.BeginInvoke(new Action(() => RemoveItemFromTransferListView(file_id)));
        }

        private void UpdateTransferProgress(string file_id, int percent)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                FileTransfer item = GetItemFromTransferListView(file_id);
                if (item != null)
                {
                    item.TransferProgress = percent;
                    item.TransferMessage = "" + percent + "% completed"; 
                }
            }));
        }

        private long GetPendingFileSizeFromServer(string file_id)
        {
            var json_data = "";
            string url = String.Format("{0}/{1}", GET_PENDING_FILEINFO_URL, Uri.EscapeDataString(file_id));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                json_data = reader.ReadToEnd();
                var fileinfo = JsonConvert.DeserializeObject<ServerFileInfo>(json_data);
                if(fileinfo.error == "success")
                    return fileinfo.file_size;
            }
            return 0;
        }

        private void LoadTransferList()
        {
            string json_data = System.IO.File.ReadAllText(@"pending_list.json");
            var pendinglist = JsonConvert.DeserializeObject<Dictionary<string, FileTransfer>>(json_data);
            foreach(KeyValuePair<string, FileTransfer> entry in pendinglist)
            {
                // If we are uploading, check total bytes server has received and proceed from there.
                if(entry.Value.transfer_type == TransferType.Upload)
                {
                    long file_size = GetPendingFileSizeFromServer(entry.Key);
                    entry.Value.bytes_transfered = file_size;
                }
                AddItemToPendingTransferList(entry.Key, entry.Value);
            }
        }

        private void SerializeTransferList()
        {
            string json_data = JsonConvert.SerializeObject(pendingTransferList);
            System.IO.File.WriteAllText(@"pending_list.json", json_data);
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
                this.Dispatcher.BeginInvoke(new Action(() =>
                        fileListView.Items.Clear()));
                json_data = reader.ReadToEnd();
                var filelist = JsonConvert.DeserializeObject<List<ServerFileInfo>>(json_data);
                foreach (ServerFileInfo item in filelist)
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                        fileListView.Items.Add(item)));
                }
            }

        }

        private TransferStatus UploadDataToServer(string file_id, byte[] data, int length)
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
                        return TransferStatus.CHUNK_TRANSFER_SUCCESS;
                    }
                    else if(result.error == "file id is invalid")
                    {
                        return TransferStatus.FILE_ID_NOT_EXIST;
                    }
                }
            }
            catch (Exception)
            { }
            return TransferStatus.SEVRER_NOTREACHABLE;
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

                    // Send data to webserver
                    TransferStatus status = UploadDataToServer(file_id, buffer, bytesRead);
                    if (status == TransferStatus.CHUNK_TRANSFER_SUCCESS)
                    {
                        transferobj.bytes_transfered += bytesRead;
                        UpdateTransferProgress(file_id, (int)((double)((double)transferobj.bytes_transfered / (double)transferobj.total_size) * 100));
                        SerializeTransferList();
                        // Check if we have completed the transfer
                        if (transferobj.bytes_transfered >= transferobj.total_size)
                        {
                            if(EndUpload(file_id) == 0)
                            {
                                return TransferStatus.FILE_TRANSFER_COMPLETE;
                            }
                        }
                        return TransferStatus.CHUNK_TRANSFER_SUCCESS;
                    }
                    else if( status == TransferStatus.FILE_ID_NOT_EXIST)
                    {
                        // Server does not have valid id. It mean transaction is complete, we need to clear our dictionary
                        return TransferStatus.FILE_TRANSFER_COMPLETE;
                    }
                    
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
                    transferobj.TransferProgress = 0;
                    transferobj.file_localpath = localfilepath;
                    transferobj.total_size = new System.IO.FileInfo(localfilepath).Length;
                    transferobj.transfer_type = TransferType.Upload;
                    transferobj.TransferMessage = "Uploading";
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

        private TransferStatus DownloadDataFromServer(string file_id, ref byte[] data, long offset, int bytes_to_read, ref int bytes_read)
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
                            return TransferStatus.CHUNK_TRANSFER_SUCCESS;

                    } while (bytes_read < bytes_to_read);
                    return TransferStatus.CHUNK_TRANSFER_SUCCESS;
                }
            }
            catch (Exception)
            { }
            return TransferStatus.SEVRER_NOTREACHABLE;
        }

        private TransferStatus DownloadFileChunk(string file_id)
        {
            try
            {
                FileTransfer transferobj = pendingTransferList[file_id];
                byte[] buffer = new byte[50 * 1024]; // 50KB
                int bytesToRead = 50 * 1024;
                int bytesRead = 0;
                TransferStatus status = DownloadDataFromServer(file_id, ref buffer, transferobj.bytes_transfered, bytesToRead, ref bytesRead);
                if (status == TransferStatus.CHUNK_TRANSFER_SUCCESS)
                {
                    transferobj.bytes_transfered += bytesRead;
                    if (bytesRead > 0)
                    {
                        using (var outFileSteam = new FileStream(transferobj.file_localpath, FileMode.Append))
                        {
                            outFileSteam.Write(buffer, 0, bytesRead);
                        }
                    }

                    UpdateTransferProgress(file_id, (int)((double)((double)transferobj.bytes_transfered / (double)transferobj.total_size) * 100));
                    SerializeTransferList();
                    if (transferobj.bytes_transfered >= transferobj.total_size)
                    {
                        return TransferStatus.FILE_TRANSFER_COMPLETE;
                    }

                    return TransferStatus.CHUNK_TRANSFER_SUCCESS;
                }
                else if(status == TransferStatus.SEVRER_NOTREACHABLE)
                {
                    return TransferStatus.SEVRER_NOTREACHABLE;
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
            transferobj.TransferProgress = 0;
            string local_folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MyDrive");
            System.IO.Directory.CreateDirectory(local_folder);
            transferobj.file_localpath = System.IO.Path.Combine(local_folder, file_name);
            transferobj.transfer_type = TransferType.Download;
            transferobj.TransferMessage = "Downloading";
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
