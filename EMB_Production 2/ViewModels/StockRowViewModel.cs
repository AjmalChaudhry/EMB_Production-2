using System;
using System.Collections.Generic;

namespace EMB_Production.ViewModels
{
    public class StockCustomerReservationViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; }
        public string CustomerName { get; set; }
        public string JobOrders { get; set; }
        public decimal ReservedQty { get; set; }
        public decimal DispatchedQty { get; set; }
        public decimal RemainingQty { get; set; }
    }

    public class StockRowViewModel
    {
        public int Id { get; set; }

        public string ItemType { get; set; }
        public int? ItemId { get; set; }
        public string ItemName { get; set; }

        public int? ProdId { get; set; }
        public string ProductName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? WarehouseId { get; set; }
        public string WarehouseName { get; set; }

        public string StockStage { get; set; }
        public string TransactionType { get; set; }

        public decimal OpeningQty { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal Qty { get; set; }
        public decimal CurrentQty { get; set; }

        // Finished Stock Central Values
        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public decimal? Rate { get; set; }
        public decimal? Amount { get; set; }
        public decimal? LastKnownRate { get; set; }
        public string Unit { get; set; }

        public string ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public string ReferenceNo { get; set; }

        public string StockDescription { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public DateTime? RecordTimeStamp { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class StockDetailViewModel
    {
        public int Id { get; set; }

        public int? ProdId { get; set; }
        public string ProductName { get; set; }

        public string ItemType { get; set; }
        public int? ItemId { get; set; }
        public string ItemName { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public int? WarehouseId { get; set; }
        public string WarehouseName { get; set; }

        public string StockStage { get; set; }
        public string TransactionType { get; set; }

        public decimal OpeningQty { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal Qty { get; set; }
        public decimal CurrentQty { get; set; }

        // Finished Stock Central Values
        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public decimal? Rate { get; set; }
        public decimal? Amount { get; set; }
        public decimal? LastKnownRate { get; set; }
        public string Unit { get; set; }

        public string ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public string ReferenceNo { get; set; }

        public string StockDescription { get; set; }
        public string Remarks { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public DateTime? RecordTimeStamp { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }

        public decimal UnitCost { get; set; }
        public decimal WeightedAvgCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalFinishedCost { get; set; }
        public decimal ReservedValue { get; set; }
        public decimal FreeStockValue { get; set; }

        public int QcPostCount { get; set; }
        public int DispatchCount { get; set; }
        public int ReverseCount { get; set; }
        public int MovementCount { get; set; }

        public DateTime? LastMovementDate { get; set; }
        public DateTime? FirstMovementDate { get; set; }

        public int PendingDispatchCount { get; set; }
        public decimal PendingDispatchQty { get; set; }

        public List<StockCustomerReservationViewModel> CustomerReservations { get; set; }
    }

    public class StockDashboardViewModel
    {
        public decimal CustomerReservedStock { get; set; }
        public decimal FreeCompanyStock { get; set; }
        public decimal TotalFinishedStock { get; set; }

        public int TotalProducts { get; set; }
        public int TotalBranches { get; set; }
    }
}