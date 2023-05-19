using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TrueStorageCheck_GUI
{
    /// <summary>
    /// Extended WPF Window with BlurBehind and compatibility with both MahApps and MaterialDesignInXamlToolkit
    /// </summary>
    public class AcrylicWindow : Window
    {
        /// <summary>
        /// This property can be used to find out if the current window is using the dark theme
        /// </summary>
        public static bool IsDarkTheme { get; private set; } = false;

        internal enum AccentState
        {
            ACCENT_DISABLED = 1,            // Disabled
            ACCENT_ENABLE_GRADIENT = 0,     // No idea, it's gray no matter what
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,  // Painted with accent colour
            ACCENT_ENABLE_BLURBEHIND = 3,       // Blurbehind effect
            ACCENT_INVALID_STATE = 4        // Invalid state
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;    // Query attributes
            public IntPtr Data;                                                     // Data buffer
            public int SizeOfData;                                              // Data size
        }

        internal enum WindowCompositionAttribute    // Same as NtUserGetWindowCompositionAttribute?
        {
            WCA_ACCENT_POLICY = 19
        }

        public AcrylicWindow() : base()
        {

        }

        /// <summary>
        /// Sets various DWM window attributes
        /// </summary>
        /// <param name="hwnd">The window to modify</param>
        /// <param name="data">Pointer to the structure with the attribute data</param>
        /// <returns>    Nonzero on success, zero otherwise. You can call GetLastError on failure.</returns>
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);


        /// <summary>
		/// Invoked whenever application code or internal processes (such as a rebuilding layout pass) call ApplyTemplate. 
		/// </summary>
		public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();


            if (((App)App.Current).NoBlur)
                return;
            else
                EnableBlurBehind();

            ApplyWindows11RoundedCorners();

            // Fix background
            var color = this.Background.Clone();
            color.Opacity = 0.80;
            this.Background = color;
        }

        /// <summary>
		/// Enables BlurBehind for our fancy window
		/// </summary>
		internal void EnableBlurBehind()
        {
            var windowHelper = new WindowInteropHelper(this);

            var accent = new AccentPolicy();
            var accentStructSize = Marshal.SizeOf(accent);
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }


        // The enum flag for DwmSetWindowAttribute's second parameter, which tells the function what attribute to set.
        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33
        }

        // The DWM_WINDOW_CORNER_PREFERENCE enum for DwmSetWindowAttribute's third parameter, which tells the function
        // what value of the enum to set.
        public enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        // Import dwmapi.dll and define DwmSetWindowAttribute in C# corresponding to the native function.
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern long DwmSetWindowAttribute(IntPtr hwnd,
        DWMWINDOWATTRIBUTE attribute,
        ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute,
        uint cbAttribute);

        private bool IsWin11()
        {
            return Environment.OSVersion.Version.Build >= 22000;
        }

        internal void ApplyWindows11RoundedCorners()
        {
            if (!IsWin11())
                return;

            if (this != null)
            {
                IntPtr hWnd = new WindowInteropHelper(GetWindow(this)).EnsureHandle();
                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));
            }
        }

        internal void RemoveWindows11RoundedCorners()
        {
            if (!IsWin11())
                return;

            if (this != null)
            {
                IntPtr hWnd = new WindowInteropHelper(GetWindow(this)).EnsureHandle();
                var attribute = DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE;
                var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hWnd, attribute, ref preference, sizeof(uint));
            }
        }

    }
}
