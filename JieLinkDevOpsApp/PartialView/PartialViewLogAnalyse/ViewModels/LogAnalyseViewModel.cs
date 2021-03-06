﻿using Panuon.UI.Silver;
using Panuon.UI.Silver.Core;
using PartialViewInterface.Commands;
using PartialViewLogAnalyse.Models;
using PartialViewLogAnalyse.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PartialViewLogAnalyse.ViewModels
{
    public class LogAnalyseViewModel : DependencyObject
    {

        public DelegateCommand SelectPathCommand { get; set; }
        public DelegateCommand AnalyseCommand { get; set; }
        public string FilePath
        {
            get { return (string)GetValue(FilePathProperty); }
            set { SetValue(FilePathProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FilePath.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register("FilePath", typeof(string), typeof(LogAnalyseViewModel), new PropertyMetadata(""));


        public string Plate
        {
            get { return (string)GetValue(PlateProperty); }
            set { SetValue(PlateProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Plate.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PlateProperty =
            DependencyProperty.Register("Plate", typeof(string), typeof(LogAnalyseViewModel), new PropertyMetadata(""));



        public string DeviceName
        {
            get { return (string)GetValue(DeviceNameProperty); }
            set { SetValue(DeviceNameProperty, value); }
        }

        // Using a DependencyProperty as the backing store for DeviceName.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DeviceNameProperty =
            DependencyProperty.Register("DeviceName", typeof(string), typeof(LogAnalyseViewModel), new PropertyMetadata(""));



        public string Result
        {
            get { return (string)GetValue(ResultProperty); }
            set { SetValue(ResultProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Result.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ResultProperty =
            DependencyProperty.Register("Result", typeof(string), typeof(LogAnalyseViewModel), new PropertyMetadata(""));



        private List<string> filePathList = new List<string>();
        private string plate = string.Empty;
        private string deviceName = string.Empty;
        private BackgroundWorker backgroundWorker = new BackgroundWorker();
        private IPendingHandler pendingHandler = null;
        public LogAnalyseViewModel()
        {
            SelectPathCommand = new DelegateCommand();
            SelectPathCommand.ExecuteAction = SelectPath;
            AnalyseCommand = new DelegateCommand();
            AnalyseCommand.ExecuteAction = Analyse;
            AnalyseCommand.CanExecuteFunc = o => filePathList.Count > 0;
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.DoWork += BackgroundWorker_DoWork;
            backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;
            backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
        }



        private void SelectPath(object parameter)
        {
            System.Windows.Forms.OpenFileDialog fileDialog = new System.Windows.Forms.OpenFileDialog();
            fileDialog.Multiselect = true;
            var process = Process.GetProcessesByName("SmartCenter.Host").FirstOrDefault();
            if (process != null)
            {
                fileDialog.InitialDirectory = Path.Combine(new FileInfo(process.MainModule.FileName).Directory.FullName, "logs");
            }
            System.Windows.Forms.DialogResult result = fileDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                FilePath = string.Join(";", fileDialog.FileNames);
                filePathList = fileDialog.FileNames.ToList();
                filePathList = filePathList.FindAll(x => x.Contains("JieLink_Center_Comm") || x.Contains("JieLink_CENTER_2"));
                //按时间排序
                filePathList.Sort((a, b) =>
                {
                    return int.Parse(b.Substring(b.LastIndexOf('.') + 1).Replace("log", "0"))
                             - int.Parse(a.Substring(a.LastIndexOf('.') + 1).Replace("log", "0"));
                });

            }
        }
        private void Analyse(object parameter)
        {
            if (backgroundWorker.IsBusy)
            {
                return;
            }
            plate = Plate;//后台线程访问会有问题，赋值个临时字段解决
            deviceName = DeviceName;
            backgroundWorker.RunWorkerAsync();
            PendingBoxConfigurations configurations = new PendingBoxConfigurations();
            configurations.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            pendingHandler = PendingBox.Show(string.Format("正在分析日志文件...({0}%)", 0), "请等待", false, Application.Current.MainWindow, configurations);
        }
        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {

                RecordContext context = new RecordContext();
                context.OrderRecords = new List<JspayOrderRecord>();
                context.ParkRecords = new List<ParkRecord>();
                context.DeviceCache = new List<DeviceInfo>();
                List<string> lastLines = new List<string>();
                int totalLines = AnalyseUtils.GetTotalLines(filePathList);
                if (totalLines == 0)
                {
                    throw new Exception("日志内容为空");
                }
                int currentLines = 0;
                int currentPencent = 0;
                foreach (var filePath in filePathList)
                {
                    using (StreamReader sr = new StreamReader(filePath, Encoding.GetEncoding("gb2312")))
                    {
                        while (!sr.EndOfStream)
                        {

                            string line = sr.ReadLine();
                            AnalyseUtils.ParseRecord(context, line, lastLines);
                            AnalyseUtils.ParseOrderRecord(context, line, lastLines);
                            if (lastLines.Count > 100)
                                lastLines.RemoveAt(0);
                            lastLines.Add(line);

                            //更新进度
                            currentLines++;
                            int pencent = (currentLines * 100) / totalLines;
                            if (pencent > 99)
                                pencent = 99;
                            if (pencent != currentPencent)
                            {
                                backgroundWorker.ReportProgress(pencent);
                            }
                            currentPencent = pencent;
                        }
                    }
                }
                AnalyseUtils.MergeParkRecordAndOrder(context);

                StringBuilder sb = new StringBuilder();
                foreach (var parkRecord in context.ParkRecords)
                {
                    if ((string.IsNullOrEmpty(plate) || AnalyseUtils.FuzzyCompare(parkRecord.CredentialNo, plate) || parkRecord.HistoryCredentialNo.Any(y => AnalyseUtils.FuzzyCompare(y, plate)))
                        && (string.IsNullOrEmpty(deviceName) || parkRecord.DeviceName.Contains(deviceName))
                        )
                    {
                        sb.Append("=======================");
                        sb.Append(parkRecord.CredentialNo);
                        sb.Append("=======================");
                        sb.Append(Environment.NewLine);
                        foreach (var logNode in parkRecord.LogNodes)
                        {
                            sb.Append(logNode.LogTime.ToString("yyyy-MM-dd HH:mm:ss,fff "));
                            sb.Append(logNode.Message);
                            sb.Append(Environment.NewLine);

                        }
                    }

                }
                e.Result = sb.ToString();
            }
            catch (Exception ex)
            {
                e.Result = "日志分析异常：" + ex.ToString();
            }

        }
        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (pendingHandler != null)
                pendingHandler.UpdateMessage(string.Format("正在分析日志文件...({0}%)", e.ProgressPercentage));
        }
        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (pendingHandler != null)
                pendingHandler.Close();
            string log = e.Result as string;
            if (!string.IsNullOrEmpty(log))
            {
                this.Result = log;
            }
            else
            {
                this.Result = "无搜索结果";
            }

        }
    }
}
