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

        public bool IsRunning { get; set; }

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

        DateTime TestStartedTime;

        /// <summary>
        /// Starts the device test
        /// </summary>
        public void StartTest()
        {
            if (IsRunning)
            {
                if (DiskTest != IntPtr.Zero)
                {
                    _ = Task.Run(() =>
                    {
                        // Force stop the test
                        MainWindow.Instance.AddLog(this, "Forced test stop.");

                        if (DiskTest_ForceStopTest(DiskTest))
                        {
                            DiskTest = IntPtr.Zero;
                        }

                    }).ContinueWith(t =>
                    {
                        if (DiskTest == IntPtr.Zero)
                        {
                            if (ProgressHandler == null)
                            {
                                IsRunning = false;
                                RestoreStartButton();
                            }
                            else
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StartButton.IsEnabled = false;
                                    ToggleInteration(false);
                                });
                            }
                        }
                    });
                }
                return;
            }

            IsRunning = true;

            TestStartedTime = DateTime.Now;

            ToggleInteration(false);

            StartButton.Content = MainWindow.LanguageResource.GetString("stop");

            SetCompletionLabel(CompletionStatus.Unknown);
            ProgressBar.Value = 0;

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


                DiskTest = DiskTest_Create(LastSelectedDevice.DriveLetter, (ulong)mbToTest, stopOnFirstFailure, removeTempFiles, saveTextLog, ProgressHandler);

                bool startResult = DiskTest_PerformTest(DiskTest);

                if (!startResult)
                {
                    if (DiskTest != IntPtr.Zero)
                    {
                        var lastSuccessfulWritePosition = DiskTest_GetLastSuccessfulVerifyPosition(DiskTest);
                        MainWindow.Instance.AddLog(this, MainWindow.LanguageResource.GetString("last_successful_write") + ": " + lastSuccessfulWritePosition);
                    }
                }

                if (DiskTest != IntPtr.Zero)
                {
                    DiskTest_Destroy(DiskTest);
                    DiskTest = IntPtr.Zero;
                }

            }).ContinueWith(t =>
            {
                IsRunning = false;
                RestoreStartButton();
            });
        }


        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private ProgressDelegate ProgressHandler = null;

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

            // Create a progress delegate that reports progress to the console
            ProgressHandler = (instance, state, progress, mbChanged) =>
            {
                double remainingTimeInSeconds = (CurrentState)state == CurrentState.InProgress || (CurrentState)state == CurrentState.Verifying ? DiskTest_GetTimeRemaining(instance) : 0;

                double averageReadSpeed = DiskTest_GetAverageReadSpeed(instance);
                double averageWriteSpeed = DiskTest_GetAverageWriteSpeed(instance);

                string newLine = Environment.NewLine;

                Dispatcher.Invoke(() =>
                {
                    string stateStr = $"{MainWindow.LanguageResource.GetString("current_state")}\t\t{GetStateStringFromCurrentState((CurrentState)state)}";
                    MainWindow.Instance.AddLog(this, stateStr);
                    MainWindow.Instance.AddLog(this, $"Mb:\t{mbChanged}");

                    ProgressBar.Value = progress;

                    string infoStr = stateStr;

                    if (averageReadSpeed != 0 && !double.IsInfinity(averageReadSpeed))
                        infoStr += $"{newLine}{averageReadSpeed.ToString($"{MainWindow.LanguageResource.GetString("avg_read")} \t\t0.00 MB/s")}";

                    if (averageWriteSpeed != 0 && !double.IsInfinity(averageWriteSpeed))
                        infoStr += $"{newLine}{averageWriteSpeed.ToString($"{MainWindow.LanguageResource.GetString("avg_write")}\t\t 0.00 MB/s")}";

                    // Elapsed time
                    TimeSpan delta = DateTime.Now - TestStartedTime;
                    string formattedTime = $"{newLine}{MainWindow.LanguageResource.GetString("elapsed_time")}\t\t{delta.Hours:00}:{delta.Minutes:00}:{delta.Seconds:00}";
                    infoStr += formattedTime;

                    if (remainingTimeInSeconds != 0)
                    {
                        delta = TimeSpan.FromSeconds(remainingTimeInSeconds);
                        formattedTime = $"{newLine}{MainWindow.LanguageResource.GetString("remaining_time")}\t{delta.Hours:00}:{delta.Minutes:00}:{delta.Seconds:00}";
                        infoStr += formattedTime;
                    }


                    currentState = (CurrentState)state;

                    if (state == (int)CurrentState.Success || state == (int)CurrentState.Error || state == (int)CurrentState.Aborted)
                    {
                        if (state == (int)CurrentState.Success)
                        {
                            SetCompletionLabel(CompletionStatus.Success);

                            infoStr += $"{newLine}{newLine}{MainWindow.LanguageResource.GetString("success")}";
                        }
                        else if(state == (int)CurrentState.Aborted)
                        {
                            SetCompletionLabel(CompletionStatus.Failed);

                            infoStr += $"{newLine}{newLine}{MainWindow.LanguageResource.GetString("aborted")}";
                        }
                        else
                        {
                            SetCompletionLabel(CompletionStatus.Failed);

                            var lastSuccessfulVerifiedByte = DiskTest_GetLastSuccessfulVerifyPosition(instance);

                            if (lastSuccessfulVerifiedByte > 0)
                                infoStr += $"{newLine}{newLine}{MainWindow.LanguageResource.GetString("failed_after_byte")}{newLine}{lastSuccessfulVerifiedByte}";
                            else
                                infoStr += $"{newLine}{newLine}{MainWindow.LanguageResource.GetString("error")}";
                        }

                        UpdateCurrentInfo();
                        RestoreStartButton();

                        IsRunning = false;
                    }
                    else
                        CurrentInfo = $"{progress}%";

                    InfoContentLabel.Content = infoStr;
                });
            };
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

                    if (CanInteract && !IsRunning)
                        StartButton.IsEnabled = true;
                });
            });
        }


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

                    if(CanInteract && capacityInMb > 0)
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

        private bool CanInteract = true;

        /// <summary>
        /// Toggles interaction, this could be simpler but eh
        /// </summary>
        /// <param name="value"></param>
        private void ToggleInteration(bool value)
        {
            CanInteract = value;

            DevicesComboBox.IsEnabled = RefreshButton.IsEnabled = OptionsBorder.IsEnabled = LocalDisksCheckBox.IsEnabled = value;
        }

        IntPtr DiskTest = IntPtr.Zero;

        private void RestoreStartButton()
        {
            Dispatcher.Invoke(() =>
            {
                StartButton.Content = MainWindow.LanguageResource.GetString("start");
                ToggleInteration(true);
                StartButton.IsEnabled = true;
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

            if (MbNumericUpDown.Maximum != 0)
            {
                if(MbNumericUpDown.Value > MbNumericUpDown.Maximum)
                    MbNumericUpDown.Value = MbNumericUpDown.Maximum;

                AllAvailableSpaceCheckBox.IsChecked = (bool)(MbNumericUpDown.Value == MbNumericUpDown.Maximum || MbNumericUpDown.Value == 0);
            }
        }

        private void SaveLogToMediaCheckBox_CheckedUnchecked(object sender, RoutedEventArgs e)
        {
            if (LastSelectedDevice == null) return;

            bool removeTempFiles = (bool)RemoveTempFilesWhenDoneCheckBox.IsChecked;

            if ((!removeTempFiles && (bool)SaveLogToMediaCheckBox.IsChecked))
                SaveLogToMediaCheckBox.IsChecked = false;

            SaveLogToMediaCheckBox.IsEnabled = removeTempFiles ? true : false;
        }
    }
}
