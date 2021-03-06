using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Libraries;
using ProgrammingParadigms;

namespace Application
{
    /// <summary>
    /// <para>This is a dummy abstraction to use as an example, either to learn from or to use in testing.</para>
    /// <para>Ports:</para>
    /// <para>1. IEvent start: Start</para>
    /// </summary>
    public class ExampleDomainAbstraction : UIElement, IEvent, IDataFlow<string>
    {
        // Public fields and properties
        public string InstanceName { get; set; } = "Default";
        public List<string> TestProperty { get; set; }

        // Private fields
        private string _ignoreStringField = "";
		private Data<int> _testData = new Data<int>()
        {
            storedData = 10
        };


        // Methods

		public void DoSomething()
		{
			
		}

        public ExampleDomainAbstraction(string arg0, string arg2 = "Main")
        {		
			// BEGIN AUTO-GENERATED INSTANTIATIONS FOR Diagram1
            Apply<T1, T2> A = new Apply<T1, T2>() {}; /* {"IsRoot":true,"Description":"test"} */
            Apply<T1, T2> B = new Apply<T1, T2>() {}; /* {"IsRoot":false} */
            Apply<T1, T2> test = new Apply<T1, T2>() {}; /* {"IsRoot":false,"Description":"test"} */
            // END AUTO-GENERATED INSTANTIATIONS FOR Diagram1

			// BEGIN AUTO-GENERATED WIRING FOR Diagram1
            A.WireTo(B, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false,"Description":"","SourceGenerics":["T1","T2"],"DestinationGenerics":["T1","T2"]} */
            A.WireTo(test, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false,"Description":"","SourceGenerics":["T1","T2"],"DestinationGenerics":["T1","T2"]} */
            // END AUTO-GENERATED WIRING FOR Diagram1
			
			// BEGIN AUTO-GENERATED INSTANTIATIONS FOR Diagram2
            Apply<T1, T2> C = new Apply<T1, T2>() {}; /* {"IsRoot":false} */
            Apply<T1, T2> test1 = new Apply<T1, T2>() {}; /* {"IsRoot":false} */
			// END AUTO-GENERATED INSTANTIATIONS FOR Diagram2
			
			// BEGIN AUTO-GENERATED WIRING FOR Diagram2
            C.WireTo(test1, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false,"Description":"","SourceGenerics":["T1","T2"],"DestinationGenerics":["T1","T2"]} */
            // END AUTO-GENERATED WIRING FOR Diagram2
			
			// BEGIN AUTO-GENERATED INSTANTIATIONS FOR Diagram3
            Apply<T1, T2> E = new Apply<T1, T2>() {}; /* {"IsRoot":false} */
            Apply<T1, T2> F = new Apply<T1, T2>() {}; /* {"IsRoot":false} */
			// END AUTO-GENERATED INSTANTIATIONS FOR Diagram3

			// BEGIN AUTO-GENERATED WIRING FOR Diagram3
            E.WireTo(F, "output"); /* {"SourceType":"Apply","SourceIsReference":false,"DestinationType":"Apply","DestinationIsReference":false,"Description":"","SourceGenerics":["T1","T2"],"DestinationGenerics":["T1","T2"]} */
			// END AUTO-GENERATED WIRING FOR Diagram3
			
			// BEGIN AUTO-GENERATED INSTANTIATIONS FOR Diagram4
			// END AUTO-GENERATED INSTANTIATIONS FOR Diagram4

			// BEGIN AUTO-GENERATED WIRING FOR Diagram4
			// END AUTO-GENERATED WIRING FOR Diagram4




        }
    }
}