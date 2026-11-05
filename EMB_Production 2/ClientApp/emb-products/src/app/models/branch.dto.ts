export interface BranchListViewModel {
  id: number;              // 🔑 REQUIRED
  branchId: number | null;
  name: string;
  location?: string;
  contect?: string;
  companyId: number;
  companyName?: string;
  isActive: boolean;
  deleted?: boolean;
}



export interface BranchDetailViewModel {
  id: number;
  branchId: number | null;
  name: string;
  location?: string;
  contect?: string;
  companyId: number;
  companyName?: string;
  isActive: boolean;
  deleted?: boolean;

  createdByEmployeeId?: number | null;
  updatedByEmployeeId?: number | null;

  // ✅ formatted strings (same as old modules)
  createdDateText?: string | null;
  updatedDateText?: string | null;
}



export interface BranchForm {
  branchId?: number | null;
  name: string;
  location?: string | null;
  contect?: string | null;
  companyId: number;
  isActive: boolean;
}
