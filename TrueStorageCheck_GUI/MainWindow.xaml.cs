/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Image = System.Drawing.Image;

namespace TrueStorageCheck_GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : AcrylicWindow
    {
        public static MainWindow Instance { get; private set; }

        public const string DLL_STR = "TrueStorageCheck.dll";

        // Import the C++ function
        [DllImport(DLL_STR, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDevices([MarshalAs(UnmanagedType.I1)] bool includeLocalDisks, out IntPtr devices);

        // Device struct
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 261)]
            public string path;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 261)]
            public string name;

            public char driveLetter;

            public ulong capacity;
        }

        /// <summary>
        /// To not update the UI every time, we add the pending logs here
        /// </summary>
        Queue<string> PendingLogs = new Queue<string>();

        /// <summary>
        /// Adds text to the log TextBox and scrolls to end afterwards
        /// </summary>
        /// <param name="text"></param>
        public void AddLog(TestUserControl tuc, string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                PendingLogs.Enqueue(DateTime.Now.ToString("[HH:mm ss] ") + ((tuc == null) ? "" : (tuc.LastSelectedDevice != null) ? tuc.LastSelectedDevice.DriveLetter + ": " : "") + text);
            }
        }

        /// <summary>
        /// Used to add all pending logs to the textbox
        /// </summary>
        private void UpdateLogs()
        {
            if (PendingLogs.Count > 0)
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    // Dequeue elements, up to 10 elements at a time
                    int dequeueCount = 0;
                    while (PendingLogs.Count > 0 || dequeueCount > 10)
                    {
                        var str = PendingLogs.Dequeue();
                        LogTextBox.Text += str + "\r\n";
                    }

                    LogTextBox.ScrollToEnd();
                }));
        }

        /// <summary>
        /// Retrieves information about all connected media devices, with an option to include local disks
        /// </summary>
        /// <param name="includeLocal">A boolean value indicating whether to include local disks</param>
        /// <returns>A List of Device objects representing the available media devices</returns>
        public List<Device> GetMediaDevices(bool includeLocal)
        {
            // Convert the returned pointer to a List of DeviceInfo objects
            List<Device> devices = new();

            IntPtr deviceArrayPtr;
            int deviceCount = GetDevices(includeLocal, out deviceArrayPtr);
            AddLog(null, "Got " + deviceCount + " devices.");

            for (int i = 0; i < deviceCount; i++)
            {
                IntPtr currentPtr = IntPtr.Add(deviceArrayPtr, i * Marshal.SizeOf(typeof(DeviceInfo)));
                DeviceInfo device = Marshal.PtrToStructure<DeviceInfo>(currentPtr);

                // We ignore everything without a drive letter
                if (device.driveLetter != '\0')
                    devices.Add(new(device.name, device.path, device.driveLetter, device.capacity));

                Console.WriteLine($"Device {i + 1}:");
                Console.WriteLine($"Path: {device.path}");
                Console.WriteLine($"Name: {device.name}");
            }

            return devices;
        }


        /// <summary>
        /// Updates DeviceList
        /// </summary>
        /// <remarks>
        /// Static for convenience
        /// </remarks>
        public void UpdateDevices(bool local)
        {
            var devices = GetMediaDevices(local);

            Instance.Dispatcher.Invoke(() =>
            {
                // Instead of clearing and re-adding everything we will updating the devices

                // Remove non-existing devices
                for (int i = DeviceList.Count - 1; i >= 0; i--)
                {
                    var existingDevice = DeviceList[i];

                    if (!devices.Any(d => d.Path == existingDevice.Path))
                        DeviceList.RemoveAt(i);
                }

                foreach (var dev in devices)
                {
                    var existingDevice = DeviceList.FirstOrDefault(d => d.Path == dev.Path);
                    if (existingDevice != null)
                    {
                        // Update existing device
                        existingDevice.UpdateDevice(dev);
                    }
                    else
                    {
                        // Add new device
                        DeviceList.Add(dev);
                    }
                }
            });
        }

        /// <summary>
        /// Single instance, so I'll leave this static
        /// </summary>
        public ObservableCollection<Device> DeviceList = new();

        private ObservableCollection<Language> LanguageList = new();

        BackgroundWorker LogsWorker = new();

        public MainWindow()
        {
            try
            {
                Instance = this;

                // Use language if the resource exists
                LoadLanguageList();

                // Attempt to load system language
                var selectedLanguage = LanguageList.ToList().Find(l => l.Code.ToLower() == System.Threading.Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName.ToLower());

                // Or the first if failed
                if (selectedLanguage == null)
                    selectedLanguage = LanguagesComboBox.Items[0] as Language;

                LoadLanguage(selectedLanguage);

                InitializeComponent();
                LanguagesComboBox.ItemsSource = LanguageList;

                LanguagesComboBox.SelectedItem = selectedLanguage;
                MainGrid.IsEnabled = false;

                LogsWorker.DoWork += LogsWorker_DoWork;
                LogsWorker.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }


        }

        private bool IsClosing = false;

        private void LogsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
            while (!IsClosing)
            {
                UpdateLogs();
                System.Threading.Thread.Sleep(5000);
            }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }

        private bool _isLoaded = false;


        // Used for language management, a bit QND
        private const string resourceDefault = "TrueStorageCheck_GUI.Resources";
        private const string resourceLanguage = resourceDefault + ".language_";
        public static ResourceManager LanguageResource;

        /// <summary>
        /// Iterates resources and adds languages
        /// </summary>
        public void LoadLanguageList()
        {
            // Get all the resources, iterate and add them as languages
            string[] resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            foreach (string resourceName in resourceNames)
            {
                if (resourceName.Contains(resourceLanguage))
                {
                    ResourceManager rm = new ResourceManager(resourceName.Substring(0, resourceName.LastIndexOf('.')), Assembly.GetExecutingAssembly());

                    var language = rm.GetString("language");
                    var iso = rm.GetString("iso_code");

                    // Iterate over each resource in the resource file, get language and iso_code and add them
                    if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(iso))
                        LanguageList.Add(new(language, iso));
                }
            }
        }

        public bool LoadLanguage(Language language)
        {
            // TODO: Use properties to define the default language
            if (LanguageResource != null)
                LanguageResource.ReleaseAllResources();

            var ret = false;

            try
            {
                LanguageResource = new ResourceManager(resourceLanguage + language.Code, Assembly.GetExecutingAssembly());

                var l = LanguageResource.GetString("language");
                ret = l != null;
            }
            catch (Exception)
            {
                // Revert to English if not found
                LanguageResource = new ResourceManager(resourceLanguage + "en", Assembly.GetExecutingAssembly());
            }

            // Trigger the LanguageChanged event to update the localized strings
            LocalizedStringExtension.OnLanguageChanged();

            // QND stuf
            if (DeviceTestTabControl != null)
            {
                foreach (var testUserControl in DeviceTestTabControl.Items)
                {
                    if (typeof(TestUserControl).Equals((testUserControl as TabItem).Content.GetType()))
                    {
                        var uc = (TestUserControl)(testUserControl as TabItem).Content;
                        if (uc != null)
                            uc.UpdateCurrentInfo();
                    }
                }
            }

            return ret;
        }


        private void LanguagesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded && LanguagesComboBox.SelectedItem != null) return;

            if (!LoadLanguage(LanguagesComboBox.SelectedItem as Language))
                LanguagesComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Load previous scan if available or scan if necessary
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                BottomLabel.Content = "TrueStorageCheck GUI  v" + version.Major + "." + version.Minor + " © " + DateTime.Now.Year + " - Mywk.Net";

                // Check if DLL exists, exit otherwise
                if (!File.Exists("TrueStorageCheck.dll"))
                {
                    _ = Task.Run(() =>
                    {
                        Dispatcher.Invoke(() =>
                        {

                            MessageBox.Show(this, LanguageResource.GetString("dll_not_found_text"), LanguageResource.GetString("dll_not_found_title"), MessageBoxButton.OK);
                            this.Close();

                        });
                    });
                }
                else
                {
                    if (Properties.Settings.Default.CheckForUpdates)
                    {
                        CheckForUpdatesCheckBox.IsChecked = true;

                        if (await CheckForUpdatesAsync())
                            UpdateLabel.Visibility = Visibility.Visible;
                    }

                    AddNewDeviceTest();
                    DeviceTestTabControl.SelectedIndex = 0;

                    MainGrid.IsEnabled = true;
                    _isLoaded = true;

                    UpdateDevices(false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        /// <summary>
        /// Self explanatory
        /// </summary>
        private bool isWorking = false;

        /// <summary>
        /// Check if a newer version of the software is available
        /// </summary>
        private async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var web = new System.Net.Http.HttpClient();
                var url = "https://Mywk.Net/software.php?assembly=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                var responseString = (await web.GetAsync(url)).Content.ToString();

                foreach (var str in responseString.Split('\n'))
                {
                    if (str.Contains("Version"))
                    {
                        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                        if (version.Major + "." + version.Minor != str.Split('=')[1])
                            return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }



        /// <summary>
        /// Utility method for converting a byte array to string
        /// </summary>
        /// <param name="ba"></param>
        /// <returns></returns>
        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }



        /// <summary>
        /// Move window from anywhere
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }


        /// <summary>
        /// Open the website using the default browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BottomLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var targetURL = "https://mywk.net/software/true-storage-check";
            var psi = new ProcessStartInfo
            {
                FileName = targetURL,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private void UpdateLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BottomLabel_OnMouseLeftButtonDown(sender, e);
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (isWorking)
            {
                if (MessageBox.Show(LanguageResource.GetString("closing_analyzing_confirm"), LanguageResource.GetString("warning"), MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            IsClosing = true;
        }

        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                e.Handled = true;
            }
        }

        private bool updatingTabs = false;

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            NavigationGrid.IsEnabled = StartAllButton.IsEnabled = AllProgressGrid.IsEnabled = DeviceTestTabControl.Items.Count > 2;

            if (!_isLoaded || updatingTabs) return;

            updatingTabs = true;

            // We don't create tabs on selection changed as it can interfer with CTRL-TAB, instead we redirect to the first tab if we have reached the insert tab
            if (DeviceTestTabControl.SelectedIndex + 1 == DeviceTestTabControl.Items.Count)
            {
                DeviceTestTabControl.SelectedIndex = 0;
            }

            updatingTabs = false;
        }

        #region Navigation
        private void LeftButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceTestTabControl.SelectedIndex - 1 >= 0)
                DeviceTestTabControl.SelectedIndex--;
            else
                DeviceTestTabControl.SelectedIndex = DeviceTestTabControl.Items.Count - 2;
        }

        private void RightButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceTestTabControl.SelectedIndex + 1 < DeviceTestTabControl.Items.Count)
                DeviceTestTabControl.SelectedIndex++;
        }
        #endregion

        /// <summary>
        /// Creates a new tab with the TestUserControl
        /// </summary>
        private void AddNewDeviceTest()
        {
            if (DeviceTestTabControl.Items.Count > 10 && !App.NoMaxDevices)
                MessageBox.Show(this, LanguageResource.GetString("limit_reached"), LanguageResource.GetString("warning"), MessageBoxButton.OK);
            else
            {
                var userControl = new TestUserControl();
                var tabItem = new TabItem();

                var headerStackPanel = new StackPanel() { Orientation = Orientation.Vertical };

                Label label = new() { DataContext = userControl };
                label.SetBinding(Label.ContentProperty, new System.Windows.Data.Binding("TestName") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });

                StackPanel infoStackPanel = new() { Orientation = Orientation.Horizontal };

                Label statLabel = new Label() { Content = "●", FontSize = 20, DataContext = userControl, VerticalAlignment = VerticalAlignment.Top, Padding = new(0) };
                statLabel.SetBinding(Label.ForegroundProperty, new System.Windows.Data.Binding("HeaderBackground") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });

                Label infoLabel = new() { DataContext = userControl };
                infoLabel.SetBinding(Label.ContentProperty, new System.Windows.Data.Binding("CurrentInfo") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });

                infoStackPanel.Children.Add(statLabel);
                infoStackPanel.Children.Add(infoLabel);

                headerStackPanel.Children.Add(label);
                headerStackPanel.Children.Add(infoStackPanel);

                Button closeButton = null;

                if (DeviceTestTabControl.Items.Count > 1)
                {
                    closeButton = new Button() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right, Content = "✖️", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkRed), Background = infoLabel.Background, Cursor = Cursors.Hand };

                    closeButton.Click += TabCloseButton_Click;
                }
                else
                    // Ghost button, just so that the height doesn't change
                    closeButton = new Button() { Background = infoLabel.Background, IsHitTestVisible = false };

                headerStackPanel.Children.Add(closeButton);

                tabItem.Header = headerStackPanel;
                tabItem.Content = userControl;


                DeviceTestTabControl.Items.Insert(DeviceTestTabControl.Items.Count - 1, tabItem);

                updatingTabs = true;
                DeviceTestTabControl.SelectedIndex = DeviceTestTabControl.Items.Count - 2;
                updatingTabs = false;
            }

            UpdateHeight();
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            // QND
            var tabItem = DeviceTestTabControl.Items.OfType<TabItem>().SingleOrDefault(ti => ti.Header.Equals((sender as Button).Parent));

            if (tabItem != null && tabItem.Content.GetType() == typeof(TestUserControl) && !((TestUserControl)tabItem.Content).IsWorking)
                DeviceTestTabControl.Items.Remove(tabItem);
        }

        private void AddTabItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                AddNewDeviceTest();
                e.Handled = true;
            }
        }

        private async void CheckForUpdatesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            var update = Properties.Settings.Default.CheckForUpdates = (bool)CheckForUpdatesCheckBox.IsChecked;
            Properties.Settings.Default.Save();

            if (update)
                await CheckForUpdatesAsync();
        }

        private void StartAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Very QND, needs to be improved!!!
            List<Device> testDevices = new();

            foreach (var testUserControl in DeviceTestTabControl.Items)
            {
                if (typeof(TestUserControl).Equals((testUserControl as TabItem).Content.GetType()))
                {
                    var uc = (TestUserControl)(testUserControl as TabItem).Content;
                    if (uc != null)
                    {
                        if (!testDevices.Any(td => td.DriveLetter == uc.LastSelectedDevice.DriveLetter || td.Path == uc.LastSelectedDevice.Path))
                            uc.StartTest();
                    }
                }
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        private async void Expander_Changed(object sender, RoutedEventArgs e)
        {
            MultipleDeviceBorder.Opacity = MultipleDeviceExpander.IsExpanded ? 1 : 0.7;
            LogExpanderBorder.Opacity = LogExpander.IsExpanded ? 1 : 0.7;
            UpdateHeight();

            await Task.Delay(100);
            _ = Dispatcher.BeginInvoke(new Action(LogTextBox.ScrollToEnd));
        }

        private void UpdateHeight()
        {
            // Queue a method call using the Dispatcher
            Dispatcher.BeginInvoke(new Action(() =>
            {
                double size = Math.Max(LogExpander.ActualHeight, MultipleDeviceExpander.ActualHeight);

                this.Height = 195 + DeviceTestTabControl.ActualHeight + size;
            }), DispatcherPriority.Render);
        }
    }
}
