// ===================== BRAND LIST VIEW MODEL =====================
/**
 * List row model (used in table)
 * NOTE:
 *  - All fields must be camelCase because Angular templates use camelCase.
 *  - The API returns PascalCase, so we map it inside the component.
 */
export interface BrandListViewModel {
  id: number;
  code: string;
  brandName: string;
  description?: string | null;

  branchId?: number | null;
  branchName?: string | null;

  active: boolean;

  // Optional audit fields (not used in grid)
  createdBy?: number | null;
  createdDate?: string | null;
  updatedBy?: number | null;
  updatedDate?: string | null;
  deleted?: boolean | null;
}



// ===================== BRAND DETAIL VIEW MODEL =====================
/**
 * Detail modal model
 * Keeping backend PascalCase because detail dialog often uses raw backend fields.
 * If needed, we can normalize this too.
 */
export interface BrandDetailViewModel {
  Id: number;
  Code: string;
  BrandName: string;
  Description?: string | null;

  BranchId?: number | null;
  BranchName?: string | null;

  Active: boolean;
  Deleted?: boolean | null;

  // Audit fields
  CreatedBy?: number | null;
  CreatedByName?: string | null;

  CreatedDateFormatted?: string | null;

  UpdatedBy?: number | null;
  UpdatedByName?: string | null;

  UpdatedDateFormatted?: string | null;

  RecordTimeStamp?: string | null;
}



// ===================== PAGED RESPONSE MODEL =====================
export interface BrandPagedResponse {
  success: boolean;
  data: {
    items: BrandListViewModel[];
    totalItems: number;
  };
  message?: string;
}
