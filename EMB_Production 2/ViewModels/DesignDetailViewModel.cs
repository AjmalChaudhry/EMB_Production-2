using System;

namespace EMB_Production.ViewModels
{
    public class DesignListViewModel
    {
        public int Id { get; set; }

        public string D_Code { get; set; }
        public string DNo { get; set; }
        public string CollectionName { get; set; }

        public int? ProductId { get; set; }
        public string ProductName { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public bool Active { get; set; }
    }


    public class DesignDetailViewModel
    {
        public int Id { get; set; }

        public string D_Code { get; set; }
        public string DNo { get; set; }
        public string CollectionName { get; set; }

        public int? ProductId { get; set; }
        public string ProductName { get; set; }

        public int? MachineId { get; set; }
        public string MachineName { get; set; }

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public string CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public string UpdatedDate { get; set; }
    }


    public class DesignCreateDto
    {
        public string DNo { get; set; }

        public string CollectionName { get; set; }

        public int? ProductId { get; set; }

        public int? MachineId { get; set; }

        public int? CustomerId { get; set; }

        public bool Active { get; set; }
    }


    public class DesignUpdateDto
    {
        public int Id { get; set; }

        public string DNo { get; set; }

        public string CollectionName { get; set; }

        public int? ProductId { get; set; }

        public int? MachineId { get; set; }

        public int? CustomerId { get; set; }

        public bool Active { get; set; }
    }
}