﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ProgrammingParadigms;
using System.Windows;
using System.Windows.Forms;
using WPF = System.Windows.Controls;
using System.Windows.Input;

namespace DomainAbstractions
{
    /// <summary>
    /// <para>Contains a WPF TextBox and both implements and provides ports for setting/getting the text inside.</para>
    /// <para>Ports:</para>
    /// <para>1. IUI wpfElement: returns the contained TextBox</para>
    /// <para>2. IDataFlow&lt;string&gt; content: The string contained in the TextBox</para>
    /// <para>3. IDataFlowB&lt;string&gt; returnContent: returns the string contained in the TextBox</para>
    /// <para>4. IEvent clear: clears the text content inside the TextBox</para>
    /// <para>5. IDataFlow&lt;string&gt; textOutput: outputs the string contained in the TextBox</para>
    /// </summary>
    public class TextBox : IUI, IDataFlow<string>, IEvent
    {
        // properties
        public string InstanceName { get; set; } = "Default";
        public string Text
        {
            get => _textBox.Dispatcher.Invoke(() => _textBox.Text);
            set
            {
                _textBox.Dispatcher.Invoke(() => _textBox.Text = value);
            }
        }

        public Thickness Margin
        {
            get => _textBox.Margin;
            set => _textBox.Margin = value;
        }

        public bool AcceptsReturn
        {
            get => _textBox.AcceptsReturn;
            set => _textBox.AcceptsReturn = value;
        }

        public bool TrackIndent { get; set; } = false;

        public bool AcceptsTab
        {
            get => _textBox.AcceptsTab;
            set => _textBox.AcceptsTab = value;
        }

        public double Height
        {
            get => _textBox.Height;
            set => _textBox.MinHeight = value;
        }

        public double Width
        {
            get => _textBox.Width;
            set => _textBox.MinWidth = value;
        }

        // Fields
        private WPF.TextBox _textBox = new WPF.TextBox();

        // Outputs
        private IDataFlow<string> textOutput;
        private IEvent eventEnterPressed;

        /// <summary>
        /// <para>Contains a WPF TextBox and both implements and provides ports for setting/getting the text inside.</para>
        /// </summary>
        public TextBox(bool readOnly = false)
        {
            _textBox.AcceptsTab = true;

            _textBox.TextChanged += (sender, args) =>
            {
                if (textOutput != null) textOutput.Data = Text;
                if (_textBox.HorizontalScrollBarVisibility == WPF.ScrollBarVisibility.Visible)
                {
                    _textBox.ScrollToEnd(); 
                }

            };

            // Track indentation
            _textBox.PreviewKeyDown += (sender, args) =>
            {
                if (TrackIndent && args.Key == Key.Return)
                {
                    args.Handled = true; // Handle enter press here rather than externally

                    var text = Text;
                    var preText = text.Substring(0, _textBox.CaretIndex);
                    var postText = text.Length > _textBox.CaretIndex ? text.Substring(_textBox.CaretIndex) : "";

                    var latestLine = preText.Split(new[] {Environment.NewLine}, StringSplitOptions.None).Last();
                    var startingWhiteSpace = Regex.Match(latestLine, @"^([\s]+)").Value;

                    var indentLevel = startingWhiteSpace.Count(c => c == '\t');
                    latestLine = Environment.NewLine + new string('\t', indentLevel);

                    Text = preText + latestLine + postText;
                    _textBox.Dispatcher.Invoke(() => _textBox.CaretIndex = preText.Length + latestLine.Length);
                }
            };

            _textBox.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            _textBox.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            _textBox.IsReadOnly = readOnly;

            _textBox.KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Enter) eventEnterPressed?.Execute();
            };
        }

        // Methods


        // IUI implementation
        System.Windows.UIElement IUI.GetWPFElement()
        {
            return _textBox;
        }

        // IDataFlow<string> implementation
        string IDataFlow<string>.Data
        {
            get => Text;
            set
            {
                Text = value;
            }
        }

        // IEvent implementation
        void IEvent.Execute()
        {
            _textBox.Clear();
        }

    }
}
