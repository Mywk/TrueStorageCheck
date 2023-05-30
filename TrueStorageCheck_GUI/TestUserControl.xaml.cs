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
using static TrueStorageCheck_GUI.DiskTest;

namespace TrueStorageCheck_GUI
{
    /// <summary>
    /// Interaction logic for TestUserControl.xaml
    /// </summary>
    public partial class TestUserControl : UserControl, INotifyPropertyChanged
    {

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

        // Keep track of the last selected device
        public Device LastSelectedDevice { get; set; }

        DateTime TestStartedTime;

        /// <summary>
        /// Stops running test
        /// </summary>
        public void StopTest()
        {
            if (DiskTest != IntPtr.Zero)
            {
                _ = Task.Run(() =>
                {
                    // Force stop the test
                    MainWindow.Instance.AddLog(this, "Forced test stop.");

                    if (DiskTest_ForceStopTest(DiskTest) == 0x01)
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
        }

        private string startButtonText = "start";

        public string StartButtonText
        {
            get { 
                return MainWindow.LanguageResource.GetString(startButtonText);
            }
            set
            {
                startButtonText = value;
                OnPropertyChanged(nameof(StartButtonText));
            }
        }

        /// <summary>
        /// Starts the device test
        /// </summary>
        public void StartTest()
        {
            if (IsRunning)
            {
                StopTest();
                return;
            }

            IsRunning = true;

            TestStartedTime = DateTime.Now;

            UpdateStartButton(false);
            ToggleInteration(false);

            SetCompletionLabel(CompletionStatus.Unknown);
            ProgressBar.Value = 0;

            // Options
            bool stopOnFirstFailure = (bool)StopOnFirstFailureCheckBox.IsChecked;
            bool removeTempFiles = (bool)RemoveTempFilesWhenDoneCheckBox.IsChecked;
            bool saveTextLog = (bool)SaveLogToMediaCheckBox.IsChecked;
            int mbToTest = (bool)AllAvailableSpaceCheckBox.IsChecked ? 0 : MbNumericUpDown.Value;

            bool canTest = true;

            // Just in case
            if (DiskTest != IntPtr.Zero)
            {
                if (DiskTest_ForceStopTest(DiskTest) == 0x01)
                    DiskTest = IntPtr.Zero;
                else
                    return;
            }

            DiskTest = DiskTest_Create(LastSelectedDevice.DriveLetter, (ulong)mbToTest, stopOnFirstFailure, removeTempFiles, saveTextLog, ProgressHandler);

            // Delete any older TestFiles
            DiskTest_DeleteTestFiles(DiskTest);

            bool isDiskEmpty = DiskTest_IsDiskEmpty(DiskTest) == 0x01;
            if (!isDiskEmpty)
            {
                string newLine = Environment.NewLine;

                string message = MainWindow.LanguageResource.GetString("not_empty");
                string caption = MainWindow.LanguageResource.GetString("warning");

                MessageBoxResult result = MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    canTest = false;

                    if (DiskTest != IntPtr.Zero)
                    {
                        DiskTest_Destroy(DiskTest);
                        DiskTest = IntPtr.Zero;
                    }

                    SetCompletionLabel(CompletionStatus.Failed);
                    InfoContentTextBlock.Text = MainWindow.LanguageResource.GetString("aborted");

                    IsRunning = false;
                    RestoreStartButton();
                }
            }

            if (canTest)
            {
                _ = Task.Run(() =>
                {
                    bool startResult = DiskTest_PerformTest(DiskTest) == 0x01;

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
        }

        private void UpdateStartButton(bool start)
        {
            StartButtonText = start ? "start" : "stop";
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

                // Quite a bit QND but will do for now
                Dispatcher.Invoke(() =>
                {
                    string stateStr = GetStateStringFromCurrentState((CurrentState)state);
                    MainWindow.Instance.AddLog(this, $"{MainWindow.LanguageResource.GetString("current_state")} {stateStr}");
                    MainWindow.Instance.AddLog(this, $"Mb:\t{mbChanged}");

                    ProgressBar.Value = progress;

                    string infoStr = string.Format("{0,-24}\t{1}", MainWindow.LanguageResource.GetString("current_state"), stateStr);
                    string newLine = Environment.NewLine;

                    if (averageReadSpeed != 0 && !double.IsInfinity(averageReadSpeed))
                    {
                        string averageReadLabel = $"{MainWindow.LanguageResource.GetString("avg_read")}";
                        string averageReadValue = averageReadSpeed.ToString("0.00 MB/s");
                        infoStr += $"{newLine}{string.Format("{0,-24}\t{1}", averageReadLabel, averageReadValue)}";
                    }

                    if (averageWriteSpeed != 0 && !double.IsInfinity(averageWriteSpeed))
                    {
                        string averageWriteLabel = $"{MainWindow.LanguageResource.GetString("avg_write")}";
                        string averageWriteValue = averageWriteSpeed.ToString("0.00 MB/s");
                        infoStr += $"{newLine}{string.Format("{0,-24}\t{1}", averageWriteLabel, averageWriteValue)}";
                    }

                    // Elapsed time
                    TimeSpan delta = DateTime.Now - TestStartedTime;
                    string elapsedLabel = $"{MainWindow.LanguageResource.GetString("elapsed_time")}";
                    string elapsedValue = $"{delta.Hours:00}:{delta.Minutes:00}:{delta.Seconds:00}";
                    infoStr += $"{newLine}{string.Format("{0,-24}\t{1}", elapsedLabel, elapsedValue)}";

                    if (remainingTimeInSeconds != 0)
                    {
                        delta = TimeSpan.FromSeconds(remainingTimeInSeconds);
                        string remainingLabel = $"{MainWindow.LanguageResource.GetString("remaining_time")}";
                        string remainingValue = $"{delta.Hours:00}:{delta.Minutes:00}:{delta.Seconds:00}";
                        infoStr += $"{newLine}{string.Format("{0,-24}\t{1}", remainingLabel, remainingValue)}";
                    }

                    currentState = (CurrentState)state;

                    if (state == (int)CurrentState.Success || state == (int)CurrentState.Error || state == (int)CurrentState.Aborted)
                    {
                        if (state == (int)CurrentState.Success)
                        {
                            SetCompletionLabel(CompletionStatus.Success);

                            string successLabel = $"{MainWindow.LanguageResource.GetString("success")}";
                            string deviceLegitLabel = $"{MainWindow.LanguageResource.GetString("device_seems_legit")}";
                            infoStr += $"{newLine}{newLine}{successLabel}. {deviceLegitLabel}";
                        }
                        else if (state == (int)CurrentState.Aborted)
                        {
                            SetCompletionLabel(CompletionStatus.Failed);

                            string abortedLabel = $"{MainWindow.LanguageResource.GetString("aborted")}";
                            infoStr += $"{newLine}{newLine}{abortedLabel}";
                        }
                        else
                        {
                            SetCompletionLabel(CompletionStatus.Failed);

                            string errorLabel = $"{MainWindow.LanguageResource.GetString("error")}";
                            var lastSuccessfulVerifiedByte = DiskTest_GetLastSuccessfulVerifyPosition(instance);

                            if (lastSuccessfulVerifiedByte > 0)
                            {
                                string failedByteLabel = $"{MainWindow.LanguageResource.GetString("failed_after_byte")}";
                                infoStr += $"{newLine}{newLine}{errorLabel}. {failedByteLabel}: {lastSuccessfulVerifiedByte}{newLine}";

                            }
                            else
                            {
                                infoStr += $"{newLine}{newLine}{errorLabel}. ";
                            }

                            if (state != (int)CurrentState.Aborted)
                                infoStr += $"{MainWindow.LanguageResource.GetString("device_seems_fake")}";
                        }

                        UpdateCurrentInfo();
                        RestoreStartButton();

                        IsRunning = false;
                    }
                    else
                        CurrentInfo = $"{progress}%";

                    InfoContentTextBlock.Text = infoStr;
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

                    if (CanInteract && capacityInMb > 0)
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
                UpdateStartButton(true);
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

            Binding binding = new Binding();
            binding.Source = this;
            binding.Path = new PropertyPath("StartButtonText");
            binding.NotifyOnSourceUpdated = true;

            // This was a multi-binding but it was overkill
            LocalizedStringExtension.LanguageChanged += LocalizedStringExtension_LanguageChanged;

            BindingOperations.SetBinding(StartButton, Button.ContentProperty, binding);

            UpdateDeviceSourceList();
        }

        private void LocalizedStringExtension_LanguageChanged(object sender, EventArgs e)
        {
            StartButtonText = startButtonText;
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
                if (MbNumericUpDown.Value > MbNumericUpDown.Maximum)
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
