// ViewModels/MachinesPageViewModel.cs
using PagedList;
using EMB_Production.Models;
using EMB_Production_2.Models;

namespace EMB_Production.ViewModels
{
    public class MachinesPageViewModel
    {
        public IPagedList<Gen_Machines> Machines { get; set; }
        public Gen_Machines NewMachine { get; set; }
        public Gen_Machines EditMachine { get; set; }
        public bool ShowCreate { get; set; }
        public bool ShowEdit { get; set; }
        public int? EditId { get; set; }
        public string Search { get; set; }
        public string ActiveFilter { get; set; }
    }
}
