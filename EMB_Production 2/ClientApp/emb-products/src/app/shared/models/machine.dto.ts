export interface MachineRowViewModel {
  Id: number;

  Code: string | null;

  BrandId: number | null;
  BrandName: string | null;

  ServiceId: number | null;
  ServiceName: string | null;

  HeadtoHead?: string | null;
  FrameHeight?: string | null;

  HeadsNum?: number | null;
  NeedlesNum?: number | null;

  SerialNumber?: string | null;
  CallingNumber?: string | null;

  PurchaseDate?: string | null;
  Warranty?: number | null;
  ServiceEnd?: string | null;
  ServiceNext?: string | null;

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

export interface MachineDetailViewModel extends MachineRowViewModel { }

export interface MachineDto {
  Id: number;

  Code: string | null;
  BrandId: number | null;
  ServiceId: number | null;

  HeadtoHead: string | null;
  FrameHeight: string | null;

  HeadsNum: number | null;
  NeedlesNum: number | null;

  SerialNumber: string | null;
  CallingNumber: string | null;

  PurchaseDate: string | null;
  Warranty: number | null;
  ServiceEnd: string | null;
  ServiceNext: string | null;

  BranchId: number | null;
  Active: boolean;
}
