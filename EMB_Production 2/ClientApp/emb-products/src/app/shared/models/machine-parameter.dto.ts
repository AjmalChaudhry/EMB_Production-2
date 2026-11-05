// ===================== CREATE / UPDATE DTO =====================
export interface MachineParameterDto {
  Id: number;
  MachineId: number | null;
  HeadToHead: string;
  FrameHeight: number | null;   // numeric input
  Active: boolean;
}

// ===================== LIST ROW VIEWMODEL =====================
export interface MachineParameterRowViewModel {
  Id: number;
  MachineId: number | null;
  MachineCode: string;
  HeadToHead: string;
  FrameHeight: number | null;
  Active: boolean;
  Deleted: boolean;

  CreatedBy?: number | null;
  CreatedDate?: string | null;
  UpdatedBy?: number | null;
  UpdatedDate?: string | null;
}

// ===================== DETAIL VIEWMODEL =====================
export interface MachineParameterDetailViewModel {
  Id: number;
  MachineId: number | null;
  MachineCode: string;
  HeadToHead: string | null;
  FrameHeight: number | null;
  Active: boolean;
  Deleted?: boolean;

  CreatedByEmployeeId?: number | null;
  CreatedDate?: string | null;
  UpdatedByEmployeeId?: number | null;
  UpdatedDate?: string | null;
}
