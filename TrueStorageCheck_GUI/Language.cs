using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrueStorageCheck_GUI
{
    public class Language
    {
        public string Name { get; set; }
        public string Code { get; set; }

        public Language(string name, string code)
        {
            Name=name;
            Code=code;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
