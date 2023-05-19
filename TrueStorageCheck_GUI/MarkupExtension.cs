/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace TrueStorageCheck_GUI
{
    /// <summary>
    /// Helper for the language changed notification
    /// </summary>
    public class LanguageChangeNotifier : Freezable
    {
        protected override Freezable CreateInstanceCore()
        {
            return new LanguageChangeNotifier();
        }

        public int LanguageChangeTrigger
        {
            get { return (int)GetValue(LanguageChangeTriggerProperty); }
            set { SetValue(LanguageChangeTriggerProperty, value); }
        }

        public static readonly DependencyProperty LanguageChangeTriggerProperty =
            DependencyProperty.Register("LanguageChangeTrigger", typeof(int), typeof(LanguageChangeNotifier), new PropertyMetadata(0));
    }

    /// <summary>
    /// Converter for the language changed notification
    /// </summary>
    public class LocalizedStringConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0) return null;

            string key = values[0] as string;
            if (key == null) return null;

            var value = MainWindow.LanguageResource.GetString(key);
            return value ?? $"[{key}]";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }


    /// <summary>
    /// Used for language resources
    /// </summary>
    public class LocalizedStringExtension : MarkupExtension
    {
        public string Key { get; set; }

        public static event EventHandler LanguageChanged;

        private static LanguageChangeNotifier LanguageChangeNotifier { get; } = new LanguageChangeNotifier();

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (MainWindow.LanguageResource == null || Key == null) return null;

            MultiBinding multiBinding = new MultiBinding
            {
                Converter = new LocalizedStringConverter(),
            };

            multiBinding.Bindings.Add(new Binding { Source = this, Path = new PropertyPath(nameof(Key)) });
            multiBinding.Bindings.Add(new Binding { Source = LanguageChangeNotifier, Path = new PropertyPath(nameof(LanguageChangeNotifier.LanguageChangeTrigger)) });

            LocalizedStringExtension.LanguageChanged += (_, _) => UpdateLanguageChangeTrigger();

            return multiBinding.ProvideValue(serviceProvider);
        }

        private void UpdateLanguageChangeTrigger()
        {
            LanguageChangeNotifier.LanguageChangeTrigger++;
        }

        /// <summary>
        /// Call to update all bindings
        /// </summary>
        public static void OnLanguageChanged()
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Used to set a binding manually from code-behind
        /// </summary>
        /// <param name="label"></param>
        /// <param name="key"></param>
        public static void SetBinding(System.Windows.Controls.Label label, string key)
        {
            // Create a new Binding object, set source and path, one-way
            var binding = new Binding();
            binding.Source = Application.Current.Resources;
            binding.Path = new PropertyPath(key);
            binding.Mode = BindingMode.OneWay;

            // Set the binding on the label's Content property
            label.SetBinding(System.Windows.Controls.Label.ContentProperty, binding);
        }

    }
}
