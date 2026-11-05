// ===============================================
// ROW VIEW MODEL (List View)
// ===============================================
export interface ModuleTemplateRowViewModel {
  Id: number;
  Code: string;
  Name: string;

  BranchId?: number | null;
  BranchName?: string | null;

  Active?: boolean | null;
  Deleted?: boolean | null;

  CreatedDate?: string | null;
  UpdatedDate?: string | null;

  // Optional but useful
  RecordTimeStamp?: string | null;
}

// ===============================================
// DETAIL VIEW MODEL
// ===============================================
export interface ModuleTemplateDetailViewModel extends ModuleTemplateRowViewModel {
  Remarks?: string | null;
}

// ===============================================
// DTO FOR CREATE / UPDATE
// ===============================================
export interface ModuleTemplateDto {
  Id: number;          // 0 for create
  Code: string;
  Name: string;
  Remarks: string;
  BranchId: number | null;
  Active: boolean;
}

// ===============================================
// API RESPONSE MODELS
// ===============================================
export interface ApiResponseBase {
  success: boolean;
  message?: string;
}

export interface ApiResponseData<T> extends ApiResponseBase {
  data?: T;
}

export interface ApiResponsePaged<T> extends ApiResponseBase {
  data?: {
    items: T[];
    page: number;
    pageSize: number;
    total: number;
  };
}
