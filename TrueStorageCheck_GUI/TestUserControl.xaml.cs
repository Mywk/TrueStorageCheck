/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
using System.Management;
using System.ComponentModel;

namespace TrueStorageCheck_GUI
{
    /// <summary>
    /// Interaction logic for TestUserControl.xaml
    /// </summary>
    public partial class TestUserControl : UserControl, INotifyPropertyChanged
    {

        // Define an event for reporting the state and progress
        public delegate void ProgressDelegate(IntPtr instance, int state, int progress, int mbWritten);

        private const string DLL_STR = MainWindow.DLL_STR;

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr DiskTest_Create(char driveLetter, ulong capacityToTest, bool stopOnFirstError, bool deleteTempFiles, bool writeLogFile, ProgressDelegate callback);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern bool DiskTest_PerformTest(IntPtr diskTestInstance);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern int DiskTest_GetTestState(IntPtr diskTestInstance);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern int DiskTest_GetTestProgress(IntPtr diskTestInstance);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern bool DiskTest_ForceStopTest(IntPtr diskTestInstance);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern bool DiskTest_Destroy(IntPtr diskTestInstance);

        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern ulong DiskTest_GetLastSuccessfulVerifyPosition(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern double DiskTest_GetAverageWriteSpeed(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern double DiskTest_GetAverageReadSpeed(IntPtr diskTestInstance);
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        static extern long DiskTest_GetTimeRemaining(IntPtr diskTestInstance);

        private string _testName;
        private string _currentInfo;
        private SolidColorBrush _headerBackground;

        public string TestName
        {
            get { return _testName; }
            set
            {
                if (_testName != value)
                {
                    _testName = value;
                    OnPropertyChanged("TestName");
                }
            }
        }

        public string CurrentInfo
        {
            get { return _currentInfo; }
            set
            {
                if (_currentInfo != value)
                {
                    _currentInfo = value;
                    OnPropertyChanged("CurrentInfo");
                }
            }
        }
        
        public SolidColorBrush HeaderBackground
        {
            get { return _headerBackground; }
            set
            {
                if (_headerBackground != value)
                {
                    _headerBackground = value;
                    OnPropertyChanged("HeaderBackground");
                }
            }
        }

        public bool IsWorking { get; set; }

        enum CurrentState
        {
            Waiting = 0,
            InProgress,
            Verifying,
            Success,
            Error,
            Aborted
        }

        private CurrentState currentState = CurrentState.Waiting;

        private int progressPercentage = 0;

        private const string newLine = "\r\n";
        private string GetStateStringFromCurrentState(CurrentState state)
        {
            switch (state)
            {
                case CurrentState.Waiting:
                    return MainWindow.LanguageResource.GetString("waiting");
                case CurrentState.InProgress:
                    return MainWindow.LanguageResource.GetString("in_progress");
                case CurrentState.Verifying:
                    return MainWindow.LanguageResource.GetString("verifying");
                case CurrentState.Success:
                    return MainWindow.LanguageResource.GetString("success");
                case CurrentState.Error:
                    return MainWindow.LanguageResource.GetString("error");
                case CurrentState.Aborted:
                    return MainWindow.LanguageResource.GetString("aborted");
                default:
                    return "";
            }
        }

        public void UpdateCurrentInfo()
        {
            CurrentInfo = GetStateStringFromCurrentState(currentState);

            TestName = MainWindow.LanguageResource.GetString("device") + ": " + DevicesComboBox.SelectedItem.ToString();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler DevicesUpdated;

        // Keep track of the last selected device
        public Device LastSelectedDevice { get; set; }

        /// <summary>
        /// Starts the device test
        /// </summary>
        public void StartTest()
        {
            if (IsWorking)
            {
                if (DiskTest != IntPtr.Zero)
                {
                    _ = Task.Run(() =>
                    {
                        // Force stop the test
                        MainWindow.Instance.AddLog("Forced test stop.");

                        if (DiskTest_ForceStopTest(DiskTest))
                        {
                            DiskTest = IntPtr.Zero;
                        }

                    }).ContinueWith(t =>
                    {
                        if (DiskTest == IntPtr.Zero)
                        {
                            IsWorking = false;
                            RestoreStartButton();
                        }
                    });
                }
                return;
            }

            IsWorking = true;

            ToggleInteration(false);

            StartButton.Content = MainWindow.LanguageResource.GetString("stop");

            SetCompletionLabel(CompletionStatus.Unknown);
            ProgressBar.Value = 0;

            // Create a progress delegate that reports progress to the console
            ProgressDelegate progressHandler = (instance, state, progress, mbWritten) =>
            {
                double averageReadSpeed = DiskTest_GetAverageReadSpeed(instance);
                double averageWriteSpeed = DiskTest_GetAverageWriteSpeed(instance);
                double remainingTimeInSeconds = (CurrentState)state == CurrentState.InProgress || (CurrentState)state == CurrentState.Verifying ? DiskTest_GetTimeRemaining(instance) : 0;

                Dispatcher.Invoke(() =>
                {

                    string stateStr = MainWindow.LanguageResource.GetString("current_state") + ":\t" + GetStateStringFromCurrentState((CurrentState)state);

                    MainWindow.Instance.AddLog(stateStr);
                    MainWindow.Instance.AddLog("MbWritten:\t" + mbWritten);

                    ProgressBar.Value = progress;

                    string infoStr = stateStr;

                    if (averageReadSpeed != 0 && averageWriteSpeed != 0)
                        infoStr += newLine + newLine + averageReadSpeed.ToString(MainWindow.LanguageResource.GetString("avg_read") + " \t0.00 MB/s" + newLine) + averageWriteSpeed.ToString(MainWindow.LanguageResource.GetString("avg_write") + " \t 0.00 MB/s" + newLine + newLine);


                    if (remainingTimeInSeconds != 0)
                    {
                        TimeSpan delta = TimeSpan.FromSeconds(remainingTimeInSeconds);
                        string formattedTime = string.Format("{0}:\t{1:00}:{2:00}:{3:00}", MainWindow.LanguageResource.GetString("remaining_time"), delta.Hours, delta.Minutes, delta.Seconds);

                        infoStr += formattedTime;
                    }

                    InfoContentLabel.Content = infoStr;

                    currentState = (CurrentState)state;

                    if (state == (int)CurrentState.Success)
                    {
                        SetCompletionLabel(CompletionStatus.Success);
                        UpdateCurrentInfo();
                    }
                    else if (state == (int)CurrentState.Error || state == (int)CurrentState.Aborted)
                    {
                        SetCompletionLabel(CompletionStatus.Failed);
                        UpdateCurrentInfo();
                    }
                    else
                    {
                        CurrentInfo = progress.ToString() + "%";
                    }
                });
            };

            // Options
            bool stopOnFirstFailure = (bool)StopOnFirstFailureCheckBox.IsChecked;
            bool removeTempFiles = (bool)RemoveTempFilesWhenDoneCheckBox.IsChecked;
            bool saveTextLog = (bool)SaveLogToMediaCheckBox.IsChecked;
            int mbToTest = (bool)AllAvailableSpaceCheckBox.IsChecked ? 0 : MbNumericUpDown.Value;

            _ = Task.Run(() =>
            {
                // Just in case
                if (DiskTest != IntPtr.Zero)
                {
                    if (DiskTest_ForceStopTest(DiskTest))
                        DiskTest = IntPtr.Zero;
                    else
                        return;
                }


                DiskTest = DiskTest_Create(LastSelectedDevice.DriveLetter, (ulong)mbToTest, stopOnFirstFailure, removeTempFiles, saveTextLog, progressHandler);

                bool startResult = DiskTest_PerformTest(DiskTest);

                if (!startResult)
                {
                    if (DiskTest != IntPtr.Zero)
                    {
                        var lastSuccessfulWritePosition = DiskTest_GetLastSuccessfulVerifyPosition(DiskTest);
                        MainWindow.Instance.AddLog(MainWindow.LanguageResource.GetString("last_successful_write") + ": " + lastSuccessfulWritePosition);
                    }
                }

                if (DiskTest != IntPtr.Zero)
                {
                    DiskTest_Destroy(DiskTest);
                    DiskTest = IntPtr.Zero;
                }

            }).ContinueWith(t =>
            {
                IsWorking = false;
                RestoreStartButton();
            });
        }


        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        public TestUserControl()
        {
            InitializeComponent();

            TestName = MainWindow.LanguageResource.GetString("device_test");
            CurrentInfo = MainWindow.LanguageResource.GetString("waiting");
            HeaderBackground = (SolidColorBrush)Application.Current.Resources["HeaderColorBrush"];

            Binding binding = new Binding();
            binding.Source = MainWindow.Instance.DeviceList;
            binding.NotifyOnSourceUpdated = true;

            DevicesComboBox.SetBinding(ItemsControl.ItemsSourceProperty, binding);

            MainWindow.Instance.DeviceList.CollectionChanged += DeviceList_CollectionChanged;
        }

        private void DeviceList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateDeviceSourceList();

            // Re-select last selected device if still available
            if (LastSelectedDevice != null && MainWindow.Instance.DeviceList.Count > 0)
            {
                var dev = MainWindow.Instance.DeviceList.ToList().Find(d => d.Path == LastSelectedDevice.Path);
                if (dev != null)
                    DevicesComboBox.SelectedItem = LastSelectedDevice = dev;
            }
        }

        bool isScanning = false;

        /// <summary>
        /// Scans for devices and updates the UI accordingly
        /// </summary>
        /// <returns></returns>
        async Task RefreshAvailableDevices(bool localDisks)
        {
            if (isScanning)
                return;

            isScanning = true;

            ToggleInteration(false);

            await Task.Run(() =>
            {
                MainWindow.Instance.UpdateDevices(localDisks);

                Dispatcher.Invoke(() =>
                {
                    ToggleInteration(true);
                    isScanning = false;
                });
            });
        }



        private string filename = "";

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshAvailableDevices((bool)LocalDisksCheckBox.IsChecked);
        }


        public void UpdateDeviceSourceList()
        {
            if (DevicesComboBox.SelectedIndex == -1 && MainWindow.Instance.DeviceList.Count > 0)
                DevicesComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Updates the MbNumericUpDown values
        /// </summary>
        /// <returns>Selected device capacity in MB</returns>
        public int UpdateAllAvailableSpace()
        {
            if (LastSelectedDevice == null) return 0;

            int capacityInMb = (int)((double)LastSelectedDevice.Capacity / (1024 * 1024));

            if ((bool)AllAvailableSpaceCheckBox.IsChecked)
                MbNumericUpDown.Value = MbNumericUpDown.Maximum = capacityInMb;

            return capacityInMb;
        }

        private void DevicesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeviceSourceList();

            bool canTest = false;

            if (DevicesComboBox.SelectedIndex >= 0)
            {
                TestName = MainWindow.LanguageResource.GetString("device") + ": " + DevicesComboBox.SelectedItem.ToString();
                LastSelectedDevice = DevicesComboBox.SelectedItem as Device;

                if (LastSelectedDevice.Capacity > 0)
                {
                    var capacityInMb = UpdateAllAvailableSpace();

                    MbLabel.Content = capacityInMb.ToString() + " MB";
                    StartButton.IsEnabled = true;

                    canTest = true;
                }

            }

            if (!canTest)
            {
                MbNumericUpDown.Maximum = MbNumericUpDown.Value = 0;
                MbLabel.Content = "? MB";

                StartButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Toggles interaction, this could be simpler but eh
        /// </summary>
        /// <param name="value"></param>
        private void ToggleInteration(bool value)
        {
            DevicesComboBox.IsEnabled = RefreshButton.IsEnabled = OptionsBorder.IsEnabled = LocalDisksCheckBox.IsEnabled = value;
        }

        IntPtr DiskTest = IntPtr.Zero;

        private void RestoreStartButton()
        {
            Dispatcher.Invoke(() =>
            {
                StartButton.Content = MainWindow.LanguageResource.GetString("start");
                ToggleInteration(true);
            });
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartTest();
        }

        /// <summary>
        /// Used to update the progress completion bar & label
        /// </summary>
        internal enum CompletionStatus
        {
            Unknown,
            Success,
            Failed
        }

        private void SetCompletionLabel(CompletionStatus status)
        {
            var text = "❔";
            var color = Colors.Gray;
            switch (status)
            {
                case CompletionStatus.Unknown:
                    color = Colors.DarkOrange;
                    break;
                case CompletionStatus.Success:
                    text = "✔️";
                    color = Colors.DarkGreen;
                    ProgressBar.Value = 100;
                    break;
                case CompletionStatus.Failed:
                    text = "✖️";
                    color = Colors.DarkRed;
                    ProgressBar.Value = 100;
                    break;
                default:
                    color = Colors.Purple;
                    break;
            }

            ProgressBar.Foreground = HeaderBackground = new SolidColorBrush(color);

            ProgressCompletionBorder.Background = new SolidColorBrush(color);
            ProgressCompletionLabel.Content = text;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDeviceSourceList();
        }

        private void ShowLocalCheckBox_CheckedUnchecked(object sender, RoutedEventArgs e)
        {
            _ = RefreshAvailableDevices((bool)LocalDisksCheckBox.IsChecked);
        }

        private void AllAvailableSpaceCheckBox_CheckedUnchecked(object sender, RoutedEventArgs e)
        {
            UpdateAllAvailableSpace();
        }

        private void MbNumericUpDown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            if (LastSelectedDevice == null) return;

            if (MbNumericUpDown.Value != 0)
                AllAvailableSpaceCheckBox.IsChecked = (bool)(MbNumericUpDown.Value == MbNumericUpDown.Maximum);
        }
    }
}
