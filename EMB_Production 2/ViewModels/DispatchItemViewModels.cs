using System;

namespace EMB_Production.ViewModels
{
    public class DispatchItemRowViewModel
    {
        public int Id { get; set; }
        public int DispatchId { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public string DispatchType { get; set; }
        public string SaleType { get; set; }
        public string SaleTypeText { get; set; }

        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }

        public int? JobOrderId { get; set; }
        public string JobOrderCode { get; set; }

        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public decimal AvailableQty { get; set; }
        public decimal AvailableQtySnapshot { get; set; }

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
        public bool Deleted { get; set; }
        public bool IsPosted { get; set; }
    }

   
    public class DispatchStockLookupViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }

        public int BranchId { get; set; }
        public string BranchName { get; set; }

        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public decimal AvailableQty { get; set; }

        public decimal CostPrice { get; set; }
        public decimal BaseSalePrice { get; set; }
        public decimal SalePrice { get; set; }

        public string Unit { get; set; }
    }
}