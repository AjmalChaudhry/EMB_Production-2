// ========================= LIST ROW =========================
export interface ThreadUsageRowViewModel {
  id: number;
  code: number;

  componentName: string;
  threadName: string;
  branchName: string;

  usage: number;
  type: string;
  active: boolean;

  // optional UI helpers
  threadColor?: string | null;
  colorCode?: string | null;
}

// ========================= DETAIL VIEW =========================
export interface ThreadUsageDetailDto {
  id: number;
  code: number;

  compId: number;
  componentName: string;

  threadId: number;
  threadName: string;

  usage: number;
  type: string;

  branchId: number;
  branchName: string;

  active: boolean;
  deleted: boolean;

  // 🔐 AUDIT (DISPLAY VALUES)
  createdByEmployeeId?: string | number | null;
  updatedByEmployeeId?: string | number | null;

  createdDate?: string | null;
  updatedDate?: string | null;
}



// ========================= CREATE / UPDATE DTO =========================
export interface ThreadUsageDto {
  id: number;
  code: number;

  componentName: string;
  threadName: string;
  branchName: string;

  usage: number;
  type: string;
  active: boolean;

  // ✅ UI helpers (optional)
  threadColor?: string | null;
  colorCode?: string | null;
}


export interface ThreadUsageFormDto {
  id?: number;

  code: number;
  compId: number | null;
  threadId: number | null;
  branchId: number | null;

  usage: number;
  type: string;
  active: boolean;
}



// ========================= DROPDOWN =========================
export interface DropdownItem {
  id: number;
  name: string;
}

// ========================= API RESPONSES =========================
export interface ApiResponse {
  success: boolean;
  message?: string;
  data?: any;
}

export interface ApiResponsePaged<T> {
  success: boolean;
  message?: string;
  items: T[];
  total: number;
}
