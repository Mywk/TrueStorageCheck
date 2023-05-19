/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TrueStorageCheck_GUI
{

    /// <summary>
    /// NumericUpDown
    /// </summary>
    /// <remarks>
    /// It still baffles me that all this years WPF has no native NumericUpDown user control.
    /// </remarks>
    public class NumericUpDown : Control
    {
        static NumericUpDown()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericUpDown), new FrameworkPropertyMetadata(typeof(NumericUpDown)));
        }

        public int Value
        {
            get { return (int)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(int), typeof(NumericUpDown), new PropertyMetadata(0, OnValueChanged));

        public int Minimum
        {
            get { return (int)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(int), typeof(NumericUpDown), new PropertyMetadata(0));

        public int Maximum
        {
            get { return (int)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(int), typeof(NumericUpDown), new PropertyMetadata(100));

        public int Step
        {
            get { return (int)GetValue(StepProperty); }
            set { SetValue(StepProperty, value); }
        }

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register("Step", typeof(int), typeof(NumericUpDown), new PropertyMetadata(1));

        public ICommand IncreaseCommand => new RelayCommand(Increase);
        public ICommand DecreaseCommand => new RelayCommand(Decrease);

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            this.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private void Increase()
        {
            if (Value < Maximum)
                Value = Math.Min(Value + Step, Maximum);
        }

        private void Decrease()
        {
            if (Value > Minimum)
                Value = Math.Max(Value - Step, Minimum);
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                Increase();
            else
                Decrease();

            e.Handled = true;
        }

        private class RelayCommand : ICommand
        {
            private readonly Action _action;

            public RelayCommand(Action action)
            {
                _action = action;
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public event EventHandler CanExecuteChanged;

            public void Execute(object parameter)
            {
                _action();
            }
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            NumericUpDown numericUpDown = d as NumericUpDown;
            numericUpDown?.OnValueChanged((int)e.OldValue, (int)e.NewValue);
        }

        protected virtual void OnValueChanged(int oldValue, int newValue)
        {
            RoutedPropertyChangedEventArgs<int> args = new RoutedPropertyChangedEventArgs<int>(oldValue, newValue, ValueChangedEvent);
            RaiseEvent(args);
        }

        public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent("ValueChanged", RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(NumericUpDown));

        public event RoutedPropertyChangedEventHandler<int> ValueChanged
        {
            add { AddHandler(ValueChangedEvent, value); }
            remove { RemoveHandler(ValueChangedEvent, value); }
        }
    }
}
