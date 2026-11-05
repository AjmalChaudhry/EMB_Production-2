// ===============================
// ROW (for list grid)
// ===============================
export interface ServiceTypeRowViewModel {
  Id: number;

  Service_Type: string;
  Service_TypeDescription?: string;

  BranchId?: number | null;
  BranchName?: string | null;

  Active?: boolean;
}

// ===============================
// DETAIL (for edit/detail modal)
// ===============================
export interface ServiceTypeDetailViewModel {
  Id: number;

  Service_Type: string;
  Service_TypeDescription?: string;

  BranchId?: number | null;
  BranchName?: string | null;

  Active?: boolean;
  Deleted?: boolean;

  CreatedBy?: string | null;
  CreatedDate?: string | null;
  UpdatedBy?: string | null;
  UpdatedDate?: string | null;
}

// ===============================
// DTO used for CREATE / UPDATE
// ===============================
export interface ServiceTypeDto {
  Id: number; // 0 for create
  Service_Type: string;
  Service_TypeDescription: string;
  BranchId?: number | null;
  Active?: boolean;
}

// ===============================
// GENERIC API RESPONSES
// (you can reuse for all modules)
// ===============================
export interface ApiResponseBase {
  success: boolean;
  message?: string;
}

export interface ApiResponseData<T> extends ApiResponseBase {
  data?: T;
}

export interface ApiResponsePaged<T> extends ApiResponseBase {
  data?: T[];
  total?: number;
}

// ===============================
// SERVICE-TYPE SPECIFIC RESPONSES
// ===============================
export type ServiceTypeListResponse = ApiResponsePaged<ServiceTypeRowViewModel>;
export type ServiceTypeDetailResponse = ApiResponseData<ServiceTypeDetailViewModel>;
export type ServiceTypeSaveResponse = ApiResponseData<ServiceTypeDetailViewModel>;
export type ServiceTypeDeleteResponse = ApiResponseBase;
