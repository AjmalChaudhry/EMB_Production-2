import {
  ApiResponse,
  ApiResponseData,
  ApiResponseList,
  ApiPagedResponse
} from '../../shared/models/api-response';


// ======================================================================
// LIST ROW MODEL (PascalCase to match backend JSON)
// ======================================================================
export interface ServiceRowViewModel {
  Id: number;

  ServiceTypeId?: number | null;
  ServiceTypeName?: string | null;

  ServiceName?: string | null;
  ServiceRateCharge?: number | null;

  MachineDevice?: number | null;

  MaxRange?: string | null;
  MaxCharges?: number | null;
  MinCharges?: number | null;

  Field1?: number | null;
  Field2?: number | null;

  BranchId?: number | null;
  BranchName?: string | null;

  RecordTimeStamp?: string | null;

  Active?: boolean;
  Deleted?: boolean;

  CreatedBy?: number | null;
  CreatedDate?: string | null;

  UpdatedBy?: number | null;
  UpdatedDate?: string | null;
}



// ======================================================================
// DETAIL VIEW MODEL — same fields as row
// ======================================================================
export interface ServiceDetailViewModel extends ServiceRowViewModel { }



// ======================================================================
// DTO FOR CREATE / UPDATE  (PascalCase => must match backend)
// ======================================================================
export interface ServiceDto {
  Id: number;

  ServiceTypeId?: number | null;
  ServiceName?: string | null;
  ServiceRateCharge?: number | null;

  MachineDevice?: number | null;

  MaxRange?: string | null;
  MaxCharges?: number | null;
  MinCharges?: number | null;

  Field1?: number | null;
  Field2?: number | null;

  BranchId?: number | null;

  Active?: boolean;
}




// ======================================================================
// RESPONSE TYPES
// ======================================================================
export type ServiceListResponse = ApiResponseList<ServiceRowViewModel>;
export type ServiceDetailResponse = ApiResponseData<ServiceDetailViewModel>;
export type ServiceSaveResponse = ApiResponse;
export type ServiceDeleteResponse = ApiResponse;
