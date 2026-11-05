// ================= PRODUCT LIST (TABLE) =================
export interface ProductListViewModel {
  id: number;

  prodName: string;
  prodCode?: string;
  prodDescription?: string;

  catId: number;
  branchId?: number | null;

  categoryName?: string;
  branchName?: string;

  active: boolean;
}

// ================= PRODUCT FORM =================
export interface ProductForm {
  id: number | null;

  prodName: string;
  prodCode?: string;
  prodDescription?: string;

  catId: number;
  branchId?: number | null;

  active: boolean;
}

// ================= PRODUCT DETAIL =================
export interface ProductDto extends ProductListViewModel {
  createdByEmployeeId?: number | null;
  updatedByEmployeeId?: number | null;

  createdDateText?: string | null;
  updatedDateText?: string | null;

  recordTimeStamp?: string | Date | null;
}
