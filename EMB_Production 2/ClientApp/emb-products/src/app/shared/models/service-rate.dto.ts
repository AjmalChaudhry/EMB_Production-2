export interface ServiceRateRowViewModel {
  Id: number;

  ProductId?: number | null;
  ProductCode?: string | null;
  ProductName?: string | null;

  ServiceId?: number | null;
  ServiceName?: string | null;

  ChargeFactor?: string | null;
  Remarks?: string | null;

  BranchId?: number | null;
  BranchName?: string | null;

  Active?: boolean;
  Deleted?: boolean;

  RecordTimeStamp?: string | null;

  CreatedBy?: number | null;
  CreatedDate?: string | null;

  UpdatedBy?: number | null;
  UpdatedDate?: string | null;
}

export interface ServiceRateDetailViewModel extends ServiceRateRowViewModel {
  CreatedByEmployeeId?: number | null;
  UpdatedByEmployeeId?: number | null;
}

export interface ServiceRateDto {
  Id: number;
  ProductId: number | null;
  ServiceId: number | null;
  ChargeFactor: string;
  Remarks: string;
  BranchId: number | null;
  Active: boolean;
}



export interface ApiResponse {
  success: boolean;
  message?: string;
  data?: any;
  total?: number;
  totalItems?: number;
}


export interface ApiResponseList<T> {
  success: boolean;
  message?: string;
  data: { items: T[] };
}

export interface ApiResponseData<T> {
  success: boolean;
  message?: string;
  data: T;
}


