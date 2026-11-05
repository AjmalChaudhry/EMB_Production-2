
// ================= ROLES (CENTRAL) =================
export const ROLES = {
  ADMIN: 1,
  STORE_MANAGER: 2,
  USER: 3
};

export interface ApiResponseBase {
  success: boolean;
  message?: string;
}

export interface ApiResponse {
  success: boolean;
  message?: string;
  data?: any;
}

export interface ApiResponseData<T> extends ApiResponseBase {
  data: T;
}

export interface ApiPagedResponse<T> extends ApiResponseBase {
  data: {
    items: T[];
   
    page?: number;
    pageSize?: number;
    total?: number;
    
    totalItems?: number;
    totalPages: number;
  };
}

export interface ApiResponseList<T> extends ApiResponseBase {
  data:
  | T[]
  | {
    items?: T[];
    Items?: T[];
    total?: number;
    Total?: number;
    totalItems?: number;
    TotalItems?: number;
    page?: number;
    pageSize?: number;
  };
}



export interface DropdownItem {
  id: number;
  name: string;
  customerCode?: string | null;
}
