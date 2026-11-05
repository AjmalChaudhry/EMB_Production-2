// ================= ROW VIEW MODEL =================
// ---------------- ROW VIEW MODEL ----------------
export interface ThreadRowViewModel {
  Id: number;
  Code: string;
  Name: string;

  Type?: string | null;
  Material?: string | null;
  YarnCount?: string | null;
  Color?: string | null;
  ColorCode?: string | null;

  UnitLength?: number | null;
  UnitOfMeasure?: string | null;
  UnitCost?: number | null;

  SupplierId?: number | null;
  SupplierName?: string | null;

  BranchId?: number | null;
  BranchName?: string | null;

  Active: boolean;
}

// ---------------- DETAIL VIEW MODEL ----------------
export interface ThreadDetailViewModel extends ThreadRowViewModel {
  // ✅ AUDIT (TEXT-BASED – SAME AS FABRIC)
  CreatedByEmployeeId?: number | null;
  CreatedDateText?: string | null;

  UpdatedByEmployeeId?: number | null;
  UpdatedDateText?: string | null;
}


// ================= FORM DTO =================
export interface ThreadFormDto {
  Id: number;

  // 🔹 REQUIRED FOR TEMPLATE (readonly / display)
  Code?: string | null;

  Name: string;

  Type?: string | null;
  Material?: string | null;
  YarnCount?: string | null;
  Color?: string | null;
  ColorCode?: string | null;

  UnitLength?: number | null;
  UnitOfMeasure?: string | null;
  UnitCost?: number | null;

  SupplierId?: number | null;
  BranchId?: number | null;

  Active: boolean;
}

