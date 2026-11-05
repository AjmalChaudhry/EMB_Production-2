using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class DispatchRowViewModel
    {
        public int Id { get; set; }

        public DateTime DispatchDate { get; set; }
        public string DispatchDateText { get; set; }

        public string DispatchType { get; set; }
        public string SaleType { get; set; }
        public string SaleTypeText { get; set; }

        public int? JobOrderId { get; set; }
        public string JobOrderCode { get; set; }

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }

        public string DirectCustomerName { get; set; }
        public string DirectCustomerMobile { get; set; }
        public string DirectCustomerAddress { get; set; }

        public string DisplayCustomerName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomerName))
                    return CustomerName;

                if (!string.IsNullOrWhiteSpace(DirectCustomerName))
                    return DirectCustomerName;

                return "-";
            }
        }

        public int? DepartmentId { get; set; }
        public string DepartmentName { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; }

        public string VehicleNo { get; set; }
        public string DriverName { get; set; }

        public int ItemCount { get; set; }
        public decimal DispatchQtyTotal { get; set; }

        public decimal SalePrice { get; set; }
        public decimal TotalAmount { get; set; }

        public bool Active { get; set; }
        public bool IsPosted { get; set; }
        public bool IsReversed { get; set; }

        public string StatusText
        {
            get
            {
                if (IsReversed) return "Reversed";
                if (IsPosted) return "Posted";
                return "Draft";
            }
        }

        // Sprint-1: row created within last 24h → lightning bolt badge
        // on the date cell so dispatch supervisors can spot fresh
        // entries scanning the list.
        public bool IsRecent { get; set; }
    }

    public class DispatchDetailViewModel : DispatchRowViewModel
    {
        public string DispatchDateInput { get; set; }
        public string Remarks { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }

        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }

        public int? PostedBy { get; set; }
        public DateTime? PostedDate { get; set; }

        // Sprint-2: workflow stepper data — three-stage Draft → Posted
        // → Reversed pipeline rendered at the top of the detail drawer.
        public string[] WorkflowStages { get; set; }
        public int WorkflowStageIndex { get; set; }

        public List<DispatchItemViewModel> Items { get; set; }
            = new List<DispatchItemViewModel>();
    }

    public class DispatchDto
    {
        public int Id { get; set; }

        public DateTime DispatchDate { get; set; }

        public string DispatchType { get; set; }
        public string SaleType { get; set; }

        public int? JobOrderId { get; set; }
        public int? CustomerId { get; set; }

        public string DirectCustomerName { get; set; }
        public string DirectCustomerMobile { get; set; }
        public string DirectCustomerAddress { get; set; }

        public int? DepartmentId { get; set; }
        public int? BranchId { get; set; }

        public string VehicleNo { get; set; }
        public string DriverName { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }

        public List<DispatchItemDto> Items { get; set; }
            = new List<DispatchItemDto>();
    }

    public class DispatchItemDto
    {
        public int Id { get; set; }
        public int DispatchId { get; set; }

        public int ProductId { get; set; }
        public decimal DispatchQty { get; set; }

        public decimal CostPrice { get; set; }
        public decimal BaseSalePrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SalePrice { get; set; }
        public decimal Amount { get; set; }

        public string Unit { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
    }

    public class DispatchItemViewModel
    {
        public int Id { get; set; }
        public int DispatchId { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; }

        public decimal AvailableQtySnapshot { get; set; }

        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public decimal DispatchQty { get; set; }

        public decimal CostPrice { get; set; }
        public decimal BaseSalePrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SalePrice { get; set; }
        public decimal Amount { get; set; }

        public string Unit { get; set; }
    }

    public class DispatchStockItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; }

        public decimal AvailableQty { get; set; }

        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public decimal CostPrice { get; set; }
        public decimal BaseSalePrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal SalePrice { get; set; }
        public decimal Amount { get; set; }

        public string Unit { get; set; }
    }
}