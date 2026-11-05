/** Row for table listing */
export interface CategoryListViewModel {
  id: number;
  name: string;
  code: string;
  description?: string | null;

  quantity?: string | null;
  length?: string | null;
  weight?: string | null;
  price?: number | null;

  brandId?: number | null;
  brandName?: string | null;

  branchId?: number | null;
  branchName?: string | null;

  active: boolean;
  deleted?: boolean | null;

  createdDateFormatted?: string | null;
  updatedDateFormatted?: string | null;
}

/** Detail for modal */
export interface CategoryDetailViewModel {
  Id?: number;
  Deleted?: boolean;

  Name?: string;
  Code?: string;
  Description?: string | null;

  Quantity?: string | null;
  Length?: string | null;
  Weight?: string | null;
  Price?: number | null;

  BrandId?: number | null;
  BrandName?: string | null;

  BranchId?: number | null;
  BranchName?: string | null;

  Active?: boolean;

  CreatedBy?: number | string | null;
  CreatedDateFormatted?: string | null;

  UpdatedBy?: number | string | null;
  UpdatedDateFormatted?: string | null;

  RecordTimeStamp?: string | null;
}

/** Lookups used in dropdowns */
export interface CategoryLookups {
  // Expected structure
  branches: { Id: number; Name: string }[];
  brands: { Id: number; Name: string; Code?: string | null }[];

  // Fallbacks (some controllers return these names)
  brandList?: { Id: number; Name: string; Code?: string | null }[];
  brandsList?: { Id: number; Name: string; Code?: string | null }[];
  branchesList?: { Id: number; Name: string }[];
}
