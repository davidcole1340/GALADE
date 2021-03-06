﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Libraries;
using ProgrammingParadigms;

namespace DomainAbstractions
{
    public class VisualEdge : IEdge
    {
        // Public fields and properties
        public string InstanceName { get; set; } = "Default";
        public UIElement Render { get; set; }

        // Private fields

        // Ports
        private IUI uiLayout;

        // IEdge implementation
        public object Source { get; set; }
        public object Destination { get; set; }
        public Dictionary<string, object> Payload { get; set; } = new Dictionary<string, object>();

        // Methods
        public void InitialiseUI()
        {
            if (uiLayout != null) Render = uiLayout.GetWPFElement();
        }

        public VisualEdge()
        {

        }
    }
}
