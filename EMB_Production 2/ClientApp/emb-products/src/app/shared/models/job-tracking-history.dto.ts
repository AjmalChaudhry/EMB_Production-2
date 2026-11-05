export interface JobTrackingHistoryRowViewModel {
  id: number;

  // ✅ ADD THIS
  workOrderItemId?: number | null;
  workOrderItemLabel?: string | null;

  status?: string | null;
  statusDate?: string | null;
  remarks?: string | null;

  active?: boolean | null;
  deleted?: boolean | null;

  branchId?: number | null;
  branchName?: string | null;

  createdBy?: number | null;
  createdByName?: string | null;
  createdDate?: string | null;

  updatedBy?: number | null;
  updatedByName?: string | null;
  updatedDate?: string | null;
}



export interface JobTrackingHistoryDetailViewModel {
  id: number;

  workOrderItemId?: number;
  workOrderItemLabel?: string;

  status: string;
  statusDate: string | Date;
  statusDateText?: string;

  remarks?: string;

  // ✅ ADD THESE TWO
  branchId?: number | null;
  branchName?: string | null;

  active?: boolean;
  deleted?: boolean;

  createdBy?: number;
  createdByEmployeeId?: string;
  createdDate?: string | Date;
  createdDateText?: string;

  updatedBy?: number;
  updatedByEmployeeId?: string;
  updatedDate?: string | Date;
  updatedDateText?: string;
}


export interface JobTrackingHistoryDto {
  id: number;
  workOrderItemId: number;   // 🔒 REQUIRED
  branchId?: number | null;
  status: string;
  statusDate: Date;
  remarks?: string;
  active: boolean;
}


