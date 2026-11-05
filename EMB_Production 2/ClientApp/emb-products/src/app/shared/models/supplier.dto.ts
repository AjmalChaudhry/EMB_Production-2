
// ---------------- ROW / DETAIL ----------------
export interface SupplierRowViewModel {
  Id: number;
  SupplierName: string;

  Contact?: string | null;
  Phone?: string | null;
  Email?: string | null;
  Address?: string | null;

  BranchId?: number | null;
  BranchName?: string | null;

  Active: boolean;
  Deleted: boolean;

  CreatedBy?: number | null;
  CreatedDate?: string | null;
  UpdatedBy?: number | null;
  UpdatedDate?: string | null;
}

export interface SupplierDetailViewModel extends SupplierRowViewModel { }

// ---------------- DTO FOR CREATE / UPDATE ----------------
export interface SupplierDto {
  id: number;          // 0 -> create
  supplierName: string;

  contact?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null; 

  branchId?: number | null;

  active: boolean;
}
export interface SupplierLookup {
  id: number;
  supplierName: string;
}

// ---------------- PHASE H.0 - SUPPLIER 360 PROFILE ----------------
export interface SupplierProfileHeader {
  id: number;
  supplierName: string;
  contact?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  branchId?: number | null;
  branchName?: string | null;
  active: boolean;
  createdDate?: string | null;
  updatedDate?: string | null;
}

export interface SupplierProfileKpis {
  purchaseCount: number;
  postedCount: number;
  draftCount: number;
  totalQty: number;
  totalAmount: number;
  avgOrderAmount: number;
  lastPurchaseDate?: string | null;
  firstPurchaseDate?: string | null;
}

export interface SupplierProfileTopItem {
  itemName: string;
  itemType: string;
  totalQty: number;
  totalAmount: number;
}

export interface SupplierProfileTrendMonth {
  month: string;
  count: number;
  amount: number;
}

export interface SupplierProfileRecentPurchase {
  id: number;
  dateText: string;
  invoiceNo?: string | null;
  branchName?: string | null;
  isPosted: boolean;
  totalQty: number;
  totalAmount: number;
}

export interface SupplierProfileViewModel {
  supplier: SupplierProfileHeader;
  kpis: SupplierProfileKpis;
  topItems: SupplierProfileTopItem[];
  monthlyTrend: SupplierProfileTrendMonth[];
  recentPurchases: SupplierProfileRecentPurchase[];
}
