using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hexa.NET.ImGui.Widgets.Dialogs;

namespace OGNES.UI.General
{
    public class FileOpenDialog : OpenFileDialog
    {
        public string DialogName { get; set; }

        public FileOpenDialog() : base()
        {
            DialogName = "FileOpenDialog";
        }

        public FileOpenDialog(string name): base()
        {
            DialogName = name;
        }
    }
}
