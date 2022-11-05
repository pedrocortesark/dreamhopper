using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIM.MVVM;

namespace DreamHopper.ViewModels.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        public static MainViewModel Instance;
        public string Docname { get; private set; }
        public MainViewModel(string docName)
        {
            Instance = this;
            this.Docname = docName;
        }
    }
}
